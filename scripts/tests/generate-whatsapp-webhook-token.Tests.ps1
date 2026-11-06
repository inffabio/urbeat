Describe "New-WhatsAppWebhookVerifyToken" {
    BeforeAll {
        . (Join-Path $PSScriptRoot "..\generate-whatsapp-webhook-token.ps1")
    }

    It "generates 32 bytes as 64 lowercase hex characters by default" {
        $token = New-WhatsAppWebhookVerifyToken

        $token.Length | Should Be 64
        $token | Should Match '^[0-9a-f]{64}$'
    }

    It "honors a custom byte length between 32 and 64" {
        $token = New-WhatsAppWebhookVerifyToken -ByteLength 48

        $token.Length | Should Be 96
        $token | Should Match '^[0-9a-f]{96}$'
    }

    It "rejects byte lengths outside the supported range" {
        { New-WhatsAppWebhookVerifyToken -ByteLength 16 } | Should Throw
        { New-WhatsAppWebhookVerifyToken -ByteLength 65 } | Should Throw
    }

    It "returns a different token on each call" {
        (New-WhatsAppWebhookVerifyToken) | Should Not Be (New-WhatsAppWebhookVerifyToken)
    }
}
