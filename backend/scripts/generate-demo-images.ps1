# Creates placeholder product images locally for offline demo use.
# Run from backend/ directory:  pwsh -File scripts/generate-demo-images.ps1

$outputDir = Join-Path $PSScriptRoot ".." "src" "Urbeat.WebApi" "wwwroot" "uploads" "products"
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

$products = @(
    @{Name="Smash Burguer"; Color="FF6B35"}
    @{Name="Costela Burguer"; Color="E85D04"}
    @{Name="Bacon Triple"; Color="C0392B"}
    @{Name="Veggie Burguer"; Color="27AE60"}
    @{Name="Batata Cheddar"; Color="F1C40F"}
    @{Name="Anéis de Cebola"; Color="8E44AD"}
    @{Name="Nuggets de Frango"; Color="D35400"}
    @{Name="Coca-Cola Lata"; Color="E74C3C"}
    @{Name="Guaraná Antarctica"; Color="2ECC71"}
    @{Name="Suco Natural de Laranja"; Color="F39C12"}
    @{Name="Água Mineral"; Color="3498DB"}
    @{Name="Cerveja Artesanal IPA"; Color="D68910"}
    @{Name="Monster Triple"; Color="1A1A2E"}
    @{Name="Cheddar Lover"; Color="E67E22"}
    @{Name="Texas Burguer"; Color="922B21"}
    @{Name="Kids Burguer"; Color="F7DC6F"}
    @{Name="Combo Monstro"; Color="E74C3C"}
    @{Name="Combo Cheddar"; Color="D4AC0D"}
    @{Name="Combo Kids"; Color="5DADE2"}
    @{Name="Coca-Cola 600ml"; Color="E74C3C"}
    @{Name="Fanta Laranja"; Color="F39C12"}
    @{Name="Suco de Maracujá"; Color="F1C40F"}
    @{Name="Milkshake de Chocolate"; Color="6E2C00"}
    @{Name="Margherita"; Color="E74C3C"}
    @{Name="Pepperoni"; Color="C0392B"}
    @{Name="Quattro Formaggi"; Color="F1C40F"}
    @{Name="Calabresa com Cebola"; Color="D35400"}
    @{Name="Portuguesa"; Color="E67E22"}
    @{Name="Nutella com Morango"; Color="6E2C00"}
    @{Name="Banana com Canela"; Color="F39C12"}
    @{Name="Coca-Cola 2L"; Color="E74C3C"}
    @{Name="Suco Natural de Uva"; Color="8E44AD"}
    @{Name="Água com Gás"; Color="3498DB"}
    @{Name="Vinho Tinto Chileno"; Color="7B241C"}
    @{Name="Rock 'n Roll"; Color="1A1A2E"}
    @{Name="Heavy Metal"; Color="922B21"}
    @{Name="Punk Veggie"; Color="27AE60"}
    @{Name="Blues do Chef"; Color="8E44AD"}
    @{Name="Reggae de Frango"; Color="2980B9"}
    @{Name="Rock 'n Roll + Cheddar"; Color="D4AC0D"}
    @{Name="Blues do Chef + Catupiry"; Color="D68910"}
    @{Name="Sprite Lata"; Color="2ECC71"}
    @{Name="Heineken Long Neck"; Color="1A5276"}
    @{Name="Suco de Abacaxi com Hortelã"; Color="F1C40F"}
    @{Name="Combinado Sakura"; Color="D32F2F"}
    @{Name="Combinado Premium"; Color="880E4F"}
    @{Name="Combinado Light"; Color="388E3C"}
    @{Name="Sushi de Salmão (8 un)"; Color="E64A19"}
    @{Name="Sushi de Atum (8 un)"; Color="C62828"}
    @{Name="Temaki Salmão Filadélfia"; Color="FF7043"}
    @{Name="Hot Filadélfia (6 un)"; Color="EF5350"}
    @{Name="Sakê Quente"; Color="F5F5DC"}
    @{Name="Sakê Gelado"; Color="FFF8E1"}
    @{Name="Chá Gelado de Hibisco"; Color="D81B60"}
)

$count = 0
foreach ($p in $products) {
    $name = $p.Name
    $color = $p.Color
    $safeName = $name -replace '[^a-zA-Z0-9]', '_'
    $fileName = "$safeName.jpg"
    $filePath = Join-Path $outputDir $fileName

    # Generate SVG content with proper styling
    $svg = @"
<svg xmlns="http://www.w3.org/2000/svg" width="400" height="400" viewBox="0 0 400 400">
  <defs>
    <linearGradient id="bg" x1="0%" y1="0%" x2="100%" y2="100%">
      <stop offset="0%" style="stop-color:#$color;stop-opacity:1"/>
      <stop offset="100%" style="stop-color:#$($color);stop-opacity:0.8"/>
    </linearGradient>
  </defs>
  <rect width="400" height="400" fill="url(#bg)" rx="20"/>
  <text x="200" y="200" text-anchor="middle" dominant-baseline="central"
        font-family="Arial,sans-serif" font-size="18" fill="white" font-weight="bold">
    $(($name -replace '([&<>"])', '')).Substring(0, [Math]::Min($name.Length, 30))
  </text>
  <text x="200" y="240" text-anchor="middle" dominant-baseline="central"
        font-family="Arial,sans-serif" font-size="12" fill="white" opacity="0.8">
    Imagem ilustrativa
  </text>
</svg>
"@

    # Convert SVG to JPG using placehold.co (requires internet)
    $url = "https://placehold.co/400x400/$color/white?text=$([Uri]::EscapeDataString($name))&font=raleway"
    try {
        Invoke-WebRequest -Uri $url -OutFile $filePath -ErrorAction Stop
        Write-Host "✓ $fileName" -ForegroundColor Green
        $count++
    } catch {
        Write-Host "✗ $fileName (download failed, using SVG)" -ForegroundColor Yellow
        # Fallback: save SVG if we can't get JPG
        $svgPath = Join-Path $outputDir "$safeName.svg"
        Set-Content -Path $svgPath -Value $svg -Encoding UTF8
    }
}

Write-Host "`nDone! $count images downloaded to: $outputDir" -ForegroundColor Cyan
Write-Host "Note: The app uses placehold.co URLs by default. Run this script to cache local copies."
