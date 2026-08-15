---
name: deploy-oci
description: Deploys Urbeat to OCI through the local PowerShell pipeline. Use for OCI deployment, release, or production shipping requests.
license: proprietary
metadata:
  author: urbeat
  version: '2.0.0'
---

# OCI Deployment

This skill describes `scripts/criarDeployOracleCloud/`. It does not authorize a remote deployment by itself. Never execute a deployment without an explicit user request.

## Connection Defaults

- Server: configurable with `-ServerIP`; documented production value is `136.248.115.135`
- SSH user: `dexter`
- SSH port: `2208`
- SSH key: configurable with `-SSHKeyPath` (default `~/.ssh/id_ed25519`, with an `id_rsa` fallback)
- Every `ssh` call uses `-p $SSHPort`; every `scp` call uses the equivalent `-P $SSHPort` required by OpenSSH.
- The server may require port knocking before connecting.

## Secrets

`configs/secrets-map.json` contains OCIDs only. Never add passwords, tokens, API keys, connection strings, or secret values to the repository.

Existing OCI Vault secrets are protected configuration. Do not rotate, delete, replace, or rewrite them unless the user explicitly requests that exact change. The normal deployment path reads existing values through their OCIDs; the local secrets file is only needed when the user explicitly chooses to create a missing Vault entry.

The Vault setup script requires a local ignored JSON file supplied with `-SecretsFile` or `URBEAT_VAULT_SECRETS_FILE`. It also requires `OCI_COMPARTMENT_OCID`. The local file is read but values are never printed. Do not use `01-cleanup-secrets.ps1` during normal deployment because it schedules secrets for deletion.

Example local invocation with a path outside version control:

```powershell
$env:URBEAT_VAULT_SECRETS_FILE = "C:\secure\urbeat-vault-secrets.local.json"
$env:OCI_COMPARTMENT_OCID = "<compartment-ocid>"
$env:OCI_VAULT_MANAGEMENT_ENDPOINT = "<vault-management-endpoint>"
./deploy-all.ps1 -Step vault -ServerIP "136.248.115.135" -SSHUser dexter -SSHPort 2208 -SSHKeyPath "$env:USERPROFILE\.ssh\id_ed25519"
```

## Safe Order

Run local preflight first:

```powershell
Set-Location -LiteralPath "C:\Projetos\urbeat\scripts\criarDeployOracleCloud"
./validate-pipeline.ps1
```

The master pipeline then runs:

1. `prerequisites`: validates tools, port 80, port 2208, OCI, architecture, Nginx, and sudo.
2. `vault`: creates missing Vault secrets from the ignored local file and writes only OCIDs to `configs/secrets-map.json`.
3. `docker`: installs Docker for aarch64 and adds the configured SSH user to the Docker group.
4. `environment`: retrieves secrets from Vault, creates `/opt/urbeat/downloads/`, and uploads the protected `.env`.
5. `application`: uploads generated configuration and source from the repository root resolved from `$PSScriptRoot`, then builds and starts Compose.
6. `nginx`: installs HTTP-only configuration, creates the downloads location, runs `nginx -t`, and reloads only after a successful test.
7. `ssl`: runs Certbot after HTTP is working, then tests Nginx and reloads it after HTTPS is enabled.
8. `verify`: performs remote health and service checks.

Run all steps with explicit connection parameters:

```powershell
./deploy-all.ps1 -Step all -ServerIP "136.248.115.135" -SSHUser dexter -SSHPort 2208 -SSHKeyPath "$env:USERPROFILE\.ssh\id_ed25519"
```

The master script propagates `SSHUser`, `SSHPort`, and `SSHKeyPath` to every step and resolves child scripts relative to `$PSScriptRoot`, not the current directory.

## Validation And Risks

Run `./validate-pipeline.ps1` before deployment. It parses every PowerShell script, checks that the JSON map contains only Vault secret OCIDs, and rejects fixed repository paths or an `ubuntu` SSH default. It is local and non-destructive.

The pipeline still performs destructive remote operations when invoked: Docker rebuilds, container replacement, Nginx configuration changes, and Vault secret creation. Certificate issuance depends on DNS and HTTP reachability. Secret rotation is intentionally not automated. Review the local secret file and OCI permissions before any remote step.

## Protected Rules For Future Agents

- Do not alter the SSH defaults, deployment order, HTTP-before-SSL sequence, or preflight requirement without explicit user authorization.
- Do not add secret values to tracked files or print them in command output.
- Do not rotate or modify existing Vault secrets as part of a normal deployment or maintenance task.
- Neighborhood CSV snapshots may retain neighborhoods without geolocation with empty Latitude/Longitude fields. Coordinates approximate the first street/CEP found, using e-DNE/CEP first and real sources such as Nominatim only as fallback, never a municipality centroid. Reject partial or invalid pairs; pending neighborhoods are not a fatal publication failure, and CSV restoration preserves empty fields without inventing coordinates.

## Post-Deployment

- Frontend: `https://www.urbeat.com.br`
- API: `https://api.urbeat.com.br`
- Health: `https://www.urbeat.com.br/health`
- Swagger: `https://api.urbeat.com.br/swagger`
