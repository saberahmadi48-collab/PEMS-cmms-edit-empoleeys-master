<#
.SYNOPSIS
  PEMS -> Ubuntu VM (existing Docker Compose stack: pems-web + pems-sqlserver + pems-caddy). Run from the repository in VS Code / PowerShell.

.EXAMPLES
  .\deploy\deploy.ps1 -Action Package -DryRun      # build + verify the package; shows what would be shipped (no server contact)
  .\deploy\deploy.ps1 -Action Preflight            # read-only checks on the VM (docker, base image identity, env file, volumes, SQL, disk)
  .\deploy\deploy.ps1                              # full controlled update (confirms the target host first)
  .\deploy\deploy.ps1 -Action Status | Smoke | VerifyRuntime
  .\deploy\deploy.ps1 -Action Rollback             # recreate pems-web from the recorded previous image (database NOT reverted)
  .\deploy\deploy.ps1 -Action Backup               # fresh verified SQL backup (COPY_ONLY, CHECKSUM)
  .\deploy\deploy.ps1 -Action MigrationScript      # idempotent SQL script for REVIEW (no database connection is opened)
  .\deploy\deploy.ps1 -Action Migrate -SqlFile .\deploy\artifacts\x.sql -ConfirmReviewed   # explicit, guarded, needs a backup < 1 h old

.NOTES
  * Key-based SSH only (BatchMode, StrictHostKeyChecking=yes); no passwords are read, stored or sent. Target in deploy\targets.local.json (git-ignored).
  * Deployment never migrates the database, never runs 'compose down', never prunes images/volumes, never prints secrets.
  * The VM has no internet: the release image is built OFFLINE on the VM from the existing base image (see deploy\docker\Dockerfile.release).
#>
[CmdletBinding()]
param(
    [ValidateSet('Deploy', 'Package', 'Preflight', 'Status', 'Smoke', 'VerifyRuntime', 'Rollback', 'Reapply', 'BootCheck', 'AdoptEnv', 'PrepareVolumes', 'Backup', 'MigrationScript', 'Migrate', 'SyncKit')][string]$Action = 'Deploy',
    [string]$Target = 'default',
    [switch]$DryRun,
    [switch]$Yes,
    [switch]$RunTests,
    [string]$TestFilter = 'FullyQualifiedName~PartCatalogue|FullyQualifiedName~Organization|FullyQualifiedName~Deployment',
    [string]$KeysDir,                  # PrepareVolumes: directory ON THE VM holding the DataProtection key files to seed
    [string]$SqlFile,                  # Migrate: reviewed .sql file (local path)
    [switch]$ConfirmReviewed,
    [string]$FromMigration             # MigrationScript: last applied migration id, to also write a delta script
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
function Write-Step($m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Fail($m)       { Write-Host "ERROR: $m" -ForegroundColor Red; exit 1 }

# ---------------------------------------------------------------------------------------------- local-only actions
if ($Action -eq 'MigrationScript') {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'; $dir = Join-Path $repo 'deploy\artifacts'; New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $out = Join-Path $dir "pems-migrations-idempotent-$stamp.sql"
    Write-Step 'Generating an IDEMPOTENT migration script (no database connection is opened)'
    & dotnet ef migrations script --idempotent --project (Join-Path $repo 'PEMS.Migrations') --startup-project (Join-Path $repo 'PEMS.Web') -o $out
    if ($LASTEXITCODE -ne 0) { Fail 'dotnet ef failed (is the dotnet-ef tool installed?)' }
    $safe = $out -replace '\.sql$', '-sqlcmd-safe.sql'
    & (Join-Path $PSScriptRoot 'Convert-EfScript.ps1') -InputFile $out -OutputFile $safe     # raw SQL -> EXEC(N'...') (see the script's header for why)
    Write-Host "Raw EF script : $out"
    Write-Host "Apply THIS one: $safe   (review it, then -Action Migrate -SqlFile <this> -ConfirmReviewed)"
    if ($FromMigration) {
        $delta = Join-Path $dir "pems-migrations-delta-$stamp.sql"
        & dotnet ef migrations script $FromMigration --project (Join-Path $repo 'PEMS.Migrations') --startup-project (Join-Path $repo 'PEMS.Web') -o $delta
        if ($LASTEXITCODE -ne 0) { Fail 'dotnet ef (delta) failed' }
        Write-Host "Delta since $FromMigration : $delta"
    }
    Write-Host 'Procedure: 1) .\deploy\deploy.ps1 -Action Backup   2) review the -sqlcmd-safe script   3) -Action Migrate -SqlFile <file> -ConfirmReviewed   4) -Action Deploy. Application rollback does NOT undo schema changes.'
    return
}
if ($Action -eq 'Package') {
    if ($DryRun) { return & (Join-Path $PSScriptRoot 'Build-Package.ps1') -DryRun } else { return & (Join-Path $PSScriptRoot 'Build-Package.ps1') }
}

# ---------------------------------------------------------------------------------------------- target configuration
$cfgPath = Join-Path $PSScriptRoot 'targets.local.json'
if (-not (Test-Path $cfgPath)) { Fail 'deploy\targets.local.json not found. Copy deploy\targets.example.json to deploy\targets.local.json and fill in the verified host/user.' }
$cfg = (Get-Content $cfgPath -Raw | ConvertFrom-Json).$Target
if (-not $cfg) { Fail "target '$Target' not defined in targets.local.json" }
foreach ($k in 'host', 'user', 'port', 'rid') { if (-not $cfg.$k) { Fail "targets.local.json: '$Target.$k' is missing" } }
if ($cfg.host -match 'CHANGE_ME|<') { Fail 'targets.local.json still contains a placeholder host' }
$sshTarget = "$($cfg.user)@$($cfg.host)"
$sshOpts = @('-p', "$($cfg.port)", '-o', 'BatchMode=yes', '-o', 'StrictHostKeyChecking=yes', '-o', 'ConnectTimeout=10')
$scpOpts = @('-P', "$($cfg.port)", '-o', 'BatchMode=yes', '-o', 'StrictHostKeyChecking=yes', '-o', 'ConnectTimeout=10')
if ($cfg.identityFile) { $sshOpts += @('-i', $cfg.identityFile); $scpOpts += @('-i', $cfg.identityFile) }
$base  = if ($cfg.remoteBase) { $cfg.remoteBase } else { '/opt/pems' }
$kit   = "$base/deploy"; $state = "$base/deploy-state"; $incoming = "$base/incoming"
$ctl   = "bash $kit/remote/pems-docker.sh"

function Invoke-Remote([string]$Command, [switch]$Quiet) {
    # Windows PowerShell 5.1 turns native stderr into terminating errors under 'Stop'; capture it instead
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { $o = & ssh @sshOpts $sshTarget $Command 2>&1; $code = $LASTEXITCODE } finally { $ErrorActionPreference = $prev }
    $text = (($o | ForEach-Object { "$_" }) -join "`n")
    if (-not $Quiet -and $text) { Write-Host $text }
    return [pscustomobject]@{ Code = $code; Output = $text }
}
function Copy-ToRemote([string[]]$Paths, [string]$RemoteDir) {
    $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { $o = & scp @scpOpts -r @Paths "$sshTarget`:$RemoteDir" 2>&1; $code = $LASTEXITCODE } finally { $ErrorActionPreference = $prev }
    if ($code -ne 0) { Fail ("scp failed: " + (($o | ForEach-Object { "$_" }) -join ' ')) }
}
function Sync-Kit {
    Write-Step "syncing deployment kit to $sshTarget`:$kit (no root needed)"
    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("pems-kit-" + [guid]::NewGuid().ToString('N')); New-Item -ItemType Directory -Force -Path "$tmp\docker", "$tmp\remote" | Out-Null
    try {
        foreach ($f in 'Dockerfile.release', 'compose.pems-web.yml') { Copy-Item (Join-Path $PSScriptRoot "docker\$f") "$tmp\docker\$f" }
        # Linux consumers: LF only, UTF-8 without BOM
        $txt = (Get-Content (Join-Path $PSScriptRoot 'remote\pems-docker.sh') -Raw) -replace "`r`n", "`n"
        [System.IO.File]::WriteAllText("$tmp\remote\pems-docker.sh", $txt, (New-Object System.Text.UTF8Encoding($false)))
        $r = Invoke-Remote "mkdir -p $kit $state $incoming && chmod 700 $state" -Quiet; if ($r.Code -ne 0) { Fail "cannot prepare $kit : $($r.Output)" }
        Copy-ToRemote @("$tmp\docker", "$tmp\remote") $kit
        # the diagnostic probe (OCR + DataProtection proof) is built locally and installed beside the state
        $probeOut = Join-Path $tmp 'probe'
        & dotnet publish (Join-Path $repo 'tools\PemsLinuxProbe') -c Release -o $probeOut --nologo -v q | Out-Null
        if ($LASTEXITCODE -ne 0) { Fail 'probe publish failed' }
        Copy-Item (Join-Path $repo 'tools\PemsLinuxProbe\ocr_fixture.png') $probeOut -Force
        Copy-ToRemote @($probeOut) "$state/"      # directory copy (no wildcard: works with Windows OpenSSH and Git's scp)
    } finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
}
function Test-LocalAndSsh {
    function Row($n, $p, $d) { Write-Host ("  [{0}] {1,-30} {2}" -f $(if ($p) { 'PASS' } else { 'FAIL' }), $n, $d) -ForegroundColor $(if ($p) { 'Green' } else { 'Red' }); if (-not $p) { $script:lsOk = $false } }
    $script:lsOk = $true
    foreach ($t in 'ssh', 'scp', 'tar', 'dotnet') { Row "local: $t" ([bool](Get-Command $t -ErrorAction SilentlyContinue)) '' }
    $tcp = Test-NetConnection -ComputerName $cfg.host -Port ([int]$cfg.port) -WarningAction SilentlyContinue
    Row "network: $($cfg.host):$($cfg.port)" $tcp.TcpTestSucceeded $(if ($tcp.TcpTestSucceeded) { 'reachable' } else { 'UNREACHABLE (wrong network/VPN, VM off, firewall)' })
    if (-not $tcp.TcpTestSucceeded) { return $false }
    $r = Invoke-Remote 'echo pems-ssh-ok; uname -m' -Quiet
    Row 'ssh (key, strict host key)' ($r.Code -eq 0 -and $r.Output -match 'pems-ssh-ok') $(if ($r.Code -eq 0) { '' } else { ($r.Output -split "`n" | Select-Object -First 2) -join ' | ' })
    if ($r.Code -ne 0) { return $false }
    $arch = ($r.Output -split "`n")[-1].Trim(); $expect = switch ($arch) { 'x86_64' { 'linux-x64' } 'aarch64' { 'linux-arm64' } default { "unsupported($arch)" } }
    Row 'architecture matches package' ($expect -eq $cfg.rid) "VM=$arch -> $expect ; package rid=$($cfg.rid)"
    return $script:lsOk
}
function Confirm-Host([string]$What) {
    if ($Yes -or $DryRun) { return }
    $a = Read-Host "$What on $($cfg.host) ($sshTarget, port $($cfg.port))? Type the host name to confirm"
    if ($a -ne $cfg.host) { Fail 'host not confirmed - aborted.' }
}

Write-Host "Target '$Target' : $sshTarget  (port $($cfg.port), rid $($cfg.rid), base $base)"

# ---------------------------------------------------------------------------------------------- simple remote actions
switch ($Action) {
    'Preflight' {
        if (-not (Test-LocalAndSsh)) { Write-Host 'Preflight FAILED' -ForegroundColor Red; exit 1 }
        Sync-Kit
        $r = Invoke-Remote "$ctl preflight"; if ($r.Code -ne 0) { Write-Host 'Preflight FAILED' -ForegroundColor Red; exit 1 }; Write-Host 'Preflight PASSED' -ForegroundColor Green; return
    }
    { $_ -in 'Status', 'Smoke', 'VerifyRuntime', 'BootCheck' } { $map = @{ Status = 'status'; Smoke = 'smoke'; VerifyRuntime = 'verify-runtime'; BootCheck = 'boot-check' }; $r = Invoke-Remote "$ctl $($map[$Action])"; if ($r.Code -ne 0) { exit 1 }; return }
    'SyncKit'   { if (-not (Test-LocalAndSsh)) { exit 1 }; Sync-Kit; return }
    'Reapply'   {
        if (-not (Test-LocalAndSsh)) { exit 1 }
        if (-not $Yes) { $a = Read-Host "Recreate pems-web from the SAME image with the repository's Compose file on $($cfg.host)? (type 'reapply')"; if ($a -ne 'reapply') { Fail 'cancelled' } }
        # back up the Compose file currently on the VM BEFORE the kit sync overwrites it; reapply restores it if the new one is unhealthy
        $bk = "$state/compose-backups/compose.pems-web.yml." + (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
        $r = Invoke-Remote "mkdir -p $state/compose-backups && cp -p $kit/docker/compose.pems-web.yml $bk && echo backup=$bk" -Quiet; if ($r.Code -ne 0) { Fail "cannot back up the current Compose file: $($r.Output)" }
        Sync-Kit
        $r = Invoke-Remote "$ctl reapply $bk"; if ($r.Code -ne 0) { exit 1 }; return
    }
    'Rollback'  { if (-not $Yes) { $a = Read-Host "Recreate pems-web from the previous recorded image on $($cfg.host)? (type 'rollback')"; if ($a -ne 'rollback') { Fail 'cancelled' } }; $r = Invoke-Remote "$ctl rollback"; if ($r.Code -ne 0) { exit 1 }; return }
    'AdoptEnv'  { Confirm-Host 'Create the VM-local env file (mode 600) from the running pems-web container'; Sync-Kit; $r = Invoke-Remote "$ctl adopt-env"; if ($r.Code -ne 0) { exit 1 }; return }
    'PrepareVolumes' {
        Confirm-Host 'Create the persistent filestore / DataProtection-key volumes'; Sync-Kit
        $a = if ($KeysDir) { "prepare-volumes --keys-dir $KeysDir" } else { 'prepare-volumes' }
        $r = Invoke-Remote "$ctl $a"; if ($r.Code -ne 0) { exit 1 }; return
    }
    'Backup' { Confirm-Host 'Take a fresh verified SQL Server backup'; Sync-Kit; $r = Invoke-Remote "$ctl db-backup"; if ($r.Code -ne 0) { exit 1 }; return }
    'Migrate' {
        if (-not $SqlFile -or -not (Test-Path $SqlFile)) { Fail '-SqlFile <reviewed script> is required' }
        if (-not $ConfirmReviewed) { Fail 'refusing to apply a migration script without -ConfirmReviewed' }
        Confirm-Host "Apply $(Split-Path $SqlFile -Leaf) to PEMSDB"; Sync-Kit
        $r = Invoke-Remote "mkdir -p $state/migrations" -Quiet; Copy-ToRemote @($SqlFile) "$state/migrations/"
        $r = Invoke-Remote "$ctl db-apply $state/migrations/$(Split-Path $SqlFile -Leaf) --confirm-reviewed"; if ($r.Code -ne 0) { exit 1 }; return
    }
}

# ---------------------------------------------------------------------------------------------- Deploy
Write-Step '1/8 local + SSH preflight'
if (-not (Test-LocalAndSsh)) { Fail 'preflight failed - nothing was built, transferred or changed.' }
Write-Step '2/8 sync deployment kit + remote preflight (read-only)'
Sync-Kit
$r = Invoke-Remote "$ctl preflight"; if ($r.Code -ne 0) { Fail 'remote preflight failed - nothing was changed. Fix the FAIL lines above (see docs/deployment/LINUX_VM_DEPLOYMENT.md).' }
Confirm-Host 'Deploy a new PEMS release'

Write-Step '3/8 build + verify package'
if ($RunTests) { & dotnet test (Join-Path $repo 'PEMS.Tests') -c Release --filter $TestFilter --nologo; if ($LASTEXITCODE -ne 0) { Fail 'tests failed - deployment stopped.' } }
$pkg = if ($DryRun) { & (Join-Path $PSScriptRoot 'Build-Package.ps1') -Rid $cfg.rid -DryRun } else { & (Join-Path $PSScriptRoot 'Build-Package.ps1') -Rid $cfg.rid }
if ($DryRun) { Write-Host "DRY RUN complete: would upload pems-$($pkg.ReleaseId).tar.gz, build image pems-web:rel-$($pkg.ReleaseId) offline on the VM and recreate pems-web." -ForegroundColor Cyan; return }
$name = Split-Path $pkg.Archive -Leaf; $id = $pkg.ReleaseId; $commit = ($id -split '-', 2)[1]

Write-Step "4/8 transfer $name"
Copy-ToRemote @($pkg.Archive, "$($pkg.Archive).sha256") "$incoming/"
Write-Step '5/8 verify checksum, extract, verify manifest, build image OFFLINE on the VM'
$r = Invoke-Remote "set -e; cd $incoming; sha256sum -c $name.sha256; mkdir $id; tar -xzf $name -C $id; $ctl build $incoming/$id $id $commit"
if ($r.Code -ne 0) { Fail 'remote build failed - the running application was not touched.' }
Write-Step '6/8 controlled update (previous release kept as rollback target; automatic rollback on failed health)'
$r = Invoke-Remote "$ctl deploy $id"
if ($r.Code -ne 0) { Write-Host 'Deployment FAILED; the controller rolled back (see output). The database was not touched.' -ForegroundColor Red; exit 1 }
Write-Step '7/8 smoke tests (application + Caddy route)'
$r = Invoke-Remote "$ctl smoke"; if ($r.Code -ne 0) { Fail 'smoke tests failed - run -Action Rollback if the application is unhealthy.' }
Write-Step '8/8 runtime proofs (OCR extraction + DataProtection decrypt of the stored credential)'
$r = Invoke-Remote "$ctl export-cipher >/dev/null 2>&1; $ctl verify-runtime"; if ($r.Code -ne 0) { Write-Host 'WARNING: runtime proof incomplete (see output above).' -ForegroundColor Yellow }
Write-Host "Release $id is live on $($cfg.host)." -ForegroundColor Green
