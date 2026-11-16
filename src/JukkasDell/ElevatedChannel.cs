using System.ComponentModel;
using System.IO.Pipes;
using System.Text.Json;

namespace JukkasDell;

// The ordinary-user UI receives read-only data over a random, current-user-only
// named pipe. The elevated process accepts no file path, WMI name or shell code.
public static class ElevatedChannel
{
    private const string Prefix = "JukkasDell.reply.";
    public static bool ValidNonce(string value) => Guid.TryParseExact(value, "N", out _);
    public static int RunHelper(string? profile, string nonce, bool chargingMode = false)
    {
        if (!ValidNonce(nonce) || (profile is not null && !(chargingMode ? Metrics.ValidChargingMode(profile) : Metrics.ValidProfile(profile)))) return 64;
        if (!Program.Elevated) return 20;
        using var watchdog = new Timer(_ => Environment.Exit(25), null, TimeSpan.FromSeconds(25), Timeout.InfiniteTimeSpan);
        try
        {
            using var pipe = new NamedPipeClientStream(".", Prefix + nonce, PipeDirection.Out, PipeOptions.None);
            pipe.Connect(3000);
            var status = profile is null ? 0 : chargingMode ? DellProvider.SetChargingMode(profile) : DellProvider.SetProfile(profile);
            JsonSerializer.Serialize(pipe, new ElevatedReply(status, DellProvider.ReadSnapshot()));
            pipe.Flush();
            return status;
        }
        catch { return 22; }
    }

    public static async Task<ElevatedReply?> RequestAsync(string? profile, CancellationToken token, bool chargingMode = false)
    {
        if (profile is not null && !(chargingMode ? Metrics.ValidChargingMode(profile) : Metrics.ValidProfile(profile))) throw new ArgumentException("Unsupported Dell setting value.");
        var nonce = Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(Prefix + nonce, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var args = profile is null ? "--dell-read " + nonce : (chargingMode ? "--dell-charge " : "--dell-set ") + profile + " " + nonce;
        Process? child;
        try
        {
            child = await Task.Run(() => Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true, Verb = "runas", Arguments = args, WindowStyle = ProcessWindowStyle.Hidden
            }));
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223) { return null; }
        if (child is null) throw new InvalidOperationException("Could not start the Dell helper.");
        using (child)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                var connection = pipe.WaitForConnectionAsync(timeout.Token);
                var exit = child.WaitForExitAsync(timeout.Token);
                if (await Task.WhenAny(connection, exit) == exit && !connection.IsCompletedSuccessfully)
                    throw new InvalidOperationException("Dell helper exited before returning data.");
                await connection;
                var reply = await JsonSerializer.DeserializeAsync<ElevatedReply>(pipe, cancellationToken: timeout.Token);
                await exit;
                return reply ?? throw new InvalidOperationException("Dell helper returned no data.");
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException("Dell helper timed out. A requested change may have applied; read Dell settings to verify.");
            }
        }
    }

    public static string StatusText(int status, string? profile, bool chargingMode = false) => status switch
    {
        0 => profile is null ? "Dell settings read and timestamped." : (chargingMode ? "Charging mode verified: " + Metrics.ModeLabel(profile) : "Profile verified: " + Metrics.ProfileLabel(profile)) + ".",
        20 => "Administrator access is required.",
        21 => "This firmware does not allow that setting.",
        22 => "Firmware rejected the change or could not be reached. Read Dell settings to check the actual settings.",
        23 => "The setting read-back did not match. Read Dell settings to check the actual value.",
        24 => "Another Dell setting change is still running.",
        25 => "The settings helper timed out. Read Dell settings to verify the actual settings.",
        _ => "The Dell helper could not complete the operation."
    };
}
