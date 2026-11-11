Describe 'safe plan gate' {
  It 'does not contain forbidden write operations' {
    $gate = Get-Content (Join-Path $PSScriptRoot '..\scripts\assert-safe-plan.ps1') -Raw
    $gate | Should Match 'delete'; $gate | Should Match 'referenceCommit'; $gate | Should Not Match 'terraform\s+(apply|import|destroy|force-unlock)'
  }

  It 'allows create-only render and guard actions' {
    $plan = Join-Path $TestDrive 'create.json'; $inventory = Join-Path $TestDrive 'inventory.json'
    '{"referenceCommit":"test","resource_changes":[{"address":"terraform_data.existing_instance_guard","change":{"actions":["create"]}}]}' | Set-Content $plan
    '{"referenceCommit":"test"}' | Set-Content $inventory
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot '..\scripts\assert-safe-plan.ps1') -PlanJsonPath $plan -InventoryPath $inventory -RequireInventoryCommit test
    $LASTEXITCODE | Should Be 0
  }

  It 'allows update-only metadata actions' {
    $plan = Join-Path $TestDrive 'update.json'; $inventory = Join-Path $TestDrive 'inventory-update.json'
    '{"referenceCommit":"test","resource_changes":[{"address":"terraform_data.render_marker","change":{"actions":["update"]}}]}' | Set-Content $plan
    '{"referenceCommit":"test","resources":[]}' | Set-Content $inventory
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot '..\scripts\assert-safe-plan.ps1') -PlanJsonPath $plan -InventoryPath $inventory -RequireInventoryCommit test
    $LASTEXITCODE | Should Be 0
  }

  It 'blocks delete and replace for every resource' {
    foreach ($action in @('["delete"]', '["create","delete"]', '["replace"]')) {
      $plan = Join-Path $TestDrive ([guid]::NewGuid().ToString() + '.json'); $inventory = Join-Path $TestDrive 'inventory-critical.json'
      ('{"referenceCommit":"test","resource_changes":[{"address":"oci_core_instance.production","change":{"actions":' + $action + '}}]}') | Set-Content $plan
      '{"referenceCommit":"test"}' | Set-Content $inventory
      & pwsh -NoProfile -File (Join-Path $PSScriptRoot '..\scripts\assert-safe-plan.ps1') -PlanJsonPath $plan -InventoryPath $inventory -RequireInventoryCommit test 2>$null
      $LASTEXITCODE | Should Not Be 0
    }
  }

  It 'requires an approved inventory address for future real resources' {
    $plan = Join-Path $TestDrive 'real.json'; $inventory = Join-Path $TestDrive 'inventory-real.json'
    '{"referenceCommit":"test","resource_changes":[{"address":"oci_core_instance.production","change":{"actions":["create"]}}]}' | Set-Content $plan
    '{"referenceCommit":"test","resources":[]}' | Set-Content $inventory
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot '..\scripts\assert-safe-plan.ps1') -PlanJsonPath $plan -InventoryPath $inventory -RequireInventoryCommit test 2>$null
    $LASTEXITCODE | Should Not Be 0
  }
}
