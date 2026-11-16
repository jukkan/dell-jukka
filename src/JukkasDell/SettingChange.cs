namespace JukkasDell;

internal sealed record SettingCapability(string? Current, bool Writable, string[] Allowed);

internal static class SettingChange
{
    // Shared verification policy, exercised with fake provider responses in self-tests.
    internal static int Apply(SettingCapability? capability, string value, Func<string, int> write, Func<string?> readBack)
    {
        if (capability is not { Writable: true } c || !c.Allowed.Contains(value, StringComparer.Ordinal)) return 21;
        if (c.Current == value) return 0;
        var status = write(value);
        if (status != 0) return status;
        return readBack() == value ? 0 : 23;
    }
}
