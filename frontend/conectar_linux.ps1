# === CONFIGURAÇÃO DO SEU SERVIDOR ===
$IP = "136.248.115.135"
$Sequencia = @(1230, 5688, 9112)        # Coloque suas portas na ordem correta aqui
$Protocolos = @("tcp", "udp", "tcp")    # Defina o protocolo de cada porta na mesma ordem
# ====================================

write-host "Iniciando sequencia de port knocking para o IP $IP..." -ForegroundColor Cyan

for ($i = 0; $i -lt $Sequencia.Length; $i++) {
    $porta = $Sequencia[$i]
    $proto = $Protocolos[$i].ToLower()
    
    write-host "Batendo na porta $porta ($proto)..." -ForegroundColor Yellow
    
    if ($proto -eq "tcp") {
        $c = New-Object System.Net.Sockets.TcpClient
        $c.ConnectAsync($IP, $porta).Wait(100) | Out-Null
        $c.Close()
    } 
    elseif ($proto -eq "udp") {
        $u = New-Object System.Net.Sockets.UdpClient
        $u.Connect($IP, $porta)
        [byte[]]$b = 0
        [void]$u.Send($b, $b.Length)
        $u.Close()
    }
    
    # Pausa de 200 milissegundos entre as batidas para o Linux processar na ordem correta
    Start-Sleep -Milliseconds 200
}

write-host "Sequencia enviada! Tentando conectar via SSH automaticamente..." -ForegroundColor Green
Start-Sleep -Seconds 1

# Inicia a conexão SSH nativa do Windows 11
ssh -p 2208 dexter@$IP