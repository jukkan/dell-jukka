using System.Text;

namespace JukkasDell;

public static class DiagnosticSummary
{
    public static string Create(Snapshot? snapshot, Workload? workload, DateTimeOffset? workloadAt, string? error, DateTimeOffset? dellAt = null, string? workloadError = null, bool dellNeedsRefresh = false, ActivityHistory? activity = null)
    {
        var s = new StringBuilder("Jukka’s Dell 0.2.0 — diagnostic summary\n");
        s.AppendLine($"Exported {DateTimeOffset.Now:O}");
        if (dellAt is { } at) s.AppendLine($"Dell settings last observed: {at:O}. Other readings may have a different timestamp.");
        if (dellNeedsRefresh) s.AppendLine("Dell settings need another read after resume or an unsuccessful helper operation; cached values may be out of date.");
        if (workloadError is not null) s.AppendLine("Latest workload refresh: " + workloadError);
        if (snapshot is { } d)
        {
            var f = d.Firmware; var b = d.Battery;
            s.AppendLine($"Device reading: {d.CapturedAt:O} ({(DateTimeOffset.Now - d.CapturedAt).TotalSeconds:F0}s old)");
            s.AppendLine($"Model: {f?.Model ?? "Unavailable"}; BIOS: {f?.Bios ?? "Unavailable"}; {f?.Windows}");
            s.AppendLine($"Dell profile: {Metrics.ProfileLabel(f?.Profile)}; Windows power scheme: {f?.WindowsScheme ?? "Unavailable"}");
            s.AppendLine($"Charging mode: {Metrics.ModeLabel(b?.Mode)}; capacity: {b?.FullWh?.ToString("F2") ?? "Unavailable"} / {b?.DesignWh?.ToString("F2") ?? "Unavailable"} Wh");
            s.AppendLine($"Estimated capacity retention: {b?.Retention?.ToString("F1") ?? "Unavailable"}%; cycles: {b?.Cycles?.ToString() ?? "Unavailable"}");
            s.AppendLine($"Stored charging limits: {b?.Start?.ToString() ?? "Unavailable"}–{b?.Stop?.ToString() ?? "Unavailable"}% ({(b?.Mode == "Custom" ? "Custom mode selected" : "inactive")})");
            foreach (var e in d.Errors) s.AppendLine($"Source unavailable — {e.Key}: {e.Value}");
        }
        else s.AppendLine("Device reading unavailable.");
        if (error is not null) s.AppendLine("Latest refresh: " + error);
        if (workload is { } w)
        {
            s.AppendLine($"Workload reading: {workloadAt:O}; CPU {w.Cpu?.ToString("F1") ?? "sampling/unavailable"}%; memory {w.UsedGiB:F1}/{w.TotalGiB:F1} GiB");
            s.AppendLine($"Charge: {w.BatteryPercent?.ToString() ?? "Unavailable"}%; AC online: {w.AcOnline?.ToString() ?? "Unavailable"}; uptime: {w.Uptime:d\\.hh\\:mm}");
            if (w.Power is { } power)
            {
                var estimate = activity?.Estimate(workloadAt ?? DateTimeOffset.Now, power);
                s.AppendLine($"Battery power: {power.SignedWatts?.ToString("F2") ?? "Unavailable"} W (+ charge / - drain); remaining: {power.RemainingWh?.ToString("F2") ?? "Unavailable"} Wh");
                s.AppendLine($"Recent average drain: {estimate?.AverageDrainWatts?.ToString("F2") ?? "Unavailable"} W; estimated runtime: {Metrics.RuntimeLabel(estimate?.RuntimeHours)} (recent drain; not a guarantee)");
            }
            else s.AppendLine("Native battery power/capacity unavailable.");
            s.AppendLine("Top accessible processes (sampled CPU share):");
            foreach (var p in w.Processes) s.AppendLine($"  {p.Name}: {p.Cpu:F1}% CPU, {p.RamMiB:F0} MiB");
        }
        if (activity is not null)
        {
            s.AppendLine($"Activity history: {activity.Points.Count} samples retained in memory, up to 15 minutes; not sampled while hidden.");
            foreach (var ev in activity.Events.TakeLast(12)) s.AppendLine($"  {ev.At:O}: {ev.Label}");
        }
        s.AppendLine("CPU temperature and fan RPM unavailable. GPU/SSD integration deferred. Capacity is a firmware estimate, not a hardware diagnosis.");
        return s.ToString();
    }
}
