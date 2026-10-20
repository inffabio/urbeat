# Urbeat Print Agent para Windows

## Instalação principal (MSI)

1. Instale o driver da impressora POS-58 (ou da impressora compatível) **para todos os usuários
   (machine-wide)**. O serviço roda como `LocalSystem` e não enxerga impressoras instaladas apenas
   no perfil do usuário.
2. Baixe `Urbeat.PrintAgent-x64.msi`. O MSI é **self-contained** e não exige instalar o .NET
   separadamente.
4. Abra o MSI e conclua o assistente.
5. O MSI instala o agente em `C:\Program Files\Urbeat Print Agent` e registra o
   Windows Service `UrbeatPrintAgent` com inicialização automática.
6. Abra o dashboard, vá em **Configurar impressão**, selecione a impressora e ative
   `AutoPrint`.

Se o serviço não iniciar, consulte o **Visualizador de Eventos → Logs do Windows → Aplicativo**.
Falhas de dependência (runtime ausente) e erros do serviço são registrados sem secrets.

O serviço escuta somente `http://127.0.0.1:43111`. Jobs e chaves de idempotência ficam em
`C:\ProgramData\Urbeat\PrintAgent\print-jobs.json`, sobrevivendo ao reinício do serviço.

## Atualização

Execute o MSI de uma versão mais nova. O `MajorUpgrade` remove a versão anterior, reinstala os
arquivos e mantém o serviço configurado para iniciar automaticamente.

## Reparo e desinstalação

- **Reparo:** execute o MSI e escolha `Repair`, ou use *Aplicativos e recursos* → *Modificar*.
- **Desinstalação:** *Aplicativos e recursos* → *Urbeat Print Agent* → *Desinstalar*.
  O serviço é parado e removido.

## Fallback técnico (ZIP + PowerShell)

O ZIP é framework-dependent e requer **ASP.NET Core Runtime 9 (Hosting Bundle, x64)**.
Use somente se o MSI não puder ser instalado:

1. Extraia `Urbeat.PrintAgent-win-x64.zip`.
2. Abra o PowerShell como administrador.
3. Execute:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\install-local-agent.ps1
```

Sem privilégios administrativos, o script registra inicialização por usuário (atalho na pasta
Startup) em vez do Windows Service. Para forçar esse modo:

```powershell
.\install-local-agent.ps1 -ConsoleStartup
```

## Diagnóstico e token local

- `http://127.0.0.1:43111/health` — público, somente loopback.
- `/printers`, `/config`, `/print/order`, `/print/test` e `/jobs` exigem o header
  `X-Urbeat-Local-Token`.
- O token é gerado no primeiro setup e persistido em
  `%ProgramData%\Urbeat\PrintAgent\agent-config.json`. O dashboard faz o pareamento via
  `POST /pair` a partir de uma origem permitida (`Agent:AllowedOrigins` no `appsettings.json`).
- `GET /jobs` mostra jobs persistidos e status honesto (`queued`, `sent`, `failed`, `ambiguous`).
- `POST /jobs/{id}/retry` faz recuperação explícita de job `failed`/`ambiguous`.

## Resultado físico incerto (ambiguous)

Se o serviço cair entre o envio ao spooler e a persistência do resultado, o job é recuperado como
`ambiguous`. O agente **não reenvia automaticamente**, porque não há como saber se a impressora
recebeu os bytes. Confira a impressora e, se necessário, faça retry explícito em
`POST /jobs/{id}/retry`. O retorno `queued/sent` confirma apenas aceite do spooler, não a
impressão física.

Esse é o limite inevitável sem feedback do hardware. Se o arquivo de jobs estiver corrompido, o
agente inicia com store vazio e expõe o aviso em `/health` (`storageWarning`); nesse caso as chaves
de idempotência anteriores foram perdidas e a recuperação exige conferência manual.

## Observações

- O modo automático do Windows nunca abre `window.print()` nem diálogo de impressora.
- O driver/configuração ESC/POS da impressora continua sendo pré-requisito.
- O MSI precisa ser gerado em uma máquina com WiX Toolset v4 (SDK NuGet `WixToolset.Sdk/4.0.5`).
