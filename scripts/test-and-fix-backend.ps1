# ─────────────────────────────────────────────────────────────
# Agente de Teste e Correção Automática do Backend (Urbeat)
# ─────────────────────────────────────────────────────────────
# Este script executa os testes do backend. Se falharem, ele 
# exibe os erros formatados para que o Agente de IA possa 
# ler, analisar e aplicar as correções automaticamente.
# ─────────────────────────────────────────────────────────────

$ErrorActionPreference = "Stop"
$BackendPath = Join-Path $PSScriptRoot "..\backend"

Write-Host "==> Iniciando testes do backend (xUnit + Testcontainers)..." -ForegroundColor Cyan
Set-Location $BackendPath

# Executa os testes e captura a saída e o código de erro
$testOutput = dotnet test --no-restore --verbosity normal 2>&1
$exitCode = $LASTEXITCODE

if ($exitCode -eq 0) {
    Write-Host "✅ Todos os testes passaram com sucesso!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "❌ Falha nos testes detectada." -ForegroundColor Red
    Write-Host "==> Analisando erros para o Agente de Correção..." -ForegroundColor Yellow
    
    # Filtra apenas as linhas de erro relevantes para não sobrecarregar o contexto da IA
    $errorLines = $testOutput | Where-Object { $_ -match "Failed|Error|Exception|Stack Trace|expected|actual" }
    
    Write-Host "`n--- RESUMO DOS ERROS PARA O AGENTE ---" -ForegroundColor DarkGray
    $errorLines | Select-Object -First 30 | ForEach-Object { Write-Host $_ }
    Write-Host "----------------------------------------`n" -ForegroundColor DarkGray
    
    Write-Host "💡 INSTRUÇÃO PARA O AGENTE DE IA:" -ForegroundColor Cyan
    Write-Host "Copie e cole o seguinte comando no chat do Qwen Code para que ele corrija isso automaticamente:"
    Write-Host "`"Agente, execute o protocolo de correção de testes. Os erros são: $($errorLines -join ' | ')`"" -ForegroundColor White
}