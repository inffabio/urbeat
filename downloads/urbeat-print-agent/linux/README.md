# Urbeat Print Agent para Linux

## Conteúdo esperado do pacote

- `Urbeat.PrintAgent`
- `appsettings.json`
- `install-local-agent.sh`
- `urbeat-print-agent.service`

## Instalação rápida

```bash
tar -xzf Urbeat.PrintAgent-linux-x64.tar.gz
cd Urbeat.PrintAgent-linux-x64
chmod +x Urbeat.PrintAgent install-local-agent.sh
sudo ./install-local-agent.sh
```

## Comportamento esperado

- o agent sobe em `127.0.0.1:43111`
- o serviço roda em background via `systemd`
- a loja usa o modo `local-agent` no dashboard para impressão automática sem popup

## Padrão Linux do Urbeat

- O padrão oficial em Linux é usar o `local-agent` com a impressora cadastrada no `CUPS`.
- Se a impressora estiver em USB ou rede, configure primeiro a fila no sistema.
- O agent então usa a impressora já conhecida pelo Linux.
- `raw ESC/POS` por rede fica como opção avançada futura, não como padrão inicial.

## Guia complementar

- Consulte também `Documentacao/FrontEnd/Impressao/configuracao-linux-cups.md` para o passo a passo completo de configuração do CUPS.
