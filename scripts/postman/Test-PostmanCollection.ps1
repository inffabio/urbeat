[CmdletBinding()]
param(
    [string]$CollectionPath = 'postman/urbeat.postman_collection.json',
    [string]$LocalEnvironment = 'postman/urbeat.local.postman_environment.json',
    [string]$OciEnvironment = 'postman/urbeat.oci.postman_environment.json',
    [string]$FeatureMapPath = 'postman/feature-map.json',
    [switch]$VerifyGuard
)

$ErrorActionPreference = 'Stop'
$script:failures = [System.Collections.Generic.List[string]]::new()
function Fail([string]$message) {
    $script:failures.Add($message)
    Write-Host "FAIL $message" -ForegroundColor Red
}
function Read-Utf8Text([string]$path) {
    $full = (Resolve-Path -LiteralPath $path).Path
    return [System.IO.File]::ReadAllText($full, [System.Text.Encoding]::UTF8)
}
function Read-Utf8Json([string]$path) {
    return (Read-Utf8Text $path | ConvertFrom-Json)
}
function Get-DescriptionField([string]$description, [string]$label) {
    if ([string]::IsNullOrEmpty($description)) { return $null }
    foreach ($line in ($description -split "`n")) {
        if ($line.StartsWith($label + ':')) { return $line.Substring($label.Length + 1).Trim() }
    }
    return $null
}

$expectedGuardNames = @(
    'POST /api/auth/register/customer',
    'POST /api/auth/register/seller',
    'POST /api/auth/email/confirm/{code}',
    'POST /api/auth/email/confirm',
    'POST /api/auth/email/resend-confirmation',
    'POST /api/auth/forgot-password',
    'POST /api/auth/reset-password',
    'POST /api/auth/update-email',
    'DELETE /api/customer/addresses/{addressId}',
    'POST /api/neighborhoods/import-by-city',
    'PATCH /api/stores/{storeId}/status',
    'DELETE /api/stores/{storeId}/cuisine-types/{cuisineTypeId}',
    'POST /api/subscriptions/contract',
    'POST /api/stores/{storeId}/publish',
    'POST /api/printer-config/wifi-print',
    'DELETE /api/stores/{storeId}/additionals/{additionalId}',
    'DELETE /api/stores/{storeId}/categories/{categoryId}',
    'DELETE /api/stores/{storeId}/products/{productId}',
    'POST /api/checkout/confirm',
    'POST /api/checkout/customer-session',
    'POST /api/checkout/customer-verification/start',
    'POST /api/checkout/customer-verification/confirm',
    'POST /api/checkout/customer-verification/resend',
    'POST /api/orders',
    'PATCH /api/orders/store/customers/{customerUserId}/status',
    'POST /api/orders/{orderId}/complete',
    'PATCH /api/orders/{orderId}/status',
    'DELETE /api/orders/store/{orderId}/delivery-driver',
    'POST /api/orders/store/{orderId}/delivery-link',
    'POST /api/orders/{orderId}/delivery-confirmation',
    'DELETE /api/stores/{storeId}/payment-gateway',
    'PUT /api/stores/{storeId}/payment-gateway',
    'POST /api/payments/order',
    'POST /api/driver/orders/{orderId}/start',
    'POST /api/driver/orders/{orderId}/confirm-pin',
    'DELETE /api/delivery-drivers/{storeDriverId}',
    'PATCH /api/delivery-drivers/{storeDriverId}/status',
    'POST /api/delivery-drivers/invites/{token}/accept',
    'POST /api/delivery-drivers/store',
    'POST /api/delivery-drivers/store/{storeDriverId}/resend-invite',
    'POST /api/delivery-drivers/store/{storeDriverId}/revoke',
    'POST /api/delivery-drivers/store/invite',
    'POST /api/delivery-routes/store/{routeId}/revoke',
    'POST /api/delivery-routes/store/{routeId}/share',
    'POST /api/delivery-routes/store/send',
    'POST /api/public/deliveries/{token}/deliver',
    'POST /api/public/delivery-routes/{token}/stops/{sequence}/deliver',
    'POST /api/admin/subscriptions/notifications/process',
    'POST /api/admin/subscriptions/status',
    'POST /api/admin/delivery-neighborhoods',
    'POST /api/admin/import-neighborhoods-google',
    'POST /api/admin/import-neighborhoods-rj',
    'POST /api/admin/migrate-images',
    'POST /api/outbox/{id}/retry',
    'POST /api/admin/plans',
    'PUT /api/admin/plans/{planId}',
    'PATCH /api/admin/plans/{planId}/status',
    'DELETE /api/admin/system-parameters/{key}',
    'PUT /api/admin/system-parameters/{key}',
    'POST /api/admin/system-parameters/reload',
    'DELETE /api/LandingPageContent/{id}',
    'POST /api/webhooks/mercadopago',
    'POST /api/webhooks/asaas'
)

$allowlistPosts = @(
    'POST /api/auth/login/customer',
    'POST /api/auth/login/seller',
    'POST /api/auth/login/admin',
    'POST /api/auth/login/driver',
    'POST /api/auth/token',
    'POST /api/auth/refresh',
    'POST /api/checkout/preview'
)

$environmentCatalog = @(
    @('runMode','default'), @('allowWebhooks','default'), @('lastLoginRole','default'),
    @('baseUrl','default'), @('webBaseUrl','default'), @('webhookPublicUrl','default'),
    @('cep','default'), @('neighborhood','default'), @('cuisineType','default'),
    @('storePath','default'), @('storeSlug','default'), @('landingSection','default'),
    @('cityId','default'), @('printerPresetId','default'),
    @('customerEmail','secret'), @('newCustomerEmail','secret'), @('customerPassword','secret'),
    @('sellerEmail','secret'), @('sellerPassword','secret'), @('adminEmail','secret'),
    @('adminPassword','secret'), @('driverEmail','secret'), @('driverPassword','secret'),
    @('customerToken','secret'), @('sellerToken','secret'), @('adminToken','secret'), @('driverToken','secret'),
    @('storeId','default'), @('cuisineTypeId','default'), @('categoryId','default'), @('productId','default'),
    @('productImageUrl','default'), @('uploadedImageUrl','default'), @('additionalId','default'),
    @('additionalGroupId','default'), @('addressId','default'), @('orderId','default'),
    @('orderCode','default'), @('paymentId','default'), @('gatewayTransactionId','default'),
    @('storeDriverId','default'), @('deliveryDriverId','default'), @('routeId','default'),
    @('customerUserId','default'), @('sellerUserId','default'), @('planId','default'),
    @('systemParameterKey','default'), @('landingContentId','default'), @('outboxEventId','default'),
    @('customerNotificationId','default'), @('sellerNotificationId','default'),
    @('verificationId','default'), @('stopSequence','default'),
    @('deliveryRouteToken','secret'), @('individualDeliveryToken','secret'), @('driverInviteToken','secret'),
    @('verificationCode','secret'), @('deliveryPin','secret'), @('emailConfirmationCode','secret'),
    @('emailConfirmationToken','secret'), @('passwordResetToken','secret'), @('emailChangeChallenge','secret'),
    @('customerDocument','secret'), @('sellerDocument','secret'), @('storeDocument','secret'),
    @('storePixKey','secret'), @('asaasWebhookToken','secret'), @('paymentGatewayAccessToken','secret'),
    @('printerBase64Data','secret')
)

$allowedNonEmptyEnvironmentValues = @('baseUrl','webBaseUrl','runMode','allowWebhooks')

function Test-Guid([string]$value) {
    $parsed = [guid]::Empty
    return [guid]::TryParse($value, [ref]$parsed)
}

# ---------------- coleção ----------------
$collection = Read-Utf8Json $CollectionPath
$collectionText = Read-Utf8Text $CollectionPath

if ($collection.info.schema -ne 'https://schema.getpostman.com/json/collection/v2.1.0/collection.json') {
    Fail "schema invalido: $($collection.info.schema)"
}
if ($collection.info.name -ne 'urbeat · API REST') { Fail "info.name invalido: $($collection.info.name)" }
if (-not (Test-Guid ([string]$collection.info._postman_id))) { Fail 'info._postman_id nao e GUID' }

$map = $null
if (Test-Path -LiteralPath $FeatureMapPath) { $map = Read-Utf8Json $FeatureMapPath }

$folders = @($collection.item)
if ($folders.Count -ne 14) { Fail "pastas esperadas=14 encontradas=$($folders.Count)" }

$requests = [System.Collections.Generic.List[object]]::new()
$allIds = [System.Collections.Generic.List[string]]::new()
$subfolderCount = 0
foreach ($folder in $folders) {
    if (-not (Test-Guid ([string]$folder.id))) { Fail "pasta sem GUID: $($folder.name)" }
    $allIds.Add([string]$folder.id)
    $subfolders = @($folder.item)
    $subfolderCount += $subfolders.Count
    foreach ($subfolder in $subfolders) {
        if (-not (Test-Guid ([string]$subfolder.id))) { Fail "subpasta sem GUID: $($subfolder.name)" }
        $allIds.Add([string]$subfolder.id)
        foreach ($item in @($subfolder.item)) {
            if ($null -eq $item.request) { Fail "item aninhado inesperado: $($item.name)"; continue }
            $requests.Add([pscustomobject]@{
                Name = [string]$item.name
                Id = [string]$item.id
                FolderName = [string]$folder.name
                Subfolder = [string]$subfolder.name
                Request = $item.request
                Item = $item
            })
            $allIds.Add([string]$item.id)
        }
    }
}
if ($subfolderCount -ne 44) { Fail "subpastas esperadas=44 encontradas=$subfolderCount" }
if ($requests.Count -ne 179) { Fail "requisicoes esperadas=179 encontradas=$($requests.Count)" }

$duplicateIds = @($allIds | Group-Object | Where-Object Count -gt 1)
foreach ($g in $duplicateIds) { Fail "id duplicado: $($g.Name)" }
$duplicateNames = @($requests | Group-Object Name | Where-Object Count -gt 1)
foreach ($g in $duplicateNames) { Fail "request duplicada: $($g.Name)" }

if ($null -ne $map) {
    $mapFeatures = @{}
    foreach ($f in @($map.features)) {
        $mapFeatures[$f.id] = $f
        $expectedFolder = "$($f.id) · $($f.name)"
        $actual = @($folders | Where-Object { $_.name -eq $expectedFolder })
        if ($actual.Count -ne 1) { Fail "pasta do mapa ausente: $expectedFolder" }
        foreach ($s in @($f.subfolders)) {
            if ($actual.Count -eq 1) {
                $subActual = @($actual[0].item | Where-Object { $_.name -eq $s })
                if ($subActual.Count -ne 1) { Fail "subpasta do mapa ausente: $expectedFolder / $s" }
            }
        }
    }
    foreach ($folder in $folders) {
        if ($folder.name -notmatch '^(\d{2}) · (.+)$') { Fail "pasta fora do padrao: $($folder.name)"; continue }
        $fid = $Matches[1]
        if (-not $mapFeatures.ContainsKey($fid)) { Fail "pasta com feature desconhecida: $($folder.name)" }
    }
}

$allowedBearerTokens = @('{{customerToken}}','{{sellerToken}}','{{adminToken}}','{{driverToken}}')
$strictPublic200 = @(
    'GET /api/public/stores',
    'GET /api/stores/cuisine-types',
    'GET /api/LandingPageContent'
)
$environmentKeys = @()
if (Test-Path -LiteralPath $LocalEnvironment) {
    $environmentKeys = @((Read-Utf8Json $LocalEnvironment).values | ForEach-Object { [string]$_.key })
}
$guidRegex = '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}'
$multipartRequests = @()
$scriptTexts = [System.Collections.Generic.List[string]]::new()

if ($null -ne $collection.event) {
    foreach ($ev in @($collection.event)) { if ($ev.script.exec) { $scriptTexts.Add((@($ev.script.exec) -join "`n")) } }
}

foreach ($r in $requests) {
    if ($r.Name -notmatch '^[A-Z]+ /api/') { Fail "nome de requisicao fora do padrao: $($r.Name)" }
    if (-not (Test-Guid $r.Id)) { Fail "request sem GUID: $($r.Name)" }
    if ($r.Name -notmatch '^[A-Z]+ ') { continue }
    if ($r.Name.Split(' ')[0] -ne $r.Request.method) { Fail "metodo do nome difere do request: $($r.Name)" }

    $description = [string]$r.Request.description
    foreach ($label in @('Feature','Controller','Auth','Classificação','Pré-requisitos','Efeitos','Captura')) {
        if ($null -eq (Get-DescriptionField $description $label)) { Fail "descricao sem $label em $($r.Name)" }
    }
    $featureLine = Get-DescriptionField $description 'Feature'
    if ($null -ne $featureLine) {
        $expectedPrefix = "$($r.FolderName) / $($r.Subfolder)"
        if ($featureLine -ne $expectedPrefix) { Fail "Feature da descricao difere do caminho em $($r.Name)" }
    }
    $classLine = Get-DescriptionField $description 'Classificação'
    $manualLine = Get-DescriptionField $description 'Manual'
    if ($classLine -eq 'destructive' -or $classLine -eq 'webhook') {
        if ($manualLine -ne 'sim') { Fail "Manual: sim ausente em $($r.Name)" }
    } elseif ($null -ne $manualLine) {
        Fail "Manual indevido em $($r.Name)"
    }

    $url = $r.Request.url
    if ($null -eq $url) { Fail "url ausente em $($r.Name)"; continue }
    $raw = [string]$url.raw
    if (-not $raw.StartsWith('{{baseUrl}}/api/')) { Fail "url sem {{baseUrl}}/api/: $($r.Name)" }
    if ($raw -match 'https?://') { Fail "url com host absoluto: $($r.Name)" }
    if ($raw -match '\{\{baseUrl\}\}//') { Fail "url com barra duplicada: $($r.Name)" }
    if ($raw -match ':guid|:int') { Fail "url com constraint: $($r.Name)" }
    if (@($url.host).Count -ne 1 -or [string]$url.host[0] -ne '{{baseUrl}}') {
        Fail "host invalido em $($r.Name)"
    }
    $pathText = '/' + (@($url.path) -join '/')
    $rawPath = ($raw -split '\?')[0]
    if ($rawPath -ne ('{{baseUrl}}' + $pathText)) { Fail "url.raw difere de host+path em $($r.Name)" }

    $enabledQuery = @()
    if ($null -ne $url.query) { $enabledQuery = @($url.query | Where-Object { -not $_.disabled }) }
    $expectedRaw = '{{baseUrl}}' + $pathText
    if ($enabledQuery.Count -gt 0) {
        $expectedRaw += '?' + (($enabledQuery | ForEach-Object { "$($_.key)=$($_.value)" }) -join '&')
    }
    if ($raw -ne $expectedRaw) { Fail "url.raw difere dos parametros habilitados em $($r.Name)" }

    $variableSources = @($raw)
    if ($null -ne $r.Request.body -and $r.Request.body.mode -eq 'raw') { $variableSources += [string]$r.Request.body.raw }
    foreach ($source in $variableSources) {
        foreach ($m in [regex]::Matches($source, '\{\{([A-Za-z0-9_$]+)\}\}')) {
            $variable = $m.Groups[1].Value
            if ($variable -eq 'baseUrl' -or $variable -eq '$guid') { continue }
            if ($environmentKeys -notcontains $variable) {
                Fail "variavel nao declarada no ambiente: $variable em $($r.Name)"
            }
        }
    }

    $headers = @($r.Request.header)
    $accept = @($headers | Where-Object { $_.key -eq 'Accept' -and $_.value -eq 'application/json' })
    if ($accept.Count -ne 1) { Fail "Accept ausente em $($r.Name)" }
    foreach ($h in $headers) {
        if ($h.key -in @('Content-Type','Authorization')) { Fail "header proibido $($h.key) em $($r.Name)" }
        if ($h.key -notin @('Accept','asaas-access-token')) { Fail "header inesperado $($h.key) em $($r.Name)" }
    }
    if ($r.Name -eq 'POST /api/webhooks/asaas') {
        $asaas = @($headers | Where-Object { $_.key -eq 'asaas-access-token' -and $_.value -eq '{{asaasWebhookToken}}' })
        if ($asaas.Count -ne 1) { Fail 'header asaas-access-token ausente ou invalido' }
    } elseif (@($headers | Where-Object { $_.key -eq 'asaas-access-token' }).Count -gt 0) {
        Fail "header asaas fora do webhook em $($r.Name)"
    }

    $authType = [string]$r.Request.auth.type
    if ($authType -notin @('noauth','bearer')) { Fail "auth.type invalido em $($r.Name): $authType" }
    if ($authType -eq 'bearer') {
        $bearer = @($r.Request.auth.bearer)
        if ($bearer.Count -ne 1 -or $bearer[0].key -ne 'token' -or $allowedBearerTokens -notcontains [string]$bearer[0].value) {
            Fail "bearer invalido em $($r.Name)"
        }
    }
    if ($authType -eq 'noauth' -and [string]$r.Request.method -eq 'GET') {
        $testText = (@($r.Item.event | ForEach-Object { if ($null -ne $_.script -and $null -ne $_.script.exec) { @($_.script.exec) -join "`n" } }) -join "`n")
        if ($testText -notmatch 'pm\.test\(') {
            Fail "GET publico sem teste em $($r.Name)"
        }
        if ($testText -notmatch '401' -or $testText -notmatch '403') {
            Fail "GET publico sem assert de 401/403 em $($r.Name)"
        }
        if ($strictPublic200 -contains $r.Name -and $testText -notmatch 'eql\(200\)') {
            Fail "GET publico sem assert de 200 em $($r.Name)"
        }
    }

    $body = $r.Request.body
    if ($null -ne $body) {
        if ($body.mode -eq 'raw') {
            if ($null -eq $body.options -or $body.options.raw.language -ne 'json') {
                Fail "body raw sem language=json em $($r.Name)"
            }
            try {
                $null = $body.raw | ConvertFrom-Json
            } catch {
                Fail "body JSON invalido em $($r.Name)"
            }
            if ($body.raw -match $guidRegex) { Fail "GUID literal no body em $($r.Name)" }
            foreach ($m in [regex]::Matches($body.raw, 'https?://[^"\s\\]+')) {
                if ($m.Value -ne 'https://www.urbeat.com.br') {
                    Fail "URL externa no body em $($r.Name): $($m.Value)"
                }
            }
        } elseif ($body.mode -eq 'formdata') {
            $multipartRequests += $r
            $fileFields = @($body.formdata | Where-Object { $_.key -eq 'file' -and $_.type -eq 'file' })
            if ($fileFields.Count -ne 1) { Fail "campo file ausente em $($r.Name)" }
            foreach ($field in @($body.formdata)) {
                if ($field.PSObject.Properties.Name -contains 'src') {
                    $srcValue = $field.src
                    if ($srcValue -is [System.Array]) { if ($srcValue.Count -gt 0) { Fail "src absoluto em $($r.Name)" } }
                    elseif (-not [string]::IsNullOrEmpty($srcValue)) { Fail "src absoluto em $($r.Name)" }
                }
            }
            if ($description -notmatch 'frontend/src/assets/images/|docs/') {
                $fieldDescriptions = @($body.formdata | ForEach-Object { [string]$_.description }) -join ' '
                if ($fieldDescriptions -notmatch 'frontend/src/assets/images/|docs/') {
                    Fail "descricao multipart sem fixture em $($r.Name)"
                }
            }
        } else {
            Fail "body.mode inesperado em $($r.Name): $($body.mode)"
        }
    }

    foreach ($ev in @($r.Item.event)) {
        if ($null -ne $ev.script -and $null -ne $ev.script.exec) { $scriptTexts.Add((@($ev.script.exec) -join "`n")) }
    }
}
if ($multipartRequests.Count -ne 2) { Fail "multipart esperados=2 encontrados=$($multipartRequests.Count)" }

foreach ($text in $scriptTexts) {
    if ($text -match 'pm\.sendRequest') { Fail 'script contem pm.sendRequest' }
    if ($text -match 'setNextRequest') { Fail 'script contem setNextRequest' }
    if ($text -match 'console\.log') { Fail 'script contem console.log' }
    if ($text -match 'pm\.environment\.set' -and ($text -notmatch '\btry\b' -or $text -notmatch '\bcatch\b')) {
        Fail 'script de captura sem try/catch'
    }
}

# ---------------- guard ----------------
$guardText = $null
if ($null -ne $collection.event) {
    $prerequests = @($collection.event | Where-Object { $_.listen -eq 'prerequest' })
    if ($prerequests.Count -ne 1) { Fail 'guard de colecao (prerequest) ausente ou duplicado' }
    elseif ($prerequests[0].script.exec) { $guardText = (@($prerequests[0].script.exec) -join "`n") }
}
$guardNames = @()
$readOnlyNames = @()
if ($null -ne $guardText) {
    $roMatch = [regex]::Match($guardText, 'const readOnlyPosts = new Set\(\[(.*?)\]\);', 'Singleline')
    if (-not $roMatch.Success) { Fail 'guard sem readOnlyPosts' }
    else { $readOnlyNames = @([regex]::Matches($roMatch.Groups[1].Value, "'([^']+)'") | ForEach-Object { $_.Groups[1].Value }) }
    $destructiveMatch = [regex]::Match($guardText, 'const destructive = new Set\(\[(.*?)\]\);', 'Singleline')
    if (-not $destructiveMatch.Success) { Fail 'guard sem destructive' }
    else { $guardNames = @([regex]::Matches($destructiveMatch.Groups[1].Value, "'([^']+)'") | ForEach-Object { $_.Groups[1].Value }) }

    if ($readOnlyNames.Count -ne 7) { Fail "guard readOnlyPosts esperado=7 encontrado=$($readOnlyNames.Count)" }
    foreach ($n in $allowlistPosts) { if ($readOnlyNames -notcontains $n) { Fail "guard sem allowlist: $n" } }
    if ($guardNames.Count -ne 63) { Fail "guard destructive esperado=63 encontrado=$($guardNames.Count)" }
    foreach ($n in $expectedGuardNames) { if ($guardNames -notcontains $n) { Fail "guard sem destrutiva: $n" } }
    foreach ($n in $guardNames) { if ($expectedGuardNames -notcontains $n) { Fail "guard com destrutiva inesperada: $n" } }

    if ($guardText -notmatch "rawMode === 'write' \|\| rawMode === 'destructive'") { Fail 'guard sem normalizacao de runMode (D7)' }
    if ($guardText -notmatch ":\s*'read-only'") { Fail 'guard nao normaliza runMode invalido para read-only' }
    if ($guardText -notmatch "startsWith\('POST /api/webhooks/'\)") { Fail 'guard sem deteccao de webhook' }
    if ($guardText -notmatch 'pm\.execution\.skipRequest') { Fail 'guard sem pm.execution.skipRequest' }
    if ($guardText -notmatch 'throw new Error') { Fail 'guard sem fallback throw' }
}

# ---------------- ambientes ----------------
function Test-Environment([string]$path, [string]$expectedName, [string]$expectedBase, [string]$expectedWeb) {
    if (-not (Test-Path -LiteralPath $path)) { Fail "ambiente ausente: $path"; return }
    $environment = Read-Utf8Json $path
    if ($environment.name -ne $expectedName) { Fail "nome de ambiente invalido em $path`: $($environment.name)" }
    if ($environment._postman_variable_scope -ne 'environment') { Fail "scope invalido em $path" }
    $values = @($environment.values)
    $keys = @($values | ForEach-Object { $_.key })
    $duplicates = @($keys | Group-Object | Where-Object Count -gt 1)
    foreach ($g in $duplicates) { Fail "variavel duplicada em $path`: $($g.Name)" }
    foreach ($pair in $environmentCatalog) {
        $key = $pair[0]
        $type = $pair[1]
        $entry = @($values | Where-Object { $_.key -eq $key })
        if ($entry.Count -ne 1) { Fail "variavel ausente em $path`: $key"; continue }
        if ([string]$entry[0].type -ne $type) { Fail "tipo invalido em $path`: $key=$($entry[0].type) esperado=$type" }
        if ($key -notin $allowedNonEmptyEnvironmentValues -and -not [string]::IsNullOrEmpty([string]$entry[0].value)) {
            Fail "valor preenchido em $path`: $key"
        }
    }
    foreach ($key in $keys) {
        if (-not (@($environmentCatalog | ForEach-Object { $_[0] }) -contains $key)) { Fail "variavel inesperada em $path`: $key" }
    }
    $base = @($values | Where-Object { $_.key -eq 'baseUrl' })[0].value
    $web = @($values | Where-Object { $_.key -eq 'webBaseUrl' })[0].value
    $runMode = @($values | Where-Object { $_.key -eq 'runMode' })[0].value
    $allowWebhooks = @($values | Where-Object { $_.key -eq 'allowWebhooks' })[0].value
    if ($base -ne $expectedBase) { Fail "baseUrl invalido em $path`: $base" }
    if ($web -ne $expectedWeb) { Fail "webBaseUrl invalido em $path`: $web" }
    if ($runMode -ne 'read-only') { Fail "runMode invalido em $path`: $runMode" }
    if ([string]$allowWebhooks -ne 'false') { Fail "allowWebhooks invalido em $path`: $allowWebhooks" }
}
Test-Environment $LocalEnvironment 'urbeat · local' 'http://localhost:5000' 'http://localhost:4200'
Test-Environment $OciEnvironment 'urbeat · oci' 'https://urbeat.com.br' 'https://urbeat.com.br'

$localKeys = @((Read-Utf8Json $LocalEnvironment).values | ForEach-Object { $_.key } | Sort-Object)
$ociKeys = @((Read-Utf8Json $OciEnvironment).values | ForEach-Object { $_.key } | Sort-Object)
if (($localKeys -join '|') -ne ($ociKeys -join '|')) { Fail 'ambientes com conjuntos de variaveis diferentes' }

# ---------------- segredos ----------------
function Test-Secrets([string]$path) {
    $text = Read-Utf8Text $path
    # IDs do Postman são GUIDs; removê-los evita falsos positivos de CPF/CNPJ.
    $text = [regex]::Replace($text, '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}', '')
    $patterns = @(
        @('JWT', 'eyJ[A-Za-z0-9_-]{10,}'),
        @('Bearer literal', '(?i)bearer\s+[A-Za-z0-9._-]{10,}'),
        @('CHANGE_ME', 'CHANGE_ME'),
        @('chave privada', '-----BEGIN'),
        @('CPF', '(?<![+\d_-])\d{11}(?![\d_-])'),
        @('CNPJ', '(?<![+\d_-])\d{14}(?![\d_-])'),
        @('e-mail urbeat', '@urbeat\.com\.br')
    )
    foreach ($p in $patterns) {
        if ($text -match $p[1]) { Fail "secret pattern ($($p[0])) em $path" }
    }
    if ($text -match 'asaas-access-token"\s*:\s*"(?!\{\{asaasWebhookToken\}\})') {
        Fail "asaas-access-token literal em $path"
    }
}
foreach ($path in @($CollectionPath, $LocalEnvironment, $OciEnvironment)) { Test-Secrets $path }

# ---------------- guard: matriz determinística ----------------
if ($VerifyGuard -and $null -ne $guardText) {
    $node = Get-Command node -ErrorAction SilentlyContinue
    $matrix = $null
    $synthetic = $null
    if ($null -ne $node) {
        $tempDir = Join-Path $env:TEMP ('urbeat-postman-guard-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        try {
            $guardFile = Join-Path $tempDir 'guard.js'
            $requestsFile = Join-Path $tempDir 'requests.json'
            $driverFile = Join-Path $tempDir 'driver.js'
            $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
            [System.IO.File]::WriteAllText($guardFile, $guardText, $utf8NoBom)
            $requestPayload = @($requests | ForEach-Object {
                [pscustomobject]@{
                    name = $_.Name
                    method = [string]$_.Request.method
                    path = '/' + (@($_.Request.url.path) -join '/')
                }
            })
            [System.IO.File]::WriteAllText($requestsFile, (ConvertTo-Json -InputObject $requestPayload -Compress -Depth 5), $utf8NoBom)
            $driver = @'
const fs = require('fs');
const guard = fs.readFileSync(process.argv[2], 'utf8');
const requests = JSON.parse(fs.readFileSync(process.argv[3], 'utf8'));
function decide(req, mode, allow) {
  let skipped = false;
  const pm = {
    info: { requestName: req.name },
    environment: {
      get(k) {
        if (k === 'runMode') return mode;
        if (k === 'allowWebhooks') return allow;
        if (k === 'baseUrl') return req.baseUrl === undefined ? 'http://localhost:5000' : req.baseUrl;
        return '';
      }
    },
    request: {
      method: req.method,
      url: Object.assign(
        {
          path: req.path.split('/').filter(Boolean),
          toString() { return (req.absolute || '{{baseUrl}}') + req.path; }
        },
        req.useGetPath === false ? {} : { getPath() { return req.path; } }
      )
    },
    execution: { skipRequest() { skipped = true; } }
  };
  let threw = false;
  try { new Function('pm', guard)(pm); } catch (e) { threw = true; }
  return skipped || threw;
}
const matrix = {};
for (const req of requests) {
  matrix[req.name] = {
    'read-only': decide(req, 'read-only', 'false'),
    write: decide(req, 'write', 'false'),
    destructive: decide(req, 'destructive', 'false'),
    webhooks: decide(req, 'destructive', 'true'),
    invalid: decide(req, 'lixo', 'false')
  };
}
const synthetic = {};
const cases = [
  { label: 'method-mismatch', name: 'GET /api/public/stores', method: 'POST', path: '/api/stores', mode: 'destructive', allow: 'true', expected: true },
  { label: 'path-mismatch', name: 'GET /api/public/stores', method: 'GET', path: '/api/customer/me', mode: 'destructive', allow: 'true', expected: true },
  { label: 'extra-segment', name: 'GET /api/public/stores', method: 'GET', path: '/api/public/stores/123', mode: 'read-only', allow: 'false', expected: true },
  { label: 'missing-base-url', name: 'GET /api/public/stores', method: 'GET', path: '/api/public/stores', baseUrl: '', mode: 'destructive', allow: 'true', expected: true },
  { label: 'absolute-host', name: 'GET /api/public/stores', method: 'GET', path: '/api/public/stores', absolute: 'https://evil.example', mode: 'destructive', allow: 'true', expected: true },
  { label: 'resolved-param', name: 'GET /api/stores/{storeId}/products', method: 'GET', path: '/api/stores/abc/products', mode: 'read-only', allow: 'false', expected: false },
  { label: 'template-param', name: 'GET /api/stores/{storeId}/products', method: 'GET', path: '/api/stores/{{storeId}}/products', mode: 'read-only', allow: 'false', expected: false },
  { label: 'path-array-fallback', name: 'GET /api/stores/{storeId}/products', method: 'GET', path: '/api/stores/{{storeId}}/products', useGetPath: false, mode: 'read-only', allow: 'false', expected: false }
];
for (const item of cases) {
  synthetic[item.label] = { blocked: decide(item, item.mode, item.allow), expected: item.expected };
}
process.stdout.write(JSON.stringify({ matrix, synthetic }));
'@
            [System.IO.File]::WriteAllText($driverFile, $driver, $utf8NoBom)
            $json = & node $driverFile $guardFile $requestsFile 2>&1
            if ($LASTEXITCODE -ne 0) { Fail "execucao do guard via node falhou: $json" }
            else {
                $result = $json | ConvertFrom-Json
                $matrix = $result.matrix
                $synthetic = $result.synthetic
            }
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    $classOf = @{}
    foreach ($r in $requests) {
        $classOf[$r.Name] = Get-DescriptionField ([string]$r.Request.description) 'Classificação'
    }

    $decisions = @{}
    if ($null -ne $matrix) {
        foreach ($prop in $matrix.PSObject.Properties) {
            $inner = @{}
            foreach ($ip in $prop.Value.PSObject.Properties) { $inner[$ip.Name] = [bool]$ip.Value }
            $decisions[$prop.Name] = $inner
        }
    } else {
        # Fallback estrutural: reavalia as mesmas condicoes do guard usando as listas extraidas.
        foreach ($r in $requests) {
            $method = $r.Name.Split(' ')[0]
            $name = $r.Name
            $isRead = ($method -eq 'GET' -or $method -eq 'HEAD')
            $isAllowedPost = $readOnlyNames -contains $name
            $isWebhook = $name.StartsWith('POST /api/webhooks/')
            $isDestructive = $guardNames -contains $name
            $inner = @{}
            foreach ($modeRaw in @('read-only','write','destructive','webhooks','invalid')) {
                $allowWebhooks = ($modeRaw -eq 'webhooks')
                $rawMode = if ($modeRaw -eq 'webhooks') { 'destructive' } elseif ($modeRaw -eq 'invalid') { 'lixo' } else { $modeRaw }
                $mode = if ($rawMode -eq 'write' -or $rawMode -eq 'destructive') { $rawMode } else { 'read-only' }
                $skip = $false
                if (-not $isRead -and -not $isAllowedPost -and $mode -eq 'read-only') { $skip = $true }
                if ($isDestructive -and $mode -ne 'destructive') { $skip = $true }
                if ($isWebhook -and -not $allowWebhooks) { $skip = $true }
                $inner[$modeRaw] = $skip
            }
            $decisions[$name] = $inner
        }
        Write-Host 'AVISO: node indisponivel; matriz do guard validada pela simulacao estrutural.' -ForegroundColor Yellow
    }

    $mismatches = 0
    foreach ($r in $requests) {
        $c = $classOf[$r.Name]
        $expected = @{
            'read-only' = ($c -eq 'read')
            'write' = ($c -in @('read','write'))
            'destructive' = ($c -ne 'webhook')
            'webhooks' = $true
            'invalid' = ($c -eq 'read')
        }
        foreach ($mode in $expected.Keys) {
            $actual = -not [bool]$decisions[$r.Name][$mode]
            if ($actual -ne [bool]$expected[$mode]) {
                Fail "guard matrix: $($r.Name) em $mode executou=$actual esperado=$($expected[$mode])"
                $mismatches++
            }
        }
    }
    if ($mismatches -eq 0) {
        Write-Host ("OK guard matrix {0}x5" -f $requests.Count)
    }
    if ($null -ne $synthetic) {
        $syntheticFailures = 0
        foreach ($prop in $synthetic.PSObject.Properties) {
            if ([bool]$prop.Value.blocked -ne [bool]$prop.Value.expected) {
                Fail "guard name/url mismatch: $($prop.Name) bloqueou=$($prop.Value.blocked) esperado=$($prop.Value.expected)"
                $syntheticFailures++
            }
        }
        if ($syntheticFailures -eq 0) {
            Write-Host ("OK guard name/url mismatch cases={0}" -f @($synthetic.PSObject.Properties).Count)
        }
    } elseif ($null -ne $node) {
        Fail 'casos sintéticos de nome x URL do guard não foram executados'
    } else {
        Write-Host 'AVISO: node indisponivel; casos sintéticos de nome x URL nao executados.' -ForegroundColor Yellow
    }
}

# ---------------- relatório ----------------
if ($script:failures.Count -eq 0) {
    Write-Host ("OK structure folders={0} subfolders={1} requests={2}" -f $folders.Count, $subfolderCount, $requests.Count)
    if ($null -ne $guardText) {
        Write-Host ("OK guard read-only={0} destructive={1}" -f $readOnlyNames.Count, $guardNames.Count)
    }
    Write-Host 'OK secrets=0'
    exit 0
}
Write-Host ("FAILED {0} problema(s)" -f $script:failures.Count) -ForegroundColor Red
exit 1
