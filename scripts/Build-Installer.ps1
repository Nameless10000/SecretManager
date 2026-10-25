param(
    [string]$InnoCompiler,
    [switch]$SkipTests,
    [switch]$SkipPublish
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot = Join-Path $projectRoot 'artifacts'
$publishFolder = Join-Path $artifactRoot 'publish'
$installerFolder = Join-Path $artifactRoot 'installer'
if (-not $InnoCompiler) {
    $candidates = @(
        (Join-Path $projectRoot '.tools\InnoSetup\ISCC.exe'),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 7\ISCC.exe'
    )
    $InnoCompiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $InnoCompiler) { throw 'Install Inno Setup 6/7 from https://jrsoftware.org/isdl.php or pass -InnoCompiler.' }

Push-Location $projectRoot
try {
    if (-not $SkipTests) {
        dotnet test tests/SecretManager.Core.Tests/SecretManager.Core.Tests.csproj -c Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Core tests failed; no installer was produced.' }
        & (Join-Path $PSScriptRoot 'Test-Themes.ps1')
    }
    if (-not $SkipPublish) {
        # Delete only this known generated subtree, after verifying its exact absolute location.
        $expectedPublish = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\publish'))
        if ([IO.Path]::GetFullPath($publishFolder) -ne $expectedPublish) { throw 'Unsafe publish path.' }
        if (Test-Path -LiteralPath $publishFolder) { Remove-Item -LiteralPath $publishFolder -Recurse -Force }
        dotnet publish src/SecretManager.App/SecretManager.App.csproj -c Release -p:Platform=x64 --self-contained true --no-restore -o $publishFolder
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed; no installer was produced.' }
    }
    foreach ($required in @('SecretManager.App.exe', 'SecretManager.App.pri', 'coreclr.dll', 'Microsoft.ui.xaml.dll', 'e_sqlite3.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishFolder $required))) { throw "Published file is missing: $required" }
    }
    New-Item -ItemType Directory -Force -Path $installerFolder | Out-Null
    # A fixed filename replaces the previous installer. No versioned installer archives.
    & $InnoCompiler '/Q' "/DPublishDir=$publishFolder" "/DOutputDir=$installerFolder" (Join-Path $projectRoot 'installer\SecretManager.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed.' }
    $installer = Join-Path $installerFolder 'SecretManager-Setup.exe'
    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $installerFolder 'SecretManager-Setup.sha256'), "$hash  SecretManager-Setup.exe`n")
    Get-Item -LiteralPath $installer | Select-Object FullName, Length
}
finally { Pop-Location }
