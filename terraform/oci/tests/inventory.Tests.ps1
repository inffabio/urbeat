Describe 'OCI inventory contract' {
  It 'schema requires UTC commit and sanitized ownership fields' {
    $schema = Get-Content (Join-Path $PSScriptRoot '..\inventory\schema.json') -Raw | ConvertFrom-Json
    ($schema.required -contains 'capturedAtUtc') | Should Be $true
    ($schema.required -contains 'referenceCommit') | Should Be $true
    ($schema.properties.resources.items.required -contains 'owner') | Should Be $true
    (Get-Content (Join-Path $PSScriptRoot '..\inventory\import-map.example.json') -Raw) | Should Not Match '(?i)(secret_content|private.?key|password)'
  }
}
