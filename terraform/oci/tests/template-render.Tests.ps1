Describe 'Application templates' {
  BeforeAll {
    $root = Join-Path $PSScriptRoot '..'
    $compose = Get-Content (Join-Path $root 'templates/docker-compose.yml.tftpl') -Raw
    $nginx = (Get-Content (Join-Path $root 'templates/urbeat-api.conf.tftpl') -Raw) + (Get-Content (Join-Path $root 'templates/urbeat-frontend.conf.tftpl') -Raw)
    $cloud = Get-Content (Join-Path $root 'templates/cloud-init.yaml.tftpl') -Raw
    $all = $compose + $nginx + $cloud
  }
  It 'uses loopback API bindings and workers' { $compose | Should Match '127\.0\.0\.1:\$\{api_port_1\}:8080'; $compose | Should Match '127\.0\.0\.1:\$\{api_port_2\}:8080'; $compose | Should Match 'worker-' }
  It 'keeps Nginx on the host and Prometheus on the Docker network' {
    $compose | Should Not Match '(?m)^\s+nginx:'
    $compose | Should Not Match '80:80|443:443'
    $cloud | Should Match 'runtime env is pending pipeline delivery'
    $cloud | Should Not Match '/run/urbeat/runtime\.env'
    (Get-Content (Join-Path $root 'templates/prometheus.yml.tftpl') -Raw) | Should Match 'webapi:8080'
    (Get-Content (Join-Path $root 'templates/prometheus.yml.tftpl') -Raw) | Should Not Match '127\.0\.0\.1:.*500[12]'
    (Get-Content (Join-Path $root 'templates/urbeat-frontend.conf.tftpl') -Raw) | Should Match '\$\{frontend_path\}'
  }
  It 'configures least_conn and WebSocket forwarding' { $nginx | Should Match 'upstream urbeat_api'; $nginx | Should Match 'least_conn'; $nginx | Should Match 'proxy_http_version 1\.1'; $nginx | Should Match 'Upgrade'; $nginx | Should Match 'Connection' }
  It 'contains no secret values and has an idempotent marker' { $all | Should Not Match '(?i)(password\s*[:=]|BEGIN .* PRIVATE KEY|jwt|connection string)'; $cloud | Should Match '/var/lib/urbeat/terraform-config.json'; $cloud | Should Match 'template_hash' }
}
