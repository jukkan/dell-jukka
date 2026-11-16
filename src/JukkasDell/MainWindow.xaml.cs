using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace JukkasDell;

public partial class MainWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly WorkloadSampler _sampler = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly Forms.NotifyIcon _tray;
    private readonly Queue<double?> _history = new();
    private readonly ActivityHistory _activity = new();
    private string? _observedProfile, _observedMode;
    private readonly MonitoringSession _session = new();
    private readonly Dictionary<string, Forms.ToolStripMenuItem> _trayProfiles = new();
    private readonly Forms.ToolStripMenuItem _trayStatus, _trayRead;
    private bool _dellNeedsRefresh;
    private Snapshot? _snapshot;
    private Snapshot? _dellSnapshot;
    private Workload? _workload;
    private DateTimeOffset? _workloadAt;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;
    private string? _deviceError;
    private string? _workloadError;
    private bool _sampling, _refreshing, _writing, _quitting, _resetSampler;
    private HwndSource? _source;

    public MainWindow()
    {
        InitializeComponent();
        Icon = AppIcon.Window();
        var menu = new Forms.ContextMenuStrip();
        DarkTheme.ApplyMenu(menu);
        menu.Items.Add("Open Jukka’s Dell", null, (_, _) => Dispatcher.Invoke(ShowApp));
        _trayStatus = new Forms.ToolStripMenuItem("Dell profile not read") { Enabled = false };
        menu.Items.Add(_trayStatus);
        _trayRead = new Forms.ToolStripMenuItem("Read Dell settings…");
        _trayRead.Click += async (_, _) => await TrayDellOperationAsync(null);
        menu.Items.Add(_trayRead);
        var profiles = new Forms.ToolStripMenuItem("Thermal profile");
        DarkTheme.ApplyMenu(profiles.DropDown);
        foreach (var profile in Metrics.ThermalProfiles)
        {
            var item = new Forms.ToolStripMenuItem(Metrics.ProfileLabel(profile));
            item.Click += async (_, _) => await TrayDellOperationAsync(profile);
            _trayProfiles.Add(profile, item);
            profiles.DropDownItems.Add(item);
        }
        menu.Items.Add(profiles);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Dispatcher.Invoke(Quit));
        _tray = new Forms.NotifyIcon { Text = "Jukka’s Dell", Icon = AppIcon.Tray(),
            Visible = true, ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowApp);
        _timer.Tick += async (_, _) => await TickAsync();
        Loaded += async (_, _) => { _timer.Start(); await TickAsync(); };
        IsVisibleChanged += async (_, _) =>
        {
            _session.Invalidate();
            if (IsVisible && !_quitting)
            {
                _resetSampler = true; _history.Clear(); _lastAttempt = DateTimeOffset.MinValue;
                _activity.Break(DateTimeOffset.Now, "Monitoring resumed");
                _timer.Start(); await TickAsync();
            }
            else
            {
                _timer.Stop();
                _activity.Break(DateTimeOffset.Now, "Monitoring paused");
            }
        };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };
        Closing += OnClosing;
        Microsoft.Win32.SystemEvents.PowerModeChanged += PowerChanged;
        SourceInitialized += (_, _) =>
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _source?.AddHook(WindowHook);
            DarkTheme.ApplyTitleBar(new WindowInteropHelper(this).Handle);
        };
        UpdateProfileButtons();
    }

    private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr w, IntPtr l, ref bool handled)
    {
        if ((uint)message == Native.ShowMessage) { ShowApp(); handled = true; }
        return IntPtr.Zero;
    }
    private void ShowApp() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void PowerChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_quitting) return;
            _lastAttempt = DateTimeOffset.MinValue;
            if (e.Mode == Microsoft.Win32.PowerModes.Resume)
            {
                _session.Invalidate();
                _resetSampler = true; _history.Clear();
                _activity.Break(DateTimeOffset.Now, "Windows resumed");
                _dellNeedsRefresh = true;
                RenderDevice();
                ActionText.Text = "Resumed. Read Dell settings to check for profile changes made while asleep.";
            }
        });
    }
    private void QuitClick(object sender, RoutedEventArgs e) => Quit();
    private void Quit()
    {
        if (_writing)
        {
            ActionText.Text = "Wait for the Dell operation to finish before quitting.";
            ShowApp(); return;
        }
        _quitting = true; Close();
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_quitting) { e.Cancel = true; Hide(); return; }
        _timer.Stop(); _lifetime.Cancel();
        Microsoft.Win32.SystemEvents.PowerModeChanged -= PowerChanged;
        _source?.RemoveHook(WindowHook);
        _tray.Visible = false; _tray.ContextMenuStrip?.Dispose(); _tray.Icon?.Dispose(); _tray.Dispose();
        Application.Current.Shutdown();
    }

    private async Task TickAsync(bool forceDevice = false)
    {
        if (!IsVisible || _quitting) return;
        if (!_sampling)
        {
            _sampling = true;
            var generation = _session.Generation;
            try
            {
                var reset = _resetSampler; _resetSampler = false;
                var sample = await Task.Run(() => { if (reset) _sampler.Reset(); return _sampler.Read(); });
                if (!_quitting && IsVisible && _session.Accepts(generation))
                {
                    _workload = sample; _workloadAt = DateTimeOffset.Now; _workloadError = null;
                    _activity.Add(_workloadAt.Value, sample.Cpu, sample.Power);
                    _history.Enqueue(sample.Cpu);
                    while (_history.Count > 225) _history.Dequeue();
                    RenderWorkload();
                }
            }
            catch
            {
                if (_session.Accepts(generation))
                    _workloadError = "Workload refresh failed; last reading retained";
            }
            finally { _sampling = false; }
        }
        // A sample may finish after hiding, reopening or resuming. Do not start
        // a slow read while hidden, or publish a result from the old session.
        if (!IsVisible || _quitting) return;
        if (!_writing && !_refreshing && (forceDevice || DateTimeOffset.Now - _lastAttempt > TimeSpan.FromSeconds(30)))
            await RefreshDeviceAsync();
        if (!_quitting) RenderAge();
    }
    private async void RefreshClick(object sender, RoutedEventArgs e) => await TickAsync(forceDevice: true);
    private async Task RefreshDeviceAsync()
    {
        if (_refreshing || _quitting) return;
        _refreshing = true; _lastAttempt = DateTimeOffset.Now;
        var generation = _session.Generation;
        RefreshButton.IsEnabled = false; RefreshButton.Content = "Reading…";
        UpdateProfileButtons();
        try
        {
            var snapshot = await Workers.SnapshotAsync(_lifetime.Token);
            if (_quitting || !IsVisible || !_session.Accepts(generation))
            {
                _lastAttempt = DateTimeOffset.MinValue;
                return;
            }
            _snapshot = snapshot;
            if (_snapshot.Firmware?.Profile is not null)
            {
                _dellSnapshot = _snapshot;
                _dellNeedsRefresh = false;
                ObserveDell(_snapshot);
            }
            _deviceError = null;
            if (!_quitting) RenderDevice();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (_session.Accepts(generation))
                _deviceError = e is TimeoutException ? e.Message : "Device reading failed. Select Refresh to retry.";
        }
        finally
        {
            _refreshing = false;
            if (!_quitting) { RefreshButton.IsEnabled = !_writing; RefreshButton.Content = "Refresh"; UpdateProfileButtons(); RenderAge(); }
        }
    }
    private void RenderWorkload()
    {
        if (_workload is not { } w) return;
        CpuText.Text = w.Cpu is { } cpu ? $"{cpu:F0}%" : "Sampling…";
        MemoryText.Text = $"{w.UsedGiB:F1} / {w.TotalGiB:F1} GB";
        MemoryBar.Value = w.TotalGiB > 0 ? 100 * w.UsedGiB / w.TotalGiB : 0;
        MemoryDetail.Text = $"{Math.Max(0, w.TotalGiB - w.UsedGiB):F1} GB available · physical memory";
        PowerText.Text = w.AcOnline switch { true => "Plugged in", false => "On battery", _ => "Power source unavailable" };
        ChargeText.Text = w.BatteryPercent is { } charge ? $"{charge}%" : "Unavailable";
        ChargeDetail.Text = w.Charging ? "Charging" : w.AcOnline == true ? "Connected to power" : "Charge level";
        ProcessList.ItemsSource = w.Processes;
        var power = w.Power;
        BatteryWattsText.Text = power?.SignedWatts is { } watts ? $"{Math.Abs(watts):F1} W" : "—";
        BatteryPowerLabel.Text = power?.SignedWatts switch
        {
            > 0 => "Into battery", < 0 => "From battery", 0 => "Reported zero flow", _ => "Power unavailable"
        };
        var estimate = _activity.Estimate(_workloadAt ?? DateTimeOffset.Now, power);
        RuntimeText.Text = Metrics.RuntimeLabel(estimate.RuntimeHours);
        RuntimeHint.Text = power?.AcOnline == true ? "While unplugged" : power?.SignedWatts is < 0 ?
            estimate.AverageDrainWatts is null ? "Measuring drain…" : "Est. remaining" : "Estimate unavailable";
        BatteryEstimateDetail.Text = estimate.AverageDrainWatts is { } average
            ? $"Recent drain {average:F1} W · estimate follows workload"
            : power?.RemainingWh is { } wh ? $"{wh:F1} Wh remaining · runtime needs 16s of battery use" : "Battery capacity/rate unavailable";
        DrawChart(); RenderActivity();
    }
    private void ChartSizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();
    private void DrawChart()
    {
        if (CpuLine is null || CpuCanvas.ActualWidth <= 0) return;
        var values = _history.ToArray();
        // A null sample breaks the line rather than inventing a zero-temperature/load reading.
        var recent = values.Reverse().TakeWhile(x => x.HasValue).Reverse().ToArray();
        CpuLine.Points = new PointCollection(recent.Select((v, i) => new Point(
            CpuCanvas.ActualWidth * i / Math.Max(29, recent.Length - 1),
            CpuCanvas.Height - 3 - (CpuCanvas.Height - 6) * v!.Value / 100)));
    }
    private void RenderDevice()
    {
        var effective = EffectiveSnapshot();
        var f = effective?.Firmware; var b = effective?.Battery;
        DeviceText.Text = $"{f?.Model ?? "Device unavailable"}  ·  BIOS {f?.Bios ?? "unavailable"}";
        SchemeText.Text = "Windows power scheme: " + (f?.WindowsScheme ?? "Unavailable");
        DellObservationText.ToolTip = DellObservation.Describe(_dellSnapshot, _dellNeedsRefresh);
        DellObservationText.Text = _dellSnapshot is { } observed
            ? $"Last read {observed.CapturedAt:MMM d, HH:mm:ss}" + (_dellNeedsRefresh ? " · read again to check the current state" : " · refresh Dell settings to check for changes")
            : "Dell settings have not been read.";
        ProfileDescription.Text = f?.Profile switch
        {
            "Optimized" => "Optimized · balances performance, temperature and fan noise.",
            "Quiet" => "Quiet · lowers fan and processor speeds. Surfaces may feel warmer.",
            "Cool" => "Cool · runs fans faster to cool the surface. This may be louder.",
            "UltraPerformance" => "Ultra performance · prioritizes speed. Expect more fan noise and heat.",
            _ => "Read Dell settings to enable profile switching."
        };
        RetentionText.Text = b?.Retention is { } r ? $"{r:F0}%" : "Unavailable";
        CyclesText.Text = b?.Cycles is { } c ? c.ToString() : "Unavailable";
        var capacities = b?.FullWh is { } full && b.DesignWh is { } design ? $"{full:F1} Wh full / {design:F1} Wh design" : "Capacity readings unavailable";
        var thresholds = b?.Start is { } start && b.Stop is { } stop ? $"Stored limits {start}–{stop}%" + (b.Mode == "Custom" ? "" : " (inactive)") : "Custom limits unavailable";
        BatteryDetail.Text = $"Mode: {Metrics.ModeLabel(b?.Mode)}";
        BatteryDetail.ToolTip = $"{capacities} · {thresholds}";
        ChargingStateText.Text = _dellSnapshot is { } chargeObserved
            ? $"Last read {chargeObserved.CapturedAt:HH:mm:ss} · {thresholds}" + (_dellNeedsRefresh ? " · read again" : "")
            : "Read Dell settings to enable charging controls.";
        CapacityDetailText.Text = capacities;
        DetailsText.Text = $"{f?.Windows ?? "Windows version unavailable"}\nApp 0.2.0 · {(Program.Elevated ? "Running elevated" : "Standard user; requests elevation for Dell reads and changes")}\nClose or minimize to tray; monitoring pauses while hidden. Use Quit to exit.";
        SourcesText.Text = effective?.Errors.Count > 0 ? string.Join("\n", effective.Errors.Select(x => $"{x.Key}: {x.Value}")) : "Windows and Dell readings available. Numeric CPU temperature / fan speed are not available in this build.";
        if (_dellSnapshot is { } cached) SourcesText.Text += $"\nDell settings last read {cached.CapturedAt:HH:mm:ss}; use Read Dell settings to check changes made elsewhere.";
        UpdateProfileButtons();
    }
    private void RenderAge()
    {
        var age = _snapshot is null ? "No device reading yet" : $"Device read {_snapshot.CapturedAt:HH:mm:ss} ({(DateTimeOffset.Now - _snapshot.CapturedAt).TotalSeconds:F0}s ago)";
        var uptime = _workload is { } w ? $" · uptime {(int)w.Uptime.TotalDays}d {w.Uptime.Hours}h" : "";
        var workloadAge = _workloadAt is { } at ? $" · workload {(DateTimeOffset.Now - at).TotalSeconds:F0}s ago" : "";
        var dellAge = _dellSnapshot is { } d ? $" · Dell settings {d.CapturedAt:HH:mm:ss}" : " · Dell settings not read";
        UpdateText.Text = (_deviceError is null ? age + uptime : _deviceError + " · " + age) + workloadAge
            + dellAge + (_workloadError is null ? "" : " · " + _workloadError);
    }
    private void UpdateProfileButtons()
    {
        if (ProfileButtons is null) return;
        var firmware = EffectiveSnapshot()?.Firmware;
        ReadDellButton.IsEnabled = !_writing && !_refreshing;
        _trayRead.Enabled = ReadDellButton.IsEnabled;
        _trayStatus.Text = _dellSnapshot?.Firmware?.Profile is { } observed
            ? $"Last read: {Metrics.ProfileLabel(observed)}" + (_dellNeedsRefresh ? " · check again" : "")
            : "Dell profile not available";
        _tray.Text = _dellSnapshot?.Firmware?.Profile is { } trayProfile
            ? "Jukka’s Dell · " + Metrics.ProfileLabel(trayProfile) + " (last read)"
            : "Jukka’s Dell · profile not read";
        UpdateChargingButtons();
        foreach (Button button in ProfileButtons.Children)
        {
            var profile = button.Tag.ToString()!;
            bool selected = firmware?.Profile == profile;
            button.SetResourceReference(Control.BackgroundProperty, selected ? "AccentSurface" : "ControlSurface");
            button.SetResourceReference(Control.ForegroundProperty, selected ? "Accent" : "Text");
            button.SetResourceReference(Control.BorderBrushProperty, selected ? "Accent" : "Stroke");
            button.IsEnabled = !_writing && !_refreshing && firmware is { Writable: true } f && f.Profiles.Contains(profile);
            System.Windows.Automation.AutomationProperties.SetHelpText(button, selected ? "Last observed thermal profile; click to recheck and apply" : "Switch thermal profile");
            var trayItem = _trayProfiles[profile];
            trayItem.Enabled = button.IsEnabled;
            trayItem.Checked = selected;
        }
    }
    private void UpdateChargingButtons()
    {
        if (ChargingButtons is null) return;
        var battery = EffectiveSnapshot()?.Battery;
        foreach (Button button in ChargingButtons.Children)
        {
            var mode = button.Tag.ToString()!;
            bool selected = battery?.Mode == mode;
            button.SetResourceReference(Control.BackgroundProperty, selected ? "AccentSurface" : "ControlSurface");
            button.SetResourceReference(Control.ForegroundProperty, selected ? "Accent" : "Text");
            button.SetResourceReference(Control.BorderBrushProperty, selected ? "Accent" : "Stroke");
            button.IsEnabled = !_writing && !_refreshing && battery is { ModeWritable: true } && (battery.Modes ?? []).Contains(mode);
            System.Windows.Automation.AutomationProperties.SetHelpText(button, selected ? "Last observed charging mode; click to recheck and apply" : "Change battery charging mode; administrator approval may be required");
        }
    }
    private async void ChargingClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string mode } && Metrics.ValidChargingMode(mode))
            await DellOperationAsync(mode, chargingMode: true);
    }
    private async void ProfileClick(object sender, RoutedEventArgs e)
    {
        if (_writing || _refreshing || sender is not Button { Tag: string value } || !Metrics.ValidProfile(value)) return;
        // Even the selected value can be stale when read through the on-demand
        // elevated helper. Ask the helper to re-check rather than assuming it.
        await DellOperationAsync(value);
    }
    private async Task TrayDellOperationAsync(string? value)
    {
        // Set the busy flag before reopening starts ordinary polling.
        var operation = DellOperationAsync(value);
        ShowApp();
        await operation;
    }
    private async void ReadDellClick(object sender, RoutedEventArgs e) => await DellOperationAsync(null);
    private async Task DellOperationAsync(string? value, bool chargingMode = false)
    {
        if (_writing || _refreshing) return;
        _writing = true; UpdateProfileButtons(); RefreshButton.IsEnabled = false;
        ActionText.Text = value is null ? "Reading Dell settings… Windows may ask for administrator approval." :
            "Applying " + (chargingMode ? Metrics.ModeLabel(value) : Metrics.ProfileLabel(value)) + "… Windows may ask for administrator approval.";
        try
        {
            var reply = await ElevatedChannel.RequestAsync(value, _lifetime.Token, chargingMode);
            if (reply is null) { ActionText.Text = "Administrator approval cancelled. No operation was requested."; return; }
            _snapshot = reply.Snapshot;
            _dellSnapshot = reply.Snapshot;
            _deviceError = null;
            var missing = chargingMode ? reply.Snapshot.Battery?.Mode is null : reply.Snapshot.Firmware?.Profile is null;
            _dellNeedsRefresh = reply.Status != 0 || missing;
            ObserveDell(reply.Snapshot);
            if (reply.Status == 0 && !missing && value is not null)
                _activity.Mark(DateTimeOffset.Now, (chargingMode ? "Charge verified: " + Metrics.ModeLabel(value) : "Thermal verified: " + Metrics.ProfileLabel(value)));
            ActionText.Text = reply.Status == 0 && missing
                ? "Dell settings could not be read. See data availability below and try again."
                : ElevatedChannel.StatusText(reply.Status, value, chargingMode);
            RenderDevice();
        }
        catch (Exception e)
        {
            _dellNeedsRefresh = true;
            ActionText.Text = e is TimeoutException ? e.Message : "Dell helper did not return data. Read Dell settings to check the actual state.";
        }
        finally
        {
            _writing = false; RefreshButton.IsEnabled = !_refreshing;
            RenderDevice(); RenderAge();
        }
    }
    private Snapshot? EffectiveSnapshot()
    {
        if (_snapshot is null || _dellSnapshot is null) return _snapshot;
        var f = _snapshot.Firmware; var d = _dellSnapshot.Firmware;
        var b = _snapshot.Battery; var db = _dellSnapshot.Battery;
        return _snapshot with
        {
            Firmware = f is null ? d : f with { Profile = d?.Profile, Profiles = d?.Profiles ?? [], Writable = d?.Writable == true },
            Battery = b is null ? db : b with { Mode = db?.Mode, Start = db?.Start, Stop = db?.Stop, Modes = db?.Modes, ModeWritable = db?.ModeWritable == true },
            Errors = _snapshot.Errors.Where(x => !x.Key.StartsWith("Dell ", StringComparison.Ordinal))
                .Concat(_dellSnapshot.Errors.Where(x => x.Key.StartsWith("Dell ", StringComparison.Ordinal)))
                .ToDictionary(x => x.Key, x => x.Value)
        };
    }
    private void ObserveDell(Snapshot snapshot)
    {
        if (snapshot.Firmware?.Profile is { } profile && profile != _observedProfile)
        {
            _activity.Mark(snapshot.CapturedAt, "Thermal observed: " + Metrics.ProfileLabel(profile));
            _observedProfile = profile;
        }
        if (snapshot.Battery?.Mode is { } mode && mode != _observedMode)
        {
            _activity.Mark(snapshot.CapturedAt, "Charge observed: " + Metrics.ModeLabel(mode));
            _observedMode = mode;
        }
    }
    private void ActivityExpanded(object sender, RoutedEventArgs e) => RenderActivity();
    private void ActivitySizeChanged(object sender, SizeChangedEventArgs e) => RenderActivity();
    private void RenderActivity()
    {
        if (ActivityCanvas is null || ActivityCanvas.ActualWidth <= 0 || !ActivityExpander.IsExpanded) return;
        ActivityCanvas.Children.Clear();
        var now = DateTimeOffset.Now;
        var first = _activity.Points.Count > 0 ? _activity.Points[0].At : now;
        var span = Math.Clamp((now - first).TotalSeconds, 60, 900);
        var from = now - TimeSpan.FromSeconds(span);
        var wattsMax = Math.Max(10, _activity.Points.Select(p => Math.Abs(p.Watts ?? 0)).DefaultIfEmpty(10).Max());
        ActivityRangeText.Text = $"Last {Math.Ceiling(span / 60):F0} min · visible samples only · gaps mean paused/unavailable";
        ActivityPowerScale.Text = $"Battery ±{wattsMax:F0} W";
        var width = ActivityCanvas.ActualWidth;
        double X(DateTimeOffset at) => Math.Clamp((at - from).TotalSeconds / span, 0, 1) * width;
        void Trace(Func<ActivityPoint, double?> select, double top, double height, double min, double max, string color)
        {
            var geometry = new PathGeometry();
            PathFigure? figure = null;
            DateTimeOffset? previous = null;
            foreach (var p in _activity.Points.Where(p => p.At >= from))
            {
                var value = select(p);
                if (value is null) { figure = null; previous = null; continue; }
                var point = new Point(X(p.At), top + height * (1 - (Math.Clamp(value.Value, min, max) - min) / (max - min)));
                if (figure is null || previous is { } old && (p.At - old).TotalSeconds > 12)
                {
                    figure = new PathFigure { StartPoint = point, IsFilled = false };
                    geometry.Figures.Add(figure);
                }
                else figure.Segments.Add(new LineSegment(point, true));
                previous = p.At;
            }
            ActivityCanvas.Children.Add(new System.Windows.Shapes.Path { Data = geometry,
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)), StrokeThickness = 1.5 });
        }
        foreach (var y in new[] { 34d, 72d })
            ActivityCanvas.Children.Add(new System.Windows.Shapes.Line { X1 = 0, X2 = width, Y1 = y, Y2 = y,
                Stroke = (Brush)FindResource("Stroke"), StrokeThickness = 1 });
        Trace(p => p.Cpu, 2, 32, 0, 100, "#83E3C1");
        Trace(p => p.Watts, 43, 58, -wattsMax, wattsMax, "#86BFF5");
        foreach (var ev in _activity.Events.Where(e => e.At >= from))
            ActivityCanvas.Children.Add(new System.Windows.Shapes.Line { X1 = X(ev.At), X2 = X(ev.At), Y1 = 0, Y2 = 103,
                Stroke = (Brush)FindResource("Muted"), Opacity = .35, StrokeDashArray = new DoubleCollection([2, 3]),
                ToolTip = $"{ev.At:HH:mm:ss} · {ev.Label}" });
        ActivityEventsText.Text = string.Join(" · ", _activity.Events.TakeLast(4).Select(e => $"{e.At:HH:mm} {e.Label}"));
    }
    private void CopyClick(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(DiagnosticSummary.Create(EffectiveSnapshot(), _workload, _workloadAt, _deviceError, _dellSnapshot?.CapturedAt, _workloadError, _dellNeedsRefresh, _activity)); ActionText.Text = "Summary copied. It includes process names; no serial numbers, account names or command lines."; }
        catch (System.Runtime.InteropServices.ExternalException) { ActionText.Text = "Clipboard is busy. Try Copy summary again."; }
    }
}
