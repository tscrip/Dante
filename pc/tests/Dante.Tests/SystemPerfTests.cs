using Dante.Core.Config;
using Dante.Core.Device;
using Dante.Core.Widgets;
using Dante.Server.Features.Command;
using Dante.Server.Features.SystemPerf;

namespace Dante.Tests;

public class SystemPerfTests
{
    /// <summary>Hands out queued CPU samples and a fixed memory reading.</summary>
    private sealed class FakeStats : ISystemStats
    {
        public Queue<CpuTimes> Cpu { get; } = new();
        public MemoryUse? Memory { get; set; } = new(6UL << 30, 16UL << 30);
        public string Platform => "Test OS";
        public CpuTimes? ReadCpu() => Cpu.Count > 0 ? Cpu.Dequeue() : null;
        public MemoryUse? ReadMemory() => Memory;
    }

    private static SystemPerfSource Source(FakeStats stats) => new(stats) { FirstSampleGap = TimeSpan.Zero };

    private static WidgetDefinition Widget(string show = SystemPerfSource.Both, string smoothing = "1") =>
        new() { Id = "perf", Type = SystemPerfSource.TypeName, Settings = new() { ["show"] = show, ["smoothing"] = smoothing } };

    [Fact]
    public void Linux_counters_are_parsed()
    {
        var cpu = LinuxSystemStats.ParseStat("cpu  100 5 50 800 40 3 2 0 0 0\ncpu0 1 2 3 4\n")!.Value;
        Assert.Equal(1000UL, cpu.Total);
        Assert.Equal(160UL, cpu.Busy);   // everything but idle (800) and iowait (40)

        var mem = LinuxSystemStats.ParseMeminfo("MemTotal:       16000000 kB\nMemFree:         1000000 kB\nMemAvailable:    4000000 kB\n")!.Value;
        Assert.Equal(16000000UL * 1024, mem.Total);
        Assert.Equal(75, mem.Percent, 3);

        // Old kernels: no MemAvailable, so free + buffers + cache.
        var old = LinuxSystemStats.ParseMeminfo("MemTotal: 1000 kB\nMemFree: 100 kB\nBuffers: 50 kB\nCached: 250 kB\n")!.Value;
        Assert.Equal(60, old.Percent, 3);
        Assert.Null(LinuxSystemStats.ParseStat("nothing here"));
    }

    [Fact]
    public void Cpu_percent_is_the_busy_share_between_two_samples()
    {
        Assert.Equal(25, SystemPerfSource.Percent(new CpuTimes(100, 1000), new CpuTimes(150, 1200)));
        Assert.Equal(0, SystemPerfSource.Percent(new CpuTimes(100, 1000), new CpuTimes(100, 1000)));   // no time passed
    }

    [Fact]
    public async Task Reads_cpu_and_memory_as_two_values()
    {
        var stats = new FakeStats();
        stats.Cpu.Enqueue(new CpuTimes(0, 0));          // first poll: two samples a moment apart
        stats.Cpu.Enqueue(new CpuTimes(40, 100));
        stats.Cpu.Enqueue(new CpuTimes(50, 200));       // second poll: since the last one
        var source = Source(stats);

        var first = await source.ReadAsync(Widget(), default);
        Assert.Null(first.Error);
        Assert.Equal(40, first.Value);
        Assert.Equal(37.5, first.Value2);
        Assert.Equal("CPU 40% - RAM 6.0/16.0 GB", first.Text);

        var second = await source.ReadAsync(Widget(), default);
        Assert.Equal(10, second.Value);
        Assert.Equal(37.5, second.LevelValue);          // levels look at whichever is higher
    }

    [Fact]
    public async Task One_series_goes_in_the_first_value()
    {
        var stats = new FakeStats();
        var memOnly = await Source(stats).ReadAsync(Widget(SystemPerfSource.MemoryOnly), default);
        Assert.Equal(37.5, memOnly.Value);
        Assert.Null(memOnly.Value2);
        Assert.Equal("RAM 6.0 of 16.0 GB", memOnly.Text);

        stats.Cpu.Enqueue(new CpuTimes(0, 0));
        stats.Cpu.Enqueue(new CpuTimes(30, 100));
        var cpuOnly = await Source(stats).ReadAsync(Widget(SystemPerfSource.CpuOnly), default);
        Assert.Equal(30, cpuOnly.Value);
        Assert.Null(cpuOnly.Value2);
    }

    [Fact]
    public async Task Smoothing_averages_the_last_samples()
    {
        var stats = new FakeStats();
        foreach (var (busy, total) in new[] { (0UL, 0UL), (80UL, 100UL), (80UL, 200UL), (180UL, 300UL) }) stats.Cpu.Enqueue(new CpuTimes(busy, total));
        var source = Source(stats);
        Assert.Equal(80, (await source.ReadAsync(Widget(smoothing: "2"), default)).Value);
        Assert.Equal(40, (await source.ReadAsync(Widget(smoothing: "2"), default)).Value);   // (80 + 0) / 2
        Assert.Equal(50, (await source.ReadAsync(Widget(smoothing: "2"), default)).Value);   // (0 + 100) / 2
    }

    [Fact]
    public async Task An_os_it_cant_read_is_an_error_not_a_crash()
    {
        var reading = await Source(new FakeStats { Memory = null }).ReadAsync(Widget(), default);
        Assert.Equal("Can't read the CPU use on Test OS.", reading.Error);
    }

    [Fact]
    public async Task This_machine_gives_sensible_numbers()
    {
        var stats = ISystemStats.ForThisOs();
        if (stats is UnsupportedSystemStats) return;
        var reading = await new SystemPerfSource(stats).ReadAsync(Widget(), default);
        Assert.Null(reading.Error);
        Assert.InRange(reading.Value!.Value, 0, 100);
        Assert.InRange(reading.Value2!.Value, 0.1, 100);
    }

    [Fact]
    public void The_default_widget_is_a_dual_sparkline_sampling_every_two_seconds()
    {
        var w = new SystemPerfSource(new FakeStats()).CreateDefault();
        Assert.Equal((VisualKind.DualSparkline, "CPU", "MEM", 2, "%"), (w.Visual, w.Label1, w.Label2, w.PollSeconds, w.Unit));
    }

    [Fact]
    public void Dual_sparklines_reach_dante_with_labels_and_a_second_value()
    {
        var apps = DeviceProtocol.Widgets([new SystemPerfSource(new FakeStats()).CreateDefault() is var w ? Configure(w) : null!]);
        Assert.Equal("""{"t":"apps","apps":[{"id":"perf","name":"CPU \u0026 RAM","vis":"spark2","min":0,"max":100,"unit":"%","l1":"CPU","l2":"MEM"}]}""", apps.ToJsonString());

        var val = DeviceProtocol.Value("perf", 12.346, "x", 0, null, value2: 61.2);
        Assert.Equal("""{"t":"val","id":"perf","lvl":0,"v":12.35,"v2":61.2,"txt":"x"}""", val.ToJsonString());

        // Other visuals carry no labels; blank labels become A / B.
        Assert.Null(DeviceProtocol.Widgets([new WidgetDefinition { Id = "a", Visual = VisualKind.Sparkline, Label1 = "CPU" }])["apps"]![0]!["l1"]);
        var blank = DeviceProtocol.Widgets([new WidgetDefinition { Id = "a", Visual = VisualKind.DualSparkline }])["apps"]![0]!;
        Assert.Equal(("A", "B"), (blank["l1"]!.GetValue<string>(), blank["l2"]!.GetValue<string>()));

        static WidgetDefinition Configure(WidgetDefinition w) { w.Id = "perf"; return w; }
    }

    [Fact]
    public void A_command_can_feed_a_dual_sparkline()
    {
        var r = CommandSource.ParseOutput("""{"value":12,"value2":34,"text":"hi"}""");
        Assert.Equal((12.0, 34.0, "hi"), (r.Value!.Value, r.Value2!.Value, r.Text!));
    }
}
