using System.Text.Json.Nodes;
using Dante.Core.Config;
using Dante.Core.Device;
using Dante.Core.Engine;
using Dante.Core.Widgets;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dante.Tests;

/// <summary>The kernel side of media widgets: the protocol fields, album art in chunks, and button controls.</summary>
public class MediaProtocolTests
{
    private static byte[] FakeJpeg(int length, byte seed = 1) => [.. Enumerable.Range(0, length).Select(i => (byte)(i * 7 + seed))];

    [Fact]
    public void A_media_value_carries_artist_play_state_volume_and_the_art_fingerprint()
    {
        var art = FakeJpeg(3000);
        var reading = new WidgetReading { Text = "Bohemian Rhapsody", Media = new MediaReading("Queen", Playing: true, Muted: false, Volume: 140, Art: art) };
        var msg = DeviceProtocol.Value("media", reading, 0, null);

        Assert.Equal("Bohemian Rhapsody", msg["txt"]!.GetValue<string>());
        Assert.Equal("Queen", msg["sub"]!.GetValue<string>());
        Assert.True(msg["play"]!.GetValue<bool>());
        Assert.False(msg["mute"]!.GetValue<bool>());
        Assert.Equal(100, msg["vol"]!.GetValue<int>());                     // clamped to 0-100
        Assert.Equal(DeviceProtocol.ArtHash(art), msg["art"]!.GetValue<string>());
        Assert.Equal(8, msg["art"]!.GetValue<string>().Length);
    }

    [Fact]
    public void A_value_without_media_has_no_media_fields()
    {
        var msg = DeviceProtocol.Value("cpu", new WidgetReading { Value = 42 }, 0, null);
        Assert.Null(msg["sub"]);
        Assert.Null(msg["play"]);
        Assert.Null(msg["art"]);
    }

    [Fact]
    public void Art_arrives_in_chunks_that_fit_the_device_line_limit_and_put_back_together()
    {
        var art = FakeJpeg(5000);
        var chunks = DeviceProtocol.Art("media", art).ToList();

        Assert.Equal(5, chunks.Count);
        Assert.All(chunks, c => Assert.True(c.ToJsonString().Length < 2047, "every line must fit DANTE's 2 KB line buffer"));
        Assert.All(chunks, c => Assert.Equal(DeviceProtocol.ArtHash(art), c["h"]!.GetValue<string>()));
        Assert.Equal([0, 1, 2, 3, 4], chunks.Select(c => c["i"]!.GetValue<int>()));
        Assert.All(chunks, c => Assert.Equal(5, c["n"]!.GetValue<int>()));
        Assert.All(chunks, c => Assert.Equal(5000, c["len"]!.GetValue<int>()));

        var back = chunks.SelectMany(c => Convert.FromBase64String(c["d"]!.GetValue<string>())).ToArray();
        Assert.Equal(art, back);
    }

    [Fact]
    public void Art_that_is_too_big_for_DANTE_is_never_sent()
    {
        var huge = FakeJpeg(DeviceProtocol.MaxArtBytes + 1);
        Assert.Empty(DeviceProtocol.Art("media", huge));
        var msg = DeviceProtocol.Value("media", new WidgetReading { Text = "x", Media = new MediaReading("y", true, Art: huge) }, 0, null);
        Assert.Null(msg["art"]);   // so DANTE doesn't wait for a picture that never comes
    }

    [Fact]
    public void A_tap_on_an_on_screen_control_is_reported_as_the_control()
    {
        var evt = DeviceEvent.TryParse("""{"t":"btn","b":"sel","long":false,"scr":"media","ctl":"next"}""")!;
        Assert.Equal("next", evt.Control);
        Assert.Null(DeviceEvent.TryParse("""{"t":"btn","b":"sel","long":false,"scr":"media"}""")!.Control);
    }
}

/// <summary>The engine sends album art only when it changes, and again after DANTE asks for everything.</summary>
public class MediaEngineTests : IAsyncLifetime
{
    private sealed class FakeMediaType : IWidgetType
    {
        public WidgetReading Reading { get; set; } = new() { Text = "Nothing" };
        public string Type => "fake-media";
        public string PluginId => "fake";
        public string DisplayName => "Fake media";
        public string Description => "";
        public IReadOnlyList<SettingField> Fields { get; } = [];
        public Task<WidgetReading> ReadAsync(WidgetDefinition widget, CancellationToken ct) => Task.FromResult(Reading);
    }

    private readonly FakeDante _buddy = new();
    private readonly FakeMediaType _media = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dante-{Guid.NewGuid():N}");
    private DeviceLink _link = null!;
    private DanteEngine _engine = null!;

    public async Task InitializeAsync()
    {
        var store = new ConfigStore(Path.Combine(_dir, "config.json"));
        store.Save(new DanteConfig
        {
            Device = new DeviceSettings { PortName = "FAKE", AutoConnect = true },
            Widgets = [new WidgetDefinition { Id = "media", Name = "Now playing", Visual = VisualKind.Media, Type = "fake-media", PollSeconds = 1 }],
        });
        _link = new DeviceLink(store, NullLogger<DeviceLink>.Instance, TimeProvider.System, _ => _buddy);
        _engine = new DanteEngine(store, new WidgetTypeRegistry([_media]), _link, new RecordingOpener(), TimeProvider.System,
            NullLogger<DanteEngine>.Instance, new DeviceAlerts(_link));
        await _link.StartAsync(default);
        await _engine.StartAsync(default);
    }

    public async Task DisposeAsync()
    {
        await _engine.StopAsync(default);
        await _link.StopAsync(default);
        Directory.Delete(_dir, recursive: true);
    }

    private static bool Is(JsonObject m, string type) => m["t"]!.GetValue<string>() == type;
    private int ArtChunks() => _buddy.Received.Count(m => Is(m, "art"));

    private async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(50);
        Assert.True(condition(), "never happened. Got: " + string.Join("\n", _buddy.Received.Select(m => m.ToJsonString()[..Math.Min(120, m.ToJsonString().Length)])));
    }

    [Fact]
    public async Task Art_goes_once_per_picture_before_the_value_and_again_after_a_sync()
    {
        var art = Enumerable.Range(0, 2500).Select(i => (byte)i).ToArray();   // 3 chunks
        _media.Reading = new WidgetReading { Text = "Song A", Media = new MediaReading("Band", true, Volume: 30, Art: art) };
        _engine.PollNow("media");
        await WaitUntil(() => _buddy.Received.Any(m => Is(m, "val") && m["txt"]?.GetValue<string>() == "Song A"));
        Assert.Equal(3, ArtChunks());

        // The art arrives before the value that shows it.
        var list = _buddy.Received.ToList();
        Assert.True(list.FindLastIndex(m => Is(m, "art")) < list.FindIndex(m => Is(m, "val") && m["txt"]?.GetValue<string>() == "Song A"));

        // Same picture on the next reading (new volume): no art again.
        _media.Reading = _media.Reading with { Media = _media.Reading.Media! with { Volume = 35 } };
        _engine.PollNow("media");
        await WaitUntil(() => _buddy.Received.Any(m => Is(m, "val") && m["vol"]?.GetValue<int>() == 35));
        Assert.Equal(3, ArtChunks());

        // DANTE asks for everything (it may have restarted and lost the picture): the art goes again.
        _buddy.Press("""{"t":"sync"}""");
        await WaitUntil(() => ArtChunks() == 6);
    }
}
