<#
.SYNOPSIS
  Builds the clean PEMS runtime package (publish output only - never the source tree) for a Linux target.

.DESCRIPTION
  1. dotnet publish PEMS.Web (Release, framework-dependent, explicit RID).
  2. Removes everything that must not reach the runtime directory (see $Exclusions - each rule is justified).
  3. Scans the result for secrets / forbidden artifacts and FAILS the build if any are found.
  4. Writes release-manifest.json (+ per-file SHA-256) and packs pems-<release>.tar.gz + .sha256.
  -DryRun does steps 1-3 into a staging folder and prints what would be transferred, without creating the archive.

  Nothing here touches a server, a database or Git.
#>
[CmdletBinding()]
param(
    [string]$Rid = 'linux-x64',
    [string]$OutDir,                    # default: <repo>\deploy\artifacts
    [switch]$DryRun,
    [switch]$SkipPublish                # reuse an existing staging folder (diagnostics only)
)
$ErrorActionPreference = 'Stop'
$repo  = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$web   = Join-Path $repo 'PEMS.Web\PEMS.Web.csproj'
if (-not $OutDir) { $OutDir = Join-Path $repo 'deploy\artifacts' }

# ---- release identity -------------------------------------------------------------------------------------------
$commit = ''; $dirty = ''
try {
    $commit = (git -C $repo rev-parse --short=9 HEAD 2>$null)
    if ($LASTEXITCODE -ne 0) { $commit = '' }
    if ($commit) { if ((git -C $repo status --porcelain 2>$null | Measure-Object).Count -gt 0) { $dirty = '-dirty' } }
} catch { $commit = '' }
if (-not $commit) { $commit = 'nogit' }
$stamp     = (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')
$releaseId = "$stamp-$commit$dirty"

$stage = Join-Path $OutDir "staging\$releaseId"
if ($SkipPublish) { $last = Get-ChildItem (Join-Path $OutDir 'staging') -Directory | Sort-Object Name | Select-Object -Last 1; $stage = $last.FullName; $releaseId = $last.Name }

# ---- 1. publish -----------------------------------------------------------------------------------------------------
if (-not $SkipPublish) {
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    Write-Host "Publishing PEMS.Web ($Rid, Release, framework-dependent) ..."
    & dotnet publish $web -c Release -r $Rid --self-contained false -p:UseAppHost=false -p:DebugType=None -p:DebugSymbols=false -o $stage --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }
}

# ---- 1b. non-secret environment settings -----------------------------------------------------------------------------
# env/*.env ship (the server selects one with PEMS_ENV); env/*.secrets.env and the *.example templates never do.
$envSource = Join-Path $repo 'env'
if (Test-Path $envSource) {
    $envStage = Join-Path $stage 'env'
    New-Item -ItemType Directory -Force -Path $envStage | Out-Null
    Get-ChildItem $envSource -Filter '*.env' -File | Where-Object { $_.Name -notlike '*.secrets.env' } | Copy-Item -Destination $envStage
}

# ---- 2. exclusions (runtime-requirement based) --------------------------------------------------------------------
# Each rule = relative path/glob, reason. Anything not listed ships. Order does not matter.
$Exclusions = @(
    @{ Path = 'wwwroot\uploads';                Reason = 'USER DATA from the developer machine (documents, signatures, intake PDFs); the server keeps its own persistent uploads on the pems-uploads Docker volume.' },
    @{ Path = 'filestore';                      Reason = 'USER DATA (engineering/part-catalogue files); persistent on the server, never part of a release.' },
    @{ Path = 'logs';                           Reason = 'runtime logs of the developer machine.' },
    @{ Path = 'appsettings.Development.json';   Reason = 'development-only settings.' },
    @{ Path = 'appsettings.UAT.json';           Reason = 'Azure UAT profile; not used on this target (ASPNETCORE_ENVIRONMENT=Production).' },
    @{ Path = '*.pdb';                          Reason = 'debug symbols (also suppressed at publish time).' }
)
if ($Rid -like 'linux-*') {
    # Verified by inspecting the publish output: x64\ and x86\ hold only Windows PE DLLs (tesseract50.dll, leptonica-1.82.0.dll) - never loadable on Linux.
    $Exclusions += @{ Path = 'x64'; Reason = 'Windows-only native OCR DLLs (PE format); Linux OCR uses system libraries (see runbook).' }
    $Exclusions += @{ Path = 'x86'; Reason = 'Windows-only native OCR DLLs (PE format).' }
}
$removed = @()
foreach ($rule in $Exclusions) {
    $targets = @(Get-ChildItem -Path (Join-Path $stage $rule.Path) -Force -ErrorAction SilentlyContinue)
    if ($rule.Path -like '*`**') { $targets = @(Get-ChildItem -Path $stage -Recurse -Force -Filter $rule.Path -ErrorAction SilentlyContinue) }
    foreach ($t in $targets) {
        if ($t.PSIsContainer) {
            $n = @(Get-ChildItem $t.FullName -Recurse -File -Force).Count
            # Windows-only guard for the OCR folders: only drop them if every file is really a PE binary
            if ($rule.Path -in 'x64','x86') {
                foreach ($f in (Get-ChildItem $t.FullName -Recurse -File)) {
                    $b = [System.IO.File]::ReadAllBytes($f.FullName)[0..1]
                    if (-not ($b[0] -eq 0x4D -and $b[1] -eq 0x5A)) { throw "Refusing to exclude $($f.FullName): not a Windows PE file." }
                }
            }
            Remove-Item $t.FullName -Recurse -Force
            $removed += [pscustomobject]@{ Path = $rule.Path; Files = $n; Reason = $rule.Reason }
        } else {
            Remove-Item $t.FullName -Force
            $removed += [pscustomobject]@{ Path = $t.Name; Files = 1; Reason = $rule.Reason }
        }
    }
}
# keep an empty uploads directory so the installer can replace it with the persistent symlink deterministically
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'wwwroot\uploads') | Out-Null

# ---- 3. package verification (fail closed) ----------------------------------------------------------------------------
$problems = @()
$all = Get-ChildItem $stage -Recurse -File -Force
foreach ($f in $all) {
    $rel = $f.FullName.Substring($stage.Length).TrimStart('\')
    if ($f.Extension -in '.pfx', '.p12', '.key', '.pem', '.bak', '.mdf', '.ldf', '.log', '.user', '.sqlite', '.db') { $problems += "forbidden artifact: $rel" }
    if ($rel -like 'wwwroot\uploads\*' -or $rel -like 'filestore\*') { $problems += "user data in package: $rel" }
    if ($rel -like '*.git*' -or $rel -like '.claude*' -or $rel -like '.vs*') { $problems += "dev artifact: $rel" }
    if ($f.Extension -in '.json', '.config', '.xml', '.yml', '.yaml', '.env' -and $f.Length -lt 200KB) {
        $txt = Get-Content $f.FullName -Raw -ErrorAction SilentlyContinue
        # a literal "Password=..." in a connection string, a non-empty JSON password/secret/api-key value, or a private key block
        if ($txt -match '(?im)\b(password|pwd)\s*=\s*[^;"\s<{$][^;"\r\n]*') { $problems += "possible literal password in: $rel" }
        if ($txt -match '(?i)"[A-Za-z_:]*(password|secret|apikey|api_key|token)[A-Za-z_]*"\s*:\s*"[^"]+"') { $problems += "non-empty secret-like value in: $rel" }
        if ($txt -match '-----BEGIN [A-Z ]*PRIVATE KEY-----') { $problems += "private key material in: $rel" }
    }
}
# the dev connection string in appsettings.json is a LOCAL-WINDOWS value (integrated security); it is overridden on the server by
# ConnectionStrings__PEMSConnection and reported (not a secret, but it must not be mistaken for the Linux setting).
$devConn = $false
$aj = Join-Path $stage 'appsettings.json'
if (Test-Path $aj) { $devConn = ((Get-Content $aj -Raw) -match 'SQLEXPRESS|Trusted_Connection') }
foreach ($required in 'PEMS.Web.dll', 'PEMS.Web.runtimeconfig.json', 'PEMS.Web.deps.json', 'appsettings.json', 'wwwroot\css', 'wwwroot\js', 'fa\PEMS.Web.resources.dll', 'LatoFont') {
    if (-not (Test-Path (Join-Path $stage $required))) { $problems += "missing required runtime item: $required" }
}
if ($problems.Count -gt 0) { Write-Host "PACKAGE VERIFICATION FAILED:" -ForegroundColor Red; $problems | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }; throw 'Package verification failed (nothing was archived).' }

# ---- 4. manifest ---------------------------------------------------------------------------------------------------------
$files = Get-ChildItem $stage -Recurse -File -Force | Sort-Object FullName
$entries = foreach ($f in $files) { [pscustomobject]@{ path = $f.FullName.Substring($stage.Length).TrimStart('\').Replace('\', '/'); size = $f.Length; sha256 = (Get-FileHash $f.FullName -Algorithm SHA256).Hash.ToLower() } }
$tfm = ([regex]::Match((Get-Content (Join-Path $stage 'PEMS.Web.runtimeconfig.json') -Raw), '"tfm"\s*:\s*"([^"]+)"')).Groups[1].Value
$fw  = ([regex]::Match((Get-Content (Join-Path $stage 'PEMS.Web.runtimeconfig.json') -Raw), '"version"\s*:\s*"([^"]+)"')).Groups[1].Value
$manifest = [ordered]@{
    releaseId = $releaseId; commit = $commit; workingTreeDirty = [bool]$dirty
    builtUtc = (Get-Date).ToUniversalTime().ToString('o'); builtOn = $env:COMPUTERNAME
    targetFramework = $tfm; requiredRuntime = "Microsoft.AspNetCore.App >= $fw"; rid = $Rid; selfContained = $false
    fileCount = $entries.Count; totalBytes = ($files | Measure-Object Length -Sum).Sum
    excluded = $removed
    notes = @('appsettings.json carries a LOCAL dev connection string; production must set ConnectionStrings__PEMSConnection via /etc/pems/pems.env.',
              'No database migration is performed by deployment.')
    files = $entries
}
$manifestPath = Join-Path $stage 'release-manifest.json'
($manifest | ConvertTo-Json -Depth 6) | Set-Content -Path $manifestPath -Encoding UTF8
Write-Host ''
Write-Host "Release:   $releaseId  (commit $commit$dirty)"
Write-Host "Target:    $Rid, $tfm, framework-dependent ($($manifest.requiredRuntime))"
Write-Host ("Package:   {0} files, {1:N1} MB (publish output only - no source, no tests, no .git)" -f $entries.Count, (($files | Measure-Object Length -Sum).Sum / 1MB))
Write-Host 'Excluded from the raw publish output:'
$removed | Group-Object Path | ForEach-Object { Write-Host ("  - {0,-30} {1,5} file(s)  {2}" -f $_.Name, ($_.Group | Measure-Object Files -Sum).Sum, $_.Group[0].Reason) }
if ($devConn) { Write-Host 'NOTE: appsettings.json still has the LOCAL dev connection string -> the server MUST override ConnectionStrings__PEMSConnection (env file).' -ForegroundColor Yellow }

if ($DryRun) { Write-Host "`nDRY RUN: staging at $stage ; no archive created, nothing transferred." -ForegroundColor Cyan; return [pscustomobject]@{ ReleaseId = $releaseId; Staging = $stage; Archive = $null } }

# ---- 5. archive + checksum ------------------------------------------------------------------------------------------------
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$archive = Join-Path $OutDir "pems-$releaseId.tar.gz"
# prefer Windows' own bsdtar: when started from Git Bash, a bare 'tar' is GNU tar, which treats 'D:\...' as a remote host
$tarExe = Join-Path $env:SystemRoot 'System32\tar.exe'; if (-not (Test-Path $tarExe)) { $tarExe = 'tar' }
& $tarExe -czf $archive -C $stage .
if ($LASTEXITCODE -ne 0) { throw "tar failed (exit $LASTEXITCODE)" }
$sha = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLower()
"$sha  pems-$releaseId.tar.gz" | Set-Content -Path "$archive.sha256" -Encoding ASCII
Write-Host ("`nArchive:   {0}  ({1:N1} MB)`nSHA-256:   {2}" -f $archive, ((Get-Item $archive).Length / 1MB), $sha)
return [pscustomobject]@{ ReleaseId = $releaseId; Staging = $stage; Archive = $archive; Sha256 = $sha }
