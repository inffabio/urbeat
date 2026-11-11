[CmdletBinding()]
param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'fixtures/rendered'))
$templateDirectory = Join-Path $PSScriptRoot '..\templates'
$values = @{
  api_image='urbeat/webapi:test'; api_port_1='5001'; api_port_2='5002'; runtime_env_file='/opt/urbeat/docker/.env'; compose_root='/opt/urbeat/docker'
  config_path='/opt/urbeat/docker/generated'; frontend_path='/opt/urbeat/frontend-dist'; downloads_path='/opt/urbeat/downloads'; client_max_body_size='50m'; scrape_interval='15s'; frontend_server_names='www.example.invalid'; api_server_name='api.example.invalid'
  template_hash='synthetic-template-hash'
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
foreach ($name in @('docker-compose.yml','urbeat-frontend.conf','urbeat-api.conf','urbeat-monitoring.conf','prometheus.yml','grafana-datasources.yml')) {
  $source = Join-Path $templateDirectory ($name + '.tftpl')
  $text = Get-Content $source -Raw
  foreach ($key in $values.Keys) { $text = $text.Replace('${' + $key + '}', [string]$values[$key]) }
  [IO.File]::WriteAllText((Join-Path $OutputDirectory $name), $text, [Text.UTF8Encoding]::new($false))
}
$cloud = Get-Content (Join-Path $templateDirectory 'cloud-init.yaml.tftpl') -Raw
foreach ($key in $values.Keys) { $cloud = $cloud.Replace('${' + $key + '}', [string]$values[$key]) }
foreach ($pair in @(@('compose','docker-compose.yml'), @('nginx_frontend','urbeat-frontend.conf'), @('nginx_api','urbeat-api.conf'), @('nginx_monitoring','urbeat-monitoring.conf'), @('prometheus','prometheus.yml'), @('grafana','grafana-datasources.yml'))) {
  $content = Get-Content (Join-Path $OutputDirectory $pair[1]) -Raw
  $cloud = $cloud.Replace('${indent(6, ' + $pair[0] + ')}', (($content.TrimEnd() -split "`r?`n" | ForEach-Object { '      ' + $_ }) -join "`n"))
}
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'cloud-init.yaml'), $cloud, [Text.UTF8Encoding]::new($false))
Write-Output $OutputDirectory
