using System.Text.Json;

namespace JukkasDell;

public sealed record FirmwareData(string? Model, string? Bios, string? Windows,
    string? Profile, string[] Profiles, bool Writable, string? WindowsScheme);
public sealed record BatteryData(double? DesignWh, double? FullWh, int? Cycles,
    double? ChargeWatts, double? DischargeWatts, string? Mode, int? Start, int? Stop, string[]? Modes = null, bool ModeWritable = false)
{
    public double? Retention => Metrics.Retention(FullWh, DesignWh);
}
public sealed record Snapshot(DateTimeOffset CapturedAt, bool Elevated,
    FirmwareData? Firmware, BatteryData? Battery, Dictionary<string, string> Errors);
public sealed record ElevatedReply(int Status, Snapshot Snapshot);
public sealed record ProcessLoad(string Name, int Pid, double Cpu, double RamMiB);
public sealed record BatteryTelemetry(double? SignedWatts, double? RemainingWh, bool AcOnline, bool Charging, bool Discharging);
public sealed record Workload(double? Cpu, double UsedGiB, double TotalGiB,
    int? BatteryPercent, bool? AcOnline, bool Charging, TimeSpan Uptime, ProcessLoad[] Processes, BatteryTelemetry? Power = null);

public static class Metrics
{
    public static readonly string[] ThermalProfiles = ["Optimized", "Quiet", "Cool", "UltraPerformance"];
    public static readonly string[] ChargingModes = ["Adaptive", "Standard", "Express", "PrimAcUse"];
    public static bool ValidChargingMode(string? value) => ChargingModes.Contains(value, StringComparer.Ordinal);
    public static bool ValidProfile(string? value) => ThermalProfiles.Contains(value, StringComparer.Ordinal);
    public static double? Retention(double? full, double? design) =>
        full is > 0 && design is > 0 ? 100 * full / design : null;
    public static double? Cpu(ulong idleBefore, ulong totalBefore, ulong idleAfter, ulong totalAfter)
    {
        if (totalAfter <= totalBefore || idleAfter < idleBefore) return null;
        return Math.Clamp(100d * (1 - (double)(idleAfter - idleBefore) / (totalAfter - totalBefore)), 0, 100);
    }
    public static double? MilliUnits(object? raw)
    {
        if (raw is null) return null;
        var value = Convert.ToDouble(raw);
        return value <= 0 || value >= uint.MaxValue ? null : value / 1000;
    }
    public static double? SignedBatteryWatts(uint raw, bool charging, bool discharging)
    {
        if (raw is uint.MaxValue or 0x80000000) return null;
        int signed = unchecked((int)raw);
        if (signed > 0 && charging && !discharging || signed < 0 && discharging && !charging)
            return signed / 1000d;
        return signed == 0 ? 0 : null;
    }
    public static double? RuntimeHours(double? remainingWh, double? drainWatts) =>
        remainingWh is >= 0 && drainWatts is >= .1 && double.IsFinite(remainingWh.Value) && double.IsFinite(drainWatts.Value)
            ? remainingWh / drainWatts : null;
    public static string RuntimeLabel(double? hours) => hours is not { } h ? "—" : h >= 100 ? "~100h+" :
        h < 1d / 60 ? "<1m" : h < 1 ? $"~{(int)(h * 60)}m" : $"~{(int)h}h {(int)(h * 60) % 60:00}m";
    public static string ProfileLabel(string? profile) => profile == "UltraPerformance" ? "Ultra performance" : profile ?? "Unavailable";
    public static string ModeLabel(string? mode) => mode switch
    {
        "PrimAcUse" => "Primarily AC", "Express" => "Express charge", null => "Unavailable", _ => mode
    };
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
}
