---
name: deploy-oci
description: Deploys the Urbeat application to Oracle Cloud Infrastructure using the automated deployment pipeline. Use when the user asks to deploy, publish, or ship to production/OCI/Oracle Cloud.
license: proprietary
metadata:
  author: urbeat
  version: '1.0.0'
---

# OCI Deployment

Automated deployment pipeline for Urbeat to Oracle Cloud Infrastructure (aarch64).

## Server

- **IP:** 136.248.115.135
- **User:** ubuntu
- **Architecture:** aarch64 (ARM64)
- **SSH Key:** `~/.ssh/id_rsa` (or configured in `scripts/criarDeployOracleCloud/`)

## How to Deploy

Run the master deployment script from its own directory:

```powershell
Set-Location -LiteralPath "C:\Projetos\urbeat\scripts\criarDeployOracleCloud"
./deploy-all.ps1 -Step all -ServerIP "136.248.115.135" -SSHUser "ubuntu"
```

### Individual steps

Run a single step by name:

```powershell
./deploy-all.ps1 -Step <step> -ServerIP "136.248.115.135" -SSHUser "ubuntu"
```

Available steps (run in order):

| Step | Script | Description |
|------|--------|-------------|
| `prerequisites` | `00-prerequisites-check.ps1` | Validates OCI CLI, SSH, server reachability, vault config |
| `vault` | `01-vault-secrets.ps1` | Retrieves secrets from OCI Vault |
| `docker` | `02-docker-build.ps1` | Builds Docker images locally |
| `environment` | `03-environment-setup.ps1` | Sets up server directories, env files |
| `application` | `04-deploy-application.ps1` | Pushes images and starts containers |
| `nginx` | `05-nginx-config.ps1` | Configures reverse proxy |
| `ssl` | `06-ssl-certificates.ps1` | Renews/obtains SSL certs |
| `verify` | `07-verify-deployment.ps1` | Health checks the deployment |

## Prerequisites Check

The `prerequisites` step validates:

- **OCI CLI** installed and configured (`oci --version`)
- **SSH** client with key pair available
- **Server reachable** on ports 80 (HTTP) and 22 (SSH)
- **OCI Vault** `urbeat-vault` exists with AES-256 encryption key
- **NGINX** installed and running on server
- **aarch64** architecture confirmed
- **Sudo** access for ubuntu user

### Common Issues

**OCI Vault check fails:** The `oci` CLI command to list vaults returns empty JSON. This means either:
1. The vault `urbeat-vault` doesn't exist in the compartment
2. The OCI config profile doesn't have vault management permissions
3. The compartment ID in the script is incorrect

Fix: Verify vault exists via OCI Console, or run `oci kms vault list --compartment-id <id>` manually.

## Post-Deployment

After successful deployment:

- Frontend: https://urbeat.com.br
- Backend API: https://api.urbeat.com.br
- Health: https://urbeat.com.br/health
- Swagger: https://api.urbeat.com.br/swagger
- Hangfire: https://api.urbeat.com.br/hangfire

## Build Before Deploying

Always build the frontend before deploying to ensure the production bundle is current:

```powershell
npx ng build --configuration production
```
