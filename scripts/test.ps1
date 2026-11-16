param([string]$Executable = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\app\JukkasDell.exe'))
$ErrorActionPreference = 'Stop'
function Invoke-AppCli([string]$Arguments) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $Executable
    $info.Arguments = $Arguments
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.CreateNoWindow = $true
    $p = [System.Diagnostics.Process]::Start($info)
    $out = $p.StandardOutput.ReadToEndAsync()
    $err = $p.StandardError.ReadToEndAsync()
    if (-not $p.WaitForExit(30000)) { $p.Kill(); throw 'CLI timed out.' }
    $code = $p.ExitCode
    $stdout = $out.GetAwaiter().GetResult()
    $stderr = $err.GetAwaiter().GetResult()
    $p.Dispose()
    [pscustomobject]@{ ExitCode = $code; Output = $stdout; Error = $stderr }
}
$test = Invoke-AppCli '--self-test'
Write-Host $test.Output
if ($test.ExitCode -ne 0) { throw 'Self-tests failed.' }
$invalid = Invoke-AppCli '--set-profile NotAProfile'
if ($invalid.ExitCode -ne 64) { throw 'Invalid setting was not rejected.' }
$invalidCharge = Invoke-AppCli '--set-charging-mode Custom'
if ($invalidCharge.ExitCode -ne 64) { throw 'Custom/threshold write was not rejected.' }
$power = Invoke-AppCli '--power-snapshot'
if ($power.ExitCode -ne 0 -or -not (($power.Output | ConvertFrom-Json).CapturedAt)) { throw 'Malformed native battery response.' }
Write-Host ('Native battery reading: ' + $power.Output)
$snapshot = Invoke-AppCli '--snapshot'
if ($snapshot.ExitCode -ne 0) { throw 'Snapshot worker failed.' }
$data = $snapshot.Output | ConvertFrom-Json
if (-not $data.CapturedAt -or -not $data.Errors) { throw 'Malformed device snapshot.' }
Write-Host "Snapshot read at $($data.CapturedAt); elevated=$($data.Elevated)"
Write-Host "Model: $($data.Firmware.Model); profile: $($data.Firmware.Profile)"
Write-Host "Battery: $($data.Battery.FullWh) / $($data.Battery.DesignWh) Wh; cycles $($data.Battery.Cycles)"
Write-Host "Charging mode: $($data.Battery.Mode); supported: $($data.Battery.Modes -join ', '); writable=$($data.Battery.ModeWritable)"
Write-Host 'PASS: read-only hardware smoke test. No firmware changes made.'
