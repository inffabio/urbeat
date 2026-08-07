# 🚀 Especificação Funcional e Técnica — Publicação da Loja

> Documento de referência para implementação no `./backend` e `./frontend` do fluxo **Publicar minha loja**.

---

# 1. 🎯 Objetivo

Definir de forma completa a especificação funcional, técnica, visual e arquitetural da tela de **Publicação da Loja**, incluindo:

- revisão dos dados antes da publicação;
- validação de completude da loja;
- listagem e paginação de produtos;
- resumo operacional da loja;
- prévia da vitrine/cardápio;
- publicação final da loja;
- uso de **Redis** para cache, lock e ganho de performance;
- contratos entre frontend e backend;
- critérios de aceite para implementação por outra IA ou equipe técnica.

---

# 2. 🧭 Contexto funcional da tela

A tela representa a etapa final de onboarding/configuração da loja, composta pelos seguintes blocos:

1. **Dados da loja**
2. **Horários de atendimento**
3. **Bairros atendidos**
4. **Taxas e configurações de entrega**
5. **Produtos cadastrados**
6. **Resumo da sua loja**
7. **Prévia da sua loja**
8. **Ação principal: Publicar minha loja**
9. **Indicação de autosave**
10. **Navegação de etapas no topo**

---

# 3. 👀 Elementos identificados na interface

## 3.1 Navegação de etapas

Etapas apresentadas:

- ✅ Loja
- ✅ Horários
- ✅ Entrega
- ✅ Produtos
- ▶️ Publicar

## 3.2 Indicador de progresso

Existe um indicador visual de:

- **75% concluído**

## 3.3 Conteúdo apresentado na revisão

### 1. Dados da loja

- Logo da loja
- Nome da loja: **Burger House**
- Categoria: **Hamburgueria**
- WhatsApp
- Descrição: **Os melhores burgers da cidade!**
- Endereço
- Cidade / CEP
- Taxa de entrega
- Pedido mínimo

### 2. Horários de atendimento

Horários por dia da semana, por exemplo:

- Segunda a Quinta: `18:00 - 23:00`
- Sexta e Sábado: `18:00 - 00:00`
- Domingo: `18:00 - 23:00`

### 3. Bairros atendidos

Lista resumida de bairros, com exibição parcial e contador adicional:

- Centro
- Flamengo
- Botafogo
- Copacabana
- Laranjeiras
- Glória
- Catete
- Urca
- `+ 2 bairros`

### 4. Taxas e configurações de entrega

- Taxa de entrega: `R$ 5,00`
- Pedido mínimo: `R$ 25,00`
- Tempo médio de entrega: `30-40 min`

### 5. Produtos cadastrados

Resumo por categoria:
- Todos: `15`
- Hambúrgueres: `5`
- Batatas: `3`
- Bebidas: `3`
- Combos: `3`
- Sobremesas: `1`

Exibição parcial de cards:

- X-Burger Bacon
- Batata Frita
- Coca-Cola 350ml
- Combo Clássico
- Brownie com Sorvete
- `+ 10 produtos`

### 6. Resumo da loja

Mostra status:
- **Pronto para publicar**

Checklist:

- Dados da loja: completo
- Horários de atendimento: completo
- Bairros atendidos: completo
- Taxas e entrega: completo
- Produtos cadastrados: 15 produtos
- Configurações: tudo certo

### 7. Prévia da loja

Exibe:

- descrição curta;
- status aberto agora;
- entrega;
- pedido mínimo;
- nome da loja;
- avaliação;
- categorias;
- produtos em destaque;
- CTA de ver cardápio completo.

### 8. Rodapé da ação

- botão **Voltar**
- mensagem **Seus dados são salvos automaticamente**
- botão **Publicar minha loja**

---

# 4. ⚠️ Inconsistência funcional identificada

A interface mostra simultaneamente:

- **75% concluído**
- **Pronto para publicar**
- todas as seções aparentando completas

## Problema

Essas duas informações são conflitantes.

## Decisão obrigatória

O sistema deve possuir **uma única fonte de verdade para progresso e elegibilidade de publicação**, definida no backend.

## Regra recomendada

- Se todas as seções obrigatórias estiverem completas e válidas:
  - `completionPercentage = 100`
  - `canPublish = true`
- Se o progresso for menor que 100:
  - deve haver pelo menos uma pendência real;
  - o frontend deve exibir essas pendências;
  - o botão publicar deve respeitar `canPublish`.

---

# 5. 🧱 Escopo da implementação

## 5.1 Incluído

- tela de revisão antes da publicação;
- consolidação das seções da loja;
- validação funcional e técnica;
- listagem resumida de produtos;
- paginação de produtos;
- paginação de bairros, quando necessário;
- preview da loja;
- resumo do status;
- publicação;
- cache com Redis;
- autosave;
- auditoria básica;
- lock de concorrência na publicação.

## 5.2 Recomendado

- histórico de publicações;
- snapshots de publicação;
- invalidação seletiva de cache;
- warmup de cache após edição;
- idempotência do endpoint de publicação.

## 5.3 Fora de escopo principal, mas possível

- despublicação;
- moderação manual;
- fila para indexação externa;
- replicação de dados para app público.

---

# 6. 👤 Perfis e permissões

## Perfis sugeridos
- `OWNER`
- `MANAGER`
- `EDITOR`
- `VIEWER`

## Regras
### Pode visualizar
- OWNER
- MANAGER
- EDITOR
- VIEWER

### Pode editar
- OWNER
- MANAGER
- EDITOR

### Pode publicar
- OWNER
- MANAGER

### Não pode publicar
- VIEWER
- EDITOR sem permissão explícita

---

# 7. 🧩 Requisitos funcionais por seção

---

# 7.1 Cabeçalho da tela

## Deve conter

- título: **Publicar sua loja 🚀**
- subtítulo explicativo
- progresso percentual
- etapas do fluxo
- identificação visual da etapa atual

## Regras

- progresso calculado no backend;
- frontend apenas renderiza;
- etapas já concluídas devem aparecer com estado visual distinto;
- etapa atual deve estar destacada;
- percentual deve refletir critérios reais de conclusão.

## Estrutura de cálculo recomendada

`text
Dados da loja = 25%
Horários = 20%
Bairros atendidos = 10%
Taxas e entrega = 15%
Produtos cadastrados = 30%
Total = 100%`

---

## Dados da loja

- Campos apresentados
- Logo
- Nome da loja
- Categoria
- WhatsApp
- Descrição
- Endereço
- Cidade / CEP
- Taxa de entrega
- Pedido mínimo

>Requisitos funcionais:

- permitir ação Editar;
- exibir status da seção;
- refletir dados já salvos;
- atualizar progresso após edição;
- mostrar bloqueios e alertas.

> egras obrigatórias:

- nome da loja obrigatório;
- categoria obrigatória;
- WhatsApp obrigatório;
- descrição obrigatória;
- endereço obrigatório;
- cidade obrigatória;
- estado obrigatório;
- CEP obrigatório;
- pedido mínimo obrigatório;
- taxa de entrega obrigatória se houver entrega própria.

> Regras de validação

- nome: 3 a 120 caracteres;
- descrição: 20 a 500 caracteres;
- WhatsApp: formato normalizado;
- CEP válido;
- taxa de entrega: >= 0;
- pedido mínimo: >= 0.

> Regras de negócio

- logo pode ser opcional para salvar rascunho;
- logo pode ser recomendado para publicar, mas não - necessariamente obrigatório;
- se houver geolocalização disponível, armazenar latitude/- longitude

---

## Horários de atendimento

> Objetivo

- Permitir a revisão dos dias e horários em que a loja atende.

### Estrutura recomendada

> Cada dia da semana deve suportar:

- habilitado/desabilitado;
- um ou mais intervalos;
- operação que cruza meia-noite.
- Modelo sugerido
- json
`[
  {
    "dayOfWeek": 1,
    "enabled": true,
    "intervals": [
      { "start": "18:00", "end": "23:00" }
    ]
  }
]`

> Regras obrigatórias

- a loja deve possuir pelo menos um dia com atendimento ativo;
- cada intervalo deve ter start < end, salvo regra explícita - de virada de dia;
- não permitir intervalos sobrepostos no mesmo dia;
- considerar timezone da loja.

### Regra técnica importante

> A loja deve possuir:

- text
- timezone = America/Sao_Paulo
ou outro timezone configurado corretamente

### Regras UX

- exibir horários por dia em formato legível;
- se estiver aberta no momento, preview deve mostrar Aberto  agora;
- cálculo de aberto/fechado deve ser feito no backend ou num  helper compartilhado e confiável

---

## Bairros atendidos

### Objetivo

- Exibir as áreas onde a loja realiza entrega.

### Comportamento observado

- lista resumida de bairros;
- exibição parcial;
- contador de bairros adicionais.

### Requisitos

- permitir ação Editar;
- mostrar apenas uma quantidade resumida no card;
- abrir modal ou página dedicada para visualização completa.

### Regra mínima

- se a loja operar com entrega própria, deve haver pelo menos 1 bairro ativo;
- se operar apenas com retirada, esta regra pode ser dispensável.

> Estrutura sugerida
`json
{
  "id": "area_001",
  "name": "Copacabana",
  "active": true,
  "deliveryFee": 7.5,
  "estimatedTimeMin": 40
}`

> Regras:

- não permitir bairro duplicado por loja;
- suportar taxa específica por bairro;
- suportar tempo adicional por bairro;
- permitir ativo/inativo

---

## Taxas e configurações de entrega

### Campos

- Taxa de entrega
- Pedido mínimo
- Tempo médio de entrega
- Regra de variação conforme distância

### Requisitos

- permitir edição;
- exibir observação sobre variação por distância;
- refletir configurações reais da loja.

> Estrutura sugerida
`json
{
  "deliveryMode": "OWN_DELIVERY",
  "baseFee": 5.00,
  "minimumOrderValue": 25.00,
  "averageDeliveryTimeMin": 35,
  "dynamicFeeEnabled": true,
  "distanceRules": [
    { "maxKm": 3, "fee": 5.0, "etaMin": 30 },
    { "maxKm": 6, "fee": 8.0, "etaMin": 45 }
  ]
}`

> Regras de validação

- taxa base >= 0;
- pedido mínimo >= 0;
- tempo médio > 0;
- faixas de distância não podem se sobrepor;
- faixas devem estar ordenadas

---

## Produtos cadastrados

> Objetivo

**Exibir um resumo dos produtos ativos e do portfólio cadastrado antes da publicação.**

### Elementos identificados

- total geral;
- total por categoria;
- lista parcial de produtos;
- status do produto;
- selo como Mais pedido;
- botão Ver todos;
- contador adicional de produtos ocultos.

### Regras mínimas para publicação

> A loja só pode ser publicada se:

- existir pelo menos 1 produto ativo;
- existir pelo menos 1 categoria com produto visível;
- todos os produtos ativos tiverem:
- nome;
- preço;
- categoria;
- status válido

### Status sugeridos

- ACTIVE
- INACTIVE
- DRAFT
- OUT_OF_STOCK

### Categorias identificadas

- Todos
- Hambúrgueres
- Batatas
- Bebidas
- Combos
- Sobremesas
**Poderão ter centenas de  categorias**

### Regras adicionais

- imagem do produto não é opcional;
- produto sem preço válido não pode ser considerado publicável;
- categoria é obrigatória;
- status é obrigatório

---

### Paginação da grid

- Seção 5 Produtos cadastrados

> Obrigatoriedade:
**Sim, esta seção deve ter paginação.**

### Motivo

- a tela mostra apenas uma amostra;
- há + 10 produtos, o que confirma volume superior ao espaço  disponível;
- carregar tudo em uma única resposta prejudica performance e  escalabilidade

### Estratégia de UX na tela de publicação

- exibir apenas uma preview grid paginada;
- mostrar 5 ou 6 produtos por página;
- manter filtros por categoria;
- manter botão Ver todos para tela dedicada;
- mostrar contador:
- Mostrando 1–5 de 15 produtos

### Estratégia visual da paginação

> Desktop:

- botão Anterior
- páginas numéricas
- elipse ... quando houver muitas páginas
- botão Próxima
- contador de resultados

> Mobile:

- paginação compacta;
- Anterior e Próxima;
- contador central;
- categorias em scroll horizontal

### Estados visuais obrigatórios

- keleton loading;
- estado vazio;
- estado sem resultado de busca;
- estado de erro com retry;
- destaque visual de página ativa

### Regras técnicas

- paginação server-side;
- filtros e busca enviados ao backend;
- ordenação suportada;
- estado refletido na URL na tela dedicada

### Endpoint sugerido

`GET /stores/:storeId/products?page=1&pageSize=5&category=all&status=ACTIVE&sort=popular&search=`

### Resposta sugerida

`{
  "items": [
    {
      "id": "prod_1",
      "name": "X-Burger Bacon",
      "price": 24.90,
      "status": "ACTIVE",
      "category": "BURGERS",
      "isBestSeller": true,
      "imageUrl": "https://cdn/app/prod_1.png"
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 5,
    "totalItems": 15,
    "totalPages": 3,
    "hasPrev": false,
    "hasNext": true
  },
  "filters": {
    "category": "all",
    "status": "ACTIVE",
    "sort": "popular",
    "search": ""
  },
  "counters": {
    "all": 15,
    "burgers": 5,
    "fries": 3,
    "drinks": 3,
    "combos": 3,
    "desserts": 1
  }
}`

### Ordenações sugeridas

- popular
- updated_desc
- name_asc
- price_asc
- price_desc

### Filtros sugeridos

- categoria
- status
- busca por nome
- apenas ativos
- mais pedidos

### Tela dedicada “Ver todos”

> Criar uma tela dedicada para catálogo completo, com:

- paginação completa;
- tabs por categoria;
- busca;
- filtro por status;
- ordenação;
- URL persistente

### Outras partes que merecem paginação

- Bairros atendidos

> Deve ter paginação?

- Sim, em tela/modal dedicada, não necessariamente no card resumo

> Regra recomendada:

- até 24 bairros: lista com busca simples;
- acima de 24: paginação server-side;
- acima de 100: paginação + busca obrigatória + filtros

> Endpoint sugerido

`GET /stores/:storeId/delivery-areas?page=1&pageSize=12&search=&active=true`

### Regras da paginação

- pageSize padrão: 12
- busca por nome
- filtro por ativo/inativo
- ordenação alfabética

### Histórico de publicação

> Se existir histórico de publicações:

- sempre paginar;
- ordenar por data desc;
- permitir ver usuário e resultado da publicação.

---

## Resumo da sua loja

> Objetivo:

- Mostrar um consolidado confiável sobre a prontidão da loja para publicação.

> Regras:

- o resumo deve vir do backend;
- o frontend não deve recalcular isoladamente o estado final;
- deve exibir status geral e checklist por seção.

> Status sugeridos

- READY
- PENDING
- BLOCKED

### Campos necessários

- status geral
- percentual de completude
- canPublish
- checklist por seção
- bloqueios
- alertas
- quantidade de produtos

### Exemplo de payload

`{
  "status": "READY",
  "completionPercentage": 100,
  "canPublish": true,
  "sections": [
    {
      "key": "store_data",
      "label": "Dados da loja",
      "complete": true
    },
    {
      "key": "schedule",
      "label": "Horários de atendimento",
      "complete": true
    },
    {
      "key": "delivery_areas",
      "label": "Bairros atendidos",
      "complete": true
    },
    {
      "key": "delivery_settings",
      "label": "Taxas e entrega",
      "complete": true
    },
    {
      "key": "products",
      "label": "Produtos cadastrados",
      "complete": true,
      "count": 15
    }
  ],
  "warnings": [
    {
      "code": "MISSING_LOGO",
      "message": "Adicionar logo pode melhorar a apresentação da loja."
    }
  ],
  "blockingIssues": []
}`

---

## Prévia da sua loja

> Objetivo:

- Apresentar ao lojista como a loja ficará visível para o cliente.

> Deve conter

- banner ou área visual da marca;
- nome da loja;
- descrição;
- status aberto/agora;
- tempo médio de entrega;
- pedido mínimo;
- categoria;
- categorias/tabs do cardápio;
- amostra de produtos;
- CTA para ver cardápio completo.

> Regras funcionais:

- somente leitura;
- refletir o último estado salvo;
- atualizar após alterações;
- pode usar endpoint consolidado próprio.

### Atenção importante

> Se a loja ainda não possuir avaliações reais:

- não exibir nota fake em produção;
- ou exibir campo vazio;
- ou ocultar o bloco de avaliações até haver dados reais

---

## Publicação da loja

> Regras para permitir publicação:

> A loja só pode ser publicada se:

- dados essenciais da loja estiverem válidos;
- houver pelo menos 1 dia/intervalo de atendimento ativo;
- configurações de entrega estiverem válidas;
- houver pelo menos 1 produto ativo válido;
- não existirem erros bloqueantes;
- o usuário tiver permissão;
- a loja não estiver em publicação concorrente

### Fluxo funcional

1. usuário acessa a tela;
2. sistema carrega resumo, preview e produtos;
3. usuário revisa ou edita;
4. alterações são salvas automaticamente;
5. backend recalcula completude;
6. botão publicar é habilitado apenas com canPublish = true;
7. usuário clica em Publicar minha loja;
8. backend revalida tudo;
9. backend cria lock de publicação;
10. backend publica;
11. backend invalida cache;
12. frontend mostra sucesso.

### Status sugeridos da loja

- DRAFT
- READY_TO_PUBLISH
- PUBLISHING
- PUBLISHED
- PUBLISH_FAILED
- UNPUBLISHED

### Revalidação obrigatória

> O endpoint de publicação deve aceitar:
`Idempotency-Key: <uuid>`
> Objetivo:

- evitar dupla publicação em cliques repetidos;
- evitar inconsistência em múltiplas abas

---

## Regras de negócio consolidadas

- loja sem nome não publica;
- loja sem categoria não publica;
- loja sem descrição não publica;
- loja sem pelo menos 1 produto ativo não publica;
- loja sem horário ativo não publica;
- taxa de entrega não pode ser negativa;
- pedido mínimo não pode ser negativo;
- bairros duplicados não são permitidos;
- produtos ativos sem preço válido bloqueiam publicação;
- progresso deve vir do backend;
- botão publicar só pode estar ativo com canPublish = true;
- a seção de produtos deve ser paginada;
- a tela não deve carregar listas grandes integralmente;
- publicação deve ser protegida por lock.

---

### Modelo de dados sugerido

**stores**
`{
  "id": "store_123",
  "name": "Burger House",
  "slug": "burger-house",
  "category": "HAMBURGUERIA",
  "description": "Os melhores burgers da cidade!",
  "whatsapp": "+5521999999999",
  "logoUrl": "https://cdn/logo.png",
  "bannerUrl": "https://cdn/banner.png",
  "status": "READY_TO_PUBLISH",
  "timezone": "America/Sao_Paulo",
  "publishedAt": null,
  "createdAt": "2026-01-10T10:00:00Z",
  "updatedAt": "2026-01-10T10:00:00Z"
}`

**store_addresses**
`{
  "storeId": "store_123",
  "street": "Rua das Flores",
  "number": "123",
  "complement": null,
  "neighborhood": "Centro",
  "city": "Rio de Janeiro",
  "state": "RJ",
  "zipCode": "20040-010",
  "lat": null,
  "lng": null
}`

**store_schedules**
`{
  "id": "sch_1",
  "storeId": "store_123",
  "dayOfWeek": 1,
  "enabled": true,
  "intervals": [
    { "start": "18:00", "end": "23:00" }
  ]
}`

**delivery_settings**
`{
  "storeId": "store_123",
  "deliveryMode": "OWN_DELIVERY",
  "baseFee": 5.00,
  "minimumOrderValue": 25.00,
  "averageDeliveryTimeMin": 35,
  "dynamicFeeEnabled": true
}`

**delivery_areas**
`{
  "id": "area_1",
  "storeId": "store_123",
  "name": "Copacabana",
  "feeOverride": 7.00,
  "etaOverrideMin": 40,
  "active": true
}`

**products**
`{
  "id": "prod_1",
  "storeId": "store_123",
  "name": "X-Burger Bacon",
  "description": "Hambúrguer artesanal com bacon e queijo",
  "category": "BURGERS",
  "price": 24.90,
  "status": "ACTIVE",
  "imageUrl": "https://cdn/product.png",
  "isBestSeller": true,
  "createdAt": "2026-01-10T10:00:00Z",
  "updatedAt": "2026-01-10T10:00:00Z"
}`

**store_publication_snapshots**
`{
  "id": "snap_1",
  "storeId": "store_123",
  "version": 1,
  "payload": {},
  "publishedBy": "user_1",
  "publishedAt": "2026-01-10T12:00:00Z"
}`

**store_publication_history**
`{
  "id": "hist_1",
  "storeId": "store_123",
  "action": "PUBLISHED",
  "statusBefore": "READY_TO_PUBLISH",
  "statusAfter": "PUBLISHED",
  "performedBy": "user_1",
  "createdAt": "2026-01-10T12:00:00Z"
}`

---

## Estratégia de cache com Redis

> Objetivos:

> Usar Redis para:

- reduzir latência;
- diminuir consultas repetidas ao banco;
- acelerar o carregamento da tela;
- suportar locks de publicação;
- garantir melhor experiência em grids e previews

### stratégia principal

**Usar padrão Cache Aside.**

> Fluxo:

- verifica no Redis;
- se houver cache hit, retorna;
- se houver miss, busca no banco;
- monta payload;
- grava no Redis;
- retorna resposta

---

## Chaves de cache recomendadas

### Resumo da publicação

`store:{storeId}:publication:summary`

> TTL:

- 60s a 180s

### Elegibilidade de publicação

`store:{storeId}:publication:eligibility`

> TTL:

- 30s a 60s

### Preview da loja

`store:{storeId}:preview`

> TTL:

- 120s a 300s

### Produtos paginados

`store:{storeId}:products:page:{page}:size:{size}:category:{category}:status:{status}:sort:{sort}:search:{hash}store:{storeId}:products:page:{page}:size:{size}:category:{category}:status:{status}:sort:{sort}:search:{hash}`

> TTL:

- 60s a 180s

### Contadores de produtos

`store:{storeId}:products:counters`

> TTL:

- 300s

### Bairros paginados

`store:{storeId}:delivery-areas:page:{page}:size:{size}:search:{hash}:active:{active}`

> TTL:

- 300s

### Lock de publicação

`lock:store:{storeId}:publish`

> TTL:

- 15s a 60s

### Idempotência da publicação

`idempotency:store:{storeId}:publish:{key}`

> TTL:

- 24h

---

## Invalidação de cache

> Deve invalidar quando houver mudança em:

- dados da loja;
- horários;
- bairros;
- taxas e entrega;
- produtos;
- status da loja;
- publicação concluída;
- despublicação;
- alteração de preview

> Chaves a invalidar:

- store:{storeId}:publication:summary
- store:{storeId}:publication:eligibility
- store:{storeId}:preview
- store:{storeId}:products:*
- store:{storeId}:products:counters
- store:{storeId}:delivery-areas:*

### Helpers recomendados

- invalidateStorePublicationCache(storeId)
- invalidateStoreProductsCache(storeId)
- invalidateStoreDeliveryCache(storeId)
- invalidateStorePreviewCache(storeId)

---

## Lock e concorrência com Redis

> Problema:
**Usuário pode clicar várias vezes em Publicar minha loja ou abrir duas abas**

> Solução:
**Usar lock distribuído com Redis:**

`SETNX lock:store:{storeId}:publish "1" EX 30`

### Fluxo

- se lock não existir: cria e continua;
- se lock existir: retornar erro de conflito;
- ao concluir: remover lock;
- se houver falha inesperada: lock expira por TTL

### Resposta sugerida em caso de lock

`{
  "success": false,
  "code": "PUBLISH_IN_PROGRESS",
  "message": "Já existe uma publicação em andamento para esta loja."
}`

---

## Especificação frontend

> Rota principal
`/stores/:storeId/publish`

### Componentes sugeridos

- PublishStorePage
- PublishStepper
- PublishHeader
- CompletionProgress
- StoreDataCard
- ScheduleCard
- DeliveryAreasCard
- DeliverySettingsCard
- ProductsCard
- ProductsGrid
- ProductsPagination
- PublicationSummaryCard
- StorePreviewCard
- PublishActionsBar
- PublishButton
- AutosaveIndicator
- ValidationAlertList
- DeliveryAreasModal
- ProductsFullListDrawer ou página dedicada

---

## Observabilidade

- tempo de resposta do resumo;
- tempo de resposta da listagem de produtos;
- tempo de resposta da preview;
- taxa de cache hit/miss Redis;
- taxa de falha na publicação;
- quantidade de publicações por loja;
- tempo médio até publicação concluída

## Logs

- requestId
- storeId
- userId
- statusBefore
- statusAfter
- idempotencyKey
- cacheHit
- lockAcquired

## Tracing

> Fluxo recomendado:

- controller
- service
- redis
- repository
- db

## Segurança

> Regras:

- autenticação obrigatória;
- autorização por papel;
- validação do storeId no escopo do usuário;
- sanitização de entrada;
- rate limit para publicação;
- lock com Redis;
- idempotência;
- trilha de auditoria

## Auditoria mínima

> Registrar:

- quem publicou;
- quando publicou;
- estado anterior;
- estado resultante;
- payload resumido;
- erros de validação.

## Casos de borda

- loja sem produtos;
- loja com produtos todos inativos;
- loja sem horários ativos;
- bairro duplicado;
- taxa negativa;
- pedido mínimo negativo;
- edição em duas abas;
- publicação simultânea;
- falha do Redis;
- falha parcial entre publicação e invalidação de cache;
- página de produtos com filtro levando a página vazia;
- alteração de categoria que reduz número de páginas;
- preview com dados desatualizados após edição recente

## Regras para paginação em caso de filtro

> Se o usuário estiver em uma página inválida após trocar filtros:

- resetar automaticamente para página 1

---

## Implementação altamente recomendada

- histórico de publicação;
- snapshot da loja no momento da publicação;
- warmup de cache;
- logs e métricas detalhados
