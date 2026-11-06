<#
.SYNOPSIS
    Gera um token de verificação para o webhook do WhatsApp (Meta).

.DESCRIPTION
    Gera um token criptograficamente seguro para o parâmetro hub.verify_token da
    verificação GET do webhook da Meta. Este token NÃO é o Access Token nem o
    App Secret da Meta; ele serve apenas para confirmar a posse do endpoint
    durante a configuração do webhook.

    Guarde o valor gerado no OCI Vault ou em uma variável de ambiente protegida.
    O script apenas imprime o token no console: não salva arquivos, não altera
    .env, não cria secrets e não faz chamadas de rede.

.PARAMETER ByteLength
    Quantidade de bytes aleatórios, entre 32 e 64. O padrão é 32, que produz um
    token de 64 caracteres hexadecimais minúsculos.

.EXAMPLE
    .\generate-whatsapp-webhook-token.ps1

.EXAMPLE
    .\generate-whatsapp-webhook-token.ps1 -ByteLength 48
#>

#Requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateRange(32, 64)]
    [int]$ByteLength = 32
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Gera ByteLength bytes com RandomNumberGenerator e devolve somente hexadecimal
# minúsculo, sem espaços, quebras internas ou caracteres problemáticos.
function New-WhatsAppWebhookVerifyToken {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory = $false)]
        [ValidateRange(32, 64)]
        [int]$ByteLength = 32
    )

    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $bytes = [byte[]]::new($ByteLength)
        $random.GetBytes($bytes)
        return [System.BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $random.Dispose()
    }
}

# Quando o arquivo é apenas carregado com ponto (por exemplo, em testes), expõe
# a função sem gerar nem imprimir nenhum token.
if ($MyInvocation.InvocationName -ne '.') {
    try {
        $token = New-WhatsAppWebhookVerifyToken -ByteLength $ByteLength

        Write-Host 'Token de verificação do webhook Meta (GET com hub.verify_token):'
        Write-Host 'Não é Access Token nem App Secret. Guarde no OCI Vault ou em variável protegida.' -ForegroundColor DarkGray
        Write-Host ''
        Write-Output $token
    }
    catch {
        [Console]::Error.WriteLine("Erro ao gerar o token: $($_.Exception.Message)")
        exit 1
    }
}
