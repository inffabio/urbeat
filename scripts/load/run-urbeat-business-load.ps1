<#
.SYNOPSIS
    Runs the deep, mutable k6 business-flow load test for an explicitly authorized
    urbeat TEST environment.

.DESCRIPTION
    Safety contract:
    - Default mode is Plan: local validation only, no network calls, no k6 run.
    - Run mode requires -BusinessTestAuthorized and -Cleanup, plus -AllowRemote for
      any non-loopback target.
    - Known production urbeat hostnames are always rejected, even with -AllowRemote.
    - The wrapper never prints access tokens, cookies, passwords, pool contents, or
      admin tokens. Pool files are read locally and passed through environment
      variables; they are never copied to the artifacts directory.
    - No e-mail, WhatsApp, upload, webhook, or online-payment endpoint is called.
      Synthetic e-mails use a reserved .invalid domain. Orders use pay-on-delivery
      cash only; /api/payments is never called.
    - Store creation also enqueues the backend OSM neighborhood importer, so store
      scenarios require -AllowNeighborhoodImport as an explicit attestation that the
      test environment disables or mocks that business queue job.
    - Cleanup is a read-only SQL inventory plan. The wrapper never deletes data.
      It intentionally fails closed instead of guessing a destructive cleanup.

.PARAMETER BaseUrl
    API origin. Defaults to http://localhost:5000. Production urbeat hostnames are
    rejected. A non-loopback target requires -AllowRemote.

.PARAMETER Mode
    Plan (default), Preflight (GET-only checks), or Run (execute k6).

.PARAMETER AllowRemote
    Required for any non-loopback target. Only use with explicit authorization.

.PARAMETER BusinessTestAuthorized
    Required for Run. Confirms the target is not production and mutable testing is
    authorized.

.PARAMETER Cleanup
    Required for Run. Acknowledges that the operator has a manual cleanup/rollback
    plan. The wrapper writes a read-only inventory plan; it never deletes data.

.PARAMETER AllowSyntheticAccountCreation
    Allows the script to create confirmed synthetic Customer identities through
    /api/checkout/customer-session and promote them to Seller. This is required when
    no seller/customer pool is supplied. It never creates an unconfirmed identity,
    so no confirmation e-mail is queued.

.PARAMETER AllowNeighborhoodImport
    Required for runs that create seller or anchor stores. PUT /api/stores/{id}/address
    enqueues the backend ImportStoreNeighborhoodsJob (OpenStreetMap importer). This
    switch is an explicit attestation that the test environment disables or mocks that
    business queue job. The script also sends address coordinates so backend geocoding
    is skipped.

.PARAMETER SellerStatusSmoke
    Optional. Creates one pickup order per seller VU and moves it Received -> Preparing.

.PARAMETER Vus
    Total target virtual users. Default 5; maximum 300.

.PARAMETER SellerVus
    Seller onboarding VUs. Omit to derive a small slice (10% for VUS >= 5).

.PARAMETER CustomerVus
    Customer order VUs. Omit to receive the remaining VUs.

.PARAMETER Duration
    k6 hold duration at the customer target. Default 30s.

.PARAMETER RampUpDuration
    Customer ramp-up duration. Default 30s.

.PARAMETER SellerMaxDuration
    Maximum duration for the per-VU seller onboarding scenario. Default 10m.

.PARAMETER FulfillmentType
    pickup (default) or delivery. Delivery requires a compatible target store and
    neighborhood; pickup avoids delivery-area dependencies.

.PARAMETER TargetStoreId
    Optional existing open store with at least one available product. When omitted,
    setup creates one isolated anchor store identified by RUN_ID.

.PARAMETER BootstrapStoreId
    Optional existing store used only by /api/checkout/customer-session to create
    confirmed synthetic accounts. If omitted, the wrapper discovers a public store.

.PARAMETER SellerPoolPath
    Optional JSON file containing [{ "email": "...", "password": "..." }] for
    pre-provisioned confirmed sellers. The file must contain at least VUS entries.
    When the first account is reserved for an anchor store, VUS + 1 entries are
    required. Contents are never printed or copied.

.PARAMETER CustomerPoolPath
    Optional JSON file for pre-provisioned confirmed customers.

.PARAMETER AnchorSellerEmail
    Optional dedicated seller email for setup's anchor store. Its password must be
    supplied through URBEAT_LOAD_ANCHOR_SELLER_PASSWORD.

.PARAMETER RunId
    Synthetic data prefix. Generated when omitted. Allowed: ^[a-z0-9][a-z0-9-]{2,31}$.

.PARAMETER CleanupPlanPath
    Optional output path for the read-only cleanup inventory SQL.

.PARAMETER SummaryExportPath
    Optional k6 summary export path.

.PARAMETER K6Path
    Optional explicit k6 executable path. Defaults to k6 on PATH.

.EXAMPLE
    ./run-urbeat-business-load.ps1

.EXAMPLE
    ./run-urbeat-business-load.ps1 -Mode Preflight -BaseUrl http://localhost:5000

.EXAMPLE
    ./run-urbeat-business-load.ps1 -Mode Run -BaseUrl https://staging.example.com `
        -AllowRemote -BusinessTestAuthorized -Cleanup -AllowSyntheticAccountCreation `
        -Vus 10 -Duration 30s
#>

[CmdletBinding()]
param(
    [string]$BaseUrl = "http://localhost:5000",

    [ValidateSet("Plan", "Preflight", "Run")]
    [string]$Mode = "Plan",

    [switch]$AllowRemote,

    [switch]$BusinessTestAuthorized,

    [switch]$Cleanup,

    [switch]$AllowSyntheticAccountCreation,

    [switch]$AllowNeighborhoodImport,

    [switch]$SellerStatusSmoke,

    [switch]$SkipCustomerReadback,

    [switch]$CustomerSessionPerIteration,

    [switch]$AllowOutboxAdminRead,

    [ValidateRange(1, 300)]
    [int]$Vus = 5,

    [int]$SellerVus = -1,

    [int]$CustomerVus = -1,

    [ValidatePattern('^\d+(s|m|h)$')]
    [string]$Duration = "30s",

    [ValidatePattern('^\d+(s|m|h)$')]
    [string]$RampUpDuration = "30s",

    [ValidatePattern('^\d+(s|m|h)$')]
    [string]$SellerMaxDuration = "10m",

    [ValidatePattern('^\d+(s|m|h)$')]
    [string]$RequestTimeout = "15s",

    [ValidateRange(0, 60)]
    [int]$SleepSeconds = 1,

    [ValidateSet("pickup", "delivery")]
    [string]$FulfillmentType = "pickup",

    [string]$TargetStoreId = "",

    [string]$BootstrapStoreId = "",

    [string]$TargetDeliveryNeighborhood = "",

    [string]$TargetDeliveryCity = "São Paulo",

    [string]$SellerPoolPath = "",

    [string]$CustomerPoolPath = "",

    [string]$AnchorSellerEmail = "",

    [string]$K6Path = "",

    [string]$RunId = "",

    [string]$CleanupPlanPath = "",

    [string]$SummaryExportPath = ""
)

$ErrorActionPreference = "Stop"

function Write-Section {
    param([string]$Text)
    Write-Host ""
    Write-Host $Text -ForegroundColor Cyan
}

function Test-GuidText {
    param([string]$Value)
    return $Value -match '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$'
}

function New-RunIdValue {
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $bytes = New-Object byte[] 4
        $random.GetBytes($bytes)
    }
    finally {
        $random.Dispose()
    }

    $suffix = -join ($bytes | ForEach-Object { $_.ToString("x2") })
    return ("load-" + (Get-Date -Format "yyyyMMddHHmmss") + "-" + $suffix).ToLowerInvariant()
}

function New-SyntheticPasswordValue {
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $bytes = New-Object byte[] 18
        $random.GetBytes($bytes)
    }
    finally {
        $random.Dispose()
    }

    $hex = -join ($bytes | ForEach-Object { $_.ToString("x2") })
    return "Urbeat!" + $hex + "Aa1"
}

function Read-PoolFileValue {
    param(
        [string]$Path,
        [string]$Name,
        [int]$MinimumCount
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Name was not found at '$Path'."
    }

    $raw = Get-Content -LiteralPath $Path -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) {
        throw "$Name is empty."
    }

    # Windows environment blocks are limited. Pool files are meant for small,
    # pre-provisioned pools; large runs should use synthetic account creation.
    if ($raw.Length -gt 30000) {
        throw "$Name is too large for a Windows environment variable. Use synthetic account creation or a smaller pool."
    }

    try {
        $parsed = $raw.TrimStart([char]0xFEFF).Trim() | ConvertFrom-Json
    }
    catch {
        throw "$Name is not valid JSON."
    }

    $trimmedRaw = $raw.TrimStart([char]0xFEFF).Trim()
    if (-not $trimmedRaw.StartsWith("[")) {
        throw "$Name must be a JSON array of { email, password } objects."
    }

    $items = @($parsed)
    if ($items.Count -lt $MinimumCount) {
        throw "$Name must contain at least $MinimumCount entries for the requested VU count."
    }

    $index = 0
    foreach ($item in $items) {
        $index++
        if ($null -eq $item -or [string]::IsNullOrWhiteSpace([string]$item.email) -or [string]::IsNullOrWhiteSpace([string]$item.password)) {
            throw "$Name entry $index must contain non-empty email and password."
        }

        $email = ([string]$item.email).Trim().ToLowerInvariant()
        $at = $email.LastIndexOf("@")
        $domain = if ($at -ge 0) { $email.Substring($at + 1) } else { "" }
        if ($domain -eq "urbeat.com.br" -or $domain.EndsWith(".urbeat.com.br")) {
            throw "$Name must not contain production urbeat e-mail accounts."
        }
    }

    return $raw
}

function Write-CleanupPlanValue {
    param(
        [string]$Path,
        [string]$RunIdValue,
        [string]$EmailDomain
    )

    $parent = Split-Path -Parent $Path
    if ($parent -and -not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    $content = @"
-- urbeat business load cleanup inventory
-- RUN_ID: $RunIdValue
-- READ-ONLY PLAN. The wrapper and k6 script never delete anything.
--
-- Synthetic identity e-mails:
--   load+$RunIdValue+%@$EmailDomain
-- Synthetic store slugs:
--   load-$RunIdValue-%
-- Synthetic names/categories/products include the literal prefix:
--   [$RunIdValue]
--
-- Review every row with the environment owner before deleting anything.
-- There is no public API to delete stores or identities.
--
-- 1. Inventory identities created by this run.
SELECT 'identity' AS entity, count(*) AS total
FROM "AspNetUsers"
WHERE lower("Email") LIKE 'load+$RunIdValue+%@$EmailDomain';

-- 2. Inventory stores created by this run.
SELECT 'store' AS entity, count(*) AS total
FROM "Stores"
WHERE "Slug" LIKE 'load-$RunIdValue-%';

-- 3. Inventory products created by this run.
SELECT 'product' AS entity, count(*) AS total
FROM "Products" p
JOIN "Stores" s ON s."Id" = p."StoreId"
WHERE s."Slug" LIKE 'load-$RunIdValue-%';

-- 4. Inventory orders placed against stores created by this run.
SELECT 'order' AS entity, count(*) AS total
FROM "Orders" o
JOIN "Stores" s ON s."Id" = o."StoreId"
WHERE s."Slug" LIKE 'load-$RunIdValue-%';

-- 5. Inventory customer addresses belonging to synthetic identities.
SELECT 'customer_address' AS entity, count(*) AS total
FROM "CustomerAddresses" ca
JOIN "AspNetUsers" u ON u."Id" = ca."UserId"
WHERE lower(u."Email") LIKE 'load+$RunIdValue+%@$EmailDomain';

-- Destructive statements are intentionally omitted.
-- If the environment owner approves deletion, build a reviewed transaction that
-- scopes every statement to the RUN_ID e-mail and slug patterns above. Never
-- delete by a broad name or by a date range. Outbox messages, notifications,
-- audit logs, and SignalR deliveries may not carry RUN_ID and must be reviewed
-- separately.
"@

    $encoding = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($Path, $content, $encoding)
}

function Invoke-UrbeatGet {
    param(
        [string]$FullUri
    )

    try {
        $response = Invoke-WebRequest -Uri $FullUri -Method Get -UseBasicParsing -TimeoutSec 15 -MaximumRedirection 0
        return [pscustomobject]@{
            StatusCode = [int]$response.StatusCode
            Content    = $response.Content
            Error      = $null
        }
    }
    catch {
        $status = 0
        if ($_.Exception.Response) {
            try { $status = [int]$_.Exception.Response.StatusCode } catch { $status = 0 }
        }

        return [pscustomobject]@{
            StatusCode = $status
            Content    = $_.Exception.Message
            Error      = $true
        }
    }
}

function Assert-JsonStatus {
    param(
        [pscustomobject]$Response,
        [string]$Path,
        [int]$ExpectedStatus
    )

    if ($Response.StatusCode -ne $ExpectedStatus) {
        throw "GET $Path returned HTTP $($Response.StatusCode). Expected $ExpectedStatus."
    }

    try {
        return $Response.Content | ConvertFrom-Json
    }
    catch {
        throw "GET $Path did not return valid JSON."
    }
}

# Base URL validation -----------------------------------------------------------

$normalizedBaseUrl = $BaseUrl.Trim().TrimEnd("/")
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

if ($parsedBaseUrl.AbsolutePath -and $parsedBaseUrl.AbsolutePath -ne "/") {
    throw "BaseUrl must be an origin without a path. Received: '$BaseUrl'."
}

$hostName = $parsedBaseUrl.Host.Trim().ToLowerInvariant()
$productionHosts = @("urbeat.com.br", "api.urbeat.com.br", "www.urbeat.com.br")
if ($productionHosts -contains $hostName -or $hostName.EndsWith(".urbeat.com.br")) {
    throw "Refusing production urbeat hostname '$hostName'. This scenario is only for an explicitly authorized test environment."
}

$isLocalTarget = $parsedBaseUrl.IsLoopback -or $hostName -in @("localhost", "127.0.0.1", "::1")
if (-not $isLocalTarget -and -not $AllowRemote) {
    throw "Refusing to target a non-loopback URL without -AllowRemote. Only run against an environment you are explicitly authorized to mutate."
}

# Mode authorization ------------------------------------------------------------

if ($Mode -eq "Run") {
    if (-not $BusinessTestAuthorized) {
        throw "Run mode requires -BusinessTestAuthorized. Confirm the target is non-production and mutable testing is authorized."
    }

    if (-not $Cleanup) {
        throw "Run mode requires -Cleanup. The switch acknowledges a manual cleanup/rollback plan; the wrapper never deletes data automatically."
    }
}

# Run ID ------------------------------------------------------------------------

if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = New-RunIdValue
}

if ($RunId -notmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$' -or $RunId.Length -lt 3 -or $RunId.Length -gt 32) {
    throw "RunId must be 3-32 characters of lowercase letters, digits, and single hyphens (no leading/trailing/consecutive hyphens). Received: '$RunId'."
}

# VU split ----------------------------------------------------------------------

$resolvedSellerVus = $SellerVus
$resolvedCustomerVus = $CustomerVus

if ($resolvedSellerVus -lt 0 -and $resolvedCustomerVus -lt 0) {
    $resolvedSellerVus = if ($Vus -ge 5) { [Math]::Max(1, [Math]::Floor($Vus * 0.1)) } else { 0 }
    $resolvedCustomerVus = $Vus - $resolvedSellerVus
}
elseif ($resolvedSellerVus -lt 0) {
    $resolvedSellerVus = $Vus - $resolvedCustomerVus
}
elseif ($resolvedCustomerVus -lt 0) {
    $resolvedCustomerVus = $Vus - $resolvedSellerVus
}

if ($resolvedSellerVus -lt 0 -or $resolvedCustomerVus -lt 0 -or ($resolvedSellerVus + $resolvedCustomerVus) -ne $Vus) {
    throw "SellerVus + CustomerVus must equal Vus ($Vus). Received seller=$resolvedSellerVus customer=$resolvedCustomerVus."
}

# E-mail domain -----------------------------------------------------------------

$normalizedEmailDomain = if ([string]::IsNullOrWhiteSpace($env:EMAIL_DOMAIN)) { "loadtest.invalid" } else { $env:EMAIL_DOMAIN.Trim().ToLowerInvariant() }
if ($normalizedEmailDomain -notmatch '^[a-z0-9.-]+$' -or $normalizedEmailDomain.Contains("@")) {
    throw "EMAIL_DOMAIN must be a bare domain. Received: '$normalizedEmailDomain'."
}

if ($normalizedEmailDomain -eq "urbeat.com.br" -or $normalizedEmailDomain.EndsWith(".urbeat.com.br")) {
    throw "EMAIL_DOMAIN must not be a production urbeat domain. Received: '$normalizedEmailDomain'."
}

if (-not $normalizedEmailDomain.EndsWith(".invalid") -and -not $normalizedEmailDomain.EndsWith(".test")) {
    throw "EMAIL_DOMAIN must end with .invalid or .test so synthetic accounts can never receive real mail. Received: '$normalizedEmailDomain'."
}

# GUIDs -------------------------------------------------------------------------

if ($TargetStoreId -and -not (Test-GuidText $TargetStoreId)) {
    throw "TargetStoreId must be a GUID."
}

if ($BootstrapStoreId -and -not (Test-GuidText $BootstrapStoreId)) {
    throw "BootstrapStoreId must be a GUID."
}

if ($FulfillmentType -eq "delivery" -and $TargetStoreId -and [string]::IsNullOrWhiteSpace($TargetDeliveryNeighborhood)) {
    throw "FulfillmentType=delivery with TargetStoreId requires TargetDeliveryNeighborhood."
}

# Account sources ---------------------------------------------------------------

$sellerPoolJson = $null
$customerPoolJson = $null

if ($SellerPoolPath) {
    $sellerPoolMinimum = $Vus
    if (-not $TargetStoreId -and -not $AnchorSellerEmail) {
        $sellerPoolMinimum = $Vus + 1
    }
    $sellerPoolJson = Read-PoolFileValue -Path $SellerPoolPath -Name "SellerPoolPath" -MinimumCount $sellerPoolMinimum
}

if ($CustomerPoolPath) {
    $customerPoolJson = Read-PoolFileValue -Path $CustomerPoolPath -Name "CustomerPoolPath" -MinimumCount $Vus
}

$hasSellerPool = $sellerPoolJson -or $env:SELLER_POOL_JSON
$hasCustomerPool = $customerPoolJson -or $env:CUSTOMER_POOL_JSON

if ($Mode -eq "Run") {
    if ($resolvedSellerVus -gt 0 -and -not $hasSellerPool -and -not $AllowSyntheticAccountCreation) {
        throw "Seller scenario needs -SellerPoolPath (or SELLER_POOL_JSON) or -AllowSyntheticAccountCreation."
    }

    if ($resolvedCustomerVus -gt 0 -and -not $hasCustomerPool -and -not $AllowSyntheticAccountCreation) {
        throw "Customer scenario needs -CustomerPoolPath (or CUSTOMER_POOL_JSON) or -AllowSyntheticAccountCreation."
    }

    if (-not $TargetStoreId -and -not $hasSellerPool -and -not $AnchorSellerEmail -and -not $AllowSyntheticAccountCreation) {
        throw "Without TargetStoreId, setup needs an anchor seller: provide a seller pool, AnchorSellerEmail, or -AllowSyntheticAccountCreation."
    }

    $createsStoreAddresses = $resolvedSellerVus -gt 0 -or -not $TargetStoreId
    if ($createsStoreAddresses -and -not $AllowNeighborhoodImport) {
        throw "Seller/anchor onboarding calls PUT /api/stores/{id}/address, which enqueues ImportStoreNeighborhoodsJob (OSM). Add -AllowNeighborhoodImport only after confirming the test environment disables or mocks that business queue job."
    }
}

if ($AnchorSellerEmail -and [string]::IsNullOrWhiteSpace($env:URBEAT_LOAD_ANCHOR_SELLER_PASSWORD)) {
    throw "AnchorSellerEmail requires URBEAT_LOAD_ANCHOR_SELLER_PASSWORD in the process environment. The password is never accepted on the command line."
}

if ($AnchorSellerEmail) {
    $anchorAt = $AnchorSellerEmail.LastIndexOf("@")
    $anchorDomain = if ($anchorAt -ge 0) { $AnchorSellerEmail.Substring($anchorAt + 1).ToLowerInvariant() } else { "" }
    if ($anchorDomain -eq "urbeat.com.br" -or $anchorDomain.EndsWith(".urbeat.com.br")) {
        throw "AnchorSellerEmail must not be a production urbeat account."
    }
}

$syntheticPassword = ""
if ($AllowSyntheticAccountCreation) {
    $syntheticPassword = $env:URBEAT_LOAD_SYNTHETIC_PASSWORD
    if ([string]::IsNullOrWhiteSpace($syntheticPassword)) {
        $syntheticPassword = New-SyntheticPasswordValue
    }
    elseif ($syntheticPassword.Length -lt 12) {
        throw "URBEAT_LOAD_SYNTHETIC_PASSWORD must be at least 12 characters."
    }
}

# Output paths ------------------------------------------------------------------

$scriptPath = Join-Path $PSScriptRoot "urbeat-business-load.js"
if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
    throw "Business load script not found at '$scriptPath'."
}

$artifactsDirectory = Join-Path $PSScriptRoot "artifacts"
if (-not $CleanupPlanPath) {
    $CleanupPlanPath = Join-Path $artifactsDirectory ("cleanup-$RunId.sql")
}
$resolvedCleanupPlanPath = [System.IO.Path]::GetFullPath($CleanupPlanPath)

if (-not $SummaryExportPath) {
    $SummaryExportPath = Join-Path $artifactsDirectory ("summary-$RunId.json")
}
$resolvedSummaryExportPath = [System.IO.Path]::GetFullPath($SummaryExportPath)

# Header ------------------------------------------------------------------------

Write-Host "urbeat business load test" -ForegroundColor Cyan
Write-Host "mode=$Mode base=$normalizedBaseUrl runId=$RunId vus=$Vus sellerVus=$resolvedSellerVus customerVus=$resolvedCustomerVus" -ForegroundColor Cyan
Write-Host "fulfillment=$FulfillmentType duration=$Duration rampUp=$RampUpDuration sellerMaxDuration=$SellerMaxDuration requestTimeout=$RequestTimeout" -ForegroundColor Cyan
Write-Host "syntheticEmailDomain=$normalizedEmailDomain cleanupPlan=$resolvedCleanupPlanPath" -ForegroundColor Cyan
Write-Host "no e-mail, WhatsApp, upload, webhook, or online-payment endpoint is called; orders use cash-on-delivery only." -ForegroundColor Yellow
Write-Host "synthetic identities use reserved .invalid/.test e-mail domains; no confirmation e-mail is expected." -ForegroundColor Yellow

if ($resolvedSellerVus -gt 10 -and $Mode -eq "Run") {
    Write-Warning "Seller onboarding performs auth requests per VU. Verify RateLimiting:AuthPermitLimit in the test environment is high enough for $resolvedSellerVus sellers."
}

if ($AllowSyntheticAccountCreation) {
    Write-Host "account source: synthetic confirmed Customer -> Seller promotion (no confirmation e-mail)" -ForegroundColor Yellow
}
if ($hasSellerPool) {
    Write-Host "account source: pre-provisioned seller pool (contents not printed)" -ForegroundColor Yellow
}
if ($hasCustomerPool) {
    Write-Host "account source: pre-provisioned customer pool (contents not printed)" -ForegroundColor Yellow
}

if ($AllowNeighborhoodImport) {
    Write-Host "neighborhood import: attested as disabled/mocked in the test environment; address coordinates are sent to skip geocoding." -ForegroundColor Yellow
}
else {
    Write-Warning "Runs that create stores require -AllowNeighborhoodImport because PUT /api/stores/{id}/address enqueues ImportStoreNeighborhoodsJob (OSM)."
}

Write-CleanupPlanValue -Path $resolvedCleanupPlanPath -RunIdValue $RunId -EmailDomain $normalizedEmailDomain
Write-Host "cleanup inventory written (read-only plan; no deletion performed)." -ForegroundColor Yellow

# Preflight / Run checks --------------------------------------------------------

$resolvedBootstrapStoreId = $BootstrapStoreId
$publicStoreCount = 0

if ($Mode -in @("Preflight", "Run")) {
    Write-Section "Read-only preflight"

    $health = Invoke-UrbeatGet -FullUri ($normalizedBaseUrl + "/health")
    if ($health.StatusCode -ne 200) {
        throw "GET /health returned HTTP $($health.StatusCode). Refusing to run business mutations against an unhealthy target."
    }
    Write-Host "health: $($health.StatusCode)" -ForegroundColor Green

    $ready = Invoke-UrbeatGet -FullUri ($normalizedBaseUrl + "/health/ready")
    if ($ready.StatusCode -ne 200) {
        throw "GET /health/ready returned HTTP $($ready.StatusCode). Refusing to run business mutations against an unready target."
    }
    Write-Host "health/ready: $($ready.StatusCode)" -ForegroundColor Green

    $storesResponse = Invoke-UrbeatGet -FullUri ($normalizedBaseUrl + "/api/public/stores")
    $stores = Assert-JsonStatus -Response $storesResponse -Path "/api/public/stores" -ExpectedStatus 200
    $storeItems = @($stores)
    $publicStoreCount = $storeItems.Count
    Write-Host "public stores: $publicStoreCount" -ForegroundColor Green

    if (-not $resolvedBootstrapStoreId -and $publicStoreCount -gt 0 -and $storeItems[0].id) {
        $resolvedBootstrapStoreId = [string]$storeItems[0].id
        Write-Host "bootstrap store discovered: $resolvedBootstrapStoreId" -ForegroundColor Green
    }

    if (-not $resolvedBootstrapStoreId -and $TargetStoreId) {
        $resolvedBootstrapStoreId = $TargetStoreId
        Write-Host "bootstrap store set to target store: $resolvedBootstrapStoreId" -ForegroundColor Green
    }

    $needsBootstrapForSynthetic =
        (-not $TargetStoreId -and -not $AnchorSellerEmail -and -not $hasSellerPool) -or
        ($resolvedSellerVus -gt 0 -and -not $hasSellerPool) -or
        ($resolvedCustomerVus -gt 0 -and -not $hasCustomerPool)

    if (-not $resolvedBootstrapStoreId -and $needsBootstrapForSynthetic) {
        throw "No public store was found for synthetic account bootstrap. Set -BootstrapStoreId or -TargetStoreId."
    }

    $cuisineResponse = Invoke-UrbeatGet -FullUri ($normalizedBaseUrl + "/api/stores/cuisine-types")
    $cuisines = Assert-JsonStatus -Response $cuisineResponse -Path "/api/stores/cuisine-types" -ExpectedStatus 200
    $cuisineCount = @($cuisines).Count
    Write-Host "active cuisine types: $cuisineCount" -ForegroundColor Green
    if ($cuisineCount -eq 0) {
        throw "No active cuisine type is available; synthetic stores cannot be created."
    }

    if ($TargetStoreId) {
        $targetResponse = Invoke-UrbeatGet -FullUri ($normalizedBaseUrl + "/api/public/stores/$TargetStoreId")
        $targetStore = Assert-JsonStatus -Response $targetResponse -Path "/api/public/stores/$TargetStoreId" -ExpectedStatus 200
        if ($targetStore.isOpen -ne $true) {
            throw "Target store $TargetStoreId is not open. Checkout rejects closed stores."
        }

        $catalogResponse = Invoke-UrbeatGet -FullUri ($normalizedBaseUrl + "/api/public/stores/$TargetStoreId/catalog/products")
        $catalog = Assert-JsonStatus -Response $catalogResponse -Path "/api/public/stores/$TargetStoreId/catalog/products" -ExpectedStatus 200
        $availableProducts = @($catalog | Where-Object { $_.isAvailable -ne $false -and $_.id })
        if ($availableProducts.Count -eq 0) {
            throw "Target store $TargetStoreId has no available catalog product."
        }
        Write-Host "target store: $TargetStoreId open=true products=$($availableProducts.Count)" -ForegroundColor Green
    }
    else {
        Write-Host "target store: setup will create one isolated anchor store for RUN_ID $RunId" -ForegroundColor Yellow
    }
}

if ($Mode -in @("Plan", "Preflight")) {
    Write-Section $(if ($Mode -eq "Plan") { "Plan only - k6 was not executed" } else { "Preflight only - k6 was not executed" })
    if ($Mode -eq "Plan") {
        Write-Host "Use -Mode Preflight for GET-only checks or -Mode Run with -AllowRemote/-BusinessTestAuthorized/-Cleanup to execute." -ForegroundColor Yellow
    }
    else {
        Write-Host "Preflight performed GET-only checks. Use -Mode Run with -AllowRemote/-BusinessTestAuthorized/-Cleanup to execute." -ForegroundColor Yellow
    }
    Write-Host "No remote mutations were performed." -ForegroundColor Yellow
    exit 0
}

# Run ---------------------------------------------------------------------------

$k6Executable = $null
if ($K6Path) {
    if (-not (Test-Path -LiteralPath $K6Path -PathType Leaf)) {
        throw "K6Path was not found at '$K6Path'."
    }
    $k6Executable = (Get-Item -LiteralPath $K6Path).FullName
}
else {
    $k6Command = Get-Command k6 -ErrorAction SilentlyContinue
    if ($k6Command) {
        $k6Executable = $k6Command.Source
    }
}

if (-not $k6Executable) {
    throw "k6 is not installed or not on PATH. Install it from https://k6.io/docs/get-started/installation/ and rerun. This wrapper does not install tools automatically."
}

$summaryParent = Split-Path -Parent $resolvedSummaryExportPath
if ($summaryParent -and -not (Test-Path -LiteralPath $summaryParent -PathType Container)) {
    New-Item -ItemType Directory -Path $summaryParent -Force | Out-Null
}

$environmentNames = @(
    "BASE_URL",
    "RUN_ID",
    "VUS",
    "SELLER_VUS",
    "CUSTOMER_VUS",
    "DURATION",
    "RAMP_UP_DURATION",
    "SELLER_MAX_DURATION",
    "REQUEST_TIMEOUT",
    "SLEEP_SECONDS",
    "EMAIL_DOMAIN",
    "FULFILLMENT_TYPE",
    "SELLER_STATUS_SMOKE",
    "CUSTOMER_READBACK",
    "CUSTOMER_SESSION_PER_ITERATION",
    "BOOTSTRAP_STORE_ID",
    "TARGET_STORE_ID",
    "TARGET_DELIVERY_NEIGHBORHOOD",
    "TARGET_DELIVERY_CITY",
    "SYNTHETIC_PASSWORD",
    "ALLOW_SYNTHETIC_ACCOUNT_CREATION",
    "ALLOW_NEIGHBORHOOD_IMPORT",
    "SELLER_POOL_JSON",
    "CUSTOMER_POOL_JSON",
    "ANCHOR_SELLER_EMAIL",
    "ANCHOR_SELLER_PASSWORD",
    "BUSINESS_TEST_AUTHORIZED",
    "ALLOW_SYNTHETIC_MUTATIONS",
    "OUTBOX_ADMIN_TOKEN"
)

$previousEnvironment = @{}
foreach ($name in $environmentNames) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
}

$exitCode = 1
try {
    $env:BASE_URL = $normalizedBaseUrl
    $env:RUN_ID = $RunId
    $env:VUS = [string]$Vus
    $env:SELLER_VUS = [string]$resolvedSellerVus
    $env:CUSTOMER_VUS = [string]$resolvedCustomerVus
    $env:DURATION = $Duration
    $env:RAMP_UP_DURATION = $RampUpDuration
    $env:SELLER_MAX_DURATION = $SellerMaxDuration
    $env:REQUEST_TIMEOUT = $RequestTimeout
    $env:SLEEP_SECONDS = [string]$SleepSeconds
    $env:EMAIL_DOMAIN = $normalizedEmailDomain
    $env:FULFILLMENT_TYPE = $FulfillmentType
    $env:SELLER_STATUS_SMOKE = if ($SellerStatusSmoke) { "true" } else { "false" }
    $env:CUSTOMER_READBACK = if ($SkipCustomerReadback) { "false" } else { "true" }
    $env:CUSTOMER_SESSION_PER_ITERATION = if ($CustomerSessionPerIteration) { "true" } else { "false" }
    $env:BOOTSTRAP_STORE_ID = $resolvedBootstrapStoreId
    $env:TARGET_STORE_ID = $TargetStoreId
    $env:TARGET_DELIVERY_NEIGHBORHOOD = $TargetDeliveryNeighborhood
    $env:TARGET_DELIVERY_CITY = $TargetDeliveryCity
    $env:SYNTHETIC_PASSWORD = $syntheticPassword
    $env:ALLOW_SYNTHETIC_ACCOUNT_CREATION = if ($AllowSyntheticAccountCreation) { "true" } else { "false" }
    $env:ALLOW_NEIGHBORHOOD_IMPORT = if ($AllowNeighborhoodImport) { "true" } else { "false" }
    if ($sellerPoolJson) {
        $env:SELLER_POOL_JSON = $sellerPoolJson
    }
    if ($customerPoolJson) {
        $env:CUSTOMER_POOL_JSON = $customerPoolJson
    }
    $env:ANCHOR_SELLER_EMAIL = $AnchorSellerEmail
    $env:ANCHOR_SELLER_PASSWORD = if ($AnchorSellerEmail) { $env:URBEAT_LOAD_ANCHOR_SELLER_PASSWORD } else { "" }
    $env:BUSINESS_TEST_AUTHORIZED = "true"
    $env:ALLOW_SYNTHETIC_MUTATIONS = "true"
    $env:OUTBOX_ADMIN_TOKEN = if ($AllowOutboxAdminRead -and $env:URBEAT_LOAD_OUTBOX_ADMIN_TOKEN) { $env:URBEAT_LOAD_OUTBOX_ADMIN_TOKEN } else { "" }

    Write-Section "Executing k6"
    Write-Host "k6 run $scriptPath --summary-export $resolvedSummaryExportPath" -ForegroundColor Cyan
    Write-Host "RUN_ID=$RunId; synthetic data is isolated by this prefix. Cleanup remains manual." -ForegroundColor Yellow

    & $k6Executable run $scriptPath --summary-export $resolvedSummaryExportPath
    $exitCode = $LASTEXITCODE
}
finally {
    foreach ($name in $previousEnvironment.Keys) {
        $previousValue = $previousEnvironment[$name]
        if ($null -eq $previousValue) {
            Remove-Item -Path "Env:$name" -ErrorAction SilentlyContinue
        }
        else {
            Set-Item -Path "Env:$name" -Value $previousValue
        }
    }
}

Write-Section "Result"
$resultColor = if ($exitCode -eq 0) { "Green" } else { "Red" }
Write-Host "k6 exit code: $exitCode" -ForegroundColor $resultColor
Write-Host "cleanup inventory: $resolvedCleanupPlanPath (read-only; review before any deletion)" -ForegroundColor Yellow
Write-Host "summary export: $resolvedSummaryExportPath" -ForegroundColor Yellow
Write-Host "no cleanup was executed automatically." -ForegroundColor Yellow

exit $exitCode
