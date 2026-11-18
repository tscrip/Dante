using System.Collections.Concurrent;
using System.Globalization;
using Dante.Core.Config;
using Dante.Core.Widgets;

namespace Dante.Server.Features.SystemPerf;

/// <summary>
/// CPU and memory use as one reading: <c>Value</c> = CPU %, <c>Value2</c> = memory %, drawn by DANTE as two
/// sparklines. CPU % is the busy share of CPU time between two polls, so it is the average over the poll interval.
/// </summary>
public sealed class SystemPerfSource(ISystemStats stats) : IWidgetType
{
    public const string TypeName = "system-perf";
    public const string Both = "CPU and memory", CpuOnly = "CPU only", MemoryOnly = "Memory only";

    public string Type => TypeName;
    public string PluginId => SystemPerfPlugin.PluginId;
    public string DisplayName => "CPU & memory";
    public string Description =>
        "CPU and memory use of this PC, in %. DANTE draws the last 32 samples, so the graphs cover 32 x \"Check every\" " +
        "(2 s = about a minute). The warn / critical levels look at whichever is higher.";

    public IReadOnlyList<SettingField> Fields { get; } =
    [
        new("show", "Show", SettingKind.Choice, Both, Choices: [Both, CpuOnly, MemoryOnly],
            Help: "With one of them, pick the Sparkline or Gauge visual."),
        new("smoothing", "Smooth over (samples)", SettingKind.Number, "1",
            Help: "Averages the last few samples so short spikes don't jump around. 1 = off."),
    ];

    /// <summary>The first CPU sample needs a second one this much later to make a percentage.</summary>
    public TimeSpan FirstSampleGap { get; init; } = TimeSpan.FromMilliseconds(250);

    private sealed class State
    {
        public CpuTimes? LastCpu;
        public readonly Queue<double> Cpu = new(), Memory = new();
    }

    private readonly ConcurrentDictionary<string, State> _states = new();

    public WidgetDefinition CreateDefault() => new()
    {
        Name = "CPU & RAM",
        Type = TypeName,
        Visual = VisualKind.DualSparkline,
        Min = 0,
        Max = 100,
        Unit = "%",
        Label1 = "CPU",
        Label2 = "MEM",
        PollSeconds = 2,
        QuipsEnabled = false,
        Levels = new LevelRule { WarnAt = 85, CritAt = 95 },
        Settings = new() { ["show"] = Both, ["smoothing"] = "1" },
    };

    public async Task<WidgetReading> ReadAsync(WidgetDefinition widget, CancellationToken ct)
    {
        var show = widget.Get(this, "show");
        var wantCpu = show != MemoryOnly;
        var wantMemory = show != CpuOnly;
        var window = Math.Clamp(int.TryParse(widget.Get(this, "smoothing"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 1, 1, 30);
        var state = _states.GetOrAdd(string.IsNullOrEmpty(widget.Id) ? "(test)" : widget.Id, _ => new State());

        double? cpu = null;
        if (wantCpu)
        {
            var now = stats.ReadCpu();
            if (now is null) return WidgetReading.Fail($"Can't read the CPU use on {stats.Platform}.");
            CpuTimes? before;
            lock (state) before = state.LastCpu;
            if (before is null || now.Value.Total <= before.Value.Total)
            {
                before = now;
                await Task.Delay(FirstSampleGap, ct);
                now = stats.ReadCpu() ?? now;
            }
            lock (state) state.LastCpu = now;
            cpu = Percent(before.Value, now.Value);
        }

        MemoryUse? memory = null;
        if (wantMemory)
        {
            memory = stats.ReadMemory();
            if (memory is null) return WidgetReading.Fail($"Can't read the memory use on {stats.Platform}.");
        }

        double? cpuShown, memShown;
        lock (state)
        {
            cpuShown = cpu is { } c ? Smooth(state.Cpu, c, window) : null;
            memShown = memory is { } m ? Smooth(state.Memory, m.Percent, window) : null;
        }

        var text = (cpuShown, memory) switch
        {
            ({ } c, { } m) => $"CPU {c:0}% - RAM {Gb(m.Used)}/{Gb(m.Total)} GB",
            ({ } c, null) => $"CPU {c:0}%",
            (null, { } m) => $"RAM {Gb(m.Used)} of {Gb(m.Total)} GB",
            _ => "",
        };
        // One series: it goes in Value so single-line visuals (sparkline, gauge, bar) show it.
        return wantCpu
            ? new WidgetReading { Value = Round(cpuShown), Value2 = Round(memShown), Text = text }
            : new WidgetReading { Value = Round(memShown), Text = text };
    }

    /// <summary>Busy share of the CPU time that passed between two samples.</summary>
    public static double Percent(CpuTimes before, CpuTimes after)
    {
        var total = after.Total - before.Total;
        if (after.Total <= before.Total) return 0;
        var busy = after.Busy >= before.Busy ? after.Busy - before.Busy : 0;
        return Math.Clamp(busy * 100.0 / total, 0, 100);
    }

    private static double Smooth(Queue<double> recent, double value, int window)
    {
        recent.Enqueue(value);
        while (recent.Count > window) recent.Dequeue();
        return recent.Average();
    }

    private static double? Round(double? v) => v is { } x ? Math.Round(x, 1) : null;
    private static string Gb(ulong bytes) => (bytes / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture);
}
