using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Dante.Core.Config;
using Dante.Core.Device;
using Dante.Core.Engine;
using Dante.Core.Widgets;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dante.Tests;

/// <summary>Stands in for the ESP32: records what the PC sends and lets the test "press buttons".</summary>
public sealed class FakeDante : IDeviceTransport
{
    private readonly BlockingCollection<string> _toPc = new();
    public ConcurrentQueue<JsonObject> Received { get; } = new();
    public bool IsOpen { get; private set; }

    public void Open() => IsOpen = true;

    public string? ReadLine() => _toPc.TryTake(out var line, 100) ? line : null;

    public void WriteLine(string line)
    {
        var msg = (JsonObject)JsonNode.Parse(line)!;
        Received.Enqueue(msg);
        if (msg["t"]!.GetValue<string>() == "hello") _toPc.Add("""{"t":"hello","fw":"test","name":"DANTE","display":"oled","oled":false}""");
    }

    public void Press(string json) => _toPc.Add(json);

    public void Dispose() => IsOpen = false;
}

public sealed class RecordingOpener : IUrlOpener
{
    public List<string> Opened { get; } = [];
    public void Open(string url) => Opened.Add(url);
}

public class EngineIntegrationTests : IAsyncLifetime
{
    private readonly FakeDante _buddy = new();
    private readonly RecordingOpener _opener = new();
    private readonly PushStore _push = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dante-{Guid.NewGuid():N}");
    private ConfigStore _store = null!;
    private DeviceLink _link = null!;
    private DanteEngine _engine = null!;

    public async Task InitializeAsync()
    {
        _store = new ConfigStore(Path.Combine(_dir, "config.json"));
        _store.Save(new DanteConfig
        {
            Device = new DeviceSettings { PortName = "FAKE", AutoConnect = true },
            Widgets =
            [
                new WidgetDefinition
                {
                    Id = "builds", Name = "Broken builds", Visual = VisualKind.Counter, Type = PushSource.TypeName,
                    CountsTowardMood = true, OnSelectUrl = "{url}",
                    Takeover = new TakeoverRule { Mode = TakeoverMode.WhenPending, DelaySeconds = 0, Message = "{item} broke!" },
                },
            ],
        });

        var time = TimeProvider.System;
        var registry = new WidgetTypeRegistry([new PushSource(_push)]);
        _link = new DeviceLink(_store, NullLogger<DeviceLink>.Instance, time, _ => _buddy);
        _engine = new DanteEngine(_store, registry, _link, _opener, time, NullLogger<DanteEngine>.Instance, new DeviceAlerts(_link));
        _push.Pushed += _engine.PollNow;   // what PushFeature.Start wires up in the app
        await _link.StartAsync(default);
        await _engine.StartAsync(default);
    }

    public async Task DisposeAsync()
    {
        await _engine.StopAsync(default);
        await _link.StopAsync(default);
        Directory.Delete(_dir, recursive: true);
    }

    private async Task<JsonObject> WaitFor(Func<JsonObject, bool> predicate)
    {
        for (var i = 0; i < 100; i++)
        {
            var hit = _buddy.Received.FirstOrDefault(predicate);
            if (hit is not null) return hit;
            await Task.Delay(50);
        }
        throw new TimeoutException("Expected message never sent. Got: " + string.Join("\n", _buddy.Received.Select(m => m.ToJsonString())));
    }

    private static bool Is(JsonObject m, string type) => m["t"]!.GetValue<string>() == type;

    [Fact]
    public async Task Screen_settings_from_the_client_reach_the_buddy()
    {
        var initial = await WaitFor(m => Is(m, "cfg"));
        Assert.Equal("auto", initial["display"]!.GetValue<string>());
        Assert.False(initial["flip"]!.GetValue<bool>());
        Assert.Null(initial["screens"]);   // two-screen mode is gone
        for (var i = 0; i < 40 && _link.DeviceDisplay is null; i++) await Task.Delay(50);
        Assert.Equal("oled", _link.DeviceDisplay);
        Assert.False(_link.OledDetected);
        Assert.False(_link.TouchScreen);           // no "touch" in its hello: a board with buttons
        Assert.False(_link.DeviceTft);

        var cfg = _store.Snapshot();
        cfg.Device.Display = DisplayType.Tft35;
        cfg.Device.Flip = true;
        _store.Save(cfg);

        var updated = await WaitFor(m => Is(m, "cfg") && m["display"]!.GetValue<string>() == "tft");
        Assert.True(updated["flip"]!.GetValue<bool>());
        Assert.Equal(LinkStatus.Connected, _link.Status); // display changes don't drop the connection

        cfg.Device.Display = DisplayType.Oled128x32;
        _store.Save(cfg);
        await WaitFor(m => Is(m, "cfg") && m["display"]!.GetValue<string>() == "oled");
    }

    [Fact]
    public async Task Pushed_items_reach_the_buddy_change_its_mood_and_take_over()
    {
        await WaitFor(m => Is(m, "apps"));

        _push.Set("builds", new WidgetReading
        {
            Value = 1,
            Text = "main is red",
            Pending = [new PendingItem("b1", "Nightly", DateTimeOffset.UtcNow.AddMinutes(-40), "https://ci.example/b1")],
        });

        var val = await WaitFor(m => Is(m, "val") && m["v"]?.GetValue<double>() == 1);
        Assert.Equal("main is red", val["txt"]!.GetValue<string>());

        var alert = await WaitFor(m => Is(m, "alert"));
        Assert.Equal("Nightly broke!", alert["txt"]!.GetValue<string>());

        var mood = await WaitFor(m => Is(m, "mood") && m["n"]!.GetValue<int>() == 1);
        Assert.Equal("antsy", mood["m"]!.GetValue<string>()); // 40 min old > 30 min antsy threshold

        // Human presses SELECT on the builds screen -> PC opens the first item's link.
        _buddy.Press("""{"t":"btn","b":"sel","long":false,"scr":"builds"}""");
        for (var i = 0; i < 40 && _opener.Opened.Count == 0; i++) await Task.Delay(50);
        Assert.Equal("https://ci.example/b1", Assert.Single(_opener.Opened));

        // "I'm on it" ack, then the problem goes away -> no further alerts, mood relaxes.
        _buddy.Press("""{"t":"ack","id":"builds"}""");
        await Task.Delay(300);
        _push.Set("builds", new WidgetReading { Value = 0, Pending = [] });
        await WaitFor(m => Is(m, "mood") && m["m"]!.GetValue<string>() == "happy" && m["n"]!.GetValue<int>() == 0);
        Assert.Single(_buddy.Received, m => Is(m, "alert"));
    }
}
