param([string]$Repository = 'Nameless10000/SecretManager')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$installerFolder = Join-Path $projectRoot 'artifacts\installer'
$files = @('SecretManager-Setup.exe', 'SecretManager-Setup.sha256')
foreach ($name in $files) { if (-not (Test-Path -LiteralPath (Join-Path $installerFolder $name))) { throw "Missing $name; run Build-Installer.ps1 first." } }
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid repository name.' }

# Obtain the existing Git credential without logging it or persisting a token in the repository.
$env:GIT_TERMINAL_PROMPT = '0'
$credentialLines = "protocol=https`nhost=github.com`n`n" | git credential fill
if ($LASTEXITCODE -ne 0) { throw 'Sign in with Git Credential Manager before publishing.' }
$credential = @{}
foreach ($line in $credentialLines) { $pair = $line -split '=', 2; if ($pair.Count -eq 2) { $credential[$pair[0]] = $pair[1] } }
if (-not $credential.ContainsKey('password')) { throw 'GitHub credential unavailable.' }
$headers = @{ Authorization = 'Bearer ' + $credential['password']; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' }
$api = "https://api.github.com/repos/$Repository"
Push-Location $projectRoot
try {
    $commit = (git rev-parse HEAD).Trim()
    $existingRef = $null
    try { $existingRef = Invoke-RestMethod -Uri "$api/git/ref/tags/latest" -Headers $headers } catch { if ([int]$_.Exception.Response.StatusCode -ne 404) { throw } }
    if ($existingRef) {
        Invoke-RestMethod -Method Patch -Uri "$api/git/refs/tags/latest" -Headers $headers -ContentType 'application/json' -Body (@{sha=$commit;force=$true} | ConvertTo-Json) | Out-Null
    } else {
        Invoke-RestMethod -Method Post -Uri "$api/git/refs" -Headers $headers -ContentType 'application/json' -Body (@{ref='refs/tags/latest';sha=$commit} | ConvertTo-Json) | Out-Null
    }
    $body = @{
        tag_name='latest'; target_commitish=$commit; name='SecretManager — latest'; draft=$false; prerelease=$false
        body="Актуальный установщик Windows x64. Включает .NET и Windows App SDK. Предыдущий установщик заменяется, архив версий не хранится. SHA-256 находится в соседнем файле. Подробности запуска и проверки: README.md и WORK_LOG.md."
    } | ConvertTo-Json
    $release = $null
    try { $release = Invoke-RestMethod -Uri "$api/releases/tags/latest" -Headers $headers } catch { if ([int]$_.Exception.Response.StatusCode -ne 404) { throw } }
    if ($release) {
        $release = Invoke-RestMethod -Method Patch -Uri "$api/releases/$($release.id)" -Headers $headers -ContentType 'application/json' -Body $body
    } else {
        $release = Invoke-RestMethod -Method Post -Uri "$api/releases" -Headers $headers -ContentType 'application/json' -Body $body
    }
    foreach ($name in $files) {
        $oldAsset = $release.assets | Where-Object name -eq $name
        if ($oldAsset) { Invoke-RestMethod -Method Delete -Uri "$api/releases/assets/$($oldAsset.id)" -Headers $headers | Out-Null }
        $upload = "https://uploads.github.com/repos/$Repository/releases/$($release.id)/assets?name=$([uri]::EscapeDataString($name))"
        Invoke-RestMethod -Method Post -Uri $upload -Headers $headers -ContentType 'application/octet-stream' -InFile (Join-Path $installerFolder $name) | Out-Null
    }
    Write-Output $release.html_url
}
finally { $headers.Clear(); $credential.Clear(); $credentialLines = $null; Pop-Location }
