namespace JukkasDell;

public sealed record ActivityPoint(DateTimeOffset At, double? Cpu, double? Watts, bool? AcOnline);
public sealed record ActivityEvent(DateTimeOffset At, string Label);
public sealed record BatteryEstimate(double? AverageDrainWatts, double? RuntimeHours);

public sealed class ActivityHistory
{
    private readonly List<ActivityPoint> _points = [];
    private readonly List<ActivityEvent> _events = [];
    public IReadOnlyList<ActivityPoint> Points => _points;
    public IReadOnlyList<ActivityEvent> Events => _events;
    public void Add(DateTimeOffset at, double? cpu, BatteryTelemetry? power)
    {
        if (_points.Count > 0 && at <= _points[^1].At) return;
        if (power is { } p && (_points.Count == 0 || _points[^1].AcOnline != p.AcOnline))
            Mark(at, p.AcOnline ? "AC connected" : "On battery");
        _points.Add(new(at, cpu, power?.SignedWatts, power?.AcOnline));
        Prune(at);
    }
    public void Mark(DateTimeOffset at, string label)
    {
        _events.Add(new(at, label));
        Prune(at);
    }
    public void Break(DateTimeOffset at, string label)
    {
        if (_points.Count == 0 || at > _points[^1].At) _points.Add(new(at, null, null, null));
        Mark(at, label);
    }
    private void Prune(DateTimeOffset now)
    {
        _points.RemoveAll(p => now - p.At > TimeSpan.FromMinutes(15));
        _events.RemoveAll(p => now - p.At > TimeSpan.FromMinutes(15));
        // Also bound memory if Refresh is clicked rapidly.
        if (_points.Count > 1000) _points.RemoveRange(0, _points.Count - 1000);
        if (_events.Count > 80) _events.RemoveRange(0, _events.Count - 80);
    }
    public BatteryEstimate Estimate(DateTimeOffset now, BatteryTelemetry? power)
    {
        if (power is not { AcOnline: false, Discharging: true, Charging: false, SignedWatts: < 0 } || _points.Count < 2)
            return new(null, null);
        if ((now - _points[^1].At).TotalSeconds is < 0 or > 12) return new(null, null);
        double weighted = 0, seconds = 0;
        var cutoff = now - TimeSpan.FromMinutes(1);
        for (int i = _points.Count - 1; i > 0; i--)
        {
            var end = _points[i]; var start = _points[i - 1];
            if (end.At <= cutoff) break;
            if (start.AcOnline != false || end.AcOnline != false || start.Watts is not < 0 || end.Watts is not < 0) break;
            var gap = (end.At - start.At).TotalSeconds;
            if (gap is <= 0 or > 12) break;
            var duration = (end.At - (start.At > cutoff ? start.At : cutoff)).TotalSeconds;
            var retainedStart = end.At.AddSeconds(-duration);
            var startRate = start.Watts.Value + (end.Watts.Value - start.Watts.Value) * (retainedStart - start.At).TotalSeconds / gap;
            weighted += -(startRate + end.Watts.Value) / 2 * duration;
            seconds += duration;
        }
        if (seconds < 16) return new(null, null);
        var average = weighted / seconds;
        return new(average, Metrics.RuntimeHours(power.RemainingWh, average));
    }
}
