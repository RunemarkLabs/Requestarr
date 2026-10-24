# Local dev/test stack for Requestarr, from PowerShell (the same commands as docker/dev.sh).
#
#   .\docker\dev.ps1 up                              build and start everything, wait for health checks, run check
#   .\docker\dev.ps1 check                           health and status checks for Requestarr and the test backends
#   .\docker\dev.ps1 down                            stop the stack (data kept)
#   .\docker\dev.ps1 reset-test-backends             wipe the Sonarr/Radarr test instances back to empty and restart them
#   .\docker\dev.ps1 reset-uat [--stopped]           clean slate: remove Requestarr's Discord channels and roles, wipe its
#                                                    database and the test backends, then start it again (UAT ready, fast
#                                                    votes off). --stopped leaves Requestarr stopped, for the live Discord test
#   .\docker\dev.ps1 init                            create docker\secrets.env from the template (run by up)
#   .\docker\dev.ps1 logs [service]                  follow logs
#   .\docker\dev.ps1 stop-backend sonarr|radarr      stop only that test instance (Requestarr alerts after 3 minutes)
#   .\docker\dev.ps1 start-backend sonarr|radarr     start it again (Requestarr says it's back)
#   .\docker\dev.ps1 fake-download movie "<title>"   make a title in the test Radarr look downloaded, like a real import
#   .\docker\dev.ps1 fake-download tv "<title>" <season>   the same for a season in the test Sonarr
#   .\docker\dev.ps1 fast-votes on|off|status        dev only: removal votes in minutes (vote 5, answer 5, reminder at 3)
#
# Needs Docker Desktop, and Python 3 for reset-uat and fake-download. Secret values are read from
# docker\secrets.env and never printed. If PowerShell says running scripts is disabled, run once:
#   Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
# or use .\docker\dev.cmd, which runs this script whatever the policy is.

$ErrorActionPreference = 'Continue'
$Dir = $PSScriptRoot
$Secrets = Join-Path $Dir 'secrets.env'
$Template = Join-Path $Dir 'secrets.env.example'
$Project = 'requestarr-dev'
$Command = if ($args.Count -gt 0) { [string]$args[0] } else { '' }
# @() keeps it an array even with one argument (PowerShell would unwrap it to a string).
$Rest = @($args | Select-Object -Skip 1)

function Fail([string]$message) {
    Write-Host "dev.ps1: $message" -ForegroundColor Red
    exit 1
}

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
function Get-Python {
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

# Runs a command with some secrets set as environment variables for it only (never printed).
function With-Env([hashtable]$vars, [scriptblock]$body) {
    foreach ($name in $vars.Keys) { Set-Item -Path "Env:$name" -Value $vars[$name] }
    try { & $body } finally { foreach ($name in $vars.Keys) { Remove-Item -Path "Env:$name" -ErrorAction SilentlyContinue } }
}

function Compose {
    docker compose -f (Join-Path $Dir 'compose.yaml') --env-file $Secrets @args
    if ($LASTEXITCODE -ne 0) { Fail "docker compose $($args -join ' ') failed (exit $LASTEXITCODE)" }
}

function Port([hashtable]$secrets, [string]$name, [string]$default) {
    if ($secrets[$name]) { return $secrets[$name] } else { return $default }
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

function Check {
    $s = Read-Secrets
    $rq = "http://localhost:$(Port $s 'REQUESTARR_PORT' '9797')"
    $sonarr = "http://localhost:$(Port $s 'SONARR_TEST_PORT' '18989')"
    $radarr = "http://localhost:$(Port $s 'RADARR_TEST_PORT' '17878')"
    $ok = $true

    Write-Host '== Containers'
    Compose ps --format 'table {{.Service}}\t{{.Status}}'
    Write-Host ''
    Write-Host '== Requestarr'
    $ping = try { (Invoke-WebRequest -Uri "$rq/ping" -UseBasicParsing -TimeoutSec 10).Content -replace '\s', '' } catch { '(no answer)' }
    Write-Host ('{0,-26} {1}' -f 'ping', $ping)
    $ok = (Status-Line 'system/status' "$rq/api/v1/system/status" $s['REQUESTARR_API_KEY'] @('appName', 'instanceName', 'version', 'authentication')) -and $ok
    $ok = (Status-Line 'config/host (provisioned)' "$rq/api/v1/config/host" $s['REQUESTARR_API_KEY'] @('instanceName', 'authenticationMethod', 'authenticationRequired', 'username', 'logLevel', 'analyticsEnabled')) -and $ok
    $ok = (Status-Line 'health' "$rq/api/v1/health" $s['REQUESTARR_API_KEY'] @('source', 'type', 'message')) -and $ok
    Write-Host ('{0,-26} {1}' -f 'fast votes (dev)', (Fast-Votes-State))
    Write-Host ''
    Write-Host '== Test backends'
    $ok = (Status-Line 'sonarr-test system/status' "$sonarr/api/v3/system/status" $s['SONARR_TEST_API_KEY'] @('appName', 'version', 'authentication')) -and $ok
    $ok = (Status-Line 'sonarr-test rootfolder' "$sonarr/api/v3/rootfolder" $s['SONARR_TEST_API_KEY'] @('path', 'accessible')) -and $ok
    $ok = (Status-Line 'radarr-test system/status' "$radarr/api/v3/system/status" $s['RADARR_TEST_API_KEY'] @('appName', 'version', 'authentication')) -and $ok
    $ok = (Status-Line 'radarr-test rootfolder' "$radarr/api/v3/rootfolder" $s['RADARR_TEST_API_KEY'] @('path', 'accessible')) -and $ok
    Write-Host ''

    if ($ok) { Write-Host 'CHECK PASSED' } else { Write-Host 'CHECK FAILED'; exit 1 }
}

function Reset-Test-Backends {
    Compose rm -sf sonarr-test radarr-test test-backends-init
    docker volume rm "${Project}_sonarr-test-config" "${Project}_radarr-test-config" "${Project}_test-media" 2>$null | Out-Null
    Compose up -d --wait sonarr-test radarr-test test-backends-init
    Write-Host 'Test backends reset to empty'
}

function Reset-Uat([string]$mode) {
    $s = Read-Secrets
    Get-Python | Out-Null
    Compose rm -sf requestarr

    $code = With-Env @{ DISCORD_BOT_TOKEN = $s['DISCORD_BOT_TOKEN']; DISCORD_TEST_GUILD_ID = $s['DISCORD_TEST_GUILD_ID'] } {
        Invoke-Python @((Join-Path $Dir 'reset-discord.py'))
    }
    if ($code -ne 0) { Fail 'cleaning the Discord test server failed (see above)' }

    docker volume rm "${Project}_requestarr-config" 2>$null | Out-Null
    Reset-Test-Backends

    if ($mode -eq '--stopped') {
        Write-Host 'UAT reset: everything is clean and Requestarr is stopped. Run .\docker\dev.ps1 up when done.'
        return
    }

    Compose up -d --build --wait
    Check
    Write-Host 'UAT reset: clean and UAT ready (fast votes off). The bot recreates #requestarr-admin and its panel on start.'
}

function Backend-Service([string]$which) {
    switch ($which) {
        'sonarr' { return 'sonarr-test' }
        'radarr' { return 'radarr-test' }
        default { Fail 'say which test instance: sonarr or radarr' }
    }
}

# The dev stack's Requestarr container, and only if it is marked as the dev stack.
function Requestarr-Container {
    $name = docker ps --filter "label=com.docker.compose.project=$Project" --filter 'label=com.docker.compose.service=requestarr' --format '{{.Names}}'
    if (-not $name) { Fail "the dev stack's Requestarr isn't running (.\docker\dev.ps1 up)" }

    $envs = docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' $name
    if (-not ($envs -match '^REQUESTARR_DEV_STACK=true$')) { Fail "$name isn't marked as the dev stack; fast votes stay off" }

    return $name
}

function Fast-Votes-State {
    $name = docker ps --filter "label=com.docker.compose.project=$Project" --filter 'label=com.docker.compose.service=requestarr' --format '{{.Names}}'
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
            Write-Host 'Fast votes ON (dev stack only): new removal votes close after 5 minutes; requesters have 5 minutes to answer, reminded at minute 3.'
            Write-Host 'Requestarr picks it up within a minute and posts a "Dev: fast votes" notice in #requestarr-admin. Removals already open keep their times.'
            Write-Host 'It stays on until .\docker\dev.ps1 fast-votes off, or reset-uat (which always starts with the real settings).'
        }
        'off' {
            $name = Requestarr-Container
            docker exec $name sh -c 'rm -f /config/dev-fast-votes'
            if ($LASTEXITCODE -ne 0) { Fail 'could not turn fast votes off' }
            Write-Host 'Fast votes off: removal timings are back to the real settings. Requestarr posts a "Dev: fast votes" notice in #requestarr-admin within a minute.'
        }
        'status' { Write-Host "Fast votes: $(Fast-Votes-State)" }
        default { Fail 'fast-votes on, off or status' }
    }
}

switch ($Command) {
    'init' { Init }
    'up' {
        Require-Docker
        Init
        Compose up -d --build --wait
        Check
    }
    'check' { Require-Docker; Check }
    'down' { Require-Docker; Compose down }
    'reset-test-backends' { Require-Docker; Reset-Test-Backends }
    'reset-uat' { Require-Docker; Reset-Uat $(if ($Rest.Count -gt 0) { $Rest[0] } else { '' }) }
    'logs' { Require-Docker; Compose logs -f @Rest }
    'stop-backend' {
        Require-Docker
        $service = Backend-Service $(if ($Rest.Count -gt 0) { $Rest[0] } else { '' })
        Compose stop $service
        Write-Host "Stopped $service. Requestarr checks every minute: after 3 minutes down it posts an alert in #requestarr-admin, new requests of that type say they're paused, and the instance waits in /admin requests. After 30 minutes the alert offers Announce to users."
    }
    'start-backend' {
        Require-Docker
        $service = Backend-Service $(if ($Rest.Count -gt 0) { $Rest[0] } else { '' })
        Compose start $service
        Write-Host "Started $service. Within a minute Requestarr posts that it's back (if it had alerted) and requests work again."
    }
    'fake-download' {
        Require-Docker
        $s = Read-Secrets
        $vars = @{
            RADARR_TEST_API_KEY = $s['RADARR_TEST_API_KEY']; SONARR_TEST_API_KEY = $s['SONARR_TEST_API_KEY']
            RADARR_TEST_PORT = (Port $s 'RADARR_TEST_PORT' '17878'); SONARR_TEST_PORT = (Port $s 'SONARR_TEST_PORT' '18989')
        }
        $code = With-Env $vars { Invoke-Python (@((Join-Path $Dir 'fake-download.py')) + $Rest) }
        if ($code -ne 0) { exit $code }
    }
    'fast-votes' { Require-Docker; Fast-Votes $(if ($Rest.Count -gt 0) { $Rest[0] } else { '' }) }
    default {
        Get-Content $PSCommandPath | Select-Object -Skip 1 -First 20 | ForEach-Object { $_ -replace '^# ?', '' }
        exit 1
    }
}

exit 0
