namespace JukkasDell;

// Accessed only on the UI dispatcher. A generation identifies a continuous
// visible/awake session, so work begun before hide/resume cannot look fresh.
internal sealed class MonitoringSession
{
    public long Generation { get; private set; }
    public void Invalidate() => Generation++;
    public bool Accepts(long generation) => generation == Generation;
}

internal static class DellObservation
{
    public static string Describe(Snapshot? snapshot, bool needsRefresh)
    {
        if (snapshot is null) return "Dell settings have not been read. Use Read Dell settings to load them.";
        var profile = snapshot.Firmware?.Profile;
        var label = profile is null ? "Dell profile unavailable" : "Last observed: " + Metrics.ProfileLabel(profile);
        return $"{label} · {snapshot.CapturedAt:MMM d, HH:mm:ss}" +
            (needsRefresh ? " · read Dell settings again to check the current state." : " · use Read Dell settings to check for changes.");
    }
}
