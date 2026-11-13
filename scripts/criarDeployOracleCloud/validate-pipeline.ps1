<#
.SYNOPSIS
    Performs local, non-destructive validation of the legacy OCI deployment pipeline.

.DESCRIPTION
    This validator covers the existing PowerShell pipeline and its local invariants.
    Terraform is an explicit, separate gate handled by terraform-preflight.ps1 and
    is intentionally not required here.
#>

$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot
$scripts = Get-ChildItem -LiteralPath $scriptRoot -Filter "*.ps1" -File
$errors = [System.Collections.Generic.List[string]]::new()

foreach ($script in $scripts) {
    $tokens = $null
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile(
        $script.FullName,
        [ref]$tokens,
        [ref]$parseErrors) | Out-Null
    foreach ($parseError in $parseErrors) {
        $errors.Add("$($script.Name): $($parseError.Message)")
    }
}

$mapPath = Join-Path $scriptRoot "configs\secrets-map.json"
try {
    $map = Get-Content -LiteralPath $mapPath -Raw | ConvertFrom-Json
    foreach ($property in $map.PSObject.Properties) {
        if ([string]$property.Value -notmatch '^ocid1\.vaultsecret\.') {
            $errors.Add("secrets-map.json contains a non-secret OCID value for '$($property.Name)'.")
        }
    }
} catch {
    $errors.Add("Could not parse configs/secrets-map.json.")
}

foreach ($script in $scripts) {
    $content = Get-Content -LiteralPath $script.FullName -Raw
    if ($content -match '\[string\]\$SSHUser\s*=\s*"ubuntu"') {
        $errors.Add("$($script.Name): default SSH user must be dexter.")
    }
    if ($content -match 'C:\\Projetos\\urbeat') {
        $errors.Add("$($script.Name): contains a fixed local repository path.")
    }
}

# SSH/knocking invariants: the SSH port is protected by port knocking, so every
# SSH/SCP connection must be preceded by a knock. Defaults must stay dexter/2208,
# and deploy-all must forward the SSH settings to every step script.
$sshCallRegex = [regex]'\b(?:ssh|scp)\s+@[A-Za-z_][A-Za-z0-9_]*'
$knockCallRegex = [regex]'^(?:Send-PortKnock\s+-ServerIP|port-knock\.ps1\s+-ServerIP|&\s*\$knockScript\s+-ServerIP)'

foreach ($script in $scripts) {
    $connectLines = @(Get-Content -LiteralPath $script.FullName | Where-Object {
        $trimmed = $_.Trim()
        $trimmed -ne '' -and -not $trimmed.StartsWith('#')
    })

    $knocked = $false
    foreach ($line in $connectLines) {
        if ($knockCallRegex.IsMatch($line.Trim())) {
            $knocked = $true
        }
        if ($sshCallRegex.IsMatch($line) -and -not $knocked) {
            $errors.Add("$($script.Name): an SSH/SCP call must be preceded by a port knock.")
        }
    }

    $content = Get-Content -LiteralPath $script.FullName -Raw
    if ($sshCallRegex.IsMatch($content)) {
        if ($content -notmatch '\[string\]\$SSHUser\s*=\s*"dexter"') {
            $errors.Add("$($script.Name): default SSH user must be dexter.")
        }
        if ($content -notmatch '\[int\]\$SSHPort\s*=\s*2208') {
            $errors.Add("$($script.Name): default SSH port must be 2208.")
        }
    }

    if ($script.Name -eq "deploy-all.ps1") {
        if ($content -notmatch '\[string\]\$SSHUser\s*=\s*"dexter"') {
            $errors.Add("deploy-all.ps1: default SSH user must be dexter.")
        }
        if ($content -notmatch '\[int\]\$SSHPort\s*=\s*2208') {
            $errors.Add("deploy-all.ps1: default SSH port must be 2208.")
        }
        if ($content -notmatch '@commonParams') {
            $errors.Add("deploy-all.ps1: must forward SSH settings to each step via @commonParams.")
        }
        if ($content -notmatch '\$commonParams\s*=\s*@\{') {
            $errors.Add("deploy-all.ps1: must define the @commonParams splat with ServerIP/SSHUser/SSHPort/SSHKeyPath.")
        }
        if ($content -notmatch '\[switch\]\$AllowDirty') {
            $errors.Add("deploy-all.ps1: must expose -AllowDirty.")
        }
        if ($content -notmatch '\[string\]\$ExpectedCommit') {
            $errors.Add("deploy-all.ps1: must expose -ExpectedCommit.")
        }
        if ($content -notmatch '\$applicationParams') {
            $errors.Add("deploy-all.ps1: must forward -AllowDirty/-ExpectedCommit to the application step.")
        }
        if ($content -notmatch '\-ExtraParams') {
            $errors.Add("deploy-all.ps1: Run-Step must accept -ExtraParams.")
        }
    }
}

# ─────────────────────────────────────────
# WhatsApp configuration invariants
# ─────────────────────────────────────────
# The application only requires the WhatsApp secrets when WebhookEnabled is true. The environment
# script must map the four Vault display names, resolve them by name when secrets-map.json has no
# OCID, and never emit empty values that would override the appsettings defaults.
$environmentScriptPath = Join-Path $scriptRoot "03-setup-environment.ps1"
if (Test-Path -LiteralPath $environmentScriptPath -PathType Leaf) {
    $environmentContent = Get-Content -LiteralPath $environmentScriptPath -Raw
    foreach ($vaultSecretName in @(
        "WhatsAppWebhooksToken",
        "MetaChaveApp",
        "MetaWhatsappWABA",
        "MetaTokenWhatsapp"
    )) {
        if ($environmentContent -notmatch [regex]::Escape($vaultSecretName)) {
            $errors.Add("03-setup-environment.ps1 must map the Vault secret '$vaultSecretName'.")
        }
    }
    if ($environmentContent -notmatch 'WhatsApp__WebhookEnabled') {
        $errors.Add("03-setup-environment.ps1 must emit WhatsApp__WebhookEnabled so production starts without WhatsApp secrets.")
    }
    if ($environmentContent -notmatch 'Resolve-VaultSecretByName') {
        $errors.Add("03-setup-environment.ps1 must resolve WhatsApp secrets by Vault display name when secrets-map.json has no OCID.")
    }
    # RolePromotionChallenge__SigningKey is mandatory and has no default in any compose file, so the
    # generated .env must carry the Vault secret that docker compose interpolates.
    if ($environmentContent -notmatch '(?m)^ROLE_PROMOTION_SIGNING_KEY=\$\(\$envVars\[''ROLE_PROMOTION_SIGNING_KEY''\]\)\r?$') {
        $errors.Add("03-setup-environment.ps1 must write ROLE_PROMOTION_SIGNING_KEY to the generated .env.")
    }
    if ($environmentContent -notmatch '(?ms)if \(\[string\]::IsNullOrWhiteSpace\(\$envVars\[''ROLE_PROMOTION_SIGNING_KEY''\]\)\)[\s\S]*?exit 1') {
        $errors.Add("03-setup-environment.ps1 must abort when ROLE_PROMOTION_SIGNING_KEY does not resolve from the Vault.")
    }
    # AdminRecovery is a break-glass feature published as two Vault secrets whose display names are
    # exactly the .NET configuration keys. The pipeline must resolve both by name and write them to
    # the generated .env without printing, defaulting, or committing their values.
    foreach ($adminRecoverySecretName in @("AdminRecovery__Enabled", "AdminRecovery__Key")) {
        if ($environmentContent -notmatch [regex]::Escape($adminRecoverySecretName)) {
            $errors.Add("03-setup-environment.ps1 must resolve the Vault secret '$adminRecoverySecretName' by exact display name.")
        }
    }
    if ($environmentContent -notmatch '(?m)^AdminRecovery__Enabled=\$adminRecoveryEnabledValue\r?$') {
        $errors.Add("03-setup-environment.ps1 must write AdminRecovery__Enabled to the generated .env from the Vault value.")
    }
    if ($environmentContent -notmatch '(?m)^AdminRecovery__Key=\$adminRecoveryKeyValue\r?$') {
        $errors.Add("03-setup-environment.ps1 must write AdminRecovery__Key to the generated .env from the Vault value.")
    }
    if ($environmentContent -notmatch '(?ms)\$adminRecoveryEnabledRaw\s*=\s*Resolve-VaultSecretByName\s+-SecretName\s+\$adminRecoveryEnabledSecretName') {
        $errors.Add("03-setup-environment.ps1 must resolve AdminRecovery__Enabled with Resolve-VaultSecretByName.")
    }
    if ($environmentContent -notmatch '(?ms)\$adminRecoveryKeyValue\s*=\s*Resolve-VaultSecretByName\s+-SecretName\s+\$adminRecoveryKeySecretName') {
        $errors.Add("03-setup-environment.ps1 must resolve AdminRecovery__Key with Resolve-VaultSecretByName.")
    }
    if ($environmentContent -match '(?i)ADMIN_RECOVERY_(ENABLED|KEY)\b') {
        $errors.Add("03-setup-environment.ps1 must not use ADMIN_RECOVERY_* names for the AdminRecovery secrets.")
    }
} else {
    $errors.Add("03-setup-environment.ps1 is missing.")
}

# ─────────────────────────────────────────
# Deploy hardening invariants
# ─────────────────────────────────────────

$manifestHelper = Join-Path $scriptRoot "deploy-manifest.ps1"
if (-not (Test-Path -LiteralPath $manifestHelper -PathType Leaf)) {
    $errors.Add("deploy-manifest.ps1 is missing; deploy source validation and manifests are required.")
} else {
    $helperContent = Get-Content -LiteralPath $manifestHelper -Raw
    foreach ($functionName in @("Assert-DeploySource", "Get-FileSha256", "New-DeploymentManifest", "Write-DeploymentManifest")) {
        if ($helperContent -notmatch [regex]::Escape("function $functionName")) {
            $errors.Add("deploy-manifest.ps1: missing function $functionName.")
        }
    }
}

$applicationScript = Join-Path $scriptRoot "04-deploy-application.ps1"
if (Test-Path -LiteralPath $applicationScript -PathType Leaf) {
    $applicationContent = Get-Content -LiteralPath $applicationScript -Raw
    foreach ($invariant in @(
        @{ Pattern = '\[switch\]\$AllowDirty'; Message = "04-deploy-application.ps1 must expose -AllowDirty." },
        @{ Pattern = '\[string\]\$ExpectedCommit'; Message = "04-deploy-application.ps1 must expose -ExpectedCommit." },
        @{ Pattern = 'Assert-DeploySource'; Message = "04-deploy-application.ps1 must validate the deploy source." },
        @{ Pattern = 'deployment-manifest\.json'; Message = "04-deploy-application.ps1 must upload deployment-manifest.json." },
        @{ Pattern = '\$extractCommand\s*=\s*\$extractCommand\s*-replace\s+"`r`n",\s*"`n"'; Message = "04-deploy-application.ps1 must normalize the remote extraction command from CRLF to LF." },
        @{ Pattern = 'ssh\s+@sshOpts\s+"\$\{SSHUser\}@\$\{ServerIP\}"\s+\$extractCommand'; Message = "04-deploy-application.ps1 must send the normalized extraction command via SSH." },
        @{ Pattern = 'image:\s*urbeat/backend:latest'; Message = "04-deploy-application.ps1 must tag the backend image so workers can reuse it." },
        @{ Pattern = '(?m)^\s*worker-business:'; Message = "04-deploy-application.ps1 must define the worker-business service." },
        @{ Pattern = '(?m)^\s*worker-communications:'; Message = "04-deploy-application.ps1 must define the worker-communications service." },
        @{ Pattern = 'Hangfire__Role:\s*"Web"'; Message = "04-deploy-application.ps1 must run the API as Hangfire role Web." },
        @{ Pattern = 'Hangfire__Role:\s*"Business"'; Message = "04-deploy-application.ps1 must run the business worker as Hangfire role Business." },
        @{ Pattern = 'Hangfire__Role:\s*"Communications"'; Message = "04-deploy-application.ps1 must run the communications worker as Hangfire role Communications." },
        @{ Pattern = 'Outbox__WorkerEnabled:\s*"true"'; Message = "04-deploy-application.ps1 must enable the outbox in the business and communications workers." },
        @{ Pattern = 'Outbox__WorkerEnabled:\s*"false"'; Message = "04-deploy-application.ps1 must disable the outbox in the API." },
        @{ Pattern = 'entrypoint:\s*\["dotnet",\s*"/app/worker/Urbeat\.Worker\.dll"\]'; Message = "04-deploy-application.ps1 workers must use the published Worker entrypoint." }
    )) {
        if ($applicationContent -notmatch $invariant.Pattern) {
            $errors.Add($invariant.Message)
        }
    }
} else {
    $errors.Add("04-deploy-application.ps1 is missing.")
}

# Parse YAML structurally. A validator is mandatory: silently skipping this check can report a
# broken Compose file as valid. Prefer PyYAML, then Ruby/Psych, then docker compose config.
$yamlFiles = @((Join-Path $scriptRoot "..\..\docker\docker-compose.yml"), (Join-Path $scriptRoot "..\..\docker\prometheus\prometheus.yml"))
$applicationContent = Get-Content -LiteralPath (Join-Path $scriptRoot "04-deploy-application.ps1") -Raw
$composeMatch = [regex]::Match($applicationContent, '(?s)\$dockerCompose = @''\r?\n(.*?)\r?\n''@')
$generatedCompose = $null
if ($composeMatch.Success) {
    $generatedCompose = Join-Path ([System.IO.Path]::GetTempPath()) ("urbeat-compose-" + [guid]::NewGuid() + ".yml")
    [System.IO.File]::WriteAllText($generatedCompose, $composeMatch.Groups[1].Value, [System.Text.UTF8Encoding]::new($false))
    $yamlFiles += $generatedCompose
} else { $errors.Add("04-deploy-application.ps1: docker-compose heredoc could not be extracted for YAML validation.") }

$python = Get-Command python -ErrorAction SilentlyContinue
if (-not $python) { $python = Get-Command python3 -ErrorAction SilentlyContinue }
$ruby = Get-Command ruby -ErrorAction SilentlyContinue
$validatedWithParser = $false
try {
    if ($python) {
        & $python.Source -c "import yaml" 2>$null
        if ($LASTEXITCODE -eq 0) {
            $validatedWithParser = $true
            foreach ($yamlFile in $yamlFiles) { & $python.Source -c "import sys, yaml; yaml.safe_load(open(sys.argv[1], encoding='utf-8'))" $yamlFile; if ($LASTEXITCODE -ne 0) { $errors.Add("Invalid YAML: $yamlFile") } }
        }
    }
    if (-not $validatedWithParser -and $ruby) {
        $validatedWithParser = $true
        foreach ($yamlFile in $yamlFiles) { & $ruby.Source -e 'require "yaml"; YAML.safe_load(File.read(ARGV[0]), aliases: true)' $yamlFile; if ($LASTEXITCODE -ne 0) { $errors.Add("Invalid YAML (Ruby/Psych): $yamlFile") } }
    }
    if (-not $validatedWithParser) {
        $docker = Get-Command docker -ErrorAction SilentlyContinue
        if ($docker) {
            $validatedWithParser = $true
            foreach ($yamlFile in @($yamlFiles[0])) { & $docker.Source compose -f $yamlFile config --quiet; if ($LASTEXITCODE -ne 0) { $errors.Add("docker compose rejected $yamlFile.") } }
            if ($yamlFiles.Count -gt 1) { $errors.Add("Docker Compose validated the Compose file, but cannot validate non-Compose YAML (prometheus.yml); install Python with PyYAML or Ruby/Psych.") }
        }
    }
    if (-not $validatedWithParser) { $errors.Add("No YAML validator available. Install Python with PyYAML, Ruby/Psych, or Docker Compose.") }
} finally { if ($generatedCompose) { Remove-Item -LiteralPath $generatedCompose -Force -ErrorAction SilentlyContinue } }

# ─────────────────────────────────────────
# Role promotion signing key invariant
# ─────────────────────────────────────────
# Every backend process (two API replicas and two Hangfire workers) must receive
# RolePromotionChallenge__SigningKey through the shared anchor, and the interpolation must not
# fall back to an empty default: the application fails at startup when the key is empty.
if ($composeMatch.Success) {
    $generatedComposeText = $composeMatch.Groups[1].Value
    $anchorMatch = [regex]::Match($generatedComposeText, '(?ms)^x-backend-environment: &backend-environment[ \t]*\r?\n(.*?)(?=^\S)')
    if (-not $anchorMatch.Success) {
        $errors.Add("04-deploy-application.ps1: x-backend-environment anchor could not be extracted from the compose template.")
    } else {
        $backendEnvironmentBlock = $anchorMatch.Groups[1].Value
        if ($backendEnvironmentBlock -notmatch 'RolePromotionChallenge__SigningKey:\s*\$\{ROLE_PROMOTION_SIGNING_KEY\}') {
            $errors.Add("04-deploy-application.ps1: x-backend-environment must set RolePromotionChallenge__SigningKey from ROLE_PROMOTION_SIGNING_KEY.")
        }
        if ($backendEnvironmentBlock -match '\$\{ROLE_PROMOTION_SIGNING_KEY:-') {
            $errors.Add("04-deploy-application.ps1: ROLE_PROMOTION_SIGNING_KEY must not be interpolated with an empty default.")
        }
        # Email/WhatsApp links fall back to http://localhost:4200 when this option is unset. Both API
        # replicas and both workers must therefore receive it through the shared anchor.
        if ($backendEnvironmentBlock -notmatch 'EmailConfirmation__FrontendBaseUrl:\s*\$\{FRONTEND_BASE_URL') {
            $errors.Add("04-deploy-application.ps1: x-backend-environment must set EmailConfirmation__FrontendBaseUrl from FRONTEND_BASE_URL.")
        }
        # AdminRecovery is forwarded from .env through the same anchor so both API replicas and both
        # workers see it. Enabled must default to false and the key must have no functional fallback.
        if ($backendEnvironmentBlock -notmatch 'AdminRecovery__Enabled:\s*\$\{AdminRecovery__Enabled:-false\}') {
            $errors.Add("04-deploy-application.ps1: x-backend-environment must set AdminRecovery__Enabled from the .env with a false default.")
        }
        if ($backendEnvironmentBlock -notmatch 'AdminRecovery__Key:\s*\$\{AdminRecovery__Key\}') {
            $errors.Add("04-deploy-application.ps1: x-backend-environment must set AdminRecovery__Key from the .env.")
        }
        if ($backendEnvironmentBlock -match 'AdminRecovery__Key:\s*\$\{AdminRecovery__Key:-[^}\s]+\}') {
            $errors.Add("04-deploy-application.ps1: AdminRecovery__Key must not be interpolated with a functional fallback.")
        }
        if ($backendEnvironmentBlock -match 'AdminRecovery__Key:\s*["'']?[A-Za-z0-9+/=_\-]{16,}') {
            $errors.Add("04-deploy-application.ps1: AdminRecovery__Key must not contain a committed value.")
        }
    }

    $mergedServices = [regex]::Matches($generatedComposeText, '(?m)^[ \t]*<<:[ \t]*\*backend-environment[ \t]*\r?$')
    if ($mergedServices.Count -lt 4) {
        $errors.Add("04-deploy-application.ps1: the two API replicas and both workers must merge *backend-environment (found $($mergedServices.Count)).")
    }
}

# The repository Compose file uses the same anchor for local/internal stacks; it must forward the
# AdminRecovery values from .env exactly like production, disabled by default and without a key
# fallback or committed value.
$repositoryComposePath = Join-Path $scriptRoot "..\..\docker\docker-compose.yml"
if (Test-Path -LiteralPath $repositoryComposePath -PathType Leaf) {
    $repositoryComposeText = Get-Content -LiteralPath $repositoryComposePath -Raw
    $repositoryAnchorMatch = [regex]::Match($repositoryComposeText, '(?ms)^x-backend-environment: &backend-environment[ \t]*\r?\n(.*?)(?=^\S)')
    if (-not $repositoryAnchorMatch.Success) {
        $errors.Add("docker/docker-compose.yml: x-backend-environment anchor could not be extracted.")
    } else {
        $repositoryEnvironmentBlock = $repositoryAnchorMatch.Groups[1].Value
        if ($repositoryEnvironmentBlock -notmatch 'AdminRecovery__Enabled:\s*"\$\{AdminRecovery__Enabled:-false\}"') {
            $errors.Add("docker/docker-compose.yml: x-backend-environment must set AdminRecovery__Enabled from the .env with a false default.")
        }
        if ($repositoryEnvironmentBlock -notmatch 'AdminRecovery__Key:\s*"\$\{AdminRecovery__Key\}"') {
            $errors.Add("docker/docker-compose.yml: x-backend-environment must set AdminRecovery__Key from the .env.")
        }
        if ($repositoryEnvironmentBlock -match 'AdminRecovery__Key:\s*"\$\{AdminRecovery__Key:-[^}\s]+\}"') {
            $errors.Add("docker/docker-compose.yml: AdminRecovery__Key must not be interpolated with a functional fallback.")
        }
        if ($repositoryEnvironmentBlock -match 'AdminRecovery__Key:\s*["'']?[A-Za-z0-9+/=_\-]{16,}') {
            $errors.Add("docker/docker-compose.yml: AdminRecovery__Key must not contain a committed value.")
        }
    }
} else {
    $errors.Add("docker/docker-compose.yml is missing.")
}

# Compose performs an additional schema/interpolation check when available. It is intentionally
# best-effort here because production .env values are not present in a local validation run.
$docker = Get-Command docker -ErrorAction SilentlyContinue
$localCompose = Join-Path $scriptRoot "..\..\docker\docker-compose.yml"
if ($docker) {
    & $docker.Source compose -f $localCompose config --quiet 2>$null
    if ($LASTEXITCODE -ne 0) { $errors.Add("docker compose config rejected $localCompose.") }
}

# Multi-replica API topology invariants. Keep these checks local and textual: validation must not
# require OCI, Docker, secrets, or a running database.
$nginxScript = Join-Path $scriptRoot "05-configure-nginx.ps1"
if (Test-Path -LiteralPath $nginxScript -PathType Leaf) {
    $nginxContent = Get-Content -LiteralPath $nginxScript -Raw
    foreach ($invariant in @(
        @{ Pattern = 'least_conn;'; Message = "05-configure-nginx.ps1 must use least_conn." },
        @{ Pattern = '127\.0\.0\.1:5001'; Message = "05-configure-nginx.ps1 must include API replica port 5001." },
        @{ Pattern = '127\.0\.0\.1:5002'; Message = "05-configure-nginx.ps1 must include API replica port 5002." },
        @{ Pattern = 'proxy_set_header Upgrade \$http_upgrade;'; Message = "05-configure-nginx.ps1 must preserve WebSocket upgrade headers." }
    )) {
        if ($nginxContent -notmatch $invariant.Pattern) { $errors.Add($invariant.Message) }
    }
} else {
    $errors.Add("05-configure-nginx.ps1 is missing.")
}

$dockerScript = Join-Path $scriptRoot "02-install-docker-aarch64.ps1"
if (Test-Path -LiteralPath $dockerScript -PathType Leaf) {
    $dockerContent = Get-Content -LiteralPath $dockerScript -Raw
    if ($dockerContent -notmatch '\[System\.IO\.File\]::WriteAllText') {
        $errors.Add("02-install-docker-aarch64.ps1 must write the uploaded bash script as LF via WriteAllText for Linux compatibility.")
    }
    if ($dockerContent -match 'Out-File') {
        $errors.Add("02-install-docker-aarch64.ps1 must not upload a CRLF bash script via Out-File.")
    }
} else {
    $errors.Add("02-install-docker-aarch64.ps1 is missing.")
}

# Uploaded bash scripts must be written as LF UTF-8 without BOM via WriteAllText,
# never via Out-File (which emits CRLF/BOM and breaks the Linux shebang).
foreach ($bashScriptName in @("06-setup-ssl.ps1", "07-verify-deployment.ps1")) {
    $bashScriptPath = Join-Path $scriptRoot $bashScriptName
    if (Test-Path -LiteralPath $bashScriptPath -PathType Leaf) {
        $bashContent = Get-Content -LiteralPath $bashScriptPath -Raw
        if ($bashContent -notmatch '\[System\.IO\.File\]::WriteAllText') {
            $errors.Add("$bashScriptName must write the uploaded bash script as LF via WriteAllText for Linux compatibility.")
        }
        if ($bashContent -match 'Out-File') {
            $errors.Add("$bashScriptName must not upload a CRLF bash script via Out-File.")
        }
    } else {
        $errors.Add("$bashScriptName is missing.")
    }
}

$internalScript = Join-Path (Split-Path -Parent $scriptRoot) "deploy-internal.ps1"
if (Test-Path -LiteralPath $internalScript -PathType Leaf) {
    $internalContent = Get-Content -LiteralPath $internalScript -Raw
    foreach ($invariant in @(
        @{ Pattern = '\[switch\]\$AllowDirty'; Message = "deploy-internal.ps1 must expose -AllowDirty." },
        @{ Pattern = '\[string\]\$ExpectedCommit'; Message = "deploy-internal.ps1 must expose -ExpectedCommit." },
        @{ Pattern = 'Assert-DeploySource'; Message = "deploy-internal.ps1 must validate the deploy source." },
        @{ Pattern = 'deployment-manifest\.json'; Message = "deploy-internal.ps1 must write deployment-manifest.json." }
    )) {
        if ($internalContent -notmatch $invariant.Pattern) {
            $errors.Add($invariant.Message)
        }
    }
    if ($internalContent -match 'git push origin master') {
        $errors.Add("deploy-internal.ps1 must not push to origin during deploy.")
    }
    if ($internalContent -match 'git commit') {
        $errors.Add("deploy-internal.ps1 must not create git commits during deploy.")
    }
    if ($internalContent -match '\$SkipUpload') {
        $errors.Add("deploy-internal.ps1 must always produce a deployment manifest (no -SkipUpload escape hatch).")
    }
    if ($internalContent -match 'Set-Content|ConvertTo-Json') {
        $errors.Add("deploy-internal.ps1 must not rewrite version files (package.json/landing-page.component.ts).")
    }
    if ($internalContent -match 'StrictHostKeyChecking=no') {
        $errors.Add("deploy-internal.ps1 must not disable SSH host key checking.")
    }
} else {
    $errors.Add("scripts/deploy-internal.ps1 is missing.")
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "OCI deployment scripts, JSON map, local paths, and deploy-hardening invariants are valid." -ForegroundColor Green
