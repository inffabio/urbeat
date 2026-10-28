[CmdletBinding()]
param(
    [string]$CollectionPath = 'postman/urbeat.postman_collection.json',
    [string]$FeatureMapPath = 'postman/feature-map.json',
    [string]$ControllersPath = 'backend/src/Urbeat.WebApi/Controllers',
    [string]$Feature,
    [switch]$MapOnly
)

$ErrorActionPreference = 'Stop'
$script:failures = [System.Collections.Generic.List[string]]::new()
function Fail([string]$message) {
    $script:failures.Add($message)
    Write-Host "FAIL $message" -ForegroundColor Red
}
function Read-Utf8Json([string]$path) {
    $full = (Resolve-Path -LiteralPath $path).Path
    $text = [System.IO.File]::ReadAllText($full, [System.Text.Encoding]::UTF8)
    return ($text | ConvertFrom-Json)
}
function Read-Utf8Lines([string]$path) {
    $full = (Resolve-Path -LiteralPath $path).Path
    return [System.IO.File]::ReadAllLines($full, [System.Text.Encoding]::UTF8)
}
function To-Camel([string]$name) {
    if ([string]::IsNullOrEmpty($name)) { return $name }
    return ([char]::ToLowerInvariant($name[0]).ToString() + $name.Substring(1))
}
function Get-ExpectedPathVariable([string]$param, [string]$path) {
    switch ($param) {
        'storeId' { return 'storeId' }
        'categoryId' { return 'categoryId' }
        'productId' { return 'productId' }
        'addressId' { return 'addressId' }
        'additionalId' { return 'additionalId' }
        'cuisineTypeId' { return 'cuisineTypeId' }
        'planId' { return 'planId' }
        'routeId' { return 'routeId' }
        'storeDriverId' { return 'storeDriverId' }
        'customerUserId' { return 'customerUserId' }
        'orderId' { return 'orderId' }
        'sequence' { return 'stopSequence' }
        'key' { return 'systemParameterKey' }
        'code' { return 'emailConfirmationCode' }
        'slug' { return 'storeSlug' }
        'storePath' { return 'storePath' }
        'cep' { return 'cep' }
        'section' { return 'landingSection' }
        'cityId' { return 'cityId' }
        'notificationId' {
            if ($path.StartsWith('/api/seller/notifications')) { return 'sellerNotificationId' }
            return 'customerNotificationId'
        }
        'id' {
            if ($path.StartsWith('/api/LandingPageContent')) { return 'landingContentId' }
            return 'outboxEventId'
        }
        'token' {
            if ($path.StartsWith('/api/delivery-drivers/invites')) { return 'driverInviteToken' }
            if ($path.StartsWith('/api/public/delivery-routes')) { return 'deliveryRouteToken' }
            if ($path.StartsWith('/api/public/deliveries')) { return 'individualDeliveryToken' }
            return ''
        }
        default { return '' }
    }
}

$expectedFeatureCounts = @{
    '01' = 16; '02' = 9; '03' = 10; '04' = 25; '05' = 6; '06' = 8; '07' = 19;
    '08' = 22; '09' = 6; '10' = 3; '11' = 27; '12' = 25; '13' = 2; '14' = 1
}

$allowlistPosts = @(
    'POST /api/auth/login/customer',
    'POST /api/auth/login/seller',
    'POST /api/auth/login/admin',
    'POST /api/auth/login/driver',
    'POST /api/auth/token',
    'POST /api/auth/refresh',
    'POST /api/checkout/preview'
)

$destructiveNames = @(
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

function Get-AuthKind {
    param([string]$Block, [string]$Fallback)
    if ($Block -match '\[AllowAnonymous\]') { return 'anonymous' }
    if ($Block -match 'AdminOnly') { return 'AdminOnly' }
    if ($Block -match 'SellerOnly') { return 'SellerOnly' }
    if ($Block -match 'CustomerOnly') { return 'CustomerOnly' }
    if ($Block -match 'DeliveryDriverOnly') { return 'DeliveryDriverOnly' }
    if ($Block -match 'Roles\s*=\s*"Seller"') { return 'Role:Seller' }
    if ($Block -match '\[Authorize\b') { return 'authenticated' }
    return $Fallback
}

function Get-ControllerActions {
    param([string]$Path)
    $result = [System.Collections.Generic.List[object]]::new()
    $files = @(Get-ChildItem -LiteralPath $Path -Filter *Controller.cs -File | Sort-Object Name)
    foreach ($file in $files) {
        $lines = Read-Utf8Lines $file.FullName
        $classLine = $null
        for ($k = 0; $k -lt $lines.Count; $k++) {
            if ($lines[$k] -match '^\s*public\s+(sealed\s+)?class\s+\w+Controller') { $classLine = $k; break }
        }
        if ($null -eq $classLine) { throw "Controller nao encontrado em $($file.Name)" }
        $header = ('') ; if ($classLine -gt 0) { $header = ($lines[0..($classLine - 1)]) -join "`n" }
        $controllerRoute = ''
        if ($header -match '\[Route\("([^"]+)"\)\]') { $controllerRoute = $Matches[1] }
        $controllerAuth = Get-AuthKind -Block $header -Fallback 'anonymous'
        $controllerName = $file.BaseName -replace 'Controller$', ''

        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -notmatch '^\s*\[Http(Get|Post|Put|Patch|Delete)(\("([^"]*)"\))?') { continue }
            $method = $Matches[1].ToUpperInvariant()
            $template = $Matches[3]

            $start = $i
            while ($start -gt $classLine -and $lines[$start - 1] -match '^\s*\[') { $start-- }
            $j = $i + 1
            while ($j -lt $lines.Count -and $lines[$j] -notmatch '^\s*public\b') { $j++ }
            if ($j -ge $lines.Count) { continue }
            $block = ($lines[$start..$j]) -join "`n"

            $declLines = [System.Collections.Generic.List[string]]::new()
            for ($d = $j; $d -lt [Math]::Min($j + 14, $lines.Count); $d++) {
                $declLines.Add($lines[$d])
                if ($lines[$d] -match '\{|\=>') { break }
            }
            $declaration = $declLines -join ' '

            $action = 'Unknown'
            if ($declaration -match '\b([A-Za-z_]\w*)\s*\(') { $action = $Matches[1] }

            $bodyType = $null
            if ($declaration -match '\[FromBody\]\s*([A-Za-z_][\w<>,\.\? ]*?)\s+\w+\s*[,\)]') {
                $bodyType = $Matches[1].Trim()
            } else {
                $paramMatches = [regex]::Matches($declaration, '(?<prefix>\[[^\]]*\]\s*)?([A-Za-z_]\w*(?:Dto|Request))\s+\w+\s*[,\)]')
                foreach ($pm in $paramMatches) {
                    if ($pm.Groups['prefix'].Value -match 'From(Route|Query|Services)') { continue }
                    $bodyType = $pm.Groups[2].Value
                    break
                }
            }
            $hasFormFile = $declaration -match 'IFormFile'

            $requiredQuery = [System.Collections.Generic.List[string]]::new()
            foreach ($qm in [regex]::Matches($declaration, '\[FromQuery\]\s*([A-Za-z_][\w\?<>,\[\]\. ]*?)\s+(\w+)\s*(=|,|\))')) {
                $queryType = $qm.Groups[1].Value.Trim()
                $queryName = $qm.Groups[2].Value
                $hasDefault = $qm.Groups[3].Value -eq '='
                if (-not $hasDefault -and -not $queryType.EndsWith('?') -and $queryType -notmatch 'QueryDto$') {
                    $requiredQuery.Add($queryName)
                }
            }

            $pathText = if ([string]::IsNullOrEmpty($template)) { $controllerRoute } else { "$controllerRoute/$template" }
            $pathText = ($pathText -replace '^/+', '') -replace '\[controller\]', $controllerName
            $pathText = $pathText -replace '\{([^}:]+):[^}]+\}', '{$1}'

            $result.Add([pscustomobject]@{
                Name = "$method /$pathText"
                Method = $method
                Path = "/$pathText"
                Controller = $controllerName
                ControllerFile = $file.BaseName
                Action = $action
                Auth = (Get-AuthKind -Block $block -Fallback $controllerAuth)
                BodyType = $bodyType
                FormFile = [bool]$hasFormFile
                QueryRequired = @($requiredQuery)
            })
        }
    }
    return $result
}

function Get-DtoPropertyMap {
    param([string[]]$Roots)
    $map = @{}
    foreach ($root in $Roots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($file in (Get-ChildItem -LiteralPath $root -Recurse -Filter *.cs -File)) {
            $depth = 0
            $current = $null
            $props = $null
            foreach ($line in (Read-Utf8Lines $file.FullName)) {
                if ($depth -eq 0 -and $line -match '^\s*(?:public|internal)?\s*(?:sealed\s+)?(?:partial\s+)?(?:class|record)\s+(\w+)') {
                    $current = $Matches[1]
                    if (-not $map.ContainsKey($current)) { $map[$current] = [System.Collections.Generic.List[string]]::new() }
                    $props = $map[$current]
                    if ($line -match '\(([^\)]*)\)') {
                        foreach ($part in ($Matches[1] -split ',')) {
                            $trimmed = $part.Trim()
                            if ($trimmed -match '(\w+)\s*(=|$)') { $props.Add($Matches[1]) }
                        }
                    }
                }
                if ($depth -eq 1 -and $current -and
                    $line -match '^\s*public\s+[^\(]+?\s+(\w+)\s*\{\s*get;') {
                    if (-not $props.Contains($Matches[1])) { $props.Add($Matches[1]) }
                }
                $depth += ([regex]::Matches($line, '\{')).Count
                $depth -= ([regex]::Matches($line, '\}')).Count
                if ($depth -le 0 -and $line -match '\}') { $current = $null; $props = $null }
            }
        }
    }
    return $map
}

function Get-ExpectedClass([string]$name) {
    if ($name.StartsWith('POST /api/webhooks/')) { return 'webhook' }
    if ($destructiveNames -contains $name) { return 'destructive' }
    if ($name.StartsWith('GET ') -or $name.StartsWith('HEAD ') -or $allowlistPosts -contains $name) { return 'read' }
    return 'write'
}

function Get-AuthLabel([string]$kind) {
    switch ($kind) {
        'anonymous' { return 'No Auth' }
        'AdminOnly' { return 'adminToken' }
        'SellerOnly' { return 'sellerToken' }
        'CustomerOnly' { return 'customerToken' }
        'DeliveryDriverOnly' { return 'driverToken' }
        'Role:Seller' { return 'sellerToken' }
        'authenticated' { return 'customerToken' }
        default { return 'No Auth' }
    }
}

function Get-DescriptionField([string]$description, [string]$label) {
    if ([string]::IsNullOrEmpty($description)) { return $null }
    foreach ($line in ($description -split "`n")) {
        if ($line.StartsWith($label + ':')) { return $line.Substring($label.Length + 1).Trim() }
    }
    return $null
}

# ---- extração dos controllers ----
$actions = @(Get-ControllerActions -Path $ControllersPath)
$controllersCount = @(Get-ChildItem -LiteralPath $ControllersPath -Filter *Controller.cs -File).Count
$actionNames = @($actions | ForEach-Object { $_.Name })
$duplicateActions = @($actions | Group-Object Name | Where-Object Count -gt 1)

if ($controllersCount -ne 33) { Fail "controllers esperados=33 encontrados=$controllersCount" }
if ($actions.Count -ne 179) { Fail "actions esperadas=179 encontradas=$($actions.Count)" }
if ($duplicateActions.Count -gt 0) {
    foreach ($g in $duplicateActions) { Fail "duplicate controller action: $($g.Name)" }
}

# ---- mapa ----
if (-not (Test-Path -LiteralPath $FeatureMapPath)) {
    Fail "feature map nao encontrado: $FeatureMapPath"
} else {
    $map = Read-Utf8Json $FeatureMapPath
    if ($map.version -ne 1) { Fail "feature map version esperada=1 encontrada=$($map.version)" }
    $features = @($map.features)
    if ($features.Count -ne 14) { Fail "features esperadas=14 encontradas=$($features.Count)" }

    $expectedIds = @('01','02','03','04','05','06','07','08','09','10','11','12','13','14')
    $actualIds = @($features | ForEach-Object { $_.id })
    foreach ($id in $expectedIds) { if (-not ($actualIds -contains $id)) { Fail "feature ausente: $id" } }
    foreach ($id in $actualIds) { if (-not ($expectedIds -contains $id)) { Fail "feature inesperada: $id" } }

    $subfolderTotal = 0
    $featureById = @{}
    foreach ($f in $features) {
        $featureById[$f.id] = $f
        $subfolderTotal += @($f.subfolders).Count
    }
    if ($subfolderTotal -ne 44) { Fail "subpastas esperadas=44 encontradas=$subfolderTotal" }

    $endpoints = @($map.endpoints.PSObject.Properties)
    if ($endpoints.Count -ne 179) { Fail "endpoints no mapa esperados=179 encontrados=$($endpoints.Count)" }

    $mapNames = @($endpoints | ForEach-Object { $_.Name })
    foreach ($name in $actionNames) { if (-not ($mapNames -contains $name)) { Fail "missing endpoint: $name" } }
    foreach ($name in $mapNames) { if (-not ($actionNames -contains $name)) { Fail "extra endpoint: $name" } }

    foreach ($entry in $endpoints) {
        $value = $entry.Value
        if (-not $featureById.ContainsKey($value.feature)) {
            Fail "endpoint $($entry.Name) aponta feature inexistente: $($value.feature)"
            continue
        }
        if (-not (@($featureById[$value.feature].subfolders) -contains $value.subfolder)) {
            Fail "endpoint $($entry.Name) aponta subpasta nao declarada: $($value.subfolder)"
        }
    }

    foreach ($id in $expectedIds) {
        $count = @($endpoints | Where-Object { $_.Value.feature -eq $id }).Count
        if ($count -ne $expectedFeatureCounts[$id]) {
            Fail "feature $id esperada=$($expectedFeatureCounts[$id]) encontrada=$count"
        }
    }

    $boundaries = @{
        'GET /api/stores/cuisine-types' = '03'
        'GET /api/public/stores/{storeId}/reviews' = '03'
        'POST /api/stores/{storeId}/cuisine-types' = '04'
        'GET /api/stores/{storeId}/cuisine-types' = '04'
        'DELETE /api/stores/{storeId}/cuisine-types/{cuisineTypeId}' = '04'
        'GET /api/stores/{storeId}/payment-gateway' = '09'
        'PUT /api/stores/{storeId}/payment-gateway' = '09'
        'DELETE /api/stores/{storeId}/payment-gateway' = '09'
        'POST /api/webhooks/mercadopago' = '13'
        'POST /api/webhooks/asaas' = '13'
        'POST /api/orders' = '08'
    }
    foreach ($name in $boundaries.Keys) {
        if ($map.endpoints.PSObject.Properties.Name -contains $name) {
            $actual = $map.endpoints.$name.feature
            if ($actual -ne $boundaries[$name]) { Fail "boundary $name esperada feature $($boundaries[$name]) encontrada $actual" }
        } else {
            Fail "boundary endpoint ausente: $name"
        }
    }
    if ($mapNames -contains 'PUT /api/stores/{storeId}/cuisine-types') {
        Fail 'D11: PUT /api/stores/{storeId}/cuisine-types nao existe no contrato'
    }

    $controllerTokens = @()
    foreach ($file in (Get-ChildItem -LiteralPath $ControllersPath -Filter *Controller.cs -File)) {
        $controllerTokens += ($file.BaseName -replace 'Controller$', '')
        $controllerTokens += $file.BaseName
    }
    # "Checkout" e "Webhooks" também são termos de negócio da estrutura aprovada.
    $businessNameCollisions = @('Checkout', 'Webhooks')
    foreach ($f in $features) {
        if (($controllerTokens -contains $f.name) -and -not ($businessNameCollisions -contains $f.name)) { Fail "feature com nome de controller: $($f.name)" }
        foreach ($s in @($f.subfolders)) {
            if (($controllerTokens -contains $s) -and -not ($businessNameCollisions -contains $s)) { Fail "subpasta com nome de controller: $s" }
        }
    }
}

# ---- coleção ----
$requests = [System.Collections.Generic.List[object]]::new()
$collection = $null
if (-not $MapOnly) {
    if (-not (Test-Path -LiteralPath $CollectionPath)) {
        Fail "colecao nao encontrada: $CollectionPath"
    } else {
        $collection = Read-Utf8Json $CollectionPath
        $walk = {
            param($items, $featureId, $featureName, $subName)
            foreach ($item in @($items)) {
                if ($null -ne $item.request) {
                    $requests.Add([pscustomobject]@{
                        Name = $item.name
                        Id = $item.id
                        FeatureId = $featureId
                        FeatureName = $featureName
                        Subfolder = $subName
                        Request = $item.request
                        Item = $item
                    })
                } elseif ([string]::IsNullOrEmpty($featureId)) {
                    $name = [string]$item.name
                    $fid = ''
                    $fname = $name
                    if ($name -match '^(\d{2}) · (.+)$') { $fid = $Matches[1]; $fname = $Matches[2] }
                    & $walk $item.item $fid $fname ''
                } else {
                    & $walk $item.item $featureId $featureName ([string]$item.name)
                }
            }
        }
        & $walk $collection.item '' '' ''

        $requestNames = @($requests | ForEach-Object { $_.Name })
        $requestIds = @($requests | ForEach-Object { $_.Id })

        $duplicateNames = @($requestNames | Group-Object | Where-Object Count -gt 1)
        foreach ($g in $duplicateNames) { Fail "duplicate request: $($g.Name)" }
        $duplicateIds = @($requestIds | Group-Object | Where-Object Count -gt 1)
        foreach ($g in $duplicateIds) { Fail "duplicate request id: $($g.Name)" }

        foreach ($name in $actionNames) {
            if (-not ($requestNames -contains $name)) { Fail "missing collection request: $name" }
        }
        foreach ($name in $requestNames) {
            if (-not ($actionNames -contains $name)) { Fail "extra collection request: $name" }
        }

        $actionByName = @{}
        foreach ($a in $actions) { $actionByName[$a.Name] = $a }
        $dtoMap = Get-DtoPropertyMap -Roots @(
            'backend/src/Urbeat.Application',
            'backend/src/Urbeat.WebApi/Controllers'
        )

        $collectionClass = @{}
        $multipartCount = 0
        foreach ($r in $requests) {
            $a = $actionByName[$r.Name]
            if ($null -eq $a) { continue }

            if ([string]$r.Request.method -ne $a.Method) {
                Fail "metodo divergente do controller em $($r.Name): request=$($r.Request.method) controller=$($a.Method)"
            }
            $expectedSegments = @($a.Path.Trim('/').Split('/'))
            $actualSegments = @($r.Request.url.path)
            if ($actualSegments.Count -ne $expectedSegments.Count) {
                Fail "rota com quantidade de segmentos divergente em $($r.Name): request=$($r.Request.url.path -join '/') controller=$($a.Path)"
            } else {
                for ($si = 0; $si -lt $expectedSegments.Count; $si++) {
                    $expectedSegment = $expectedSegments[$si]
                    $actualSegment = [string]$actualSegments[$si]
                    $paramMatch = [regex]::Match($expectedSegment, '^\{([^}:]+)(?::[^}]+)?\}$')
                    if ($paramMatch.Success) {
                        $actualMatch = [regex]::Match($actualSegment, '^\{\{([A-Za-z0-9_$]+)\}\}$')
                        if (-not $actualMatch.Success) {
                            Fail "parametro de rota nao usa variavel em $($r.Name): segmento=$actualSegment esperado=$expectedSegment"
                        } else {
                            $expectedVariable = Get-ExpectedPathVariable $paramMatch.Groups[1].Value $a.Path
                            if (-not [string]::IsNullOrEmpty($expectedVariable) -and $actualMatch.Groups[1].Value -ne $expectedVariable) {
                                Fail "variavel de rota divergente em $($r.Name): $($actualMatch.Groups[1].Value) esperado=$expectedVariable"
                            }
                        }
                    } elseif ($actualSegment -ne $expectedSegment) {
                        Fail "rota divergente do controller em $($r.Name): segmento=$actualSegment esperado=$expectedSegment"
                    }
                }
            }

            if (-not (Test-Path -LiteralPath $FeatureMapPath)) { continue }
            $destination = $map.endpoints.PSObject.Properties[$r.Name]
            if ($null -eq $destination) {
                Fail "request sem destino no mapa: $($r.Name)"
            } else {
                if ($r.FeatureId -ne $destination.Value.feature) {
                    Fail "feature divergente em $($r.Name): colecao=$($r.FeatureId) mapa=$($destination.Value.feature)"
                }
                if ($r.Subfolder -ne $destination.Value.subfolder) {
                    Fail "subpasta divergente em $($r.Name): colecao=$($r.Subfolder) mapa=$($destination.Value.subfolder)"
                }
                $expectedFeatureName = $featureById[$destination.Value.feature].name
                if ($r.FeatureName -ne $expectedFeatureName) {
                    Fail "nome da feature divergente em $($r.Name): $($r.FeatureName) vs $expectedFeatureName"
                }
                $expectedPath = "Feature: $($destination.Value.feature) · $expectedFeatureName / $($destination.Value.subfolder)"
                $featureLine = Get-DescriptionField $r.Request.description 'Feature'
                if ($null -eq $featureLine -or ("Feature: " + $featureLine) -ne $expectedPath) {
                    Fail "descricao Feature divergente em $($r.Name)"
                }
            }

            $controllerLine = Get-DescriptionField $r.Request.description 'Controller'
            if ($controllerLine -ne "$($a.Controller).$($a.Action)") {
                Fail "descricao Controller divergente em $($r.Name): $controllerLine"
            }
            $authLine = Get-DescriptionField $r.Request.description 'Auth'
            $expectedAuth = Get-AuthLabel $a.Auth
            if ($authLine -ne $expectedAuth) {
                Fail "auth divergente em $($r.Name): descricao=$authLine controller=$expectedAuth"
            }
            if ($expectedAuth -eq 'No Auth') {
                if ([string]$r.Request.auth.type -ne 'noauth') { Fail "auth.type deveria ser noauth em $($r.Name)" }
            } else {
                $bearer = @($r.Request.auth.bearer)
                if ([string]$r.Request.auth.type -ne 'bearer' -or $bearer.Count -ne 1 -or [string]$bearer[0].value -ne "{{$expectedAuth}}") {
                    Fail "bearer nao corresponde ao controller em $($r.Name)"
                }
            }
            foreach ($required in @($a.QueryRequired)) {
                if ([string]::IsNullOrEmpty($required)) { continue }
                $found = @($r.Request.url.query | Where-Object { $_.key -eq $required -and -not $_.disabled })
                if ($found.Count -ne 1) {
                    Fail "query obrigatoria ausente/desabilitada em $($r.Name): $required"
                }
            }
            $classLine = Get-DescriptionField $r.Request.description 'Classificação'
            $expectedClass = Get-ExpectedClass $r.Name
            if ($classLine -ne $expectedClass) {
                Fail "classificacao divergente em $($r.Name): $classLine vs $expectedClass"
            }
            foreach ($label in @('Pré-requisitos','Efeitos','Captura')) {
                if ($null -eq (Get-DescriptionField $r.Request.description $label)) {
                    Fail "descricao sem $label em $($r.Name)"
                }
            }
            $manualLine = Get-DescriptionField $r.Request.description 'Manual'
            if ($expectedClass -eq 'destructive' -or $expectedClass -eq 'webhook') {
                if ($manualLine -ne 'sim') { Fail "descricao sem Manual: sim em $($r.Name)" }
            } elseif ($null -ne $manualLine) {
                Fail "Manual indevido em $($r.Name)"
            }
            $collectionClass[$r.Name] = $classLine

            if ($a.FormFile) {
                $multipartCount++
                if ($null -eq $r.Request.body -or $r.Request.body.mode -ne 'formdata') {
                    Fail "multipart sem formdata em $($r.Name)"
                } else {
                    $fileFields = @($r.Request.body.formdata | Where-Object { $_.key -eq 'file' -and $_.type -eq 'file' })
                    if ($fileFields.Count -ne 1) { Fail "campo file ausente em $($r.Name)" }
                    if ($fileFields.Count -eq 1 -and $fileFields[0].PSObject.Properties.Name -contains 'src') {
                        $src = $fileFields[0].src
                        if (-not ([string]::IsNullOrEmpty($src))) { Fail "multipart com src absoluto em $($r.Name)" }
                    }
                }
            }

            if ($a.BodyType) {
                if ($null -eq $r.Request.body -or $r.Request.body.mode -ne 'raw') {
                    Fail "body raw ausente em $($r.Name)"
                } else {
                    if ($null -eq $r.Request.body.options -or $r.Request.body.options.raw.language -ne 'json') {
                        Fail "body sem options.raw.language=json em $($r.Name)"
                    }
                    try {
                        $parsed = $r.Request.body.raw | ConvertFrom-Json
                    } catch {
                        Fail "body JSON invalido em $($r.Name): $($_.Exception.Message)"
                        $parsed = $null
                    }
                    if ($null -ne $parsed) {
                        $dtoName = $a.BodyType
                        $props = @()
                        if ($dtoMap.ContainsKey($dtoName)) { $props = @($dtoMap[$dtoName]) }
                        if ($dtoName -eq 'ReorderStoreCategoriesRequestDto') {
                            $dtoName = 'ReorderStoreCategoriesItemDto'
                            if ($dtoMap.ContainsKey($dtoName)) { $props = @($dtoMap[$dtoName]) }
                        }
                        if ($props.Count -eq 0) {
                            Fail "DTO sem propriedades extraidas: $dtoName"
                        }
                        $bodyKeys = @()
                        if ($parsed -is [System.Array]) {
                            if ($parsed.Count -gt 0) { $bodyKeys = @($parsed[0].PSObject.Properties.Name) }
                        } else {
                            $bodyKeys = @($parsed.PSObject.Properties.Name)
                        }
                        foreach ($prop in $props) {
                            $camel = To-Camel $prop
                            if (-not ($bodyKeys -contains $camel)) {
                                Fail "body sem chave $camel (DTO $dtoName) em $($r.Name)"
                            }
                        }
                    }
                }
            }
        }

        if ($multipartCount -ne 2) { Fail "multipart esperados=2 encontrados=$multipartCount" }

        $guardNames = @($requests | Where-Object { $_.Name.StartsWith('POST /api/webhooks/') -or $collectionClass[$_.Name] -eq 'destructive' } | ForEach-Object { $_.Name })
        $expectedGuard = @($destructiveNames)
        foreach ($n in $guardNames) { if (-not ($expectedGuard -contains $n)) { Fail "classe destrutiva/webhook fora da lista: $n" } }
        foreach ($n in $expectedGuard) { if (-not ($guardNames -contains $n)) { Fail "lista destrutiva sem requisicao: $n" } }
        foreach ($r in $requests) {
            if ($r.Name.StartsWith('DELETE ') -and $collectionClass[$r.Name] -ne 'destructive') {
                Fail "DELETE nao destrutivo: $($r.Name)"
            }
        }
    }
}

# ---- relatório ----
$authCounts = @{}
foreach ($a in $actions) { $authCounts[$a.Auth] = 1 + [int]$authCounts[$a.Auth] }
$classCounts = @{ read = 0; write = 0; destructive = 0; webhook = 0 }
foreach ($a in $actions) { $c = Get-ExpectedClass $a.Name; $classCounts[$c]++ }

if ($PSBoundParameters.ContainsKey('Feature') -and -not [string]::IsNullOrWhiteSpace($Feature) -and $null -ne $map) {
    $featureActions = @($actions | Where-Object { $map.endpoints.PSObject.Properties[$_.Name].Value.feature -eq $Feature })
    $featureRequests = @($requests | Where-Object { $_.FeatureId -eq $Feature })
    $missing = @($featureActions | Where-Object { -not (@($featureRequests | ForEach-Object { $_.Name }) -contains $_.Name) }).Count
    $duplicate = @($featureRequests | Group-Object Name | Where-Object Count -gt 1).Count
    Write-Host ("OK feature={0} requests={1} missing={2} duplicate={3}" -f $Feature, $featureRequests.Count, $missing, $duplicate)
    if ($missing -gt 0 -or $duplicate -gt 0) { $script:failures.Add("feature $Feature incompleta") }
}

if ($script:failures.Count -eq 0) {
    Write-Host ("OK controllers={0} actions={1} features=14 subfolders=44 endpoints=179" -f $controllersCount, $actions.Count)
    Write-Host ("OK auth anonymous={0} SellerOnly={1} CustomerOnly={2} AdminOnly={3} Role:Seller={4} DeliveryDriverOnly={5} authenticated={6}" -f `
        [int]$authCounts['anonymous'], [int]$authCounts['SellerOnly'], [int]$authCounts['CustomerOnly'], [int]$authCounts['AdminOnly'], `
        [int]$authCounts['Role:Seller'], [int]$authCounts['DeliveryDriverOnly'], [int]$authCounts['authenticated'])
    Write-Host ("OK class read={0} write={1} destructive={2} webhook={3} guard={4}" -f `
        $classCounts['read'], $classCounts['write'], $classCounts['destructive'], $classCounts['webhook'], $destructiveNames.Count)
    if ($null -ne $collection) {
        Write-Host ("OK coverage missing=0 extra=0 duplicate=0 requests={0}" -f $requests.Count)
    }
    exit 0
}

Write-Host ("FAILED {0} problema(s)" -f $script:failures.Count) -ForegroundColor Red
exit 1
