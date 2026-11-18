using System.Globalization;
using System.Runtime.InteropServices;

namespace Dante.Server.Features.SystemPerf;

/// <summary>CPU time counters since boot: busy and total, in the OS's own units. Percentages come from two samples.</summary>
public readonly record struct CpuTimes(ulong Busy, ulong Total);

/// <summary>Physical memory in bytes. "Used" leaves out caches the OS hands back as soon as a program needs them.</summary>
public readonly record struct MemoryUse(ulong Used, ulong Total)
{
    public double Percent => Total == 0 ? 0 : Used * 100.0 / Total;
}

/// <summary>Reads CPU and memory counters. One implementation per OS; the plugin itself doesn't care which.</summary>
public interface ISystemStats
{
    /// <summary>The OS name shown on the plugin page ("Linux", "Windows", "macOS").</summary>
    string Platform { get; }
    CpuTimes? ReadCpu();
    MemoryUse? ReadMemory();

    static ISystemStats ForThisOs() =>
        OperatingSystem.IsWindows() ? new WindowsSystemStats()
        : OperatingSystem.IsMacOS() ? new MacSystemStats()
        : OperatingSystem.IsLinux() ? new LinuxSystemStats()
        : new UnsupportedSystemStats();
}

/// <summary>Linux (and WSL): /proc/stat and /proc/meminfo.</summary>
public sealed class LinuxSystemStats(string procStat = "/proc/stat", string procMeminfo = "/proc/meminfo") : ISystemStats
{
    public string Platform => "Linux";

    public CpuTimes? ReadCpu() => File.Exists(procStat) ? ParseStat(File.ReadAllText(procStat)) : null;
    public MemoryUse? ReadMemory() => File.Exists(procMeminfo) ? ParseMeminfo(File.ReadAllText(procMeminfo)) : null;

    /// <summary>The first line: "cpu user nice system idle iowait irq softirq steal ...". Idle and iowait aren't busy.</summary>
    public static CpuTimes? ParseStat(string text)
    {
        var line = text.Split('\n').FirstOrDefault(l => l.StartsWith("cpu ", StringComparison.Ordinal));
        if (line is null) return null;
        var n = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Take(8)
            .Select(s => ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0).ToArray();
        if (n.Length < 4) return null;
        ulong total = 0;
        foreach (var v in n) total += v;
        var idle = n[3] + (n.Length > 4 ? n[4] : 0);
        return new CpuTimes(total - idle, total);
    }

    /// <summary>Used = MemTotal - MemAvailable (what free, top and the system monitor call "used").</summary>
    public static MemoryUse? ParseMeminfo(string text)
    {
        ulong? Kb(string key)
        {
            var line = text.Split('\n').FirstOrDefault(l => l.StartsWith(key + ":", StringComparison.Ordinal));
            if (line is null) return null;
            var num = line[(key.Length + 1)..].Trim().Split(' ')[0];
            return ulong.TryParse(num, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v * 1024 : null;
        }
        var total = Kb("MemTotal");
        if (total is not { } t || t == 0) return null;
        // Kernels before 3.14 have no MemAvailable: free + buffers + page cache is close enough.
        var available = Kb("MemAvailable") ?? (Kb("MemFree") ?? 0) + (Kb("Buffers") ?? 0) + (Kb("Cached") ?? 0);
        return new MemoryUse(t - Math.Min(available, t), t);
    }
}

/// <summary>Windows: GetSystemTimes and GlobalMemoryStatusEx (the numbers Task Manager shows).</summary>
public sealed class WindowsSystemStats : ISystemStats
{
    public string Platform => "Windows";

    public CpuTimes? ReadCpu()
    {
        if (!OperatingSystem.IsWindows() || !GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        var total = kernel + user;              // kernel time includes idle time
        return new CpuTimes(total - idle, total);
    }

    public MemoryUse? ReadMemory()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0) return null;
        return new MemoryUse(status.TotalPhys - status.AvailPhys, status.TotalPhys);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}

/// <summary>
/// macOS: the Mach host statistics. Memory "used" is what Activity Monitor shows: app memory
/// (internal pages minus purgeable) + wired + compressed.
/// </summary>
public sealed class MacSystemStats : ISystemStats
{
    public string Platform => "macOS";

    private const int HostCpuLoadInfo = 3, HostVmInfo64 = 4;
    private const int CpuStateUser = 0, CpuStateSystem = 1, CpuStateIdle = 2, CpuStateNice = 3;

    public CpuTimes? ReadCpu()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        var ticks = new uint[4];
        var count = 4;
        if (host_statistics(mach_host_self(), HostCpuLoadInfo, ticks, ref count) != 0) return null;
        ulong busy = (ulong)ticks[CpuStateUser] + ticks[CpuStateSystem] + ticks[CpuStateNice];
        return new CpuTimes(busy, busy + ticks[CpuStateIdle]);
    }

    public MemoryUse? ReadMemory()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        ulong total = 0;
        var size = (nint)sizeof(ulong);
        if (sysctlbyname("hw.memsize", ref total, ref size, 0, 0) != 0 || total == 0) return null;
        var host = mach_host_self();
        if (host_page_size(host, out var pageSize) != 0) return null;

        // struct vm_statistics64: 38 natural_t (152 bytes). Read the fields we need by byte offset.
        var info = new uint[38];
        var count = info.Length;
        if (host_statistics64(host, HostVmInfo64, info, ref count) != 0) return null;
        ulong At(int byteOffset) => info[byteOffset / 4];
        ulong wired = At(12), purgeable = At(88), compressed = At(128), internalPages = At(140);
        var used = (internalPages - Math.Min(purgeable, internalPages) + wired + compressed) * (ulong)pageSize;
        return new MemoryUse(Math.Min(used, total), total);
    }

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern uint mach_host_self();

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern int host_statistics(uint host, int flavor, [Out] uint[] info, ref int count);

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern int host_statistics64(uint host, int flavor, [Out] uint[] info, ref int count);

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern int host_page_size(uint host, out nuint pageSize);

    [DllImport("/usr/lib/libSystem.dylib", CharSet = CharSet.Ansi, BestFitMapping = false)]
    private static extern int sysctlbyname([MarshalAs(UnmanagedType.LPUTF8Str)] string name, ref ulong oldp, ref nint oldlenp, nint newp, nint newlen);
}

public sealed class UnsupportedSystemStats : ISystemStats
{
    public string Platform => RuntimeInformation.OSDescription;
    public CpuTimes? ReadCpu() => null;
    public MemoryUse? ReadMemory() => null;
}
