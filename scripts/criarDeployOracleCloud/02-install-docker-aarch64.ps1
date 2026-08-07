<#
.SYNOPSIS
    Installs Docker and Docker Compose for aarch64 on Ubuntu server
.DESCRIPTION
    Connects via SSH and installs Docker CE + Docker Compose plugin
    specifically for aarch64 architecture. This runs ONCE only.
.REQUIREMENTS
    - SSH access to 136.248.115.135
    - Ubuntu user with sudo privileges
#>

param(
    [Parameter(Mandatory=$false)]
    [string]$ServerIP = "136.248.115.135",

    [Parameter(Mandatory=$false)]
    [string]$SSHUser = "ubuntu",

    [Parameter(Mandatory=$false)]
    [string]$SSHKeyPath = "~/.ssh/id_ed25519"
)

Write-Host "🐳 Starting Docker ARM64 Installation..." -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray
Write-Host "🖥️  Server: $ServerIP" -ForegroundColor White
Write-Host "👤 User: $SSHUser" -ForegroundColor White
Write-Host "🔑 SSH Key: $SSHKeyPath" -ForegroundColor White

# Resolve SSH key path
$resolvedKeyPath = (Resolve-Path $SSHKeyPath -ErrorAction SilentlyContinue).Path
if (-not $resolvedKeyPath) {
    $resolvedKeyPath = Resolve-Path "$env:USERPROFILE\.ssh\id_rsa" -ErrorAction SilentlyContinue
}

# ─────────────────────────────────────────
# Docker installation script for ARM64
# ─────────────────────────────────────────

$dockerInstallScript = @'
#!/bin/bash
set -e

echo "🔍 Checking architecture..."
ARCH=$(uname -m)
echo "Architecture: $ARCH"

if [ "$ARCH" != "aarch64" ]; then
    echo "⚠️  Warning: Expected aarch64 but got $ARCH"
fi

echo "🔄 Checking if Docker is already installed..."
if command -v docker &> /dev/null; then
    DOCKER_VERSION=$(docker --version)
    echo "✅ Docker already installed: $DOCKER_VERSION"

    # Check Docker Compose
    if docker compose version &> /dev/null; then
        COMPOSE_VERSION=$(docker compose version)
        echo "✅ Docker Compose already installed: $COMPOSE_VERSION"
        echo "DOCKER_ALREADY_INSTALLED=true"
        exit 0
    fi
fi

echo "📦 Updating package index..."
sudo apt-get update -y

echo "📦 Installing prerequisites..."
sudo apt-get install -y \
    ca-certificates \
    curl \
    gnupg \
    lsb-release \
    apt-transport-https \
    software-properties-common

echo "🔑 Adding Docker GPG key..."
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | \
    sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg

echo "📋 Adding Docker repository for ARM64..."
echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] \
  https://download.docker.com/linux/ubuntu \
  $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
  sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

echo "📦 Updating package index with Docker repo..."
sudo apt-get update -y

echo "🐳 Installing Docker CE (ARM64)..."
sudo apt-get install -y \
    docker-ce \
    docker-ce-cli \
    containerd.io \
    docker-buildx-plugin \
    docker-compose-plugin

echo "👤 Adding ubuntu user to docker group..."
sudo usermod -aG docker ubuntu

echo "🔧 Enabling and starting Docker service..."
sudo systemctl enable docker
sudo systemctl start docker

echo "✅ Verifying Docker installation..."
sudo docker --version
sudo docker compose version

echo "🔧 Configuring Docker daemon for ARM64..."
sudo tee /etc/docker/daemon.json > /dev/null <<EOF
{
  "log-driver": "json-file",
  "log-opts": {
    "max-size": "100m",
    "max-file": "3"
  },
  "default-address-pools": [
    {"base": "172.20.0.0/16", "size": 24}
  ]
}
EOF

sudo systemctl restart docker
sudo systemctl status docker --no-pager

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "✅ Docker ARM64 installation completed successfully!"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
'@

# Save script to temp file
$tempScript = [System.IO.Path]::GetTempFileName() + ".sh"
$dockerInstallScript | Out-File -FilePath $tempScript -Encoding UTF8 -NoNewline

Write-Host "`n📤 Uploading installation script to server..." -ForegroundColor Yellow

# Upload script via SCP
scp -i $resolvedKeyPath -o StrictHostKeyChecking=no $tempScript "${SSHUser}@${ServerIP}:/tmp/install-docker.sh"

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Failed to upload script. Check SSH connection." -ForegroundColor Red
    Remove-Item $tempScript -Force
    exit 1
}

Write-Host "✅ Script uploaded successfully" -ForegroundColor Green

Write-Host "`n🚀 Executing Docker installation on server..." -ForegroundColor Yellow
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray

# Execute script via SSH
ssh -i $resolvedKeyPath -o StrictHostKeyChecking=no "${SSHUser}@${ServerIP}" "chmod +x /tmp/install-docker.sh && /tmp/install-docker.sh"

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Docker installation failed!" -ForegroundColor Red
    Remove-Item $tempScript -Force
    exit 1
}

Write-Host "`n🔍 Verifying Docker installation..." -ForegroundColor Yellow
ssh -i $resolvedKeyPath -o StrictHostKeyChecking=no "${SSHUser}@${ServerIP}" "docker --version && docker compose version"

Write-Host "`n🧹 Cleaning up temporary files..." -ForegroundColor Yellow
try {
    Remove-Item $tempScript -Force -ErrorAction SilentlyContinue
} catch {
    Write-Host "⚠️ Warning: Could not remove local temp file $tempScript" -ForegroundColor Yellow
}

try {
    ssh -i $resolvedKeyPath -o StrictHostKeyChecking=no "${SSHUser}@${ServerIP}" "rm -f /tmp/install-docker.sh" 2>$null
} catch {
    Write-Host "⚠️ Warning: Could not remove remote temp file" -ForegroundColor Yellow
}

Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Gray
Write-Host "🎉 Docker ARM64 installation completed!" -ForegroundColor Green
Write-Host "⚠️  NOTE: You may need to reconnect SSH for group changes to take effect." -ForegroundColor Yellow