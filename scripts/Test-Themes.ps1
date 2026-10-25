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
    foreach ($name in @('result.json', 'failure.png')) {
        $previous = Join-Path $output $name
        if (Test-Path -LiteralPath $previous) { Remove-Item -LiteralPath $previous }
    }
    $env:SECRETMANAGER_THEME_CHECK_OUTPUT = $output
    $checkExecutable = Join-Path $projectRoot 'tests\SecretManager.ThemeChecks\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\SecretManager.ThemeChecks.exe'
    $process = Start-Process -FilePath $checkExecutable -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(45000)) { $process.Kill(); throw 'Theme checks exceeded 45 seconds.' }
    $report = Join-Path $output 'result.json'
    if (-not (Test-Path -LiteralPath $report)) { throw 'Theme checks produced no report.' }
    $result = Get-Content -Raw -LiteralPath $report | ConvertFrom-Json
    if ($process.ExitCode -ne 0 -or -not $result.Passed) { throw ($result | ConvertTo-Json -Depth 4) }
    $result | ConvertTo-Json
}
finally { Remove-Item Env:SECRETMANAGER_THEME_CHECK_OUTPUT -ErrorAction SilentlyContinue; Pop-Location }
