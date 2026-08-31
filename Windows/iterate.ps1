<#
.SYNOPSIS
    One iteration of Ghozt: bump, test, build the MSI, commit and push.

.DESCRIPTION
    The standing rule is that every iteration ships a full MSI and reaches the repository. Doing
    those by hand is how one of them gets skipped, so they are one command.

    Order matters and is enforced:

      1. Bump the version in App\Directory.Build.props.
      2. Regenerate brand assets, run the tests, publish self-contained, package the MSI.
         A failing test stops here and produces no installer.
      3. Commit the source and push.

    The MSI is built BEFORE the commit deliberately. A commit that claims to be an iteration but
    whose code does not package is worse than no commit: the history then contains versions that
    were never buildable, and the next person cannot tell which.

.PARAMETER Message
    The commit subject. Required - an iteration without a stated reason is not reviewable.

.PARAMETER Bump
    patch (default), minor, major, or none to rebuild the current version.

.PARAMETER NoPush
    Commit but do not push. For working offline; the push still has to happen later.

.EXAMPLE
    .\iterate.ps1 -Message "Add the Replay workspace timeline"
    .\iterate.ps1 -Message "Fix the inspector contrast" -Bump none
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Message,
    [ValidateSet('major', 'minor', 'patch', 'none')][string]$Bump = 'patch',
    [switch]$NoPush
)

$ErrorActionPreference = 'Stop'

# git reports routinely on stderr - line-ending notices, push progress, branch advice - and none of
# it means failure. PowerShell 5.1 raises NativeCommandError for ANY native stderr output while
# $ErrorActionPreference is 'Stop', so a successful `git add` that mentioned CRLF conversion aborted
# the whole iteration.
#
# Relaxing the preference for the duration of the call is the only form that reliably suppresses it -
# redirecting the stream or assigning it to a variable does not. The exit code is the real signal and
# is checked immediately after, so nothing is being swallowed: a genuine git failure still throws.
function Invoke-Git {
    param([string[]]$Arguments)

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & git @Arguments 2>&1
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }

    foreach ($line in $output) { Write-Host "    $line" }
    if ($code -ne 0) { throw "git $($Arguments[0]) failed with exit code $code" }
}


$productRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$propsPath = Join-Path $productRoot 'App\Directory.Build.props'

# ---------------------------------------------------------------- version

[xml]$props = Get-Content $propsPath
$current = $props.Project.PropertyGroup.Version
if (-not $current) { throw "No Version in $propsPath" }

$parts = $current.Split('.')
if ($parts.Count -ne 3) { throw "Version '$current' is not major.minor.patch" }

switch ($Bump) {
    'major' { $next = "$([int]$parts[0] + 1).0.0" }
    'minor' { $next = "$($parts[0]).$([int]$parts[1] + 1).0" }
    'patch' { $next = "$($parts[0]).$($parts[1]).$([int]$parts[2] + 1)" }
    'none'  { $next = $current }
}

if ($next -ne $current) {
    Write-Host "==> Version $current -> $next" -ForegroundColor Cyan

    # Rewritten as text rather than through the XML DOM: Save() reformats the whole file and the
    # diff then hides the one line that actually changed.
    $text = Get-Content $propsPath -Raw
    $text = $text -replace "<Version>$([regex]::Escape($current))</Version>", "<Version>$next</Version>"
    $text = $text -replace "<AssemblyVersion>[^<]*</AssemblyVersion>", "<AssemblyVersion>$next.0</AssemblyVersion>"
    $text = $text -replace "<FileVersion>[^<]*</FileVersion>", "<FileVersion>$next.0</FileVersion>"
    Set-Content $propsPath $text -NoNewline -Encoding UTF8
} else {
    Write-Host "==> Version stays at $current" -ForegroundColor Cyan
}

# ---------------------------------------------------------------- build

& (Join-Path $productRoot 'Windows\Installer\msi\build-msi.ps1') -Version $next
if ($LASTEXITCODE -ne 0) { throw "installer build failed - nothing committed" }

$msi = Join-Path $productRoot "Windows\artifacts\Ghozt-$next.msi"
if (-not (Test-Path $msi)) { throw "expected $msi but it is not there" }

# ---------------------------------------------------------------- commit and push

Push-Location $productRoot
try {
    # Explicit paths only. `git add -A` has swept other agents' in-flight edits and embedded
    # repositories into commits on this machine.
    Invoke-Git @('add', 'App', 'Blueprints', 'Windows/Installer', 'Windows/iterate.ps1', '.gitignore', 'AGENTS.md', 'HANDOFF.md', 'docs')

    $staged = & git diff --cached --name-only
    if (-not $staged) {
        Write-Host "==> Nothing to commit" -ForegroundColor Yellow
    }
    else {
        $size = "{0:N1} MB" -f ((Get-Item $msi).Length / 1MB)
        $body = @"
$Message

Ghozt $next - installer built and verified before commit.
Artifact: Ghozt-$next.msi ($size, self-contained win-x64).
"@
        Invoke-Git @('commit', '-m', $body)
    }

    if (-not $NoPush) {
        Write-Host "==> Pushing to origin" -ForegroundColor Cyan
        Invoke-Git @('push', 'origin', 'HEAD')
    }
}
finally {
    Pop-Location
}

Write-Host ""
Write-Host "==> Iteration $next complete" -ForegroundColor Green
Write-Host "    $msi"
Write-Host ""
Write-Host "    Still to do by hand: publish to the registry." -ForegroundColor Yellow
Write-Host "    php artisan app-store:publish-artefact ghozt $next <file> --channel=private ..."
