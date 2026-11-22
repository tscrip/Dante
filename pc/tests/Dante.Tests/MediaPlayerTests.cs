using System.Text.Json.Nodes;
using Dante.Core.Config;
using Dante.Core.Device;
using Dante.Core.Plugins;
using Dante.Server.Features.MediaPlayer;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Dante.Tests;

public class MediaPlayerSourceTests
{
    private const char Sep = '\u001f';
    private static string Line(params string[] fields) => string.Join(Sep, fields);

    [Fact]
    public void Linux_picks_the_player_that_is_playing()
    {
        var output = string.Join('\n',
            Line("firefox", "Paused", "Some video", "", ""),
            Line("spotify", "Playing", "Bohemian Rhapsody", "Queen", "https://i.scdn.co/image/abc"),
            Line("vlc", "Stopped", "", "", ""));
        var t = LinuxNowPlaying.Pick(output)!;
        Assert.Equal("spotify", t.Player);
        Assert.Equal("Bohemian Rhapsody", t.Title);
        Assert.Equal("Queen", t.Artist);
        Assert.Equal("https://i.scdn.co/image/abc", t.ArtUrl);
    }

    [Fact]
    public void Linux_falls_back_to_a_paused_player_and_ignores_stopped_ones()
    {
        Assert.Equal("firefox", LinuxNowPlaying.Pick(Line("firefox", "Paused", "Video", "", ""))!.Player);
        Assert.Null(LinuxNowPlaying.Pick(Line("vlc", "Stopped", "", "", "")));
        Assert.Null(LinuxNowPlaying.Pick(""));
    }

    [Fact]
    public async Task Linux_says_how_to_fix_a_missing_playerctl_and_returns_nothing_without_players()
    {
        var missing = new FakeRunner { Missing = { "playerctl" } };
        var ex = await Assert.ThrowsAsync<ActionFailedException>(() => new LinuxNowPlaying(missing, new NoHttp()).GetAsync("", default));
        Assert.Contains("playerctl", ex.Message);

        var none = new FakeRunner();
        none.Results[$"playerctl -a metadata --format {Line("{{playerName}}", "{{status}}", "{{title}}", "{{artist}}", "{{mpris:artUrl}}")}"] =
            new(1, "", "No players found");
        Assert.Null(await new LinuxNowPlaying(none, new NoHttp()).GetAsync("", default));
    }

    [Fact]
    public async Task Linux_sends_play_pause_to_the_player_on_screen()
    {
        var run = new FakeRunner();
        Assert.True(await new LinuxNowPlaying(run, new NoHttp()).ControlAsync("spotify", MediaCommand.Next, default));
        Assert.Equal("playerctl -p spotify next", Assert.Single(run.Calls));
        Assert.False(await new LinuxNowPlaying(run, new NoHttp()).ControlAsync(null, MediaCommand.Next, default));   // media keys instead
    }

    [Fact]
    public void Mac_reads_a_track_line()
    {
        var t = MacNowPlaying.ParseTrack("Spotify", "playing\tBohemian Rhapsody\tQueen\thttps://i.scdn.co/image/abc\n")!;
        Assert.True(t.Playing);
        Assert.Equal("Queen", t.Artist);
        Assert.Equal("https://i.scdn.co/image/abc", t.ArtUrl);

        var music = MacNowPlaying.ParseTrack("Music", "paused\tClair de Lune\tDebussy\t")!;
        Assert.False(music.Playing);
        Assert.Null(music.ArtUrl);
        Assert.Null(MacNowPlaying.ParseTrack("Music", ""));   // stopped: the script returns nothing
    }

    [Fact]
    public void Mac_reads_apple_music_artwork_data()
    {
        Assert.Equal([0xFF, 0xD8, 0xFF, 0xE0], MacNowPlaying.ParseAppleScriptData("«data JPEGFFD8FFE0»"));
        Assert.Null(MacNowPlaying.ParseAppleScriptData("missing value"));
    }

    [Fact]
    public async Task Mac_controls_spotify_and_music_directly_but_nothing_else()
    {
        var run = new FakeRunner();
        var mac = new MacNowPlaying(run, new NoHttp());
        Assert.True(await mac.ControlAsync("Spotify", MediaCommand.PlayPause, default));
        Assert.Equal("osascript -e tell application \"Spotify\" to playpause", Assert.Single(run.Calls));
        Assert.False(await mac.ControlAsync("Chrome", MediaCommand.Next, default));
    }

    [Fact]
    public void Windows_helper_lines_are_read()
    {
        var s = WindowsNowPlaying.ParseLine("""{"ok":true,"none":false,"app":"Spotify.exe","title":"Song","artist":"Band","playing":true,"artKey":"k","art":"AAEC"}""")!;
        Assert.True(s.Ok);
        Assert.Equal("Spotify.exe", s.App);
        Assert.True(s.Playing);
        Assert.Equal("AAEC", s.Art);
        Assert.True(WindowsNowPlaying.ParseLine("""{"ok":true,"none":true}""")!.None);
        Assert.False(WindowsNowPlaying.ParseLine("""{"ok":false,"error":"Access denied"}""")!.Ok);
        Assert.Null(WindowsNowPlaying.ParseLine("WARNING: something from PowerShell"));
    }

    [Fact]
    public void Album_art_becomes_a_small_square_jpeg_that_fits_DANTE()
    {
        using var source = new SKBitmap(640, 480);
        for (var y = 0; y < 480; y++)
            for (var x = 0; x < 640; x++)
                source.SetPixel(x, y, new SKColor((byte)x, (byte)y, (byte)(x ^ y)));   // busy, so it doesn't compress to nothing
        using var png = source.Encode(SKEncodedImageFormat.Png, 100);

        var jpeg = AlbumArt.ToDeviceJpeg(png.ToArray())!;
        Assert.True(jpeg.Length <= DeviceProtocol.MaxArtBytes);
        Assert.Equal([0xFF, 0xD8], jpeg[..2]);                 // a JPEG
        using var back = SKBitmap.Decode(jpeg);
        Assert.Equal((AlbumArt.Size, AlbumArt.Size), (back.Width, back.Height));
        // DANTE's decoder needs baseline JPEGs: a baseline frame marker (FF C0), no progressive one (FF C2).
        // (0xFF in the picture data is always followed by 00, so these two bytes only ever appear as markers.)
        Assert.True(HasMarker(jpeg, 0xC0));
        Assert.False(HasMarker(jpeg, 0xC2));
    }

    [Fact]
    public void Things_that_are_not_pictures_give_no_art()
    {
        Assert.Null(AlbumArt.ToDeviceJpeg([1, 2, 3, 4]));
        Assert.Null(AlbumArt.ToDeviceJpeg(null));
    }

    private static bool HasMarker(byte[] jpeg, byte marker)
    {
        for (var i = 0; i + 1 < jpeg.Length; i++) if (jpeg[i] == 0xFF && jpeg[i + 1] == marker) return true;
        return false;
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("tests never use the internet");
    }
}

public class MediaPlayerServiceTests
{
    private sealed class FakeSource : INowPlayingSource
    {
        public NowPlaying? Now { get; set; }
        public int ArtLoads { get; private set; }
        public List<(string? App, MediaCommand Command)> Commands { get; } = [];
        public bool HandlesControls { get; set; } = true;
        public byte[]? Art { get; set; }

        public Task<NowPlaying?> GetAsync(string player, CancellationToken ct) =>
            Task.FromResult(Now is null ? null : Now with { LoadArt = _ => { ArtLoads++; return Task.FromResult(Art); } });

        public Task<bool> ControlAsync(string? app, MediaCommand command, CancellationToken ct)
        {
            Commands.Add((app, command));
            return Task.FromResult(HandlesControls);
        }
    }

    private sealed class FakeMedia : IMediaBackend
    {
        public List<string> Calls { get; } = [];
        public VolumeState? Volume { get; set; } = new(40, false);
        public Task PlayPauseAsync(CancellationToken ct) { Calls.Add("key:play"); return Task.CompletedTask; }
        public Task NextAsync(CancellationToken ct) { Calls.Add("key:next"); return Task.CompletedTask; }
        public Task PreviousAsync(CancellationToken ct) { Calls.Add("key:prev"); return Task.CompletedTask; }
        public Task<bool> ToggleSpeakerMuteAsync(CancellationToken ct) { Calls.Add("mute"); return Task.FromResult(true); }
        public Task<bool> ToggleMicMuteAsync(CancellationToken ct) => Task.FromResult(false);
        public Task<int?> ChangeVolumeAsync(bool up, CancellationToken ct) { Calls.Add(up ? "vol+" : "vol-"); return Task.FromResult<int?>(50); }
        public Task<VolumeState?> GetVolumeAsync(CancellationToken ct) => Task.FromResult(Volume);
    }

    private readonly FakeSource _source = new();
    private readonly FakeMedia _media = new();
    private readonly FakeDante _dante = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dante-{Guid.NewGuid():N}");

    private MediaPlayerService Make()
    {
        var store = new ConfigStore(Path.Combine(_dir, "config.json"));
        var link = new DeviceLink(store, NullLogger<DeviceLink>.Instance, TimeProvider.System, _ => _dante);
        return new MediaPlayerService(_source, _media, new MediaPlayerPlugin(), new PluginSettings(store), new DeviceAlerts(link),
            NullLogger<MediaPlayerService>.Instance);
    }

    private static readonly WidgetDefinition Widget = new MediaPlayerWidget(null!).CreateDefault();

    private static byte[] Png()
    {
        using var img = new SKBitmap(300, 300);
        img.Erase(SKColors.Teal);
        using var png = img.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    [Fact]
    public async Task Nothing_playing_still_shows_the_volume()
    {
        var reading = await Make().ReadAsync(Widget, default);
        Assert.Equal("Nothing playing", reading.Text);
        Assert.False(reading.Media!.Playing);
        Assert.Equal(40, reading.Media.Volume);
        Assert.Equal(40, reading.Value);
    }

    [Fact]
    public async Task A_song_gives_title_artist_play_state_mute_and_art_made_once_per_picture()
    {
        _source.Now = new NowPlaying("Bohemian Rhapsody", "Queen", true, "spotify", ArtKey: "https://art/1");
        _source.Art = Png();
        _media.Volume = new VolumeState(70, true);
        var service = Make();

        var first = await service.ReadAsync(Widget, default);
        Assert.Equal("Bohemian Rhapsody", first.Text);
        Assert.Equal("Queen", first.Media!.Artist);
        Assert.True(first.Media.Playing);
        Assert.True(first.Media.Muted);
        Assert.NotNull(first.Media.Art);

        await service.ReadAsync(Widget, default);
        Assert.Equal(1, _source.ArtLoads);           // same picture: not loaded and shrunk again

        _source.Now = _source.Now with { ArtKey = "https://art/2" };
        await service.ReadAsync(Widget, default);
        Assert.Equal(2, _source.ArtLoads);           // new song, new picture
    }

    [Fact]
    public async Task A_problem_on_the_PC_shows_as_a_plain_message()
    {
        var service = new MediaPlayerService(new ThrowingSource(), _media, new MediaPlayerPlugin(),
            new PluginSettings(new ConfigStore(Path.Combine(_dir, "config.json"))), new DeviceAlerts(new DeviceLink(
                new ConfigStore(Path.Combine(_dir, "c2.json")), NullLogger<DeviceLink>.Instance, TimeProvider.System, _ => _dante)),
            NullLogger<MediaPlayerService>.Instance);
        var reading = await service.ReadAsync(Widget, default);
        Assert.Equal("Install playerctl", reading.Error);
    }

    private sealed class ThrowingSource : INowPlayingSource
    {
        public Task<NowPlaying?> GetAsync(string player, CancellationToken ct) => throw new ActionFailedException("Install playerctl");
        public Task<bool> ControlAsync(string? app, MediaCommand command, CancellationToken ct) => Task.FromResult(false);
    }

    private static DeviceEvent Press(string json) => DeviceEvent.TryParse(json)!;

    [Theory]
    [InlineData("""{"t":"btn","b":"sel","long":false,"scr":"m","ctl":"volu"}""", "vol+")]
    [InlineData("""{"t":"btn","b":"sel","long":false,"scr":"m","ctl":"vold"}""", "vol-")]
    [InlineData("""{"t":"btn","b":"sel","long":false,"scr":"m","ctl":"mute"}""", "mute")]
    public async Task Volume_and_mute_controls_go_to_the_speakers(string press, string expected)
    {
        var service = Make();
        Assert.True(service.Handle(Press(press), Widget));
        await service.LastCommand!;
        Assert.Equal(expected, Assert.Single(_media.Calls));
    }

    [Fact]
    public async Task Play_pause_goes_to_the_player_on_screen_and_falls_back_to_media_keys()
    {
        _source.Now = new NowPlaying("Song", "Band", true, "spotify");
        var service = Make();
        await service.ReadAsync(Widget, default);   // learns which player is on screen

        Assert.True(service.Handle(Press("""{"t":"btn","b":"sel","long":false,"scr":"m","ctl":"play"}"""), Widget));
        await service.LastCommand!;
        Assert.Equal(("spotify", MediaCommand.PlayPause), Assert.Single(_source.Commands));
        Assert.Empty(_media.Calls);

        _source.HandlesControls = false;   // e.g. Windows: no direct control, so the media keys
        Assert.True(service.Handle(Press("""{"t":"btn","b":"sel","long":false,"scr":"m","ctl":"prev"}"""), Widget));
        await service.LastCommand!;
        Assert.Equal("key:prev", Assert.Single(_media.Calls));
    }

    [Fact]
    public async Task With_buttons_SELECT_plays_and_a_long_SELECT_skips()
    {
        var service = Make();
        Assert.True(service.Handle(Press("""{"t":"btn","b":"sel","long":false,"scr":"m"}"""), Widget));
        await service.LastCommand!;
        Assert.True(service.Handle(Press("""{"t":"btn","b":"sel","long":true,"scr":"m"}"""), Widget));
        await service.LastCommand!;
        Assert.Equal([MediaCommand.PlayPause, MediaCommand.Next], _source.Commands.Select(c => c.Command));
    }

    [Fact]
    public void Presses_elsewhere_and_MAGIC_are_not_ours()
    {
        var service = Make();
        var other = new WidgetDefinition { Type = "days-until" };
        Assert.False(service.Handle(Press("""{"t":"btn","b":"sel","long":false,"scr":"x"}"""), other));
        Assert.False(service.Handle(Press("""{"t":"btn","b":"sel","long":false,"scr":"face"}"""), null));
        Assert.False(service.Handle(Press("""{"t":"btn","b":"magic","long":false,"scr":"m"}"""), Widget));
        Assert.False(service.Handle(Press("""{"t":"btn","b":"sel","long":false,"scr":"m","ctl":"selfdestruct"}"""), Widget));
    }

    [Fact]
    public async Task A_failing_command_is_explained_on_DANTE()
    {
        var service = new MediaPlayerService(new ThrowingControlSource(), _media, new MediaPlayerPlugin(),
            new PluginSettings(new ConfigStore(Path.Combine(_dir, "config.json"))), MakeAlerts(out var dante), NullLogger<MediaPlayerService>.Instance);
        Assert.True(service.Handle(Press("""{"t":"btn","b":"sel","long":false,"scr":"m","ctl":"next"}"""), Widget));
        await service.LastCommand!;
        for (var i = 0; i < 40 && !dante.Received.Any(m => m["t"]!.GetValue<string>() == "say"); i++) await Task.Delay(50);
        Assert.Contains(dante.Received, m => m["t"]!.GetValue<string>() == "say" && m["txt"]!.GetValue<string>().Contains("playerctl"));
    }

    private DeviceAlerts MakeAlerts(out FakeDante dante)
    {
        var fake = new FakeDante();
        dante = fake;
        var store = new ConfigStore(Path.Combine(_dir, "alerts.json"));
        var cfg = store.Snapshot();
        cfg.Device.PortName = "FAKE";
        store.Save(cfg);
        var link = new DeviceLink(store, NullLogger<DeviceLink>.Instance, TimeProvider.System, _ => fake);
        link.StartAsync(default).GetAwaiter().GetResult();
        for (var i = 0; i < 60 && link.Status != LinkStatus.Connected; i++) Thread.Sleep(50);
        return new DeviceAlerts(link);
    }

    private sealed class ThrowingControlSource : INowPlayingSource
    {
        public Task<NowPlaying?> GetAsync(string player, CancellationToken ct) => Task.FromResult<NowPlaying?>(null);
        public Task<bool> ControlAsync(string? app, MediaCommand command, CancellationToken ct) =>
            throw new ActionFailedException("Install playerctl for media controls");
    }
}
