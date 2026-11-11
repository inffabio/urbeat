Describe 'Terraform preflight boundary' {
  BeforeAll { . (Join-Path $PSScriptRoot '..\terraform-preflight.ps1'); $script:root = Join-Path $PSScriptRoot '..\..\..' }
  It 'keeps Terraform out of the legacy pipeline validator' {
    $text = Get-Content (Join-Path $PSScriptRoot '..\validate-pipeline.ps1') -Raw
    $text | Should Not Match '(?i)Get-Command\s+terraform'
    $text | Should Not Match '(?i)Terraform CLI is not installed'
  }
  It 'exposes explicit local and operational modes without OCI write commands' {
    $text = Get-Content (Join-Path $PSScriptRoot '..\terraform-preflight.ps1') -Raw
    $text | Should Match 'ValidateSet\(''Render'',''Validate'',''OperationalGate''\)'; $text | Should Match 'InventoryPath'; $text | Should Not Match '(?i)terraform\s+(apply|import|destroy|force-unlock)'
  }
  It 'returns a reviewable object for synthetic inventory' {
    $inventory = Join-Path $TestDrive 'inventory.json'
    '{"referenceCommit":"3c475c5"}' | Set-Content $inventory
    $result = Invoke-TerraformPreflight -Mode Render -TerraformDirectory (Join-Path $script:root 'terraform/oci') -InventoryPath $inventory -ExpectedApplicationCommit '3c475c5'
    $result.ConfigHash | Should Not BeNullOrEmpty
    $result.Passed | Should Be $true
  }
  It 'blocks operational mode without approved artifacts and lock marker' {
    $inventory = Join-Path $TestDrive 'inventory-operational.json'; '{"referenceCommit":"3c475c5","resources":[]}' | Set-Content $inventory
    $result = Invoke-TerraformPreflight -Mode OperationalGate -TerraformDirectory (Join-Path $script:root 'terraform/oci') -InventoryPath $inventory -ExpectedApplicationCommit '3c475c5'
    $result.Passed | Should Be $false
    ($result.Errors -join "`n") | Should Match 'approved import-map|locking marker|plan JSON'
  }
}
