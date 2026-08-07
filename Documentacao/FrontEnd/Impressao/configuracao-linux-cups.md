# Configuração Linux com CUPS

## Objetivo

Este é o padrão oficial do Urbeat para impressão em Linux:

- `CUPS + local-agent`

O fluxo recomendado é:

1. cadastrar a impressora no Linux via `CUPS`
2. validar que o sistema consegue imprimir
3. instalar o `local-agent`
4. selecionar a impressora da fila no dashboard
5. deixar o aceite do pedido imprimir automaticamente sem popup

## Quando usar

Use este caminho quando a loja operar com:

- mini PC Linux
- desktop Linux
- impressora USB térmica
- impressora térmica em rede local já configurável no CUPS

## Pré-requisitos

- Linux com `systemd`
- `CUPS` instalado
- acesso administrativo (`sudo`)
- pacote do `Urbeat.PrintAgent-linux-x64.tar.gz`

## Instalar CUPS

Exemplo para Debian/Ubuntu:

```bash
sudo apt update
sudo apt install -y cups cups-client
sudo systemctl enable cups
sudo systemctl start cups
```

## Verificar o serviço

```bash
systemctl status cups
lpstat -r
```

Esperado:

- `scheduler is running`

## Descobrir impressoras

```bash
lpstat -p -d
lpinfo -v
```

## Configurar a impressora no CUPS

### Opção 1: via interface web

Abra:

- `http://localhost:631`

Depois:

1. `Administration`
2. `Add Printer`
3. escolha a impressora USB ou de rede
4. finalize a criação da fila

### Opção 2: via linha de comando

Exemplo genérico USB:

```bash
lpadmin -p POS58 -E -v usb://POS58/ThermalPrinter -m everywhere
```

Exemplo de rede local:

```bash
lpadmin -p POS58-NET -E -v socket://192.168.0.100:9100 -m raw
```

## Definir impressora padrão (opcional)

```bash
lpadmin -d POS58
lpstat -d
```

## Testar impressão pelo sistema

```bash
echo "Teste Urbeat via CUPS" | lp -d POS58
```

Se isso falhar, não avance para o `local-agent` ainda.

## Instalar o local-agent

1. baixe `Urbeat.PrintAgent-linux-x64.tar.gz`
2. extraia o pacote
3. rode:

```bash
tar -xzf Urbeat.PrintAgent-linux-x64.tar.gz
cd Urbeat.PrintAgent-linux-x64
chmod +x Urbeat.PrintAgent install-local-agent.sh
sudo ./install-local-agent.sh
```

## Verificar agent

```bash
systemctl status urbeat-print-agent
curl http://127.0.0.1:43111/health
curl http://127.0.0.1:43111/printers
```

Esperado:

- serviço ativo
- `health` com status `ok`
- lista de impressoras contendo a fila do CUPS cadastrada

## Configurar no dashboard

No Urbeat:

1. abra `/app/configuracoes/impressao`
2. escolha `local-agent`
3. selecione a impressora detectada
4. mantenha `POS-58`, `58mm` e `autoCut` desligado para POS-58
5. rode `Imprimir teste`

## Comportamento esperado

- o dashboard salva a configuração para a loja atual
- ao aceitar pedido, o Urbeat tenta imprimir automaticamente
- não aparece popup do navegador

## Troubleshooting

### A fila não aparece no dashboard

Verifique:

```bash
lpstat -p -d
curl http://127.0.0.1:43111/printers
```

Se a fila existe no CUPS mas não aparece no agent, reinicie o serviço:

```bash
sudo systemctl restart urbeat-print-agent
```

### O CUPS imprime, mas o agent não

Verifique:

```bash
journalctl -u urbeat-print-agent -n 100 --no-pager
```

### Impressora de rede não responde

Teste conectividade:

```bash
ping 192.168.0.100
nc -vz 192.168.0.100 9100
```

## Posição oficial do Urbeat

- Linux padrão: `CUPS + local-agent`
- `raw ESC/POS` por rede: opção avançada futura
