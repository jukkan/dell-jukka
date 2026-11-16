using System.Text.Json;

namespace JukkasDell;

internal static class SelfTests
{
    public static int Run()
    {
        int count = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            count++;
        }
        try
        {
            Check(Metrics.ThermalProfiles.All(Metrics.ValidProfile), "Four known thermal profiles");
            foreach (var invalid in new[] { "", "optimized", "Optimized --shell", "AdminPwd", "UltraPerformance;whoami", "Factory" })
                Check(DellProvider.SetProfile(invalid) == 64, "Reject unrecognized command before touching WMI");
            Check(Metrics.Retention(67.454, 84.290) is > 80 and < 80.1, "Capacity ratio");
            Check(Metrics.Retention(1, 0) is null && Metrics.Retention(null, 1) is null, "Missing capacity is not zero health");
            Check(Metrics.MilliUnits(uint.MaxValue) is null && Metrics.MilliUnits(0) is null, "Unavailable battery sentinel");
            Check(Metrics.Cpu(100, 1000, 300, 1400) == 50, "CPU includes idle time within kernel time");
            Check(Metrics.Cpu(100, 1000, 100, 1000) is null && Metrics.Cpu(100, 1000, 90, 1400) is null, "Reset CPU sample");
            var fixture = new Snapshot(DateTimeOffset.Now, false,
                new("XPS fixture", "1.0", "Windows fixture", "Optimized", ["Optimized"], false, "Balanced"),
                new(null, null, 0, null, null, "Adaptive", 50, 90), new() { ["Battery"] = "Access required" });
            var roundtrip = JsonSerializer.Deserialize<Snapshot>(JsonSerializer.Serialize(fixture));
            Check(roundtrip?.Battery is { Retention: null, Cycles: 0 }, "JSON preserves null and real zero");
            var summary = DiagnosticSummary.Create(roundtrip, null, null, "Test stale source");
            Check(summary.Contains("inactive") && summary.Contains("Unavailable") && summary.Contains("Test stale source"), "Summary marks unavailable and stale data");
            Check(!summary.Contains("C:\\") && !summary.Contains("InstanceName"), "Export excludes paths and raw device identifiers");
            Check(ElevatedChannel.ValidNonce(Guid.NewGuid().ToString("N")) && !ElevatedChannel.ValidNonce("pipe\\arbitrary"), "Pipe identifier is a strict nonce");
            Check(ElevatedChannel.RunHelper("AdminPwd", Guid.NewGuid().ToString("N")) == 64, "Elevated channel rejects arbitrary settings before WMI");
            Check(ElevatedChannel.StatusText(25, null).Contains("timed out"), "Timeouts are not success");
            var workload = new Workload(25, 12, 32, 96, true, false, TimeSpan.FromHours(27), [new("fixture", 1, 2, 100)]);
            Check(DiagnosticSummary.Create(fixture, workload, DateTimeOffset.Now, null, fixture.CapturedAt).Contains("Dell settings last observed"), "Mixed readings have separate timestamps");
            var session = new MonitoringSession();
            var beforeHide = session.Generation;
            Check(session.Accepts(beforeHide), "Accept samples from the current visible session");
            session.Invalidate(); // hidden
            session.Invalidate(); // reopened
            Check(!session.Accepts(beforeHide), "Reject an in-flight sample after hide and reopen");
            var beforeResume = session.Generation;
            session.Invalidate();
            Check(!session.Accepts(beforeResume) && session.Accepts(session.Generation), "Resume rejects pre-sleep work but accepts new samples");
            Check(DellObservation.Describe(null, false).Contains("not been read"), "Unread settings are explicit");
            Check(DellObservation.Describe(fixture, true).Contains("read Dell settings again"), "Resume and uncertain helper results ask for a new read");
            Check(DellObservation.Describe(fixture, false).Contains("Last observed: Optimized"), "Cached profile is an observation rather than a live-state claim");
            Check(DellObservation.Describe(fixture with { Firmware = null }, false).Contains("unavailable"), "Missing profile is not replaced with a default");
            var staleSummary = DiagnosticSummary.Create(fixture, workload, DateTimeOffset.Now, null, fixture.CapturedAt, "Workload refresh failed", true);
            Check(staleSummary.Contains("Workload refresh failed") && staleSummary.Contains("cached values may be out of date"), "Export preserves workload failure and uncertain Dell state");
            foreach (var invalid in new[] { "", "Custom", "adaptive", "Standard --shell", "AdminPwd", "ThermalManagement" })
            {
                Check(DellProvider.SetChargingMode(invalid) == 64, "Reject unsafe/unsupported charging mode before WMI");
                Check(ElevatedChannel.RunHelper(invalid, Guid.NewGuid().ToString("N"), true) == 64, "Charging helper independently rejects invalid value");
            }
            int writes = 0, reads = 0;
            int FakeWrite(string value) { writes++; return 0; }
            string? FakeRead() { reads++; return "Standard"; }
            var capability = new SettingCapability("Adaptive", true, ["Adaptive", "Standard"]);
            Check(SettingChange.Apply(null, "Standard", FakeWrite, FakeRead) == 21 && writes == 0, "Missing capability cannot write");
            Check(SettingChange.Apply(capability with { Writable = false }, "Standard", FakeWrite, FakeRead) == 21 && writes == 0, "Read-only capability cannot write");
            Check(SettingChange.Apply(capability, "Express", FakeWrite, FakeRead) == 21 && writes == 0, "Firmware unsupported value cannot write");
            Check(SettingChange.Apply(capability, "Adaptive", FakeWrite, FakeRead) == 0 && writes == 0, "Idempotent operation does not write");
            Check(SettingChange.Apply(capability, "Standard", FakeWrite, FakeRead) == 0 && writes == 1 && reads == 1, "Successful write requires read-back");
            Check(SettingChange.Apply(capability, "Standard", _ => 22, () => throw new Exception("must not read")) == 22, "Provider rejection remains failure");
            Check(SettingChange.Apply(capability, "Standard", _ => 0, () => "Adaptive") == 23, "Read-back mismatch cannot report success");
            Check(SettingChange.Apply(capability, "Standard", _ => 0, () => null) == 23, "Missing read-back cannot report success");
            Check(System.Runtime.InteropServices.Marshal.SizeOf<Native.BatteryState>() == 32, "Native battery layout matches Windows ABI");
            Check(Metrics.SignedBatteryWatts(unchecked((uint)-20000), false, true) == -20, "Signed discharge milliwatts convert correctly");
            Check(Metrics.SignedBatteryWatts(12000, true, false) == 12, "Charging milliwatts convert correctly");
            Check(Metrics.SignedBatteryWatts(uint.MaxValue, false, true) is null && Metrics.SignedBatteryWatts(0x80000000, false, true) is null, "Unknown battery-rate sentinels are not measurements");
            Check(Metrics.SignedBatteryWatts(20000, false, true) is null && Metrics.SignedBatteryWatts(20000, true, true) is null, "Contradictory rate/flags remain unavailable");
            Check(Metrics.SignedBatteryWatts(0, false, false) == 0 && Metrics.SignedBatteryWatts(0, false, true) == 0, "Reported zero battery flow is preserved regardless of state flags");
            Check(Metrics.RuntimeHours(60, 20) == 3 && Metrics.RuntimeHours(null, 20) is null && Metrics.RuntimeHours(60, 0) is null, "Wh/W runtime and missing-data handling");
            Check(Metrics.RuntimeHours(double.NaN, 20) is null && Metrics.RuntimeHours(60, double.PositiveInfinity) is null, "Non-finite estimates are rejected");
            var history = new ActivityHistory();
            var origin = DateTimeOffset.UtcNow;
            var drain = new BatteryTelemetry(-20, 60, false, false, true);
            for (int i = 0; i <= 12; i += 4) history.Add(origin.AddSeconds(i), 25, drain);
            Check(history.Estimate(origin.AddSeconds(12), drain).RuntimeHours is null, "Runtime warms up before enough continuous samples");
            history.Add(origin.AddSeconds(16), 50, drain);
            var estimated = history.Estimate(origin.AddSeconds(16), drain);
            Check(estimated.AverageDrainWatts == 20 && estimated.RuntimeHours == 3, "Stable discharge produces smoothed runtime");
            Check(history.Estimate(origin.AddSeconds(16), drain with { AcOnline = true }).RuntimeHours is null, "No runtime estimate while plugged in");
            Check(history.Estimate(origin.AddSeconds(16), drain with { RemainingWh = null }).RuntimeHours is null, "Missing remaining capacity cannot produce runtime");
            Check(history.Estimate(origin.AddSeconds(40), drain).RuntimeHours is null, "Stale last sample cannot produce runtime");
            history.Add(origin.AddSeconds(40), 20, drain);
            Check(history.Estimate(origin.AddSeconds(40), drain).RuntimeHours is null, "Sampling gap resets estimate");
            history.Break(origin.AddSeconds(41), "Paused fixture");
            for (int i = 44; i <= 56; i += 4) history.Add(origin.AddSeconds(i), 20, drain);
            Check(history.Estimate(origin.AddSeconds(56), drain).RuntimeHours is null, "Pause does not bridge drain history");
            history.Add(origin.AddSeconds(60), 20, drain);
            Check(history.Estimate(origin.AddSeconds(60), drain).RuntimeHours == 3, "Estimate recovers after new continuous samples");
            var ac = new BatteryTelemetry(10, 60, true, true, false);
            history.Add(origin.AddSeconds(64), 10, ac);
            Check(history.Estimate(origin.AddSeconds(64), ac).RuntimeHours is null && history.Events.Any(e => e.Label == "AC connected"), "AC transition clears runtime and marks activity");
            history.Add(origin.AddMinutes(17), 20, ac);
            Check(history.Points.Count == 1 && history.Events.All(e => e.At >= origin.AddMinutes(1)), "History and events expire after 15 minutes");
            var powerSummary = DiagnosticSummary.Create(fixture, workload with { Power = drain }, origin, null, null, null, false, history);
            Check(powerSummary.Contains("-20.00") || powerSummary.Contains("-20,00"), "Summary exports signed battery watts");
            Check(powerSummary.Contains("not sampled while hidden"), "Export explains visible-only history");
            Check(ElevatedChannel.StatusText(0, "Standard", true).Contains("Charging mode verified"), "Charging operation reports correct setting type");
            Console.WriteLine($"PASS: {count} checks. No hardware writes performed.");
            return 0;
        }
        catch (Exception e) { Console.WriteLine("FAIL: " + e.Message); return 1; }
    }

    public static async Task<int> ChargingHelperSmokeAsync()
    {
        if (!Program.Elevated) return 20;
        try
        {
            var snapshot = DellProvider.ReadSnapshot();
            var mode = snapshot.Battery?.Mode;
            if (!Metrics.ValidChargingMode(mode)) return 21;
            // Recheck the existing mode through the real pipe helper without changing it.
            var reply = await ElevatedChannel.RequestAsync(mode, CancellationToken.None, chargingMode: true);
            if (reply?.Status != 0 || reply.Snapshot.Battery?.Mode != mode) return 1;
            Console.WriteLine("PASS: charging-mode helper pipe and idempotent mode verification: " + mode);
            return 0;
        }
        catch (Exception e) { Console.WriteLine("FAIL: " + e.GetType().Name); return 1; }
    }

    public static async Task<int> HelperSmokeAsync()
    {
        // Run from an already-elevated developer terminal: no UAC automation.
        // The set operation is deliberately idempotent, only after confirming Optimized.
        if (!Program.Elevated) return 20;
        try
        {
            var read = await ElevatedChannel.RequestAsync(null, CancellationToken.None);
            if (read?.Status != 0 || read.Snapshot.Firmware?.Profile != "Optimized") return 1;
            var same = await ElevatedChannel.RequestAsync("Optimized", CancellationToken.None);
            if (same?.Status != 0 || same.Snapshot.Firmware?.Profile != "Optimized") return 1;
            Console.WriteLine("PASS: current-user pipe read and idempotent Optimized helper write; separate snapshot returned.");
            return 0;
        }
        catch (Exception e) { Console.WriteLine("FAIL: " + e.GetType().Name); return 1; }
    }
}
