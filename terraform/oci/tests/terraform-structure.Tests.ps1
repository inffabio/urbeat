Describe 'Terraform OCI safety invariants' {
  BeforeAll { $root = Join-Path $PSScriptRoot '..'; $files = Get-ChildItem $root -Recurse -File | Where-Object { $_.Extension -in '.tf','.tftpl' } }
  It 'pins Terraform, provider and remote backend' {
    (Get-Content (Join-Path $root 'versions.tf') -Raw) | Should Match 'required_version'
    (Get-Content (Join-Path $root 'versions.tf') -Raw) | Should Match 'oracle/oci'
    (Get-Content (Join-Path $root 'backend.tf') -Raw) | Should Match 'backend\s+"s3"'
  }
  It 'uses official OCI authentication enum values' {
    $provider = Get-Content (Join-Path $root 'providers.tf') -Raw
    $vars = Get-Content (Join-Path $root 'variables.tf') -Raw
    $provider | Should Match 'var\.auth == "ApiKey"'
    $vars | Should Match 'ApiKey.*InstancePrincipal.*ResourcePrincipal'
    $vars | Should Not Match 'API_KEY|INSTANCE_PRINCIPAL|RESOURCE_PRINCIPAL'
  }
  It 'contains no secret content or private key literals' {
    foreach ($file in $files) { (Get-Content $file.FullName -Raw) | Should Not Match '(?i)(secret_content|BEGIN .* PRIVATE KEY|password\s*=|token\s*=)' }
  }
  It 'keeps the protected topology' {
    $compose = Get-Content (Join-Path $root 'templates/docker-compose.yml.tftpl') -Raw
    $nginx = (Get-Content (Join-Path $root 'templates/urbeat-api.conf.tftpl') -Raw) + (Get-Content (Join-Path $root 'templates/urbeat-frontend.conf.tftpl') -Raw)
    $compose | Should Match '127\.0\.0\.1:\$\{api_port_1\}:8080'
    $compose | Should Match '127\.0\.0\.1:\$\{api_port_2\}:8080'
    $compose | Should Match 'worker-business'
    $compose | Should Match 'worker-communications'
    $nginx | Should Match 'least_conn'
  }
  It 'labels terraform_data as scaffold rather than imported protection' {
    $text = (Get-ChildItem (Join-Path $root 'modules') -Recurse -Filter '*.tf' | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
    $text | Should Match '(?i)scaffold|metadata.only|does not import'
    $text | Should Not Match 'terraform_data[\s\S]*prevent_destroy'
  }
}
