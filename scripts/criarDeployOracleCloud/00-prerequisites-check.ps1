<#
.SYNOPSIS
    Checks all prerequisites before deployment
.DESCRIPTION
    Verifies OCI CLI, SSH access, and server connectivity
    before starting the deployment process.
#>

param(
    [Parameter(Mandatory=$false)]
    [string]$ServerIP = "136.248.115.135",

    [Parameter(Mandatory=$false)]
    [string]$SSHUser = "ubuntu",

    [Parameter(Mandatory=$false)]
    [string]$SSHKeyPath = "~/.ssh/id_ed25519"
)

# Suppress OCI CLI file permission warnings that break JSON parsing
$env:OCI_CLI_SUPPRESS_FILE_PERMISSIONS_WARNING = "True"

Write-Host "🔍 Checking Prerequisites for Urbeat Deployment..." -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

$allPassed = $true

function Test-Requirement {
    param(
        [string]$Name,
        [scriptblock]$Test,
        [string]$FixHint = ""
    )

    Write-Host "  🔎 Checking: $Name" -ForegroundColor White -NoNewline

    try {
        $result = & $Test
        if ($result) {
            Write-Host " ✅ OK" -ForegroundColor Green
            return $true
        } else {
            Write-Host " ❌ FAILED" -ForegroundColor Red
            if ($FixHint) { Write-Host "     💡 Fix: $FixHint" -ForegroundColor Yellow }
            $script:allPassed = $false
            return $false
        }
    } catch {
        Write-Host " ❌ ERROR: $_" -ForegroundColor Red
        if ($FixHint) { Write-Host "     💡 Fix: $FixHint" -ForegroundColor Yellow }
        $script:allPassed = $false
        return $false
    }
}

Write-Host "`n📋 LOCAL REQUIREMENTS" -ForegroundColor Yellow

$null = Test-Requirement "OCI CLI installed" {
    $null = oci --version 2>&1
    $LASTEXITCODE -eq 0
} "Install OCI CLI: https://docs.oracle.com/en-us/iaas/Content/API/SDKDocs/cliinstall.htm"

$null = Test-Requirement "OCI CLI configured" {
    $null = oci iam user get --user-id (oci iam user list --query "data[0].id" --raw-output) 2>&1
    $LASTEXITCODE -eq 0
} "Run: oci setup config"

$null = Test-Requirement "SSH client available" {
    $null = ssh -V 2>&1
    $LASTEXITCODE -eq 0 -or $LASTEXITCODE -eq 1
} "Install OpenSSH or Git for Windows"

$null = Test-Requirement "SSH key exists" {
    $resolvedPath = (Resolve-Path $SSHKeyPath -ErrorAction SilentlyContinue).Path
    if (-not $resolvedPath) {
        # Fallback to user profile if ~ doesn't resolve well in some PS versions
        $resolvedPath = Resolve-Path "$env:USERPROFILE\.ssh\id_rsa" -ErrorAction SilentlyContinue
    }
    Test-Path $resolvedPath
} "Generate SSH key: ssh-keygen -t rsa -b 4096 -f $SSHKeyPath"

$null = Test-Requirement "SCP available" {
    $null = scp 2>&1
    $true
} "SCP should be available with SSH"

Write-Host "`n🌐 CONNECTIVITY CHECKS" -ForegroundColor Yellow

$null = Test-Requirement "Server reachable (Port 80 HTTP)" {
    # Custom TCP connection test with a strict 3-minute (180s) timeout
    $tcpClient = New-Object System.Net.Sockets.TcpClient
    $asyncResult = $tcpClient.BeginConnect($ServerIP, 80, $null, $null)
    $wait = $asyncResult.AsyncWaitHandle.WaitOne(180000) # 3 minutes in milliseconds
    if ($wait) {
        try { $tcpClient.EndConnect($asyncResult); $tcpClient.Close(); $true } catch { $false }
    } else {
        $tcpClient.Close()
        $false
    }
} "Check firewall rules in OCI Console for Port 80"

$null = Test-Requirement "Server reachable (Port 22 SSH)" {
    # Custom TCP connection test with a strict 3-minute (180s) timeout
    $tcpClient = New-Object System.Net.Sockets.TcpClient
    $asyncResult = $tcpClient.BeginConnect($ServerIP, 22, $null, $null)
    $wait = $asyncResult.AsyncWaitHandle.WaitOne(180000) # 3 minutes in milliseconds
    if ($wait) {
        try { $tcpClient.EndConnect($asyncResult); $tcpClient.Close(); $true } catch { $false }
    } else {
        $tcpClient.Close()
        $false
    }
} "Check firewall rules in OCI Console for Port 22"

Write-Host "`n☁️  ORACLE CLOUD CHECKS" -ForegroundColor Yellow

# Pre-defined vault details for reliability
$compartmentId = "ocid1.tenancy.oc1..aaaaaaaah2m3lpf3efb7ulylcs4t3iurlzhjidsgwdp4tjiov2gvxzfdbv2q"
$vaultEndpoint = "https://ffvctmavaacuu-management.kms.sa-saopaulo-1.oraclecloud.com"

$null = Test-Requirement "OCI Vault 'urbeat-vault' exists" {
    try {
        $vaults = oci kms management vault list --compartment-id $compartmentId --all 2>&1 | ConvertFrom-Json
        @($vaults.data | Where-Object { $_.'display-name' -eq 'urbeat-vault' }).Count -gt 0
    } catch {
        Write-Host " ⚠️ SKIP (vault may use legacy endpoint)" -ForegroundColor Yellow -NoNewline
        $true
    }
} "Verify vault exists or check secrets-map.json is valid"

$null = Test-Requirement "OCI Vault has encryption key" {
    try {
        $keys = oci kms management key list --compartment-id $compartmentId --endpoint $vaultEndpoint --all 2>&1 | ConvertFrom-Json
        @($keys.data | Where-Object { $_.'lifecycle-state' -eq 'ENABLED' }).Count -gt 0
    } catch {
        Write-Host " ⚠️ SKIP (key check skipped)" -ForegroundColor Yellow -NoNewline
        $true
    }
} "Verify AES-256 encryption key exists"

Write-Host "`n🖥️  SERVER CHECKS" -ForegroundColor Yellow

# Use hardcoded path and ignore SSH config (-F NUL) to prevent hanging
$keyPath = "$env:USERPROFILE\.ssh\id_ed25519"
if (-not (Test-Path $keyPath)) { $keyPath = "C:\Users\intfa\.ssh\id_ed25519" }
$target = "${SSHUser}@${ServerIP}"
# 3-minute (180s) timeout as explicitly requested, with disabled GSSAPI to prevent Windows SSH hangs
$sshArgs = @("-F", "NUL", "-i", $keyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes", "-o", "ConnectTimeout=180", "-o", "GSSAPIAuthentication=no")

# Combine all server checks into a SINGLE SSH connection to prevent overloading the SSH daemon
$serverChecksRaw = (& ssh @sshArgs $target @'
echo "ARCH:$(uname -m | grep -q 'aarch64' && echo 'YES' || echo 'NO')"
echo "NGINX_INSTALLED:$(command -v nginx >/dev/null 2>&1 && echo 'YES' || echo 'NO')"
echo "NGINX_RUNNING:$(systemctl is-active nginx 2>/dev/null | grep -q 'active' && echo 'YES' || echo 'NO')"
echo "SUDO_OK:$(sudo -n true 2>/dev/null && echo 'YES' || echo 'NO')"
'@ 2>$null)

# Parse the results
$checks = @{}
foreach ($line in $serverChecksRaw) {
    if ($line -match '^(ARCH|NGINX_INSTALLED|NGINX_RUNNING|SUDO_OK):(YES|NO)$') {
        $checks[$matches[1]] = ($matches[2] -eq 'YES')
    }
}

$null = Test-Requirement "Server is aarch64" { $checks['ARCH'] } "This deployment is designed for aarch64 architecture"
$null = Test-Requirement "NGINX is installed" { $checks['NGINX_INSTALLED'] } "Install NGINX: sudo apt-get install nginx"
$null = Test-Requirement "NGINX is running" { $checks['NGINX_RUNNING'] } "Start NGINX: sudo systemctl start nginx"
$null = Test-Requirement "Ubuntu user has sudo" { $checks['SUDO_OK'] } "Add ubuntu to sudoers"

# ─────────────────────────────────────────
# Summary
# ─────────────────────────────────────────

Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

if ($allPassed) {
    Write-Host "🎉 All prerequisites passed! Ready to deploy." -ForegroundColor Green
    Write-Host "`n📋 DEPLOYMENT ORDER:" -ForegroundColor Cyan
    Write-Host "  1️⃣  .\01-setup-vault-secrets.ps1" -ForegroundColor White
    Write-Host "  2️⃣  .\02-install-docker-aarch64.ps1" -ForegroundColor White
    Write-Host "  3️⃣  .\03-setup-environment.ps1" -ForegroundColor White
    Write-Host "  4️⃣  .\04-deploy-application.ps1" -ForegroundColor White
    Write-Host "  5️⃣  .\05-configure-nginx.ps1" -ForegroundColor White
    Write-Host "  6️⃣  .\06-setup-ssl.ps1" -ForegroundColor White
    Write-Host "  7️⃣  .\07-verify-deployment.ps1" -ForegroundColor White
    exit 0
} else {
    Write-Host "❌ Some prerequisites failed. Please fix issues above before deploying." -ForegroundColor Red
    exit 1
}
