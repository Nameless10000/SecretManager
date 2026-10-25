param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$Output)
$ErrorActionPreference = 'Stop'
# Diagnostic rerun only, after a failed native check. Dumps stay on the ephemeral
# runner and are never uploaded: only the debugger's exception/stack text is logged.
$tools = Join-Path $PSScriptRoot '..\.tools\native-debug'
New-Item -ItemType Directory -Force -Path $tools | Out-Null
Invoke-WebRequest https://download.sysinternals.com/files/Procdump.zip -OutFile (Join-Path $tools 'procdump.zip')
Expand-Archive (Join-Path $tools 'procdump.zip') (Join-Path $tools 'procdump') -Force
$dumpFolder = Join-Path $Output 'native-dumps'
New-Item -ItemType Directory -Force -Path $dumpFolder | Out-Null
& (Join-Path $tools 'procdump\procdump64.exe') -accepteula -e 1 -f 'C000027B,E0434352' -ma -x $dumpFolder $Executable
$dump = Get-ChildItem -LiteralPath $dumpFolder -Filter '*.dmp' | Select-Object -First 1
if (-not $dump) { Write-Output 'Diagnostic rerun produced no crash dump.'; return }
$debugger = 'C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe'
if (-not (Test-Path -LiteralPath $debugger)) {
    $setup = Join-Path $tools 'winsdksetup.exe'
    Invoke-WebRequest https://go.microsoft.com/fwlink/?linkid=2272610 -OutFile $setup
    $install = Start-Process -FilePath $setup -ArgumentList '/features OptionId.WindowsDesktopDebuggers /quiet /norestart' -WindowStyle Hidden -Wait -PassThru
    if ($install.ExitCode -notin @(0, 3010)) { throw "Debugger installation failed: $($install.ExitCode)" }
}
$symbols = Join-Path $tools 'symbols'
& $debugger -y "srv*$symbols*https://msdl.microsoft.com/download/symbols" -z $dump.FullName -c '.exr -1; !analyze -v; q'
