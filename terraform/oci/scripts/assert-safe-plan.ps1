[CmdletBinding()]
param([Parameter(Mandatory)][string]$PlanJsonPath, [Parameter(Mandatory)][string]$InventoryPath, [Parameter(Mandatory)][string]$RequireInventoryCommit)
$plan = if ($PlanJsonPath -eq '-') { $input | Out-String | ConvertFrom-Json } else { Get-Content $PlanJsonPath -Raw | ConvertFrom-Json }
$inventory = Get-Content $InventoryPath -Raw | ConvertFrom-Json
if ($inventory.referenceCommit -ne $RequireInventoryCommit) { throw 'Inventory reference commit mismatch.' }
$approved = @($inventory.resources | Where-Object { $_.approved -eq $true -or $_.importStatus -eq 'approved' } | ForEach-Object { $_.terraformAddress })
foreach ($change in @($plan.resource_changes)) {
  $actions = @($change.change.actions)
  # No delete/replace is safe, including non-critical resources.
  if ($actions -contains 'delete' -or $actions -contains 'replace') { throw "Unsafe destructive action at $($change.address)." }
  if ($actions | Where-Object { $_ -notin @('create','update') }) { throw "Unsupported plan action at $($change.address)." }
  # Only render/metadata guards are allowed until an approved import inventory
  # explicitly names a future real OCI address.
  if ($change.address -notmatch '^terraform_data\.' -and $approved -notcontains $change.address) { throw "Address is not in the render/import allowlist: $($change.address)." }
}
exit 0
