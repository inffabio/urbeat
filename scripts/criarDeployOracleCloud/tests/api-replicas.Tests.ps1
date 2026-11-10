Describe "multi-replica API topology" {
    BeforeAll {
        $script:application = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\04-deploy-application.ps1") -Raw
        $script:nginx = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\05-configure-nginx.ps1") -Raw
        $script:compose = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\..\..\docker\docker-compose.yml") -Raw
        $script:localNginx = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\..\..\docker\nginx\conf.d\00-shared.conf") -Raw
        $script:localHttps = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\..\..\docker\nginx\conf.d\20-https.conf") -Raw
        $script:localHttp = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\..\..\docker\nginx\conf.d\10-http.conf") -Raw
        $script:docs = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\..\..\docs\operacoes\api-replicas-load-test.md") -Raw
        $script:program = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\..\..\backend\src\Urbeat.WebApi\Program.cs") -Raw
    }

    It "defines two explicit OCI API services on distinct loopback ports" {
        $application | Should Match '(?m)^  backend:'
        $application | Should Match '(?m)^  backend-replica-2:'
        $application | Should Match '127\.0\.0\.1:5001:5000'
        $application | Should Match '127\.0\.0\.1:5002:5000'
        ($application -match 'deploy:\s*\r?\n\s+replicas:') | Should Be $false
    }

    It "keeps Outbox and Hangfire out of both API replicas" {
        $application | Should Match '(?s)backend:.*?Hangfire__Role:\s*"Web".*?Outbox__WorkerEnabled:\s*"false"'
        $application | Should Match '(?s)backend-replica-2:.*?Hangfire__Role:\s*"Web".*?Outbox__WorkerEnabled:\s*"false"'
        $application | Should Match '(?m)^  worker-business:'
        $application | Should Match '(?m)^  worker-communications:'
    }

    It "configures least_conn, both upstreams and websocket proxying" {
        $nginx | Should Match 'least_conn;'
        $nginx | Should Match '127\.0\.0\.1:5001'
        $nginx | Should Match '127\.0\.0\.1:5002'
        $nginx | Should Match 'proxy_set_header Upgrade \$http_upgrade;'
        $nginx | Should Match 'location = /health'
        $nginx | Should Match 'location = /health/ready'
        $nginx | Should Match 'location = /health/live'
    }

    It "does not collapse readiness or liveness into the legacy health endpoint" {
        foreach ($config in @($nginx, $localHttps, $localHttp)) {
            $config | Should Match 'location = /health/ready'
            $config | Should Match 'location = /health/live'
            ($config -match 'location /health\s*\{\s*proxy_pass[^;]+/health;') | Should Be $false
        }
    }

    It "keeps local Compose aligned with two replicas" {
        $compose | Should Match '(?m)^  webapi:'
        $compose | Should Match '(?m)^  webapi_replica_2:'
        $compose | Should Match '5001:8080'
        $compose | Should Match '5002:8080'
        $localNginx | Should Match 'least_conn;'
    }

    It "documents the connection budget and progressive RUN_ID load procedure" {
        $docs | Should Match '4 processos.*80'
        $docs | Should Match 'total <= 90'
        $application | Should Match 'DB_LOGS_MAX_POOL_SIZE:-1'
        $docs | Should Match '50.*100.*150.*200 VUs'
        $docs | Should Match 'RUN_ID'
    }

    It "serializes PostgreSQL migrations and seeders across API replicas" {
        $program | Should Match 'PostgresStartupLock.AcquireAsync'
        $program | Should Match 'pg_advisory_lock'
        $program | Should Match 'await using var startupLock'
        $docs | Should Match 'advisory lock'
    }
}
