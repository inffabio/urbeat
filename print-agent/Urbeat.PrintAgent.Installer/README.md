# Urbeat Print Agent — instalador MSI (WiX v4)

Este projeto gera o MSI x64 que instala o agente em `Program Files\Urbeat Print Agent`,
registra o Windows Service `UrbeatPrintAgent` com inicialização automática e configura
recuperação em falha (restart).

## Pré-requisitos de build

- .NET SDK 9
- WiX Toolset v4 (SDK `WixToolset.Sdk/4.0.5`, `WixToolset.Util.wixext` e `WixToolset.UI.wixext` restaurados via NuGet)

## Build

```powershell
dotnet publish ../Urbeat.PrintAgent/Urbeat.PrintAgent.csproj -c Release -r win-x64 --self-contained true -o ../out/win-x64-msi
dotnet build ./Urbeat.PrintAgent.Installer.wixproj -c Release -p:ProductVersion=1.0.0 -p:PublishDir=..\out\win-x64-msi
```

O script oficial empacota tudo:

```powershell
./scripts/print-agent/build-print-agent-packages.ps1 -Configuration Release
```

## Comportamento do MSI

- abre o assistente gráfico `WixUI_Minimal` para instalação/reparo/desinstalação;
- é **self-contained** (inclui o runtime .NET 9; não depende de runtime instalado);
- instala arquivos em `Program Files\Urbeat Print Agent`;
- registra e inicia o serviço automaticamente (`Start="auto"`);
- `Stop="both"` e `Remove="uninstall"` param/removem o serviço em upgrade/desinstalação;
- `MajorUpgrade` permite atualizar versões anteriores;
- recuperação do serviço configurada com `util:ServiceConfig` (3 tentativas de restart);
- o agente continua escutando apenas `http://127.0.0.1:43111`;
- jobs e chaves de idempotência ficam em `ProgramData\Urbeat\PrintAgent\print-jobs.json`;
- o alvo MSBuild `GenerateWixFileComponents` gera `Generated/Files.wxs` a partir do publish
  self-contained (`print-agent/out/win-x64-msi`), mantendo IDs/GUIDs determinísticos por caminho
  relativo. O arquivo gerado não é versionado.
- o serviço roda como `LocalSystem`; impressoras precisam ser instaladas machine-wide.
- jobs persistidos como `sending` são recuperados como `ambiguous` após crash. O agente não
  reenvia automaticamente porque `queued/sent` confirma apenas o aceite do spooler, nunca a
  impressão física. A recuperação é explícita via `POST /jobs/{id}/retry`.

O ZIP/PowerShell permanece como fallback técnico em `downloads/urbeat-print-agent/windows`.

## Limitação conhecida

Se o SDK do WiX v4 não estiver disponível no ambiente de build, o script gera apenas o ZIP e
emite um aviso; o MSI precisa ser gerado em uma máquina com acesso ao pacote NuGet do WiX.
