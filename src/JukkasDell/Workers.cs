using System.Text.Json;

namespace JukkasDell;

public static class Workers
{
    public static async Task<Snapshot> SnapshotAsync(CancellationToken token)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            ArgumentList = { "--snapshot" }
        }};
        process.Start();
        // Drain both pipes immediately; bounded lifetime even if WMI ignores its own timeout.
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var json = await output;
            await error;
            if (process.ExitCode != 0) throw new InvalidOperationException("Device worker exited unexpectedly.");
            return JsonSerializer.Deserialize<Snapshot>(json) ?? throw new InvalidOperationException("No device data returned.");
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
            if (!token.IsCancellationRequested && timeout.IsCancellationRequested)
                throw new TimeoutException("Device reading took too long. Select Refresh to retry.");
            throw;
        }
    }

}
