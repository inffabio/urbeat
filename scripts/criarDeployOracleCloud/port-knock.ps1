param(
    [string]$ServerIP = "136.248.115.135",
    [int[]]$Ports = @(1230, 5688, 9112),
    [string[]]$Protocols = @("tcp", "udp", "tcp")
)

if ($Ports.Count -ne $Protocols.Count) {
    throw "Port knocking requires one protocol for each port."
}

Write-Host "Sending port-knock sequence to $ServerIP..." -ForegroundColor Cyan

for ($i = 0; $i -lt $Ports.Count; $i++) {
    $port = $Ports[$i]
    $protocol = $Protocols[$i].ToLowerInvariant()

    if ($protocol -eq "tcp") {
        $client = [System.Net.Sockets.TcpClient]::new()
        try {
            $client.ConnectAsync($ServerIP, $port).Wait(100) | Out-Null
        }
        finally {
            $client.Dispose()
        }
    }
    elseif ($protocol -eq "udp") {
        $client = [System.Net.Sockets.UdpClient]::new()
        try {
            $client.Connect($ServerIP, $port)
            [byte[]]$payload = 0
            [void]$client.Send($payload, $payload.Length)
        }
        finally {
            $client.Dispose()
        }
    }
    else {
        throw "Unsupported port-knock protocol '$protocol'."
    }

    Start-Sleep -Milliseconds 200
}

Write-Host "Port-knock sequence sent." -ForegroundColor Green
