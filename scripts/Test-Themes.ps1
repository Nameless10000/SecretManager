param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $projectRoot
try {
    if (-not $SkipBuild) {
        dotnet build tests/SecretManager.ThemeChecks/SecretManager.ThemeChecks.csproj -c Release -p:Platform=x64 --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Theme check build failed.' }
    }
    $output = Join-Path $projectRoot 'artifacts\theme-checks'
    New-Item -ItemType Directory -Force -Path $output | Out-Null
    foreach ($name in @('result.json', 'failure.png', 'teardown-stage.txt', 'native-error.txt')) {
        $previous = Join-Path $output $name
        if (Test-Path -LiteralPath $previous) { Remove-Item -LiteralPath $previous }
    }
    $env:SECRETMANAGER_THEME_CHECK_OUTPUT = $output
    $checkExecutable = Join-Path $projectRoot 'tests\SecretManager.ThemeChecks\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\SecretManager.ThemeChecks.exe'
    $process = Start-Process -FilePath $checkExecutable -WindowStyle Hidden -PassThru
    # Keep the native process handle while it is alive; some PowerShell/.NET versions
    # otherwise lose access to ExitCode after a short-lived WinUI process terminates.
    $null = $process.Handle
    if (-not $process.WaitForExit(45000)) { $process.Kill(); throw 'Theme checks exceeded 45 seconds.' }
    $process.WaitForExit()
    $exitCode = $process.ExitCode
    $report = Join-Path $output 'result.json'
    if (-not (Test-Path -LiteralPath $report)) { throw 'Theme checks produced no report.' }
    $result = Get-Content -Raw -LiteralPath $report | ConvertFrom-Json
    if ($exitCode -ne 0 -or -not $result.Passed) {
        $stage = Join-Path $output 'teardown-stage.txt'
        if (Test-Path -LiteralPath $stage) { Get-Content -LiteralPath $stage | Write-Output }
        $nativeError = Join-Path $output 'native-error.txt'
        if (Test-Path -LiteralPath $nativeError) { Get-Content -LiteralPath $nativeError | Write-Output }
        try {
            Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddMinutes(-5);Level=2} -ErrorAction Stop |
                Where-Object Message -Like '*SecretManager.ThemeChecks*' | Select-Object -First 2 -ExpandProperty Message | Write-Output
        } catch { Write-Output 'Windows crash diagnostics unavailable.' }
        throw ("Theme process exit code: $exitCode`n" + ($result | ConvertTo-Json -Depth 4))
    }
    $result | ConvertTo-Json
}
finally { Remove-Item Env:SECRETMANAGER_THEME_CHECK_OUTPUT -ErrorAction SilentlyContinue; Pop-Location }
