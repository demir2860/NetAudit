# NetAudit Pro Launcher - No Console Window
$exePath = Join-Path $PSScriptRoot "dist\NetAudit.UI.exe"

if (-Not (Test-Path $exePath)) {
    [System.Windows.Forms.MessageBox]::Show("NetAudit.UI.exe not found at $exePath", "Error", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error)
    exit 1
}

$pinfo = New-Object System.Diagnostics.ProcessStartInfo
$pinfo.FileName = $exePath
$pinfo.UseShellExecute = $false
$pinfo.RedirectStandardOutput = $false
$pinfo.RedirectStandardError = $false
$pinfo.CreateNoWindow = $true
$pinfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden

$process = [System.Diagnostics.Process]::Start($pinfo)
