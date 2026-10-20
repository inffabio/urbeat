param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

$resolvedPublish = (Resolve-Path $PublishDir).Path
$exePath = Join-Path $resolvedPublish 'Urbeat.PrintAgent.exe'

if (-not (Test-Path $exePath)) {
    throw "Executável não encontrado em $exePath. Rode o publish antes de gerar o MSI."
}

$files = Get-ChildItem -Path $resolvedPublish -Recurse -File |
    Where-Object { $_.FullName -ne $exePath } |
    Sort-Object FullName

$builder = [System.Text.StringBuilder]::new()
[void]$builder.AppendLine('<?xml version="1.0" encoding="UTF-8"?>')
[void]$builder.AppendLine('<!-- GERADO automaticamente por scripts/print-agent/generate-wix-file-components.ps1 -->')
[void]$builder.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
[void]$builder.AppendLine('  <Fragment>')
[void]$builder.AppendLine('    <ComponentGroup Id="ProductComponents" Directory="INSTALLFOLDER">')

$baseUri = [System.Uri]($resolvedPublish.TrimEnd('\') + '\')

foreach ($file in $files) {
    $relative = $baseUri.MakeRelativeUri([System.Uri]$file.FullName).ToString().Replace('/', '\')
    $md5 = [System.Security.Cryptography.MD5]::Create()
    try {
        $bytes = $md5.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($relative.ToLowerInvariant()))
    }
    finally {
        $md5.Dispose()
    }
    $guid = ([System.Guid]::new($bytes)).ToString('D').ToUpperInvariant()
    $hash = ($bytes | ForEach-Object { $_.ToString('x2') }) -join ''
    $componentId = "cmp_$hash"
    $fileId = "fil_$hash"
    $source = '$(var.PublishDir)\' + $relative.Replace('/', '\')
    $escapedSource = [System.Security.SecurityElement]::Escape($source)

    [void]$builder.AppendLine("      <Component Id=`"$componentId`" Guid=`"$guid`">")
    [void]$builder.AppendLine("        <File Id=`"$fileId`" Source=`"$escapedSource`" KeyPath=`"yes`" />")
    [void]$builder.AppendLine('      </Component>')
}

[void]$builder.AppendLine('    </ComponentGroup>')
[void]$builder.AppendLine('  </Fragment>')
[void]$builder.AppendLine('</Wix>')

$directory = Split-Path -Parent $OutputPath
if ($directory) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

Set-Content -Path $OutputPath -Value $builder.ToString() -Encoding UTF8
Write-Host "Componentes WiX gerados: $($files.Count) arquivo(s) -> $OutputPath"
