using System.Runtime.InteropServices;

namespace JukkasDell;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct FileTime { public uint Low, High; public ulong Value => ((ulong)High << 32) | Low; }
    [StructLayout(LayoutKind.Sequential)] internal struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtended;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct PowerStatus
    {
        public byte Ac, Flags, Percent, Saver;
        public uint LifeTime, FullLifeTime;
    }
    // BOOLEAN is one byte; SYSTEM_BATTERY_STATE is 32 bytes on x64 and x86.
    [StructLayout(LayoutKind.Sequential)] internal struct BatteryState
    {
        public byte Ac, Present, Charging, Discharging, Spare1, Spare2, Spare3, Tag;
        public uint Max, Remaining, Rate, Estimated, Alert1, Alert2;
    }
    [DllImport("powrprof.dll")]
    private static extern uint CallNtPowerInformation(int level, IntPtr input, uint inputSize, out BatteryState output, uint outputSize);
    internal static BatteryTelemetry? ReadBattery()
    {
        // Query-only SystemBatteryState, with no input buffer: no policy changes.
        if (CallNtPowerInformation(5, IntPtr.Zero, 0, out var state, (uint)Marshal.SizeOf<BatteryState>()) != 0 || state.Present == 0)
            return null;
        return new(Metrics.SignedBatteryWatts(state.Rate, state.Charging != 0, state.Discharging != 0),
            state.Remaining == uint.MaxValue ? null : state.Remaining / 1000d,
            state.Ac != 0, state.Charging != 0, state.Discharging != 0);
    }
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetSystemPowerStatus(out PowerStatus status);
    [DllImport("kernel32.dll")] internal static extern ulong GetTickCount64();
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root, IntPtr scheme, IntPtr sub, IntPtr setting, IntPtr buffer, ref uint size);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    internal static string? ActivePowerScheme()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var scheme) != 0) return null;
        try
        {
            uint size = 0;
            PowerReadFriendlyName(IntPtr.Zero, scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
            if (size == 0 || size > 4096) return null;
            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                return PowerReadFriendlyName(IntPtr.Zero, scheme, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == 0
                    ? Marshal.PtrToStringUni(buffer) : null;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { LocalFree(scheme); }
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
    internal static readonly uint ShowMessage = RegisterWindowMessage("JukkasDell.ShowWindow.v1");
    internal static readonly IntPtr HWND_BROADCAST = new(0xffff);
}

public sealed class WorkloadSampler
{
    private ulong _idle, _total;
    private long _stamp;
    private Dictionary<int, (long Start, double Cpu)> _processes = [];
    public void Reset() { _total = 0; _idle = 0; _stamp = 0; _processes.Clear(); }
    public Workload Read()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = _stamp == 0 ? 0 : (double)(now - _stamp) / Stopwatch.Frequency;
        double? cpu = null;
        if (Native.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var total = kernel.Value + user.Value;
            if (_total != 0) cpu = Metrics.Cpu(_idle, _total, idle.Value, total);
            _idle = idle.Value; _total = total;
        }
        var mem = new Native.MemoryStatus { Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MemoryStatus>() };
        if (!Native.GlobalMemoryStatusEx(ref mem)) throw new InvalidOperationException("Memory status unavailable.");
        var hasPower = Native.GetSystemPowerStatus(out var power);
        var rows = new List<ProcessLoad>();
        var next = new Dictionary<int, (long, double)>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == 0) continue;
                    var start = process.StartTime.ToUniversalTime().Ticks;
                    var seconds = process.TotalProcessorTime.TotalSeconds;
                    next[process.Id] = (start, seconds);
                    if (elapsed > 0 && _processes.TryGetValue(process.Id, out var old) && old.Start == start)
                    {
                        double load = Math.Clamp(100 * (seconds - old.Cpu) / elapsed / Environment.ProcessorCount, 0, 100);
                        rows.Add(new(process.ProcessName, process.Id, load, process.WorkingSet64 / 1048576d));
                    }
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
        _processes = next; _stamp = now;
        return new(cpu, (mem.TotalPhys - mem.AvailPhys) / 1073741824d, mem.TotalPhys / 1073741824d,
            hasPower && power.Percent <= 100 ? power.Percent : null,
            hasPower && power.Ac != 255 ? power.Ac == 1 : null, hasPower && (power.Flags & 8) != 0 && power.Flags != 255,
            TimeSpan.FromMilliseconds(Native.GetTickCount64()), rows.OrderByDescending(p => p.Cpu).Take(5).ToArray(), Native.ReadBattery());
    }
}
