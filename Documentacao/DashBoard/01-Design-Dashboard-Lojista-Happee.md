# Dashboard do Lojista Urbeat

Data: 2026-07-29

## Objetivo

Criar o painel final do lojista Urbeat em `/app/dashboard`, usando a pasta `Documentacao/DashBoard/html` como referencia de hierarquia, navegacao e fluxos, mas conectado ao produto real: login vendedor existente, APIs reais, regras do backend, SignalR de pedidos e identidade visual Urbeat.

O dashboard deve funcionar para qualquer tipo de loja cadastrada no Urbeat. Nenhuma decisao de texto, seed, imagem ou comportamento deve assumir que a loja e uma hamburgueria ou que se chama Brasa Burguer.

O principal trabalho do dashboard e ser a area operacional que o dono da loja deixa aberta aguardando pedidos chegarem. Quando um cliente finaliza um pedido valido para aquela loja, o backend deve sinalizar o lojista autenticado em tempo real e a interface deve destacar o novo pedido sem depender de refresh manual.

## Decisoes Aprovadas

- Rota base do painel: `/app`.
- Primeira rota do lojista autenticado: `/app/dashboard`.
- `/app` e seus filhos exigem token autenticado com papel `Seller`; cliente/admin nao entram no shell do lojista.
- Login reaproveitado: `/login-vendedor` com `AuthService.loginSeller`.
- Apos login vendedor bem-sucedido, redirecionar para `/app/dashboard`.
- A referencia Brasa Burguer sera usada como prototipo de UX, nao como identidade final.
- O dashboard usa o prototipo HTML como referencia de estrutura visual: sidebar azul escura, destaque laranja para item ativo, topbar operacional com periodo/atualizar/notificacoes/data, cards densos em fundo `#f5f6fb`, metricas com icones coloridos, superficies brancas e tipografia compacta. A identidade e os dados continuam genericos da loja autenticada, sem Brasa Burguer hardcoded.
- Backend continua dono das regras de negocio: status de pedido, autorizacao, totais, loja, produtos, assinatura e entregas.
- Frontend nao simula regras sensiveis; apenas apresenta dados e chama APIs.
- Telas existentes em `frontend/src/app/features/store-config` devem ser reaproveitadas ou migradas para dentro do novo shell.
- O dashboard deve iniciar o hub de vendedor ao entrar no shell e ficar ouvindo novos pedidos da loja autenticada.
- O evento real atual para pedido/notificacao do vendedor e `ReceiveSellerNotification`; notificacoes com `NotificationType.NewOrder` devem atualizar o dashboard e a area de pedidos.

## Fontes de Referencia

### Prototipo HTML

Arquivos em `Documentacao/DashBoard/html`:

- `index.html`: visao geral, menu lateral, cabecalho, metricas, ultimos pedidos e atalhos.
- `pedidos.html`: fluxo operacional de pedidos por status.
- `cardapio-produtos.html`, `cardapio-categorias.html`, `cardapio-adicionais.html`: gestao de cardapio.
- `clientes.html`: listagem e metricas de clientes.
- `entregas.html`: acompanhamento de entregas.
- `avaliacoes.html`: espaco para avaliacoes.
- `mensalidade.html`: plano, cobrancas e pagamento.
- `instalar.html`: instalacao PWA.
- `configuracoes-*.html`: horarios, informacoes, impressao, bio e bairros.

### Instrucoes Angular/Ionic

`Documentacao/DashBoard/INSTRUCOES-ANGULAR20-IONIC.md` contem boas diretrizes de Angular 20, Ionic 8, standalone components, signals, reactive forms, app shell, estados e acessibilidade.

Partes que devem ser adaptadas:

- Nao criar um projeto novo.
- Nao usar dados principais em `localStorage`.
- Nao preservar marca Brasa Burguer.
- Nao trocar o design system Urbeat por azul/laranja/roxo/Plus Jakarta Sans.
- Nao implementar pagamento ficticio de mensalidade com cartao real ou dados sensiveis.

## Arquitetura de Rotas

Rotas canonicas:

| Rota | Tela | Fonte de Dados |
|---|---|---|
| `/app/dashboard` | Visao geral | Pedidos, loja, assinatura, notificacoes |
| `/app/pedidos` | Pedidos do dia/historico | `/api/orders/store`, `/api/orders/store/{id}`, `PATCH /api/orders/{id}/status` |
| `/app/cardapio/produtos` | Produtos | APIs existentes de produtos da loja |
| `/app/cardapio/categorias` | Categorias | APIs existentes de categorias da loja |
| `/app/cardapio/adicionais` | Adicionais/opcionais | Produtos e grupos de opcoes existentes |
| `/app/clientes` | Clientes | Novo agregado de clientes ou derivado de pedidos |
| `/app/entregas` | Entregas | Pedidos delivery e status operacionais |
| `/app/avaliacoes` | Avaliacoes | Reviews da loja, hoje publicas por loja |
| `/app/mensalidade` | Assinatura e cobrancas | `/api/subscriptions/my`, `/api/subscriptions/my/charges` |
| `/app/instalar` | Instalacao PWA | Service worker/install prompt frontend |
| `/app/configuracoes/horarios` | Horarios | API existente de business hours |
| `/app/configuracoes/informacoes` | Dados da loja | API existente de store/update/address |
| `/app/configuracoes/impressao` | Impressao | Mock adapter inicialmente |
| `/app/configuracoes/bio` | Logo, banner e bio | API existente de store/upload-image/update |
| `/app/configuracoes/bairros` | Areas/taxas de entrega | APIs existentes de delivery/neighborhoods |

Fluxos separados:

- `/configurar-loja` continua sendo o onboarding inicial da loja e renderiza `WizardHeader` e `WizardFooter`.
- `/configurar-loja/horarios`, `/configurar-loja/entrega`, `/configurar-loja/produtos` e `/configurar-loja/publicar` preservam a navegacao sequencial do wizard.
- `/app/configuracoes/informacoes`, `/app/configuracoes/horarios`, `/app/configuracoes/bairros` e `/app/cardapio/produtos` reaproveitam as mesmas telas dentro do shell do dashboard, mas sem `WizardHeader`/`WizardFooter`.
- Dentro de `/app`, as telas reaproveitadas exibem apenas uma barra simples de salvamento com botao `Salvar`.

## App Shell

Criar `SellerAppShellComponent` com:

- Sidebar desktop propria, colapsavel, inspirada no HTML de referencia, com logo da loja quando disponivel, icones, grupos `Menu` e `Sistema`, status da loja, card de suporte e acoes de som/logout.
- Layout responsivo em grid para mobile/tablet.
- `router-outlet` para conteudo.
- Sidebar com nome da loja autenticada, status de funcionamento e menu.
- Topbar global com nome da loja, estado operacional e controle de som nas paginas internas; `/app/dashboard` oculta a topbar global e usa topbar propria com titulo, periodo, atualizar, badge de notificacoes e data para evitar cabecalho duplicado.
- Banner de mensalidade quando a assinatura estiver pendente, bloqueada ou proxima do vencimento.
- Acao de suporte e logout.

O shell deve carregar dados globais por uma facade do lojista e disponibilizar para paginas filhas:

- loja atual;
- assinatura;
- notificacoes;
- quantidade de pedidos pendentes;
- status aberto/fechado.

## Fluxo de Pedidos em Tempo Real

Quando o dono da loja entra no painel:

1. Login vendedor em `/login-vendedor` autentica o usuario com papel `Seller`.
2. Redirecionamento leva para `/app/dashboard`.
3. `SellerAppShellComponent` carrega a loja do vendedor com `GET /api/stores/my-store`.
4. `SellerAppShellComponent` inicia `SignalRService.startSellerHub()`.
5. O frontend registra listener para `ReceiveSellerNotification`.
6. Ao receber uma notificacao do tipo `NewOrder`, a facade deve:
   - incrementar/atualizar badge de pedidos pendentes;
   - recarregar ou inserir o pedido novo na lista operacional;
   - exibir toast/banner claro: `Novo pedido recebido`;
   - destacar o card do pedido novo ate o lojista abrir ou aceitar;
   - tocar alerta sonoro de novo pedido quando o som estiver habilitado e o navegador permitir;
   - oferecer controle visivel para ativar/desativar som de pedidos;
   - manter fallback por botao `Atualizar` e polling leve caso o WebSocket caia.
7. Na tela `/app/pedidos`, o pedido novo deve entrar na coluna/lista `Recebidos`.
8. A mudanca de etapa deve ser feita por botoes explicitos no card, nao por arrastar e soltar, para evitar confusao operacional.
9. A acao `Aceitar pedido` deve chamar `PATCH /api/orders/{orderId}/status` para mover `Received` para `Preparing`.
10. As colunas do kanban sao informativas; as transicoes continuam governadas pelo backend.

O dashboard nao deve receber pedidos de outras lojas. A filtragem/autorizacao precisa vir do backend pelo vendedor autenticado. O frontend nao deve confiar em `storeId` recebido via evento para autorizar exibicao; deve buscar/confirmar o pedido via endpoint de vendedor.

### Alerta Sonoro

O alerta sonoro faz parte do fluxo operacional do dashboard. Como o lojista pode deixar a tela aberta aguardando pedidos, a chegada de um novo pedido deve ter sinal visual e sonoro.

Regras:

- Som padrao curto, reconhecivel e nao agressivo.
- Controle no shell/topbar para `Som ligado` / `Som desligado`.
- Estado da preferencia pode ficar em storage local do navegador, pois e apenas preferencia de UI, nao regra de negocio.
- Se o navegador bloquear autoplay ate haver interacao do usuario, mostrar CTA discreto: `Ativar som de pedidos`.
- Nunca depender somente do som: tambem deve haver toast/banner, badge, destaque visual e area `aria-live`.
- Respeitar contexto do usuario: nao tocar em loop infinito; se houver multiplos pedidos em sequencia, agrupar ou limitar repeticao.
- O som deve disparar apenas para pedidos novos da loja autenticada, nao para atualizacoes comuns de status.
- Deve existir fallback silencioso se audio falhar.

## Reaproveitamento Frontend

Reaproveitar ou migrar:

- `SellerLoginPageComponent`: manter como entrada real do lojista.
- `AuthService`: login, token e refresh.
- `authGuard`: ajustar para redirecionar nao autenticado para `/login-vendedor`.
- `StoreService`: loja, produtos, categorias, horarios, entrega e publicacao.
- `OrderService`: expandir para endpoints de vendedor.
- `SellerOrdersPageComponent`: primeira versao operacional de `/app/pedidos`, agrupando pedidos por status ativo, enviando transicoes permitidas ao backend, confirmando cancelamento inline e expondo detalhe acessivel por teclado.
- `SellerDashboardPageComponent`: pagina operacional com topbar rica, aviso `Loja liberada` quando a assinatura nao esta bloqueada, cards de metrica com icones e ultimos pedidos em estrutura de tabela responsiva. A tela nao afirma mensalidade em dia sem carregar dados financeiros de assinatura.
- `SellerShellFacade`: estado central do painel, incluindo pulso de novos pedidos e pulso de atividade operacional para atualizar telas relacionadas.
- `SellerAppShellComponent`: mostra fallback operacional quando a conexao em tempo real do vendedor nao esta conectada, mantendo o painel utilizavel por atualizacao manual.
- `SellerDeliveriesPageComponent`: primeira versao real de `/app/entregas`, derivada de pedidos em `OnDelivery` com detalhes de entrega.
- `SellerCustomersPageComponent`: primeira versao real de `/app/clientes`, derivada dos pedidos recentes e agrupada por cliente.
- `SellerReviewsPageComponent`: primeira versao real de `/app/avaliacoes`, usando avaliacoes da loja.
- `SellerMarketingPageComponent`: primeira versao de `/app/marketing`, centralizando link publico e indicadores de prova social enquanto campanhas/cupons aguardam contrato backend.
- `SellerInstallPageComponent`: primeira versao de `/app/instalar`, com `beforeinstallprompt` quando o navegador suporta instalacao, fallback por navegador/plataforma quando o prompt nao esta disponivel, manifest web e service worker Angular habilitado no build de producao.
- `StoreConfigPageComponent`: migrar informacoes gerais para `/app/configuracoes/informacoes`.
- `StoreHoursPageComponent`: usar em `/app/configuracoes/horarios`.
- `StoreDeliveryPageComponent`: usar em `/app/configuracoes/bairros`.
- `StoreProductsPageComponent`: usado em `/app/cardapio/produtos` e, provisoriamente, em `/app/cardapio/categorias` porque a tela atual ja gerencia produtos e categorias.
- `StoreProductsPageComponent`: tambem usado provisoriamente em `/app/cardapio/adicionais` como primeira entrada para grupos/opcionais existentes ate haver tela dedicada.
- `StoreConfigPageComponent`: tambem usado provisoriamente em `/app/configuracoes/bio` para descricao/logo/banner dentro do shell.
- `SellerSubscriptionPageComponent`: primeira versao real de `/app/mensalidade`, consumindo assinatura e historico de cobrancas do backend.
- `ToastService`: feedback padronizado.
- `SignalRService`: base para novas notificacoes em tempo real.

## Reaproveitamento Backend

Ja disponivel:

- `GET /api/stores/my-store`
- `GET/PUT /api/stores/{storeId}`
- `GET/PUT /api/stores/{storeId}/business-hours`
- `PATCH /api/stores/{storeId}/delivery-config`
- `GET /api/stores/{storeId}/products`
- `POST/PUT/DELETE /api/stores/{storeId}/products`
- `GET/POST/PUT/DELETE /api/stores/{storeId}/categories`
- `PUT /api/stores/{storeId}/categories/reorder`
- `GET /api/orders/store/report`
- `GET /api/orders/store`
- `GET /api/orders/store/{orderId}`
- `PATCH /api/orders/{orderId}/status`
- `GET /api/seller/notifications`
- `PATCH /api/seller/notifications/{notificationId}/read`
- `GET /api/subscriptions/my`
- `GET /api/subscriptions/my/charges`
- `GET /api/public/stores/{storeId}/reviews`
- `/hubs/seller-notifications` via SignalR.

Relatorio do dashboard:

- `GET /api/orders/store/report` retorna `totalOrders`, `totalRevenue`, `inProgressOrders`, `startDateUtc` e `endDateUtc`.
- `inProgressOrders` considera `Received`, `Preparing`, `Ready` e `OnDelivery`, filtrados pela loja do vendedor autenticado.
- `/app/dashboard` permite filtrar metricas por Tudo, Hoje, Semana e Mes usando `startDateUtc`/`endDateUtc` no endpoint de relatorio.
- Periodos de negocio sao calculados pelo calendario `America/Sao_Paulo` no frontend e enviados como boundaries UTC; exibicao de datas/horas operacionais tambem fixa `America/Sao_Paulo`.
- Consultas operacionais de pedidos usam indice composto `Orders(StoreId, Status, CreatedAtUtc)` aplicado por migration.

Lacunas provaveis:

- DTO de resumo de pedido para vendedor ainda e compacto; o detalhe do pedido ja retorna cliente, telefone, fulfillment, pagamento, endereco, observacoes e composicao dos itens sob demanda.
- Dashboard ja recebe andamento operacional e filtros de periodo no `StoreOrdersSimpleReportResponseDto`; breakdown por pagamento ainda depende de evolucao futura do agregado.
- Notificacao SignalR atual envia dados de notificacao, nao o card completo do pedido; frontend deve buscar detalhes/resumo do pedido ao receber `NewOrder`.
- Clientes, entregas e avaliacoes do vendedor ja possuem endpoints protegidos para evitar agregacao fragil no frontend.
- Rotas de categorias, adicionais e bio ja possuem paginas dedicadas dentro do shell `/app`.
- Rotas de edicao operacional usam guard de alteracoes pendentes quando a pagina expoe `hasUnsavedChanges()`.

## Padroes de Projeto

Frontend:

- Feature folders por dominio: `seller-shell`, `seller-dashboard`, `seller-orders`, `seller-menu`, `seller-settings`.
- `data-access` por feature para chamadas HTTP/facades.
- Facade pattern para estado de pagina e orquestracao de APIs.
- Shell e dashboard devem ser compostos por componentes pequenos: cards de metrica, cabecalho, sala operacional, listas e estados.
- Mapper functions para transformar DTO em view model.
- Componentes apresentacionais sem chamadas HTTP.
- Reactive Forms tipados em formularios longos.
- Signals para estado local e derived state.
- `takeUntilDestroyed()` quando subscriptions forem inevitaveis.
- `@if`, `@for`, `@switch`, `@empty` nos templates.
- Componentes compartilhados: page header, metric card, status chip, empty state, loading skeleton, responsive list, confirm action.

Backend:

- Queries/servicos de leitura para agregados de dashboard.
- DTOs especificos para tela, sem expor entidades.
- State machine existente para status de pedido.
- Autorizacao `SellerOnly` em todos endpoints do painel.
- Backend recalcula metricas e filtra por loja do vendedor autenticado.
- Auditoria/log nos eventos de status e configuracoes criticas.

## Estados Obrigatorios

Toda tela do painel deve ter:

- loading com skeleton;
- vazio com proxima acao clara;
- erro com retry;
- sem resultados quando filtro nao encontra dados;
- sucesso por toast;
- confirmacao para exclusoes/cancelamentos;
- feedback para alteracao de status;
- navegacao por teclado;
- touch targets de pelo menos 44px;
- textos em pt-BR.

## Primeiro Recorte de Implementacao

Entrega 1 deve criar a base navegavel e util:

1. `SellerAppShellComponent` em `/app`.
2. Redirecionamento pos-login para `/app/dashboard`.
3. `/app/dashboard` com dados reais basicos.
4. Menu lateral/mobile com links canonicos.
5. Redirecionamentos legados de `/configurar-loja/*`.
6. Reaproveitamento inicial de produtos, categorias, horarios, informacoes e entrega dentro das rotas novas. `/app/cardapio` redireciona para `/app/cardapio/produtos`.
7. `/app/mensalidade` com status da assinatura, proximo vencimento, ultima cobranca, mensagem de regularizacao e historico de cobrancas.
8. `/app/entregas` com pedidos em rota, contato do cliente e endereco, derivado de `GET /api/orders/store` + detalhe do pedido.
9. `/app/clientes` com clientes agrupados a partir dos pedidos recentes, total de pedidos, total gasto, telefone e ultimo pedido.
10. `/app/avaliacoes` com nota media, total e comentarios de clientes.
11. `/app/marketing` com link publico da loja, status, prova social e lacuna explicitada para campanhas/cupons.
7. Conexao com hub de vendedor e listener de novos pedidos.
8. Alerta sonoro configuravel para novos pedidos.

Entrega 2 deve focar operacao:

1. `/app/pedidos` com listagem real da loja. Primeira versao implementada com `GET /api/orders/store` filtrado por status ativo.
2. Agrupamento por status. Primeira versao implementada para Recebidos, Preparando, Prontos e Em entrega, sem depender da primeira pagina historica sem filtro.
3. Acoes contextuais usando `PATCH /api/orders/{id}/status`. Primeira versao cobre transicoes operacionais principais, entrega direta de pedido pronto e cancelamento com confirmacao visual inline, sem dialogo nativo bloqueante.
4. Painel de detalhe do pedido. Primeira versao busca `GET /api/orders/store/{orderId}` sob demanda, mostra cliente, telefone, tipo, pagamento, endereco, itens, variacao, escolhas, adicionais, observacoes e total, foca o botao `Fechar` ao abrir e fecha por `Escape`.
5. Destaque em tempo real para pedidos recebidos via SignalR. Primeira versao recarrega a lista quando `newOrderPulse` muda, anuncia `Novo pedido #codigo recebido.` via `aria-live` e destaca o card recem-chegado.
7. Cards operacionais com resumo rico do vendedor: cliente, telefone, tipo, pagamento, endereco resumido e itens principais retornam em `GET /api/orders/store` e aparecem diretamente no board.
6. Sincronizacao operacional. Mudancas manuais de status em `/app/pedidos` emitem `orderActivityPulse`, permitindo que `/app/dashboard` recarregue metricas e pedidos recentes sem aguardar outro evento SignalR.

Limitacao atual: o card operacional ja mostra resumo rico do vendedor. O painel de detalhe continua necessario para composicao completa de itens, observacoes, historico e dados extensos.

Entrega 3 deve completar gestao:

1. Clientes.
2. Entregas.
3. Avaliacoes.
4. Mensalidade.
5. Instalar PWA. Primeira versao implementada em `/app/instalar` com `InstallPromptService`, `manifest.webmanifest`, `ngsw-config.json` e service worker Angular no build de producao. O service worker cacheia app shell/assets, nao `/api`, para preservar dados operacionais frescos.
6. Configuracoes restantes.

## Anti-Objetivos

- Nao criar uma segunda aplicacao Angular.
- Nao duplicar regra de negocio sensivel no frontend.
- Nao usar Bootstrap, jQuery ou dashboard pronto.
- Nao copiar CSS/classes do prototipo HTML.
- Nao hardcodar Brasa Burguer, hamburgueres ou dados seed como conteudo real.
- Nao expor dados pessoais de clientes em logs.
- Nao implementar integracao real com impressora neste ciclo.
- Nao capturar dados reais de cartao na mensalidade.
