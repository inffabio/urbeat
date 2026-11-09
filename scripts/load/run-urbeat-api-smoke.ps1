<#
.SYNOPSIS
    Runs the non-destructive k6 smoke test against an explicitly authorized urbeat API.

.DESCRIPTION
    Validates the target URL locally, checks that k6 is installed, and forwards the
    settings as environment variables to `urbeat-api-smoke.js`.

    The wrapper never installs tools, never sends credentials, and only runs GET
    requests to `/health` and, optionally, a public read-only path. It refuses
    non-loopback targets unless `-AllowRemote` is supplied.

.PARAMETER BaseUrl
    API origin, for example http://localhost:5000 or an authorized staging URL.

.PARAMETER PublicPath
    Optional safe public GET path, for example /api/public/plans.

.PARAMETER Vus
    Number of virtual users. Defaults to 1; keep it low for smoke tests.

.PARAMETER Duration
    k6 scenario duration, for example 30s or 2m. Defaults to 30s.

.PARAMETER SleepSeconds
    Pause between iterations. Defaults to 1 second to keep the test gentle.

.PARAMETER AllowRemote
    Required to target any non-loopback URL. Only use with explicit authorization.

.EXAMPLE
    ./run-urbeat-api-smoke.ps1

.EXAMPLE
    ./run-urbeat-api-smoke.ps1 -BaseUrl https://staging.example.com -AllowRemote -Vus 2 -Duration 30s -PublicPath /api/public/plans
#>

[CmdletBinding()]
param(
    [string]$BaseUrl = "http://localhost:5000",

    [string]$PublicPath = "",

    [ValidateRange(1, 50)]
    [int]$Vus = 1,

    [ValidatePattern('^\d+(s|m|h)$')]
    [string]$Duration = "30s",

    [ValidateRange(0, 60)]
    [int]$SleepSeconds = 1,

    [switch]$AllowRemote
)

$ErrorActionPreference = "Stop"

$normalizedBaseUrl = $BaseUrl.Trim().TrimEnd('/')
$parsedBaseUrl = $null

if (-not [Uri]::TryCreate($normalizedBaseUrl, [UriKind]::Absolute, [ref]$parsedBaseUrl)) {
    throw "BaseUrl must be an absolute http(s) URL. Received: '$BaseUrl'."
}

if ($parsedBaseUrl.Scheme -notin @("http", "https")) {
    throw "BaseUrl must use http or https. Received scheme: '$($parsedBaseUrl.Scheme)'."
}

if ($parsedBaseUrl.UserInfo) {
    throw "BaseUrl must not include credentials."
}

if ($parsedBaseUrl.Query -or $parsedBaseUrl.Fragment) {
    throw "BaseUrl must not include a query string or fragment."
}

$isLocalTarget = $parsedBaseUrl.IsLoopback -or $parsedBaseUrl.Host -in @("localhost", "127.0.0.1", "::1")

if (-not $isLocalTarget -and -not $AllowRemote) {
    throw "Refusing to target a non-loopback URL without -AllowRemote. Only run against URLs you are explicitly authorized to test."
}

if ($PublicPath) {
    if (-not $PublicPath.StartsWith('/')) {
        throw "PublicPath must start with '/'. Received: '$PublicPath'."
    }

    if ($PublicPath -match '\s') {
        throw "PublicPath must not contain whitespace."
    }
}

$k6Command = Get-Command k6 -ErrorAction SilentlyContinue
if (-not $k6Command) {
    throw "k6 is not installed or not on PATH. Install it from https://k6.io/docs/get-started/installation/ and rerun. This wrapper does not install tools automatically."
}

$scriptPath = Join-Path $PSScriptRoot "urbeat-api-smoke.js"
if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
    throw "Smoke script not found at '$scriptPath'."
}

$previousEnvironment = @{
    BASE_URL = $env:BASE_URL
    PUBLIC_PATH = $env:PUBLIC_PATH
    VUS = $env:VUS
    DURATION = $env:DURATION
    SLEEP_SECONDS = $env:SLEEP_SECONDS
}

$exitCode = 1

try {
    $env:BASE_URL = $normalizedBaseUrl
    $env:PUBLIC_PATH = $PublicPath
    $env:VUS = [string]$Vus
    $env:DURATION = $Duration
    $env:SLEEP_SECONDS = [string]$SleepSeconds

    $displayPublicPath = $PublicPath
    if (-not $displayPublicPath) {
        $displayPublicPath = "(not configured)"
    }

    Write-Host "urbeat API smoke test: base=$normalizedBaseUrl publicPath=$displayPublicPath vus=$Vus duration=$Duration" -ForegroundColor Cyan
    Write-Host "GET-only; health-only runs do not measure the order flow." -ForegroundColor Yellow

    & $k6Command.Source run $scriptPath
    $exitCode = $LASTEXITCODE
}
finally {
    foreach ($name in $previousEnvironment.Keys) {
        if ($null -eq $previousEnvironment[$name]) {
            Remove-Item -Path "Env:$name" -ErrorAction SilentlyContinue
        }
        else {
            Set-Item -Path "Env:$name" -Value $previousEnvironment[$name]
        }
    }
}

exit $exitCode
