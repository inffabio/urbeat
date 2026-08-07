# Rebuild do Fluxo Cliente da Loja

## Objetivo

Recriar o front da loja do cliente usando `Documentacao/FrontEnd/NovaVersaoFront270726/Loja/index.html` como referencia visual e `Correcoes_Loja_Urbeat.md` como checklist de qualidade, sem copiar CSS acumulado, dados estaticos ou logica baseada no prototipo.

O rebuild cobre o fluxo completo:

- Cardapio: `/:storePath`.
- Detalhe/configuracao do produto: `/:storePath/produto/:productId`.
- Carrinho: `/:storePath/carrinho`.
- Cadastro/endereco: `/:storePath/checkout/cadastro`.
- Escolha de pagamento: `/:storePath/checkout/pagamento`.
- Pagamento online: `/:storePath/checkout/pagar`.
- Pagamento na entrega: `/:storePath/checkout/entrega`.
- Acompanhamento: `/:storePath/pedido/:orderId`.

## Decisao De Arquitetura

Usar rebuild incremental sobre os servicos Angular existentes. Nao criar uma nova camada paralela nem portar a estrutura do prototipo estatico.

Servicos atuais preservados:

- `StoreService` para loja publica e cobertura de entrega.
- `CatalogService` para categorias/produtos publicos.
- `CartService` para carrinho local do cliente.
- `StoreFilterStateService` para categoria, busca e scroll.
- `CheckoutService` para preview e confirmacao.
- `PaymentService` para iniciar pagamento online.
- `OrderService` e `SignalRService` para tracking.

Regras de negocio continuam no backend. O frontend envia IDs, quantidades, peso, variacao, grupos e observacoes; preco final, frete, minimo, disponibilidade, status e pagamento sao recomputados pelo servidor.

## Contratos Dinamicos

Dados de loja:

- `GET /api/public/stores/by-path/{storePath}`.
- `GET /api/public/stores/{storeId}`.
- `GET /api/public/stores/{storeId}/delivery-check?neighborhood=...`.

Catalogo:

- `GET /api/public/stores/{storeId}/catalog/categories`.
- `GET /api/public/stores/{storeId}/catalog/products`.
- `GET /api/public/stores/{storeId}/catalog/products/featured` fica reservado para telas que carreguem destaques separadamente; o cardapio principal pode derivar `Destaques` de `isFeatured` no retorno de produtos.

Checkout e pedidos:

- `POST /api/checkout/preview` para resumo server-side.
- `POST /api/checkout/confirm` para criar pedido autenticado.
- `POST /api/payments/order` para iniciar pagamento online.
- `GET /api/orders/{orderId}` para tracking.
- SignalR `CustomerNotificationHub` para `OrderStatusUpdated` e eventos de entrega.

Webhooks (`/api/webhooks/mercadopago`, `/api/webhooks/asaas`) sao integracao backend/gateway. O frontend nao chama webhook diretamente.

## Componentes

Componentes existentes a manter e endurecer:

- `ProductCardComponent`: estados simples, configuravel, em carrinho e indisponivel.
- `FloatingCartComponent`: largura total, safe-area e sem colisao com footer.
- `FooterNavComponent`: navegacao do fluxo da loja.
- `CategoryTabsComponent`: chips com scroll horizontal limitado ao container.
- `StoreMetricsComponent`: status, tempo medio e pedido minimo.
- `EmptyStateComponent`: erro/lista vazia/busca sem resultado.

Componentes a criar ou extrair durante o rebuild:

- `StoreHeaderHeroComponent`: banner, logo, nome, subtitulo e metricas.
- `QuantityControlComponent`: controle acessivel `- N +`, reutilizado em card, detalhe e carrinho.
- `ProductOptionGroupComponent`: renderizador de grupos com `radio`, `checkbox` e limites min/max.
- `CheckoutSummaryComponent`: subtotal, frete, minimo, frete gratis e total.
- `ReceiveMethodSelectorComponent`: entrega/retirada com estados desabilitados.
- `PaymentOptionCardComponent`: opcoes de pagamento com input semantico.

Paginas devem compor componentes; logica duplicada entre telas deve ir para servicos ou componentes compartilhados quando tiver responsabilidade clara.

## Cardapio

O cardapio sera mobile-first e centralizado no desktop. A tela deve respeitar a linguagem visual do prototipo: fundo creme, superficie branca, bordeaux `#D54A51`, chips arredondados, hero com logo centralizado e tipografia Inter. A lista de itens deve seguir uma experiencia familiar de apps de delivery como iFood: leitura rapida, categorias sempre faceis de acessar, cards compactos, foto consistente, preco evidente e acao de compra previsivel. Nao copiar marca, cores proprietarias ou elementos identicos do iFood; usar apenas o padrao de usabilidade.

Comportamento:

- Carregar loja por `storePath` e depois categorias/produtos por `storeId`.
- Ordenar categorias por `displayOrder`, com desempate por nome/id.
- Ocultar categorias inativas ou sem produtos renderizaveis.
- Aba `Todos` mostra produtos agrupados por categoria.
- Aba `Destaques` aparece quando existirem produtos destacados.
- Busca filtra por nome e descricao.
- Estado vazio mostra `Nenhum produto encontrado`, explicacao curta e acao para limpar busca.
- Abrir produto preserva categoria, busca e posicao de scroll.

Cards:

- Layout principal em lista vertical, com separadores leves e densidade maior que cards promocionais.
- Foto do produto com tamanho fixo consistente e cantos arredondados; fallback visual quando nao houver imagem.
- Nome, descricao curta e preco alinhados para leitura em varredura, evitando sombras pesadas.
- Categorias devem funcionar como menu horizontal sticky dentro da loja quando a rolagem passar pelo hero, mantendo `Todos`, `Destaques` e categorias acessiveis.
- Produto simples sem escolhas mostra botao `+` e permite adicionar direto.
- Produto configuravel mostra seta/texto de escolha e abre detalhe.
- Produto ja adicionado mostra quantidade apenas para produto simples.
- Produto indisponivel aparece bloqueado ou nao e acionavel, conforme retorno da API.
- Nome e descricao aceitam ate duas linhas; preco e controle nunca se sobrepoem.

## Produto

A pagina de produto sera um renderizador dirigido por dados do backend.

Formas de venda:

- `single`: preco base do produto.
- `size`: exige variacao ativa antes de adicionar.
- `fixed_weight`: exige variacao ativa antes de adicionar.
- `variable_weight`: usa `weightConfig` com minimo, maximo e incremento.

Grupos e opcoes:

- Ordenar grupos e itens por `displayOrder`.
- Usar `radio` quando `choiceType = single`.
- Usar `checkbox` quando `choiceType = multiple`.
- Respeitar `minChoices` e `maxChoices`.
- Interpretar texto de regra para o cliente: `Escolha 1 opcao`, `Escolha de 1 a 3 opcoes`, `Opcional - escolha ate 3 opcoes`.
- Exibir mensagens de validacao proximas ao grupo.
- Bloquear adicionar enquanto grupos obrigatorios estiverem invalidos.
- Permitir editar item do carrinho abrindo a pagina de produto com o item existente e restaurando variacao, peso, grupos, adicionais, observacoes e quantidade.

O total exibido no detalhe e uma pre-visualizacao local para orientar o cliente; o checkout sempre valida/recalcula no backend.

## Carrinho E Checkout

Carrinho:

- Listar itens com imagem, nome, configuracoes escolhidas, observacao, quantidade e subtotal visual.
- Reutilizar `QuantityControlComponent`.
- Chamar `checkout.preview` para resumo real do servidor.
- Exibir loja fechada, pedido minimo, frete gratis e frete pendente de endereco quando aplicavel.

Cadastro/endereco:

- Manter fluxo atual de cadastro/login automatico quando aplicavel.
- Buscar CEP, permitir revisao manual, validar campos e checar cobertura por bairro.
- Mensagens de erro agrupadas e legiveis.

Pagamento:

- `checkout/pagamento` separa pagar no app e pagar na entrega.
- `checkout/pagar` confirma pedido com metodo online e inicia pagamento via `PaymentService`.
- `checkout/entrega` confirma pedido com dinheiro/cartao na entrega e observacoes de troco/preferencia.

Tracking:

- Buscar pedido por API.
- Atualizar por SignalR quando `OrderStatusUpdated` chegar.
- Manter fallback de refresh manual.

## CSS E Acessibilidade

CSS deve ficar por componente. Evitar `!important`, seletores globais para telas especificas e regras de fonte que igualem todos os elementos.

Regras obrigatorias:

- Mobile-first para 320, 360, 375, 390, 412 px, tablet e desktop.
- Conteudo centralizado no desktop sem scroll horizontal global.
- Apenas categorias podem ter scroll horizontal.
- Toque minimo 44 x 44 px.
- Safe-area em barras fixas/sticky.
- Foco visivel para teclado.
- Cards clicaveis funcionam com Enter e Espaco.
- Inputs reais para radio/checkbox.
- Botoes com nome acessivel.
- Quantidade anunciada com `aria-live`.
- Contraste suficiente em textos secundarios.

Tipografia:

- Uma familia principal: Inter com fallback system UI.
- Pesos preferenciais: 400, 500, 600, 700.
- Nome da loja: 28-32 px, peso 700.
- Categoria: 20-24 px, peso 700.
- Produto no card: 17-18 px, peso 600/700.
- Descricao: 14-16 px, peso 400.
- Preco: 18-20 px, peso 700.
- Usar `clamp()` em titulos responsivos da loja, categorias e produto.

## Testes E Verificacao

Testes unitarios/focados:

- `ProductCardComponent`: produto simples, configuravel, indisponivel, nome longo, quantidade 1 e 10.
- `ProductOptionGroupComponent`: obrigatorio invalido, valido, maximo excedido, single/multiple, item com preco.
- `CartService`: itens com option groups distintos nao devem ser mesclados indevidamente.
- Edicao de item do carrinho: reabre produto com escolhas restauradas e substitui o item correto.
- Paginas de loja: filtro por categoria, busca, estado vazio, preservar estado ao voltar.
- Carrinho/checkout: preview chamado com IDs/selecoes, erro de API exibido.

Verificacao local:

```bash
cd frontend
npx jest --no-coverage
npx ng build
```

Quando houver mudanca backend associada, tambem rodar:

```bash
dotnet build backend/Urbeat.sln
dotnet test backend/tests/Urbeat.UnitTests
```

## Fora Do Escopo Desta Spec

- Criar novas regras de negocio no frontend.
- Implementar meio a meio de pizza se o backend nao fornecer contrato especifico.
- Criar cupons/descontos sem suporte backend.
- Chamar webhooks diretamente pelo navegador.
- Copiar `localStorage` ou dados estaticos do prototipo `Loja/`.

## Definicao De Pronto

- Fluxo completo do cliente remodelado e usando APIs reais.
- Checkout envia somente IDs, quantidades e selecoes; backend recalcula totais.
- Cards, carrinho e footer nao colidem em nenhuma largura alvo.
- Produto configuravel valida variacoes/grupos antes de adicionar.
- Categoria, busca e scroll preservados ao voltar.
- Estados de loading, erro e vazio implementados.
- Layout centralizado no desktop e sem scroll horizontal global.
- Testes focados e build frontend executados sem novas falhas.
