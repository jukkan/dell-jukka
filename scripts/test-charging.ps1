param([switch]$AllowFirmwareWrite)
$ErrorActionPreference='Stop'
if (-not $AllowFirmwareWrite) {throw 'This optional integration test changes a charging mode and restores it. Invoke explicitly with -AllowFirmwareWrite.'}
if ($env:COMPUTERNAME -ine 'JukkaXPS15') {throw 'Live charging test is restricted to JukkaXPS15.'}
$principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {throw 'Run this explicit hardware test from an elevated developer shell.'}
$exe=Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\app\JukkasDell.exe'
function Invoke-Cli([string]$arguments) {
 $info=New-Object System.Diagnostics.ProcessStartInfo
 $info.FileName=$exe;$info.Arguments=$arguments;$info.UseShellExecute=$false
 $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true;$info.CreateNoWindow=$true
 $p=[System.Diagnostics.Process]::Start($info)
 $out=$p.StandardOutput.ReadToEndAsync();$err=$p.StandardError.ReadToEndAsync()
 try {
  if (-not $p.WaitForExit(45000)) {$p.Kill();throw 'Hardware test command timed out; actual state must be checked.'}
  [pscustomobject]@{Code=$p.ExitCode;Text=$out.GetAwaiter().GetResult();Error=$err.GetAwaiter().GetResult()}
 } finally {$p.Dispose()}
}
$beforeReply=Invoke-Cli '--snapshot'
if ($beforeReply.Code -ne 0) {throw 'Cannot capture original charging state.'}
$before=$beforeReply.Text | ConvertFrom-Json
$original=$before.Battery.Mode
if ($original -notin @('Adaptive','Standard','Express','PrimAcUse') -or -not $before.Battery.ModeWritable) {throw 'Original mode is outside the tested restore allowlist, or is not writable.'}
$helper=Invoke-Cli '--charge-helper-test'
if ($helper.Code -ne 0) {throw 'Charging helper IPC test failed.'}
Write-Host $helper.Text
$alternate=if ($original -eq 'Standard') {'Adaptive'} else {'Standard'}
if ($alternate -notin $before.Battery.Modes) {throw 'Alternate mode not advertised.'}
try {
 $change=Invoke-Cli ("--set-charging-mode "+$alternate)
 if ($change.Code -ne 0) {throw "Charging write failed with status $($change.Code)."}
 $observed=Invoke-Cli '--snapshot'
 if ($observed.Code -ne 0 -or ($observed.Text | ConvertFrom-Json).Battery.Mode -ne $alternate) {throw 'Independent charging read-back failed.'}
 Write-Host "Verified charging change: $original -> $alternate"
} finally {
 $restore=Invoke-Cli ("--set-charging-mode "+$original)
 if ($restore.Code -ne 0) {throw "Original mode restoration not verified (status $($restore.Code)); read Dell settings now."}
 $afterReply=Invoke-Cli '--snapshot'
 if ($afterReply.Code -ne 0) {throw 'Final independent snapshot failed; restoration requires checking.'}
 $after=$afterReply.Text | ConvertFrom-Json
 if ($after.Battery.Mode -ne $original) {throw 'Final independent snapshot did not show original mode.'}
 if ($after.Firmware.Profile -ne $before.Firmware.Profile) {throw 'Thermal profile differs from the original; investigate external changes.'}
 Write-Host "Verified restoration: $($after.Battery.Mode); thermal profile unchanged: $($after.Firmware.Profile)"
}
Write-Host 'PASS: explicit charging-mode round trip and restoration. Custom mode/thresholds untouched.'
