# Local test stacks for Requestarr, from PowerShell (the same commands as docker/dev.sh): QA (the owner's
# manual QA, the default) and dev (Claude Code's automated testing), side by side. Add -Stack dev (or
# --stack dev) to act on the dev stack; QA is used otherwise.
#
#   .\docker\dev.ps1 up [--ref <git ref>]            build Requestarr from the stack's branch (qa or dev; --ref for
#                                                    another commit), start everything, wait for health checks, run check
#   .\docker\dev.ps1 check                           health and status checks: Requestarr, its Discord bot, test backends
#   .\docker\dev.ps1 stacks                          both stacks side by side: branch and commit, version, ports, bot
#   .\docker\dev.ps1 down                            stop the stack (data kept)
#   .\docker\dev.ps1 reset-qa [--stopped]            clean slate for QA: remove Requestarr's channels and roles from the QA
#                                                    server, wipe its database and test backends, then start it again from
#                                                    qa (fast votes off). --stopped leaves it stopped. reset-uat still works
#   .\docker\dev.ps1 reset-dev [--stopped]           the same for the dev stack and the dev server (live tests: --stopped)
#   .\docker\dev.ps1 reset-test-backends             wipe the stack's Sonarr/Radarr test instances back to empty
#   .\docker\dev.ps1 init                            create docker\secrets.env from the template (run by up)
#   .\docker\dev.ps1 logs [service]                  follow logs
#   .\docker\dev.ps1 stop-backend sonarr|radarr      stop only that test instance (Requestarr alerts after 3 minutes)
#   .\docker\dev.ps1 start-backend sonarr|radarr     start it again (Requestarr says it's back)
#   .\docker\dev.ps1 fake-download movie "<title>"   make a title in the test Radarr look downloaded, like a real import
#   .\docker\dev.ps1 fake-download tv "<title>" <season>   the same for a season in the test Sonarr
#   .\docker\dev.ps1 fast-votes on|off|status        test timings for removal votes (vote 5 min, answer 5, reminder at 3)
#
# Needs Docker Desktop and Git, and Python 3 for check, the resets and fake-download. Secret values are
# read from docker\secrets.env and never printed. If PowerShell says running scripts is disabled, run once:
#   Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
# or use .\docker\dev.cmd, which runs this script whatever the policy is.

$ErrorActionPreference = 'Continue'
$Dir = $PSScriptRoot
$Repo = Split-Path $Dir -Parent
$Secrets = Join-Path $Dir 'secrets.env'
$Template = Join-Path $Dir 'secrets.env.example'

function Fail([string]$message) {
    Write-Host "dev.ps1: $message" -ForegroundColor Red
    exit 1
}

# -Stack/--stack and -Ref/--ref can go anywhere; everything else is the command and its arguments.
$Stack = if ($env:REQUESTARR_STACK) { $env:REQUESTARR_STACK } else { 'qa' }
$StackGiven = $false
$Ref = ''
$positional = New-Object System.Collections.ArrayList
for ($i = 0; $i -lt $args.Count; $i++) {
    $arg = [string]$args[$i]
    if ($arg -match '^--?stack$') {
        if ($i + 1 -ge $args.Count) { Fail '-Stack needs qa or dev' }
        $Stack = [string]$args[++$i]; $StackGiven = $true
    } elseif ($arg -match '^--?stack[:=](.+)$') {
        $Stack = $Matches[1]; $StackGiven = $true
    } elseif ($arg -match '^--?ref$') {
        if ($i + 1 -ge $args.Count) { Fail '-Ref needs a branch or commit' }
        $Ref = [string]$args[++$i]
    } else {
        [void]$positional.Add($arg)
    }
}
$Command = if ($positional.Count -gt 0) { [string]$positional[0] } else { '' }
# @() keeps it an array even with one argument (PowerShell would unwrap it to a string).
$Rest = @($positional | Select-Object -Skip 1)

function Require-Docker {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        Fail 'Docker is needed: install Docker Desktop and start it, then open a new PowerShell tab.'
    }

    docker info --format '{{.ServerVersion}}' 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Fail 'Docker Desktop is not running (or not reachable). Start it, wait until it says Engine running, and try again.'
    }
}

# Python 3 (python, or the py launcher). The Microsoft Store placeholder "python" doesn't count.
function Get-Python([switch]$Optional) {
    foreach ($candidate in @(@('python'), @('py', '-3'))) {
        if (Get-Command $candidate[0] -ErrorAction SilentlyContinue) {
            $extra = @($candidate | Select-Object -Skip 1)
            $version = & $candidate[0] @extra --version 2>&1
            if ($LASTEXITCODE -eq 0 -and "$version" -match '^Python 3') {
                # The comma keeps a one-item array an array (PowerShell would unwrap it).
                return ,$candidate
            }
        }
    }

    if ($Optional) { return $null }
    Fail 'Python 3 is needed for this command: install it from https://www.python.org/downloads/ (tick "Add python.exe to PATH"), then open a new PowerShell tab.'
}

function Invoke-Python([string[]]$arguments) {
    $python = Get-Python
    $extra = @($python | Select-Object -Skip 1)
    # Its output goes to the screen, so only the exit code is returned.
    & $python[0] @extra -I @arguments | Out-Host
    return $LASTEXITCODE
}

function Read-Secrets {
    if (-not (Test-Path $Secrets)) {
        Fail 'docker\secrets.env is missing: run .\docker\dev.ps1 init (or up) first.'
    }

    $values = @{}
    foreach ($line in Get-Content $Secrets) {
        if ($line -match '^\s*([A-Z0-9_]+)=(.*)$') {
            $values[$Matches[1]] = $Matches[2]
        }
    }

    return $values
}

# Runs a command with some values set as environment variables for it only (never printed).
function With-Env([hashtable]$vars, [scriptblock]$body) {
    foreach ($name in $vars.Keys) { Set-Item -Path "Env:$name" -Value ([string]$vars[$name]) }
    try { & $body } finally { foreach ($name in $vars.Keys) { Remove-Item -Path "Env:$name" -ErrorAction SilentlyContinue } }
}

function Value([hashtable]$secrets, [string]$name, [string]$default) {
    if ($secrets[$name]) { return $secrets[$name] } else { return $default }
}

# QA only: the versions staging has (what was last published for it, staged-versions.py), which QA's what's-new
# posts leave out (docs/development.md#version-posts). Empty before anything is published for staging.
function Staged-Versions {
    $python = Get-Python -Optional
    if (-not $python) {
        Write-Host "Python isn't installed, so the QA stack can't tell what staging has: its what's new shows every version."
        return ''
    }
    $extra = @($python | Select-Object -Skip 1)
    $out = & $python[0] @extra -I (Join-Path $Dir 'staged-versions.py')
    if ($LASTEXITCODE -ne 0) { return '' }
    return ("$out").Trim()
}

# What the chosen stack uses: its Compose project, ports, and its own Discord bot (QA: DISCORD_*, dev: DEV_DISCORD_*).
function Get-StackInfo([string]$which) {
    $s = Read-Secrets
    switch ($which) {
        'qa' {
            return @{
                Name = 'QA'; Project = 'requestarr-qa'; Secrets = $s
                Env = @{
                    REQUESTARR_STACK = 'qa'
                    STACK_REQUESTARR_PORT = (Value $s 'REQUESTARR_PORT' '29797')
                    STACK_SONARR_TEST_PORT = (Value $s 'SONARR_TEST_PORT' '28989')
                    STACK_RADARR_TEST_PORT = (Value $s 'RADARR_TEST_PORT' '27878')
                    STACK_DISCORD_BOT_TOKEN = $s['DISCORD_BOT_TOKEN']
                    STACK_DISCORD_APPLICATION_ID = $s['DISCORD_APPLICATION_ID']
                    STACK_DISCORD_GUILD_ID = $s['DISCORD_TEST_GUILD_ID']
                    STACK_DISCORD_ADMIN_ROLE_ID = $s['DISCORD_ADMIN_ROLE_ID']
                    # Set when the stack is built (Build-Image), the only time Requestarr's container is recreated.
                    REQUESTARR_STAGED_VERSIONS = ''
                }
            }
        }
        'dev' {
            return @{
                Name = 'Dev'; Project = 'requestarr-dev'; Secrets = $s
                Env = @{
                    REQUESTARR_STACK = 'dev'
                    STACK_REQUESTARR_PORT = (Value $s 'DEV_REQUESTARR_PORT' '39797')
                    STACK_SONARR_TEST_PORT = (Value $s 'DEV_SONARR_TEST_PORT' '38989')
                    STACK_RADARR_TEST_PORT = (Value $s 'DEV_RADARR_TEST_PORT' '37878')
                    STACK_DISCORD_BOT_TOKEN = $s['DEV_DISCORD_BOT_TOKEN']
                    STACK_DISCORD_APPLICATION_ID = $s['DEV_DISCORD_APPLICATION_ID']
                    STACK_DISCORD_GUILD_ID = $s['DEV_DISCORD_GUILD_ID']
                    STACK_DISCORD_ADMIN_ROLE_ID = $s['DEV_DISCORD_ADMIN_ROLE_ID']
                }
            }
        }
        default { Fail "-Stack is qa or dev (got '$which')" }
    }
}

function Compose {
    $info = $script:Info
    # Inside the script block $args would be the block's own (empty) arguments.
    $composeArgs = $args
    With-Env $info.Env {
        docker compose -p $info.Project -f (Join-Path $Dir 'compose.yaml') --env-file $Secrets @composeArgs
    }
    if ($LASTEXITCODE -ne 0) { Fail "docker compose $($args -join ' ') failed (exit $LASTEXITCODE)" }
}

# Builds requestarr:<stack> from the stack's branch as it is on GitHub (origin/qa or origin/dev), or from -Ref:
# committed files only, so what runs is exactly that commit. The image is labelled with the commit and version.
function Build-Image {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { Fail 'Git is needed to build the stack (https://git-scm.com/download/win).' }
    $ref = $Ref
    if (-not $ref) {
        git -C $Repo fetch -q origin $Stack 2>$null
        if ($LASTEXITCODE -ne 0) { Write-Host "Couldn't fetch origin/$Stack; using the copy already here." }
        $ref = "origin/$Stack"
    }
    if ($Stack -eq 'qa') {
        $script:Info.Env.REQUESTARR_STAGED_VERSIONS = Staged-Versions
        $staged = if ($script:Info.Env.REQUESTARR_STAGED_VERSIONS) { $script:Info.Env.REQUESTARR_STAGED_VERSIONS } else { 'none' }
        Write-Host "Versions staging has (QA's what's new leaves them out): $staged"
    }

    $sha = git -C $Repo rev-parse --verify -q "$ref^{commit}"
    if ($LASTEXITCODE -ne 0 -or -not $sha) { Fail "no branch or commit '$ref' (git fetch first?)" }
    $props = (git -C $Repo show "${sha}:src/Directory.Build.props") -join "`n"
    $version = if ($props -match '<AssemblyVersion>(\d+\.\d+\.\d+)') { $Matches[1] } else { '?' }
    Write-Host "Building requestarr:$Stack from $ref @ $($sha.Substring(0, 7)) (version $version)"

    # cmd's pipe keeps the archive binary (Windows PowerShell's pipe would turn it into text).
    Push-Location $Repo
    try {
        cmd /c "git archive --format=tar $sha | docker build --quiet -t requestarr:$Stack -f docker/Dockerfile --label org.opencontainers.image.revision=$sha --label org.opencontainers.image.ref.name=$ref --label org.opencontainers.image.version=$version - >NUL"
        $code = $LASTEXITCODE
    } finally { Pop-Location }
    if ($code -ne 0) { Fail 'the image build failed' }
}

function Init {
    if (Test-Path $Secrets) {
        Write-Host 'secrets.env already exists, leaving it as is'
        return
    }

    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $lines = foreach ($line in Get-Content $Template) {
        if ($line -match '^([A-Z0-9_]+)=GENERATE$') {
            $bytes = New-Object byte[] 16
            $rng.GetBytes($bytes)
            "$($Matches[1])=$(($bytes | ForEach-Object { $_.ToString('x2') }) -join '')"
        } else {
            $line
        }
    }

    # UTF-8 without a byte order mark, like dev.sh writes it.
    [System.IO.File]::WriteAllLines($Secrets, [string[]]$lines, (New-Object System.Text.UTF8Encoding $false))
    Write-Host 'Created docker\secrets.env with generated local test keys (gitignored)'
}

# Prints one status line (HTTP code and chosen JSON fields); returns $true for HTTP 200.
function Status-Line([string]$name, [string]$url, [string]$key, [string[]]$fields) {
    $code = 0
    $body = $null

    try {
        $response = Invoke-WebRequest -Uri $url -Headers @{ 'X-Api-Key' = $key } -UseBasicParsing -TimeoutSec 15
        $code = [int]$response.StatusCode
        $body = $response.Content
    } catch {
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode } else { $code = 0 }
    }

    $summary = '(no JSON)'
    if ($body) {
        try {
            # Windows PowerShell keeps a JSON array as one object unless it's enumerated like this.
            $items = ConvertFrom-Json -InputObject $body
            $parts = foreach ($item in $items) { ($fields | ForEach-Object { "$_=$($item.$_)" }) -join ', ' }
            $summary = $parts -join '; '
        } catch { }
    }

    Write-Host ('{0,-26} HTTP {1}  {2}' -f $name, $code, $summary)
    return ($code -eq 200)
}

function Requestarr-Name([string]$project) {
    return (docker ps --filter "label=com.docker.compose.project=$project" --filter 'label=com.docker.compose.service=requestarr' --format '{{.Names}}')
}

# Which branch and commit the running Requestarr was built from (the image labels Build-Image sets).
function Image-Line([string]$project) {
    $name = Requestarr-Name $project
    if (-not $name) { return 'not running' }
    # As JSON: Windows PowerShell drops the double quotes a Go template would need in its arguments.
    $labels = (docker inspect --format '{{json .Config.Labels}}' $name) -join '' | ConvertFrom-Json
    $revision = [string]$labels.'org.opencontainers.image.revision'
    if (-not $revision) { return 'unknown (not built by dev.ps1 or dev.sh)' }
    return "$($labels.'org.opencontainers.image.ref.name') @ $($revision.Substring(0, 7)) (version $($labels.'org.opencontainers.image.version'))"
}

function Check {
    $info = $script:Info
    $s = $info.Secrets
    $rq = "http://localhost:$($info.Env.STACK_REQUESTARR_PORT)"
    $sonarr = "http://localhost:$($info.Env.STACK_SONARR_TEST_PORT)"
    $radarr = "http://localhost:$($info.Env.STACK_RADARR_TEST_PORT)"
    $ok = $true

    Write-Host "== $($info.Name) stack (Compose project $($info.Project))"
    Write-Host ('{0,-26} {1}' -f 'built from', (Image-Line $info.Project))
    Write-Host ''
    Write-Host '== Containers'
    Compose ps --format 'table {{.Service}}\t{{.Status}}'
    Write-Host ''
    Write-Host "== Requestarr ($rq)"
    $ping = try { (Invoke-WebRequest -Uri "$rq/ping" -UseBasicParsing -TimeoutSec 10).Content -replace '\s', '' } catch { '(no answer)' }
    Write-Host ('{0,-26} {1}' -f 'ping', $ping)
    $ok = (Status-Line 'system/status' "$rq/api/v1/system/status" $s['REQUESTARR_API_KEY'] @('appName', 'instanceName', 'version', 'authentication')) -and $ok
    $ok = (Status-Line 'config/host (provisioned)' "$rq/api/v1/config/host" $s['REQUESTARR_API_KEY'] @('instanceName', 'authenticationMethod', 'authenticationRequired', 'username', 'logLevel', 'analyticsEnabled')) -and $ok
    $ok = (Status-Line 'health' "$rq/api/v1/health" $s['REQUESTARR_API_KEY'] @('source', 'type', 'message')) -and $ok
    Write-Host ('{0,-26} {1}' -f 'fast votes', (Fast-Votes-State))
    Write-Host ''
    Write-Host "== Discord ($($info.Name) bot and server)"
    if (Get-Python -Optional) {
        $code = With-Env $info.Env { Invoke-Python @((Join-Path $Dir 'discord-status.py'), $info.Project) }
        $ok = ($code -eq 0) -and $ok
    } else {
        Write-Host '(install Python 3 to see the bot and server here)'
    }
    Write-Host ''
    Write-Host '== Test backends'
    $ok = (Status-Line 'sonarr-test system/status' "$sonarr/api/v3/system/status" $s['SONARR_TEST_API_KEY'] @('appName', 'version', 'authentication')) -and $ok
    $ok = (Status-Line 'sonarr-test rootfolder' "$sonarr/api/v3/rootfolder" $s['SONARR_TEST_API_KEY'] @('path', 'accessible')) -and $ok
    $ok = (Status-Line 'radarr-test system/status' "$radarr/api/v3/system/status" $s['RADARR_TEST_API_KEY'] @('appName', 'version', 'authentication')) -and $ok
    $ok = (Status-Line 'radarr-test rootfolder' "$radarr/api/v3/rootfolder" $s['RADARR_TEST_API_KEY'] @('path', 'accessible')) -and $ok
    Write-Host ''

    if ($ok) { Write-Host "CHECK PASSED ($($info.Name) stack)" } else { Write-Host "CHECK FAILED ($($info.Name) stack)"; exit 1 }
}

function Stacks {
    Write-Host ('{0,-6} {1,-44} {2,-28} {3}' -f 'STACK', 'BUILT FROM', 'REQUESTARR', 'DISCORD BOT')
    foreach ($which in @('qa', 'dev')) {
        $info = Get-StackInfo $which
        $name = Requestarr-Name $info.Project
        $bot = '-'
        if ($name) {
            $ready = docker logs $name 2>&1 | ForEach-Object { "$_" } | Select-String -Pattern "Discord bot ready in server '[^']*'" | Select-Object -Last 1
            $bot = if ($ready) { $ready.Matches[0].Value -replace 'Discord bot ready in server', 'online in' } else { 'not connected' }
        }
        Write-Host ('{0,-6} {1,-44} {2,-28} {3}' -f $which, (Image-Line $info.Project), "http://localhost:$($info.Env.STACK_REQUESTARR_PORT)", $bot)
    }
}

function Reset-Test-Backends {
    $project = $script:Info.Project
    Compose rm -sf sonarr-test radarr-test test-backends-init
    docker volume rm "${project}_sonarr-test-config" "${project}_radarr-test-config" "${project}_test-media" 2>$null | Out-Null
    Compose up -d --wait sonarr-test radarr-test test-backends-init
    Write-Host "Test backends reset to empty ($($script:Info.Name) stack)"
}

function Reset-Stack([string]$mode) {
    $info = $script:Info
    Get-Python | Out-Null
    Compose rm -sf requestarr

    if ($info.Env.STACK_DISCORD_BOT_TOKEN -and $info.Env.STACK_DISCORD_GUILD_ID) {
        $code = With-Env $info.Env { Invoke-Python @((Join-Path $Dir 'reset-discord.py')) }
        if ($code -ne 0) { Fail "cleaning the $($info.Name) Discord server failed (see above)" }
    }

    docker volume rm "$($info.Project)_requestarr-config" 2>$null | Out-Null
    Reset-Test-Backends

    if ($mode -eq '--stopped') {
        Write-Host "$($info.Name) reset: everything is clean and Requestarr is stopped. Run .\docker\dev.ps1 -Stack $Stack up when done."
        return
    }

    Build-Image
    Compose up -d --wait
    Check
    Write-Host "$($info.Name) reset: clean and ready (fast votes off). The bot recreates #requestarr-admin and its panel on start."
}

function Backend-Service([string]$which) {
    switch ($which) {
        'sonarr' { return 'sonarr-test' }
        'radarr' { return 'radarr-test' }
        default { Fail 'say which test instance: sonarr or radarr' }
    }
}

# The stack's Requestarr container, and only if it is marked as a local test stack.
function Requestarr-Container {
    $name = Requestarr-Name $script:Info.Project
    if (-not $name) { Fail "the $($script:Info.Name) stack's Requestarr isn't running (.\docker\dev.ps1 -Stack $Stack up)" }

    $envs = docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' $name
    if (-not ($envs -match '^REQUESTARR_DEV_STACK=true$')) { Fail "$name isn't marked as a local test stack; fast votes stay off" }

    return $name
}

function Fast-Votes-State {
    $name = Requestarr-Name $script:Info.Project
    if (-not $name) { return 'unknown (Requestarr not running)' }
    docker exec $name test -e /config/dev-fast-votes 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { return 'ON (test timings, minutes)' } else { return 'off' }
}

function Fast-Votes([string]$state) {
    switch ($state) {
        'on' {
            $name = Requestarr-Container
            docker exec $name sh -c 'touch /config/dev-fast-votes'
            if ($LASTEXITCODE -ne 0) { Fail 'could not turn fast votes on' }
            Write-Host "Fast votes ON ($($script:Info.Name) stack): new removal votes close after 5 minutes; requesters have 5 minutes to answer, reminded at minute 3."
            Write-Host 'Requestarr picks it up within a minute and posts a "Dev: fast votes" notice in #requestarr-admin. Removals already open keep their times.'
            Write-Host 'It stays on until fast-votes off, or a reset (which always starts with the real settings).'
        }
        'off' {
            $name = Requestarr-Container
            docker exec $name sh -c 'rm -f /config/dev-fast-votes'
            if ($LASTEXITCODE -ne 0) { Fail 'could not turn fast votes off' }
            Write-Host 'Fast votes off: removal timings are back to the real settings. Requestarr posts a "Dev: fast votes" notice in #requestarr-admin within a minute.'
        }
        'status' { Write-Host "Fast votes ($($script:Info.Name) stack): $(Fast-Votes-State)" }
        default { Fail 'fast-votes on, off or status' }
    }
}

# reset-qa and reset-dev name their stack; refuse a -Stack that says otherwise.
function Only-Stack([string]$which) {
    if ($StackGiven -and $Stack -ne $which) { Fail "$Command acts on the $which stack (drop -Stack $Stack)" }
    $script:Stack = $which
}

function First-Rest { if ($Rest.Count -gt 0) { return $Rest[0] } else { return '' } }

switch ($Command) {
    'init' { Init }
    'up' {
        Require-Docker
        Init
        $script:Info = Get-StackInfo $Stack
        Build-Image
        Compose up -d --wait
        Check
    }
    'check' { Require-Docker; $script:Info = Get-StackInfo $Stack; Check }
    'stacks' { Require-Docker; Stacks }
    'down' { Require-Docker; $script:Info = Get-StackInfo $Stack; Compose down }
    'reset-test-backends' { Require-Docker; $script:Info = Get-StackInfo $Stack; Reset-Test-Backends }
    { $_ -in @('reset-qa', 'reset-uat') } { Require-Docker; Only-Stack 'qa'; $script:Info = Get-StackInfo $Stack; Reset-Stack (First-Rest) }
    'reset-dev' { Require-Docker; Only-Stack 'dev'; $script:Info = Get-StackInfo $Stack; Reset-Stack (First-Rest) }
    'logs' { Require-Docker; $script:Info = Get-StackInfo $Stack; Compose logs -f @Rest }
    'stop-backend' {
        Require-Docker
        $script:Info = Get-StackInfo $Stack
        $service = Backend-Service (First-Rest)
        Compose stop $service
        Write-Host "Stopped $service ($($script:Info.Name) stack). Requestarr checks every minute: after 3 minutes down it posts an alert in #requestarr-admin, new requests of that type say they're paused, and the instance waits in /admin requests. After 30 minutes the alert offers Announce to users."
    }
    'start-backend' {
        Require-Docker
        $script:Info = Get-StackInfo $Stack
        $service = Backend-Service (First-Rest)
        Compose start $service
        Write-Host "Started $service ($($script:Info.Name) stack). Within a minute Requestarr posts that it's back (if it had alerted) and requests work again."
    }
    'fake-download' {
        Require-Docker
        $script:Info = Get-StackInfo $Stack
        $s = $script:Info.Secrets
        $vars = @{
            RADARR_TEST_API_KEY = $s['RADARR_TEST_API_KEY']; SONARR_TEST_API_KEY = $s['SONARR_TEST_API_KEY']
            RADARR_TEST_PORT = $script:Info.Env.STACK_RADARR_TEST_PORT; SONARR_TEST_PORT = $script:Info.Env.STACK_SONARR_TEST_PORT
            REQUESTARR_COMPOSE_PROJECT = $script:Info.Project
        }
        $code = With-Env $vars { Invoke-Python (@((Join-Path $Dir 'fake-download.py')) + $Rest) }
        if ($code -ne 0) { exit $code }
    }
    'fast-votes' { Require-Docker; $script:Info = Get-StackInfo $Stack; Fast-Votes (First-Rest) }
    default {
        Get-Content $PSCommandPath | Select-Object -Skip 1 -First 25 | ForEach-Object { $_ -replace '^# ?', '' }
        exit 1
    }
}

exit 0
