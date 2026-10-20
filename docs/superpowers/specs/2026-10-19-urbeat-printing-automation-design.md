# Urbeat — Impressão automática de pedidos

## Objetivo

Garantir que pedidos aceitos pelo vendedor sejam enviados automaticamente para a impressora configurada da loja, sem diálogo de impressão no Windows, mantendo o aceite do pedido independente da disponibilidade da impressora.

## Decisões

- Arquitetura híbrida: backend autoritativo para o aceite e evento durável; dashboard/agente local para executar a impressão.
- A impressão automática só ocorre quando `AutoPrint` estiver habilitado na configuração da loja.
- O aceite não será desfeito se a impressão falhar.
- Retentativas automáticas em aproximadamente 2s, 5s e 15s.
- A operação será idempotente para evitar duplicidades em reentrega SignalR, recarga do dashboard ou retry.
- No Windows, não usar `window.print()`, `PrintDialog`, seleção de impressora ou botão manual durante o fluxo automático.
- O Windows usará Print Agent local como Windows Service e impressão RAW via `winspool.drv`.

## Fluxo funcional

1. O cliente cria o pedido.
2. O pedido entra como `Received`.
3. O vendedor aceita e o backend executa `Received -> Preparing`.
4. O backend grava o evento/outbox de pedido aceito.
5. O dashboard recebe o evento por SignalR e reconcilia pedidos aceitos ao carregar/reconectar.
6. O dashboard verifica `AutoPrint`, a impressora selecionada e o agente local.
7. Uma fila local inicia o job de impressão.
8. Em falha, executa as tentativas 2s/5s/15s.
9. Após esgotar as tentativas, apresenta falha no dashboard sem alterar o status do pedido.

## Contratos e idempotência

O evento de impressão deve conter, no mínimo, `orderId`, `storeId`, sequência da transição de aceite e `acceptedAtUtc`. A chave de operação deve combinar pedido, sequência e impressora/perfil configurados.

O backend deve validar que o evento pertence à loja e preservar a ordenação por pedido. O dashboard e o agente devem tolerar entrega at-least-once.

O armazenamento de jobs do agente não pode depender somente da memória se a idempotência precisar sobreviver ao reinício do serviço; deve existir persistência local apropriada ou uma estratégia equivalente de recuperação.

## Plataformas

- Windows: MSI gráfico, instalação como Windows Service, descoberta via `Get-Printer`, envio RAW por `winspool.drv`.
- Linux: agente local com CUPS, `lpstat` e `lp`.
- macOS: agente local com CUPS, `lpstat` e `lp`.
- Android: adaptador Bluetooth Capacitor quando aplicável.
- iOS/Wi-Fi: adaptador de rede compatível quando aplicável.
- Navegador: somente fallback manual/teste; nunca caminho automático do Windows.

## Instalador Windows

O MSI deve:

- instalar o executável e arquivos necessários em `Program Files`;
- instalar, iniciar e configurar o serviço para inicialização automática;
- restringir a API local a `127.0.0.1`;
- permitir reparar, atualizar e desinstalar;
- usar versionamento do pacote;
- validar dependências e informar falhas de instalação;
- oferecer um fluxo simples para usuário leigo.

O pacote ZIP/PowerShell existente permanecerá como fallback técnico e de recuperação.

## Escopo provável

### Backend

- evento/outbox durável de pedido aceito para impressão;
- contratos e validações de propriedade da loja;
- integração com a entrega SignalR existente;
- testes de transição, duplicidade, concorrência e ordenação.

### Frontend

- fila de impressão com Signals;
- deduplicação e retentativas;
- reconciliação após reload/reconexão;
- respeito obrigatório a `AutoPrint`;
- estados de agente, sucesso, retry e falha definitiva;
- manutenção dos adaptadores atuais.

### Print Agent

- persistência local de jobs/idempotência;
- execução Windows/Linux/macOS conforme a plataforma;
- MSI Windows e configuração como Windows Service;
- testes unitários do executor, descoberta e ciclo de vida do serviço.

## Critérios de aceite

- Aceitar um pedido com `AutoPrint` habilitado imprime automaticamente sem popup no Windows.
- A impressora configurada é usada sem tela de escolha.
- Falhas tentam novamente em 2s, 5s e 15s.
- O mesmo aceite não imprime duas vezes por duplicidade de evento ou reload.
- Falha definitiva não cancela nem reverte o pedido.
- O dashboard informa agente indisponível, retry em andamento e falha final.
- O MSI instala o serviço e o serviço inicia automaticamente após reinicialização do Windows.
- Linux/macOS continuam usando CUPS e Android continua usando o adaptador nativo existente.
- Backend, frontend e Print Agent possuem testes para os cenários críticos.

## Riscos

- Um dashboard fechado não executa impressão local; a reconciliação ocorrerá quando o dashboard voltar, desde que o evento/estado pendente seja recuperável.
- Serviços Windows podem exigir permissões administrativas no primeiro install.
- Drivers ESC/POS e nomes do spooler variam por fabricante.
- Impressão RAW depende de driver/configuração compatível com a impressora.
- A existência do MSI não elimina a necessidade de instalar/configurar o driver da impressora.
