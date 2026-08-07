# Backlog - Dashboard do Lojista Urbeat

Data: 2026-07-29

## Resumo

- Objetivo: entregar o painel final do lojista Urbeat em `/app/dashboard`, reaproveitando login, telas, APIs e regras existentes.
- Perfil: lojista/seller autenticado.
- Prioridade: alta.
- Premissa: dashboard generico para qualquer loja Urbeat, sem dependencia de Brasa Burguer.
- Premissa operacional: o dashboard e a tela que o dono da loja deixa aberta aguardando pedidos em tempo real.
- Premissa de UI: priorizar telas medias/tablets/notebooks compactos; mobile continua suportado.
- Premissa Angular: preferir componentes pequenos e apresentacionais, com paginas apenas orquestrando dados.

## RF-DASH-01 - Shell do Lojista

### User Stories

- Como lojista, quero acessar um painel protegido em `/app`, para gerenciar minha loja em uma area unica.
- Como lojista, quero navegar por uma sidebar clara em telas medias/desktop e por um layout funcional no mobile, para acessar rapidamente as funcoes principais.
- Como lojista, quero ver o status da minha loja e notificacoes no topo/menu, para saber se estou recebendo pedidos.
- Como lojista, quero deixar o dashboard aberto e receber sinal imediato quando chegar pedido da minha loja, para aceitar e iniciar o preparo rapidamente.
- Como lojista, quero ouvir um alerta sonoro quando chegar pedido novo, para perceber a venda mesmo sem olhar a tela no momento.

### Criterios de Aceite

- [x] `/login-vendedor` redireciona para `/app/dashboard` apos login bem-sucedido.
- [x] `/app` exige autenticacao de vendedor.
- [x] `/app` rejeita token autenticado sem papel `Seller` e redireciona para `/login-vendedor`.
- [x] Usuario nao autenticado em `/app/*` e redirecionado para `/login-vendedor`.
- [x] Shell usa layout Angular responsivo e `router-outlet` para conteudo filho.
- [x] Menu mostra dados da loja autenticada, nao dados fixos.
- [x] Em viewport compacto, a navegacao permanece visivel e nao depende de overlay/modal para fechar.
- [x] Logout limpa sessao e redireciona para `/login-vendedor`.
- [x] Logout tambem limpa estado do shell e encerra o hub do vendedor.
- [x] Ao entrar em `/app`, o hub `/hubs/seller-notifications` e iniciado para o usuario vendedor autenticado.
- [x] O shell escuta `ReceiveSellerNotification` e trata `NotificationType.NewOrder` como novo pedido recebido.
- [x] Pedido novo atualiza badge/menu/dashboard sem refresh manual.
- [x] Evento duplicado de uma mesma notificacao nao infla badge de nao lidas.
- [x] Pedido novo dispara alerta sonoro quando o som estiver habilitado.
- [x] O painel oferece controle claro para ligar/desligar som de pedidos.
- [x] Se o navegador bloquear audio, o painel mostra acao `Ativar som de pedidos`.
- [x] Se SignalR desconectar, a UI informa reconexao/fallback sem bloquear uso manual.

### Tarefas Tecnicas

#### Backend (.NET 9)

- [x] Confirmar que `GET /api/stores/my-store` retorna dados suficientes para sidebar/topbar.
- [x] Confirmar comportamento de assinatura bloqueada em `StoreResponse.isSubscriptionBlocked`.
- [x] Avaliar se `GET /api/seller/panel` ainda e util ou pode permanecer apenas como smoke endpoint.
- [x] Confirmar que `NotifySellerNewOrderAsync` e chamado para todos pedidos validos que devem aparecer para o lojista.
- [x] Confirmar contrato do evento `ReceiveSellerNotification` e `NotificationType.NewOrder`.

#### Frontend (Angular 20)

- [x] Criar `features/seller-shell/seller-app-shell.component.*`.
- [x] Criar `SellerShellFacade` para loja, notificacoes, assinatura e pedidos pendentes.
- [x] Atualizar `app.routes.ts` com rota `/app` protegida.
- [x] Atualizar `SellerLoginPageComponent` para navegar para `/app/dashboard`.
- [x] Ajustar `authGuard` para redirecionar para `/login-vendedor`.
- [x] Criar sidebar/topbar compartilhadas.
- [x] Componentizar shell/dashboard em blocos pequenos e testaveis.
- [x] Criar redirecionamentos legados de `/configurar-loja/*`.
- [x] Garantir que `/configurar-loja/*` nao renderize configuracao fora do shell protegido.
- [x] Iniciar `SignalRService.startSellerHub()` no shell.
- [x] Criar listener centralizado para `ReceiveSellerNotification`.
- [x] Atualizar badge de pedidos pendentes ao receber `NewOrder`.
- [x] Tornar atualizacao de badge idempotente por `notification.id`.
- [x] Exibir alerta `Novo pedido recebido`.
- [x] Recarregar resumo/lista de pedidos apos evento, sem confiar no evento como fonte unica de dados.
- [x] Criar `OrderSoundAlertService` ou adapter equivalente para tocar som curto de novo pedido.
- [x] Persistir preferencia local `som de pedidos ligado/desligado` como preferencia de UI.
- [x] Tratar falha de autoplay/audio sem quebrar o fluxo.

#### Dados e Infra

- [x] Nenhuma nova tabela esperada.
- [x] Validar comportamento em refresh de pagina e token expirado.
- [x] Validar logout/re-login sem reload para nao vazar dados de outro lojista.
- [ ] Validar WebSocket em producao via `/hubs/seller-notifications` e nginx.

### DoD

- [x] Shell navegavel em desktop e mobile.
- [x] Login real reaproveitado.
- [x] Novo pedido recebido em tempo real atualiza o painel do lojista.
- [x] Novo pedido dispara alerta visual e sonoro quando habilitado.
- [x] Testes de rota/guard/login atualizados.
- [x] `npx ng build` passa.

### Dependencias

- RFs relacionadas: RF-DASH-02, RF-DASH-03.
- Bloqueios: nenhum conhecido.

### Riscos e Mitigacao

- Risco: conflito de rota `/app` com loja publica `/:storePath`.
- Mitigacao: declarar `/app` antes de `:storePath` em `app.routes.ts`.
- Risco: perder pedido se WebSocket desconectar.
- Mitigacao: reconexao automatica, botao Atualizar e recarga periodica leve enquanto houver falha de conexao.
- Risco: navegador bloquear audio automatico.
- Mitigacao: exigir/solicitar interacao inicial `Ativar som de pedidos` e manter alerta visual obrigatorio.

### Ordem de Entrega

1. Rotas e shell.
2. Facade global.
3. Redirecionamentos e login.
4. SignalR de novos pedidos.
5. Alerta sonoro configuravel.

## RF-DASH-02 - Visao Geral da Loja

### User Stories

- Como lojista, quero ver pedidos, faturamento, ticket medio e pedidos em andamento, para entender rapidamente o dia da loja.
- Como lojista, quero alternar periodo entre hoje, semana e mes, para comparar desempenho basico.
- Como lojista, quero acessar ultimos pedidos e atalhos principais, para agir rapidamente.
- Como lojista, quero que um pedido novo apareca imediatamente na visao geral, para nao perder venda enquanto estou com o painel aberto.

### Criterios de Aceite

- [x] `/app/dashboard` mostra metricas reais da loja autenticada.
- [x] Total de pedidos e faturamento usam backend, nao calculos hardcoded.
- [x] Ticket medio e calculado a partir de total de pedidos e receita.
- [x] Pedidos em andamento consideram status `Received`, `Preparing`, `Ready` e `OnDelivery`.
- [x] Ultimos pedidos exibem codigo, horario, status e total.
- [x] Atalhos navegam para pedidos, cardapio, entregas e configuracoes.
- [x] Atualizar recarrega dados e mostra feedback.
- [x] Loading, vazio e erro sao exibidos corretamente.
- [x] `ReceiveSellerNotification` com `NewOrder` recarrega metricas e ultimos pedidos.
- [x] Pedido novo fica destacado ate o lojista abrir detalhes ou aceitar.
- [x] Pedido novo dispara som uma vez por evento ou lote, sem loop infinito.

### Tarefas Tecnicas

#### Backend (.NET 9)

- [x] Avaliar expansao de `StoreOrdersSimpleReportResponseDto` ou criar `SellerDashboardSummaryResponseDto`.
- [x] Implementar/ajustar service de leitura para metricas por periodo.
- [x] Incluir pedidos em andamento e ultimos pedidos no agregado.
- [x] Garantir filtro por vendedor autenticado.
- [x] Testar metricas de andamento e loja do vendedor.

#### Frontend (Angular 20)

- [x] Criar `SellerDashboardPageComponent`.
- [x] Criar `SellerDashboardService` ou metodo em `OrderService` para resumo.
- [x] Criar `MetricCardComponent` compartilhado.
- [x] Criar `PageHeaderComponent` compartilhado.
- [x] Criar skeletons e empty state.
- [x] Formatar moeda/data em pt-BR com timezone `America/Sao_Paulo` para telas operacionais.
- [x] Integrar dashboard ao listener central de novos pedidos.
- [x] Integrar dashboard ao servico de alerta sonoro.

#### Dados e Infra

- [x] Migration adicionada para indice operacional de pedidos por loja/status/data.

### DoD

- [x] Dashboard usa dados reais.
- [x] Testes unitarios de calculo/mapeamento.
- [x] Build frontend e testes focados passam.

### Dependencias

- RFs relacionadas: RF-DASH-01, RF-DASH-03.
- Bloqueios: DTO atual de relatorio e simples demais.

### Riscos e Mitigacao

- Risco: N+1 ao buscar ultimos pedidos com detalhes.
- Mitigacao: endpoint agregado deve retornar o necessario em uma consulta.

### Ordem de Entrega

1. Endpoint/agregado minimo.
2. Pagina dashboard.
3. Estados e testes.

## RF-DASH-03 - Pedidos Operacionais

### User Stories

- Como lojista, quero visualizar pedidos por status, para acompanhar a operacao.
- Como lojista, quero avancar pedidos por acoes contextuais, para manter cliente e loja sincronizados.
- Como lojista, quero ver detalhes do pedido antes de agir, para evitar erros.
- Como lojista, quero que o pedido novo entre automaticamente na coluna de recebidos, para aceitar sem atualizar a pagina.

### Criterios de Aceite

- [x] `/app/pedidos` lista pedidos da loja autenticada.
- [x] Pedidos sao agrupados por status operacional.
- [x] Acoes permitidas seguem `OrderStatusStateMachine` do backend.
- [x] Pedido cancelado ou entregue nao apresenta acao de avanco.
- [x] Cancelamento pede confirmacao visual inline.
- [x] Cada mudanca de status mostra toast.
- [x] Tela atualiza badge e metricas apos mudanca.
- [x] Mobile usa segment/status selecionado em vez de colunas espremidas.
- [x] Pedido novo recebido por SignalR entra em `Recebidos` apos confirmacao por endpoint de vendedor.
- [x] O card novo recebe destaque visual e alerta acessivel via `aria-live`.
- [x] O card novo tambem aciona som se a preferencia estiver ativa.

Estado atual da primeira versao:

- [x] `/app/pedidos` possui pagina real componentizada.
- [x] Lista usa `GET /api/orders/store` filtrado por status ativo.
- [x] Agrupa pedidos em Recebidos, Preparando, Prontos e Em entrega.
- [x] Envia transicoes operacionais via `PATCH /api/orders/{orderId}/status`, incluindo cancelamento.
- [x] Recarrega a lista quando o shell recebe `NewOrder`.
- [x] Evita PATCH duplicado enquanto o pedido esta atualizando.
- [x] Refresh silencioso por realtime nao substitui o quadro atual por erro global.
- [x] Cancelamento pede confirmacao antes de enviar `PATCH`.
- [x] Confirmacao de cancelamento e inline e nao depende de `window.confirm`.
- [x] Cada mudanca de status mostra toast de sucesso ou erro.
- [x] Mudanca manual de status emite pulso operacional para o dashboard recarregar metricas e pedidos recentes.
- [x] Mobile possui seletor de etapa para evitar colunas espremidas.
- [x] Painel de detalhe busca `GET /api/orders/store/{orderId}` sob demanda e mostra cliente, telefone, endereco, pagamento e itens completos disponiveis.
- [x] Painel de detalhe foca o botao `Fechar` ao abrir e fecha com `Escape`.
- [x] Pedido recebido por realtime anuncia `Novo pedido #codigo recebido.` e destaca o card correspondente.
- [x] Cards completos usam DTO de vendedor mais rico e detalhe sob demanda para informacoes extensas.

Rotas reaproveitadas no shell atual:

- [x] `/app/cardapio` redireciona para `/app/cardapio/produtos`.
- [x] `/app/cardapio/produtos` usa `StoreProductsPageComponent` protegido pelo shell.
- [x] `/app/cardapio/categorias` usa `StoreProductsPageComponent` provisoriamente, pois a tela atual tambem gerencia categorias.
- [x] `/app/configuracoes/informacoes` usa `StoreConfigPageComponent` protegido pelo shell.
- [x] `/app/configuracoes/horarios` usa `StoreHoursPageComponent` protegido pelo shell.
- [x] `/app/configuracoes/bairros` usa `StoreDeliveryPageComponent` protegido pelo shell.
- [x] `/app/mensalidade` possui primeira tela real com `GET /api/subscriptions/my` e `GET /api/subscriptions/my/charges`.
- [x] `/app/entregas` possui primeira tela real derivada de pedidos `OnDelivery` com detalhe filtrado para `FulfillmentType.Delivery`.
- [x] `/app/clientes` possui primeira tela real derivada de pedidos recentes detalhados, agrupando por cliente.
- [x] `/app/avaliacoes` possui primeira tela real com avaliacoes da loja.
- [x] `/app/marketing` possui primeira tela com link publico e indicadores atuais; campanhas/cupons dependem de contrato backend futuro.
- [x] Cards completos usam DTO de vendedor mais rico e detalhe sob demanda para informacoes extensas.

### Tarefas Tecnicas

#### Backend (.NET 9)

- [x] Expandir `OrderSummaryResponseDto` para vendedor ou criar `SellerOrderSummaryResponseDto`.
- [x] Incluir cliente, contato, fulfillment, pagamento, endereco resumido, itens principais e horario.
- [x] Garantir que `PATCH /api/orders/{orderId}/status` audita transicao.
- [x] Testar transicoes invalidas e autorizacao por dono da loja.

#### Frontend (Angular 20)

- [x] Expandir `OrderService` com `getStoreOrders`, `getStoreOrder`, `updateStatus`.
- [x] Criar `SellerOrdersPageComponent`.
- [x] Criar componentes de card/grupo de pedidos.
- [x] Criar mapper de `OrderStatus` para labels: Recebido, Em preparacao, Pronto, Em entrega, Concluido, Cancelado.
- [x] Criar painel de detalhe com itens, endereco, pagamento e observacoes.
- [x] Criar confirm action visual para cancelamento.
- [x] Integrar pagina de pedidos ao stream/facade de novos pedidos.
- [x] Em evento `NewOrder`, recarregar os status ativos pelo endpoint protegido do vendedor.

#### Dados e Infra

- [x] Criar indice `Orders(StoreId, Status, CreatedAtUtc)` para consultas operacionais do vendedor.

### DoD

- [x] Status alterado no frontend reflete estado persistido no backend.
- [x] Transicoes invalidas bloqueadas pelo backend.
- [x] Testes unitarios de agrupamento/status.

### Dependencias

- RFs relacionadas: RF-DASH-01, RF-DASH-02.
- Bloqueios: resumo atual de pedido nao traz dados suficientes para cards completos.

### Riscos e Mitigacao

- Risco: estado visual divergir do backend apos erro.
- Mitigacao: pessimistic update ou rollback com reload do pedido.
- Risco: evento trazer apenas notificacao e nao dados completos do pedido.
- Mitigacao: buscar detalhes pelo endpoint protegido do vendedor antes de inserir/atualizar o card.

### Ordem de Entrega

1. DTO/endpoints suficientes.
2. Listagem e agrupamento.
3. Acoes e detalhes.

## RF-DASH-04 - Cardapio no Painel

### User Stories

- Como lojista, quero gerenciar produtos, categorias e adicionais dentro do painel, para manter meu cardapio atualizado.
- Como lojista, quero reaproveitar os dados ja cadastrados, para nao refazer configuracoes.

### Criterios de Aceite

- [x] `/app/cardapio/produtos` reaproveita produtos reais da loja.
- [x] `/app/cardapio/categorias` reaproveita categorias reais.
- [x] `/app/cardapio/adicionais` mostra grupos/opcionais existentes ou uma primeira versao documentada.
- [x] Criar, editar, ativar/desativar e excluir usa APIs reais.
- [x] Categorias impedem duplicidade e respeitam regras backend.
- [x] UI nao assume categorias de hamburgueria.

### Tarefas Tecnicas

#### Backend (.NET 9)

- [x] Confirmar cobertura dos endpoints existentes de produto/categoria.
- [ ] Avaliar necessidade de endpoint especifico para adicionais/opcionais globais.

#### Frontend (Angular 20)

- [x] Migrar/adaptar `StoreProductsPageComponent` para rotas `/app/cardapio/*`.
- [ ] Separar componentes grandes se necessario: product editor, category manager, option groups editor.
- [ ] Remover dependencia de wizard/stepper quando usado dentro do painel.
- [ ] Padronizar filtros, metricas e estados.

#### Dados e Infra

- [x] Nenhuma migration prevista.

### DoD

- [x] Produtos/categorias editaveis dentro do shell.
- [x] Build e testes existentes continuam passando.

### Dependencias

- RFs relacionadas: RF-DASH-01.
- Bloqueios: componente atual e grande e pode precisar de decomposicao incremental.

### Riscos e Mitigacao

- Risco: refatorar demais e quebrar cadastro atual.
- Mitigacao: primeiro encapsular rotas novas reutilizando componente, depois decompor.

### Ordem de Entrega

1. Rotas novas apontando para componentes existentes.
2. Ajustes visuais no shell.
3. Decomposicao gradual.

## RF-DASH-05 - Configuracoes da Loja

### User Stories

- Como lojista, quero editar informacoes, horarios, bio, entrega e bairros no painel, para manter minha loja operacional.
- Como lojista, quero salvar/cancelar alteracoes com feedback claro, para evitar perda de dados.

### Criterios de Aceite

- [x] `/app/configuracoes/horarios` usa horarios reais.
- [x] `/app/configuracoes/informacoes` usa dados reais da loja.
- [x] `/app/configuracoes/bio` usa descricao, logo e banner reais.
- [x] `/app/configuracoes/bairros` usa areas/taxas reais.
- [ ] Formularios bloqueiam saida com alteracoes nao salvas quando aplicavel.
- [ ] Campos obrigatorios e erros sao acessiveis.

### Tarefas Tecnicas

#### Backend (.NET 9)

- [x] Confirmar endpoints de update/store/address/media.
- [x] Confirmar regras de horarios com multiplos turnos e timezone.

#### Frontend (Angular 20)

- [x] Reaproveitar `StoreHoursPageComponent`.
- [x] Reaproveitar/adaptar `StoreConfigPageComponent` para informacoes/bio.
- [x] Reaproveitar `StoreDeliveryPageComponent` para bairros.
- [ ] Criar tabs de configuracoes dentro do shell.
- [ ] Implementar `CanDeactivate` para formularios sujos.

#### Dados e Infra

- [x] Nenhuma migration prevista.

### DoD

- [x] Configuracoes principais editaveis em `/app/configuracoes/*`.
- [x] Estados de erro/sucesso padronizados.

### Dependencias

- RFs relacionadas: RF-DASH-01.
- Bloqueios: impressao sera mock inicial.

### Riscos e Mitigacao

- Risco: duplicar telas antigas e novas.
- Mitigacao: redirects legados e reaproveitamento dos componentes existentes.

### Ordem de Entrega

1. Horarios e bairros.
2. Informacoes e bio.
3. Impressao mock.

## RF-DASH-06 - Clientes, Entregas e Avaliacoes

### User Stories

- Como lojista, quero consultar clientes, para entender recorrencia e historico.
- Como lojista, quero acompanhar entregas, para monitorar atrasos e status.
- Como lojista, quero ver avaliacoes, para acompanhar reputacao.

### Criterios de Aceite

- [x] Clientes lista dados agregados sem expor informacao desnecessaria.
- [x] Entregas lista somente pedidos delivery da loja autenticada.
- [x] Avaliacoes mostra media, distribuicao e comentarios quando existirem.
- [x] Dados pessoais nao aparecem em logs.
- [x] Exportacao, se implementada, gera CSV local sem enviar dados para terceiros.

### Tarefas Tecnicas

#### Backend (.NET 9)

- [ ] Criar endpoint agregado de clientes por loja ou adaptar `orders/store`.
- [ ] Criar endpoint de entregas por loja ou filtro dedicado em pedidos.
- [ ] Criar endpoint protegido de avaliacoes do vendedor ou validar uso da rota publica.

#### Frontend (Angular 20)

- [x] Criar paginas `/app/clientes`, `/app/entregas`, `/app/avaliacoes`.
- [x] Criar listas responsivas e filtros.
- [x] Criar modais de detalhe quando necessario.

#### Dados e Infra

- [x] Endpoints protegidos adicionados para agregados de clientes, entregas e avaliacoes do vendedor.
- [x] `/app/clientes`, `/app/entregas` e `/app/avaliacoes` consomem endpoints dedicados/protegidos em vez de montar dados por N+1 ou rota publica.
- [x] `/app/cardapio/categorias`, `/app/cardapio/adicionais` e `/app/configuracoes/bio` usam paginas dedicadas.
- [x] Guard de alteracoes pendentes aplicado em rotas de edicao com `hasUnsavedChanges()`.

### DoD

- [x] Paginas funcionam com loja vazia e com dados reais.
- [x] Estados loading/empty/error implementados.

### Dependencias

- RFs relacionadas: RF-DASH-03.
- Bloqueios: endpoints agregados ainda nao existem.

### Riscos e Mitigacao

- Risco: privacidade de clientes.
- Mitigacao: retornar apenas dados necessarios e mascarar onde fizer sentido.

### Ordem de Entrega

1. Entregas derivadas de pedidos.
2. Avaliacoes.
3. Clientes agregado.

## RF-DASH-07 - Mensalidade e Instalar

### User Stories

- Como lojista, quero ver minha mensalidade e cobrancas, para saber se minha loja esta regular.
- Como lojista, quero instalar o painel como app, para acesso rapido no celular.

### Criterios de Aceite

- [x] `/app/mensalidade` mostra status, plano e historico real.
- [x] Banner global reflete mensalidade bloqueada/pendente quando backend indicar.
- [x] `/app/instalar` mostra instrucoes e botao somente quando browser suportar install prompt.
- [x] Nao solicitar dados reais de cartao.

### Tarefas Tecnicas

#### Backend (.NET 9)

- [x] Confirmar resposta de `/api/subscriptions/my` e `/my/charges`.
- [x] Definir se pagamento real de assinatura fica fora deste ciclo.

#### Frontend (Angular 20)

- [x] Criar `SubscriptionService`.
- [x] Criar pagina mensalidade.
- [x] Criar `InstallPromptService` com adaptador seguro para browser APIs.
- [x] Configurar PWA se ainda nao estiver configurado.

#### Dados e Infra

- [x] Validar manifest/service worker em build de producao.

### DoD

- [x] Mensalidade exibe dados reais.
- [x] Instalar tem fallback por navegador/plataforma.

### Dependencias

- RFs relacionadas: RF-DASH-01.
- Bloqueios: PWA pode exigir configuracao de assets/manifest.

### Riscos e Mitigacao

- Risco: browser nao oferecer prompt de instalacao.
- Mitigacao: instrucoes por plataforma e estado explicativo.

### Ordem de Entrega

1. Mensalidade.
2. Banner global.
3. Instalar/PWA.

## Ordem Recomendada Geral

1. RF-DASH-01 - Shell do Lojista.
2. RF-DASH-02 - Visao Geral da Loja.
3. RF-DASH-04 - Reaproveitar Cardapio no Painel.
4. RF-DASH-05 - Configuracoes da Loja.
5. RF-DASH-03 - Pedidos Operacionais.
6. RF-DASH-07 - Mensalidade e Instalar.
7. RF-DASH-06 - Clientes, Entregas e Avaliacoes.

## Riscos Globais

- Worktree ja possui muitas alteracoes: antes de implementar, limitar patches aos arquivos do dashboard e evitar tocar em arquivos sensiveis.
- `StoreProductsPageComponent` e grande: refatorar em passos pequenos e com testes.
- Rotas `/app/*` devem ser declaradas antes de `:storePath` para nao serem capturadas como loja publica.
- Alguns endpoints atuais nao retornam dados suficientes para os cards do painel; priorizar DTOs especificos em vez de multiplas chamadas por item.
- Design system do prototipo conflita com Urbeat; usar prototipo como hierarquia, nao como tokens.

## Testes Minimos Por Entrega

- Frontend: testes de rota, guard, mappers, metricas, filtros e status.
- Backend: testes de autorizacao, agregados por loja, transicoes de pedido e privacidade de dados.
- Build: `dotnet build backend/Urbeat.sln` e `npx ng build` antes de deploy.
