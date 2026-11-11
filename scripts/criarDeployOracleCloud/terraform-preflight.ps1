[CmdletBinding()]
param(
  [ValidateSet('Render','Validate','OperationalGate')][string]$Mode = 'Render',
  [string]$TerraformDirectory,
  [string]$InventoryPath,
  [string]$ExpectedApplicationCommit,
  [string]$PlanJsonPath,
  [string]$ImportMapPath,
  [string]$HumanApprovalMarker,
  [string]$LockValidationMarker
)

function Get-TemplateHash([string]$directory) {
  $hash = ''
  if (Test-Path -LiteralPath $directory) {
    Get-ChildItem -LiteralPath $directory -Recurse -File -Filter '*.tftpl' | Sort-Object FullName | ForEach-Object {
      $sha = [Security.Cryptography.SHA256]::Create()
      try { $hash = ([BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($_.FullName)))).Replace('-','') + $hash } finally { $sha.Dispose() }
    }
  }
  return $hash
}

function Invoke-TerraformPreflight {
  param([ValidateSet('Render','Validate','OperationalGate')][string]$Mode = 'Render', [string]$TerraformDirectory, [string]$InventoryPath, [string]$ExpectedApplicationCommit, [string]$PlanJsonPath, [string]$ImportMapPath, [string]$HumanApprovalMarker, [string]$LockValidationMarker)
  $errors = [Collections.Generic.List[string]]::new()
  $inventory = $null
  if ($InventoryPath -and (Test-Path -LiteralPath $InventoryPath)) { $inventory = Get-Content -LiteralPath $InventoryPath -Raw | ConvertFrom-Json }
  if ($Mode -ne 'Render' -and -not (Get-Command terraform -ErrorAction SilentlyContinue)) { $errors.Add('Terraform CLI is required for Validate or OperationalGate.') }
  if ($Mode -eq 'OperationalGate') {
    if (-not $inventory) { $errors.Add('OperationalGate requires an approved inventory.') }
    if (-not $ExpectedApplicationCommit) { $errors.Add('OperationalGate requires ExpectedApplicationCommit.') }
    if ($inventory -and $inventory.referenceCommit -ne $ExpectedApplicationCommit) { $errors.Add('Inventory/reference commit mismatch.') }
    if (-not $PlanJsonPath -or -not (Test-Path -LiteralPath $PlanJsonPath)) { $errors.Add('OperationalGate requires a plan JSON artifact.') }
    if (-not $HumanApprovalMarker -or -not (Test-Path -LiteralPath $HumanApprovalMarker)) { $errors.Add('OperationalGate requires a human approval marker.') }
    if (-not $LockValidationMarker -or -not (Test-Path -LiteralPath $LockValidationMarker)) { $errors.Add('OperationalGate requires a validated locking marker; backend configurability is not automatic locking.') }
    if (-not $ImportMapPath -or -not (Test-Path -LiteralPath $ImportMapPath)) { $errors.Add('OperationalGate requires an approved import-map.') }
    if ($inventory -and -not @($inventory.resources | Where-Object { $_.approved -eq $true -or $_.importStatus -eq 'approved' })) { $errors.Add('OperationalGate is blocked: no approved real OCI resources/import addresses exist; terraform_data is scaffold metadata only.') }
  }
  if ($Mode -in @('Validate','OperationalGate') -and $errors.Count -eq 0) {
    Push-Location $TerraformDirectory
    try {
      & terraform init -backend=false | Out-Null
      if ($LASTEXITCODE -ne 0) { $errors.Add('Terraform local initialization failed.') }
      & terraform validate | Out-Null
      if ($LASTEXITCODE -ne 0) { $errors.Add('Terraform local validation failed.') }
    } finally { Pop-Location }
  }
  [pscustomobject]@{ Passed = $errors.Count -eq 0; Mode = $Mode; ConfigHash = Get-TemplateHash $TerraformDirectory; InventoryCommit = if ($inventory) { $inventory.referenceCommit } else { $null }; PlanPath = $PlanJsonPath; Errors = @($errors) }
}

if ($MyInvocation.InvocationName -ne '.') { Invoke-TerraformPreflight @PSBoundParameters }
