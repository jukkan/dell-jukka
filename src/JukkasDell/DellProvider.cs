using System.Management;

namespace JukkasDell;

public static class DellProvider
{
    private const string BiosNamespace = @"root\dcim\sysman\biosattributes";
    private static List<Dictionary<string, object?>> Query(string ns, string query)
    {
        using var search = new ManagementObjectSearcher(new ManagementScope(ns), new ObjectQuery(query),
            new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(5), ReturnImmediately = false });
        using var results = search.Get();
        var rows = new List<Dictionary<string, object?>>();
        foreach (ManagementObject item in results)
        {
            using (item)
                rows.Add(item.Properties.Cast<PropertyData>().ToDictionary(x => x.Name, x => (object?)x.Value));
        }
        return rows;
    }
    private static Dictionary<string, object?>? One(string ns, string query) => Query(ns, query).FirstOrDefault();
    private static object? Value(Dictionary<string, object?>? row, string name) => row?.GetValueOrDefault(name);
    private static string? Text(Dictionary<string, object?>? row, string name) => Value(row, name)?.ToString();
    private static int? Int(Dictionary<string, object?>? row, string name) => Value(row, name) is { } v && Convert.ToInt64(v) < uint.MaxValue ? Convert.ToInt32(v) : null;
    private static string Error(Exception e) => e switch
    {
        UnauthorizedAccessException => "Administrator access required",
        ManagementException m when m.ErrorCode == ManagementStatus.AccessDenied => "Administrator access required",
        ManagementException m when m.ErrorCode is ManagementStatus.InvalidClass or ManagementStatus.InvalidNamespace => "Not exposed by this device",
        _ => "Provider unavailable (" + e.GetType().Name + ")"
    };
    private static T? Try<T>(Dictionary<string, string> errors, string source, Func<T?> read) where T : class
    {
        try { return read(); }
        catch (Exception e) { errors[source] = Error(e); return null; }
    }

    public static Snapshot ReadSnapshot()
    {
        var errors = new Dictionary<string, string>();
        var system = Try(errors, "Device", () => One("root/cimv2", "SELECT Model FROM Win32_ComputerSystem"));
        var bios = Try(errors, "BIOS", () => One("root/cimv2", "SELECT SMBIOSBIOSVersion FROM Win32_BIOS"));
        var os = Try(errors, "Windows", () => One("root/cimv2", "SELECT Caption,BuildNumber FROM Win32_OperatingSystem"));
        var profile = Try(errors, "Dell thermal profile", () => One(BiosNamespace,
            "SELECT CurrentValue,PossibleValue,ReadOnly FROM EnumerationAttribute WHERE AttributeName='ThermalManagement'"));
        var scheme = Try(errors, "Windows power scheme", Native.ActivePowerScheme);
        var firmware = new FirmwareData(Text(system, "Model"), Text(bios, "SMBIOSBIOSVersion"),
            os is null ? null : Text(os, "Caption") + " · build " + Text(os, "BuildNumber"), Text(profile, "CurrentValue"),
            (Value(profile, "PossibleValue") as string[] ?? []).Where(Metrics.ValidProfile).ToArray(),
            profile is not null && Value(profile, "ReadOnly") is { } readOnly && !Convert.ToBoolean(readOnly), scheme);

        var portable = Try(errors, "Battery design capacity", () => One("root/cimv2", "SELECT DesignCapacity FROM Win32_PortableBattery"));
        var full = Try(errors, "Battery full capacity", () => One("root/wmi", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity"));
        var cycles = Try(errors, "Battery cycles", () => One("root/wmi", "SELECT CycleCount FROM BatteryCycleCount"));
        var status = Try(errors, "Battery power", () => One("root/wmi", "SELECT ChargeRate,DischargeRate,Charging,Discharging FROM BatteryStatus"));
        var charge = Try(errors, "Dell charging mode", () => One(BiosNamespace,
            "SELECT CurrentValue,PossibleValue,ReadOnly FROM EnumerationAttribute WHERE AttributeName='PrimaryBattChargeCfg'"));
        var thresholds = Try(errors, "Dell charging thresholds", () => Query(BiosNamespace,
            "SELECT AttributeName,CurrentValue FROM IntegerAttribute WHERE AttributeName='CustomChargeStart' OR AttributeName='CustomChargeStop'"));
        var battery = new BatteryData(Metrics.MilliUnits(Value(portable, "DesignCapacity")), Metrics.MilliUnits(Value(full, "FullChargedCapacity")),
            Int(cycles, "CycleCount"), Value(status, "Charging") is true ? Metrics.MilliUnits(Value(status, "ChargeRate")) : null,
            Value(status, "Discharging") is true ? Metrics.MilliUnits(Value(status, "DischargeRate")) : null, Text(charge, "CurrentValue"),
            Int(thresholds?.FirstOrDefault(x => Text(x, "AttributeName") == "CustomChargeStart"), "CurrentValue"),
            Int(thresholds?.FirstOrDefault(x => Text(x, "AttributeName") == "CustomChargeStop"), "CurrentValue"),
            (Value(charge, "PossibleValue") as string[] ?? []).Where(Metrics.ValidChargingMode).ToArray(),
            charge is not null && Value(charge, "ReadOnly") is { } modeReadOnly && !Convert.ToBoolean(modeReadOnly));
        return new Snapshot(DateTimeOffset.Now, Program.Elevated, firmware, battery, errors);
    }

    public static int SetProfile(string value) => Metrics.ValidProfile(value) ? SetKnownSetting(false, value) : 64;
    public static int SetChargingMode(string value) => Metrics.ValidChargingMode(value) ? SetKnownSetting(true, value) : 64;

    // Private mapping only. No public attribute-name setter or multi-attribute write.
    private static int SetKnownSetting(bool chargingMode, string value)
    {
        if (!Program.Elevated) return 20;
        using var gate = new Mutex(false, @"Local\JukkasDell.SettingsWriter");
        bool held;
        try { held = gate.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
        if (!held) return 24;
        try
        {
            var attribute = chargingMode ? "PrimaryBattChargeCfg" : "ThermalManagement";
            SettingCapability? ReadCapability()
            {
                var row = One(BiosNamespace, $"SELECT CurrentValue,PossibleValue,ReadOnly FROM EnumerationAttribute WHERE AttributeName='{attribute}'");
                if (row is null) return null;
                return new(Text(row, "CurrentValue"), Value(row, "ReadOnly") is { } ro && !Convert.ToBoolean(ro),
                    Value(row, "PossibleValue") as string[] ?? []);
            }
            int Write(string requested)
            {
                using var interfaces = new ManagementObjectSearcher(BiosNamespace, "SELECT * FROM BIOSAttributeInterface");
                using var interfaceRows = interfaces.Get();
                using var iface = interfaceRows.Cast<ManagementObject>().FirstOrDefault();
                if (iface is null) return 21;
                using var input = iface.GetMethodParameters("SetAttribute");
                input["SecType"] = (uint)0;
                input["SecHndCount"] = (uint)0;
                input["SecHandle"] = Array.Empty<byte>();
                input["AttributeName"] = attribute;
                input["AttributeValue"] = requested;
                using var result = iface.InvokeMethod("SetAttribute", input, null);
                return result is not null && Convert.ToInt32(result["Status"]) == 0 ? 0 : 22;
            }
            return SettingChange.Apply(ReadCapability(), value, Write, () => ReadCapability()?.Current);
        }
        catch { return 22; }
        finally { gate.ReleaseMutex(); }
    }
}
