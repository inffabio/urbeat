Describe 'OCI runbook safety' {
  BeforeAll { $readme = Get-Content (Join-Path $PSScriptRoot '..\README.md') -Raw; $runbooks = (Get-ChildItem (Join-Path $PSScriptRoot '..\runbooks') -File | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n" }
  It 'documents validation and review gates' { $readme | Should Match 'fmt -check'; $readme | Should Match 'validate'; $readme | Should Match 'refresh-only'; $readme | Should Match 'revis'; $runbooks | Should Match 'import' }
  It 'does not prescribe automatic destructive writes' { ($readme + $runbooks) | Should Not Match 'apply\s+-auto-approve'; ($readme + $runbooks) | Should Not Match 'terraform destroy' }
}
