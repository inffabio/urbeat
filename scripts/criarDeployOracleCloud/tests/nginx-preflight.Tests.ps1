Describe "NGINX deployment safeguards" {
    It "checks the exact active service state" {
        $content = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\00-prerequisites-check.ps1") -Raw

        $content | Should Match "grep -q '\^active\$'"
    }

    It "starts NGINX when it is not running before reloading" {
        $content = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\05-configure-nginx.ps1") -Raw

        $content | Should Match "systemctl is-active --quiet nginx"
        $content | Should Match "systemctl enable --now nginx"
    }

    It "recovers an orphan NGINX master before starting systemd" {
        $content = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\05-configure-nginx.ps1") -Raw

        $content | Should Match "pgrep -x nginx"
        $content | Should Match "nginx -s stop"
        $content | Should Match "systemctl enable --now nginx"
    }

    It "does not cache the Angular app shell or service-worker files" {
        $content = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\05-configure-nginx.ps1") -Raw

        $content | Should Match "location = /index.html"
        $content | Should Match "location = /ngsw.json"
        $content | Should Match "location = /ngsw-worker.js"
        $content | Should Match "no-cache, no-store, must-revalidate"
    }

    It "accepts API uploads through the frontend /api/ proxy with a 50M body limit" {
        $content = Get-Content -LiteralPath (Join-Path $PSScriptRoot "..\05-configure-nginx.ps1") -Raw

        # Isolate the www/frontend vhost so the backend vhost cannot satisfy the assertion.
        $backendMarker = "`$nginxBackend = @'"
        $frontendEnd = $content.IndexOf($backendMarker)
        $frontendEnd | Should BeGreaterThan 0
        $frontendOnly = $content.Substring(0, $frontendEnd)

        $apiMatch = [regex]::Match($frontendOnly, 'location\s+/api/\s*\{(?<block>[^}]*)\}', 'Singleline')
        $apiMatch.Success | Should Be $true

        $apiBlock = $apiMatch.Groups['block'].Value
        $apiBlock | Should Match 'client_max_body_size\s+50M;'
        $apiBlock | Should Match 'proxy_connect_timeout\s+75s;'
        $apiBlock | Should Match 'proxy_send_timeout\s+300s;'
        $apiBlock | Should Match 'proxy_read_timeout\s+300s;'
    }
}
