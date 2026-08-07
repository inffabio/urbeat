# Especificação Funcional e Visual — Tela 8: Acompanhamento do Pedido da Urbeat

## Projeto

- **Stack alvo:** Angular 20 + Ionic
- **Visão do software:** Existe uma empresa chamada Urbeat que controla todos os clientes vendedores que farão cadastro e cada um terá sua página de venda.
- **Objetivo:** Criar a tela de acompanhamento do pedido, responsável por confirmar o envio do pedido e permitir ao cliente acompanhar em tempo real o andamento da preparação até a entrega.

---

## Visão Geral da Tela

> Esta tela representa a etapa de confirmação e acompanhamento do pedido no fluxo de delivery mobile do cliente da Urbeat.

> Ela tem como principais objetivos:

- ✅ Confirmar visualmente que o pedido foi enviado com sucesso
- 🧾 Exibir resumo rápido do pedido
- ⏱️ Exibir previsão de entrega
- 📍 Exibir detalhes da entrega
- 🚚 Exibir acompanhamento em tempo real do status do pedido
- 🔄 Atualizar o andamento do pedido até a entrega final
- 💬 Oferecer canal de ajuda ao cliente
- 🏠 Permitir voltar para o início
- 📦 Permitir abrir a tela completa de acompanhamento do pedido

---

## Estrutura Geral da Tela

> A tela pode ser dividida em 7 blocos principais:

- Header de confirmação
- Card de resumo do pedido
- Bloco de previsão de entrega
- Linha do tempo de acompanhamento
- Detalhes da entrega
- Ações de ajuda e acompanhamento
- Ação secundária para voltar ao início

---

## Estrutura Visual por Seções

> Header de Confirmação

- **Função:** Confirmar que o pedido foi recebido com sucesso e está em preparação
- **Elementos identificados:**
  - Ícone de sucesso com check verde em destaque
  - Título: `Pedido enviado!`
  - Subtítulo com mensagem de confirmação do recebimento e início da preparação

> Comportamento esperado

- O header deve transmitir segurança e sucesso na ação finalizada
- O ícone de sucesso deve possuir destaque visual central
- A mensagem deve deixar claro que o pedido foi recebido corretamente
- O conteúdo deve ser objetivo, amigável e fácil de entender

---

> Resumo do Pedido

- **Função:** Exibir um resumo rápido do pedido recém-enviado

### Elementos identificados

- Número do pedido
- Ícone de sacola/pedido
- Quantidade de itens
- Valor total do pedido

### Comportamento esperado

- Deve exibir o identificador do pedido de forma clara
- Deve exibir a quantidade total de itens
- Deve exibir o valor final pago ou a pagar
- O card deve ser compacto, legível e escaneável rapidamente

### Padronização visual

- Card com fundo branco
- Número do pedido em destaque moderado
- Ícone à esquerda
- Valor total alinhado à direita
- Bordas suaves e espaçamento confortável

---

> Previsão de Entrega

- **Função:** Informar ao cliente o intervalo estimado para recebimento do pedido

### Elementos identificados

- Ícone de relógio
- Label: `Previsão de entrega`
- Texto de destaque com data/período estimado
  - Exemplo: `Hoje, entre 18:20 e 18:50`

### Comportamento esperado

- A previsão deve ser exibida com destaque visual
- A informação deve vir do backend ou do motor logístico
- O horário deve poder ser atualizado conforme mudança operacional
- O texto deve ser de fácil leitura e localizado no padrão do app

---

> Acompanhe seu Pedido

- **Função:** Exibir a evolução do pedido em tempo real por etapas

### Título da seção

- `Acompanhe seu pedido`

### Etapas identificadas

- `Pedido recebido`
- `Preparando seu pedido`
- `Saiu para entrega`
- `Entregue`

### Comportamento esperado

- A timeline deve mostrar as etapas em ordem cronológica
- Apenas a etapa atual deve aparecer como ativa
- Etapas anteriores podem aparecer como concluídas
- Etapas futuras devem aparecer como inativas
- Cada etapa pode exibir horário quando disponível
- O status deve ser atualizado em tempo real ou por polling/refresh automático

### Regras funcionais

- O status inicial deve ser `Pedido recebido`
- Quando a cozinha iniciar produção, mudar para `Preparando seu pedido`
- Quando o entregador sair, mudar para `Saiu para entrega`
- Quando o pedido for finalizado, mudar para `Entregue`
- As mudanças devem refletir no front sem necessidade de novo pedido manual sempre que possível

### Mensagem auxiliar

- Abaixo da timeline deve existir uma mensagem informativa indicando que o cliente receberá atualização quando o pedido sair para entrega

### Padronização visual

- Linha horizontal ou progress tracker
- Etapa ativa em laranja/coral
- Etapas inativas em cinza
- Etapas concluídas podem usar laranja/coral ou verde, conforme padrão definido
- Ícones/pontos bem visíveis e conectados visualmente

---

> Detalhes da Entrega

- **Função:** Exibir os principais dados logísticos e financeiros do pedido

### Blocos identificados

- `Endereço de entrega`
- `Tipo`
- `Pagamento`

### Conteúdo esperado

- **Endereço de entrega**
  - Rua, número, bairro, cidade e estado
- **Tipo**
  - Exemplo: `Entrega`
  - Pode futuramente suportar `Retirada no local`
- **Pagamento**
  - Exemplo: `Pagar na entrega • Dinheiro`
  - Deve refletir a escolha feita no checkout

### Comportamento esperado

- Os dados devem vir das etapas anteriores do fluxo
- As informações devem ser somente leitura nesta tela
- O conteúdo deve ser legível, organizado e objetivo

---

> Ajuda

- **Função:** Oferecer suporte ao cliente caso haja dúvidas ou problemas com o pedido

### Elementos identificados

- Texto: `Precisa de ajuda?`
- Seta indicativa de ação
- Possibilidade de abrir chat ou canal de suporte

### Comportamento esperado

- Ao tocar, deve abrir:
  - chat online
  - WhatsApp
  - Tawk.to
  - ou central de ajuda da loja, conforme configuração
- O canal de ajuda deve ser configurável por loja

---

> Ações Finais

### Botão principal

- **Elemento identificado:** Botão largo com texto `ACOMPANHAR PEDIDO`
- **Função:** Abrir a visualização detalhada e atualizável do pedido em andamento

### Comportamento esperado

- Deve ser a ação principal da tela
- Pode levar para:
  - tela detalhada de rastreio
  - atualização expandida do pedido
  - acompanhamento com auto refresh

---

### Ação secundária

- **Elemento identificado:** Texto ou botão `Voltar para o início`
- **Função:** Permitir retornar para a tela inicial da loja

### Comportamento esperado

- Não deve apagar histórico do pedido
- Deve manter a possibilidade de o cliente voltar a acompanhar o pedido depois
- Deve possuir estilo secundário

---

## Especificação Visual

### Paleta de Cores

- **Laranja/Coral:** usado em botões principais, etapa ativa da timeline, links e destaque de informações importantes
- **Verde:** usado no ícone de sucesso e, opcionalmente, em etapas concluídas
- **Branco:** cards e superfícies principais
- **Bege/Creme muito claro:** fundo geral, mantendo consistência com o app
- **Cinza escuro / preto:** textos principais
- **Cinza médio/claro:** descrições, divisores, linhas da timeline e estados inativos

<  --app-primary: #f57c52;
   --app-primary-dark: #e5673f;
   --app-accent-green: #2e7d32;
   --app-bg: #f7f1ea;
   --app-surface: #ffffff;
   --app-text-primary: #222222;
   --app-text-secondary: #6b6b6b;
   --app-border-light: #ececec; />

---

### Tipografia

- **Hierarquia recomendada**
  - Título de sucesso: destaque, semibold/bold
  - Subtítulo: regular
  - Títulos de seção: semibold
  - Dados do pedido: regular ou semibold leve
  - Valor total: semibold/bold
  - Status atual: destaque visual na timeline
  - Texto do botão principal: bold

- **Nome fonte**
  - Google Fonts Nunito Sans

---

### Bordas e Formas

- Layout moderno, amigável e arredondado
- Cards com cantos suaves
- Botões com bordas arredondadas
- Ícone de sucesso central com aparência amigável
- Timeline com pontos circulares e conectores discretos

< --radius-sm: 8px;
  --radius-md: 12px;
  --radius-lg: 16px;
  --radius-full: 999px; />

---

### Espaçamentos

<  
--space-1: 4px;
--space-2: 8px;
--space-3: 12px;
--space-4: 16px;
--space-5: 20px;
--space-6: 24px; >

- Espaçamento confortável entre cards e seções
- Boa separação entre timeline e detalhes da entrega
- Respiro visual entre ajuda e ações finais
- Área inferior suficiente para não conflitar com safe area

---

## Sugestões de Imagens e Ícones

> Sugestões visuais para reforçar clareza, status e leitura rápida da tela:

### Confirmação do pedido

- Ícone principal sugerido:
  - círculo com check
  - selo de sucesso
- Sugestão Ionic:
  - `checkmark-circle`
- Diretriz:
  - usar cor verde
  - manter o ícone centralizado e com destaque

### Resumo do pedido

- Ícone sugerido:
  - `bag-handle-outline`
  - `receipt-outline`
  - ou sacola minimalista customizada

### Previsão de entrega

- Ícone sugerido:
  - `time-outline`
  - `alarm-outline`

### Timeline do pedido

- Ícones ou marcadores sugeridos por etapa:
  - `radio-button-on` para etapa ativa
  - `checkmark-circle-outline` para etapa concluída
  - `ellipse-outline` para etapa futura
- Alternativa:
  - dots customizados conectados por linha horizontal

### Detalhes da entrega

- **Endereço**
  - `location-outline`
- **Tipo de entrega**
  - `bicycle-outline`, `car-outline` ou `rocket-outline`
  - caso queira algo mais neutro: `cube-outline`
- **Pagamento**
  - `wallet-outline`
  - `cash-outline`
  - `card-outline`, conforme método

### Ajuda

- Ícone sugerido:
  - `help-circle-outline`
  - `chatbubble-ellipses-outline`
  - `headset-outline`

### Botão principal

- Ícone opcional:
  - `navigate-outline`
  - `sync-outline`
  - `trail-sign-outline`

### Diretriz importante

- Os ícones devem manter consistência com as telas anteriores
- O ícone de sucesso deve ser o principal elemento visual do topo
- O status atual do pedido deve ser facilmente identificável em poucos segundos

---

## Padronização de Cards e Botões

> Card de resumo do pedido

- Fundo branco
- Cantos arredondados
- Ícone à esquerda
- Informações principais organizadas em coluna
- Valor total com destaque à direita
- Estrutura compacta e objetiva

---

> Card de previsão

- Fundo branco
- Borda leve ou sem borda forte
- Ícone de relógio
- Texto principal em laranja/coral
- Informação resumida e de fácil leitura

---

> Card de detalhes da entrega

- Fundo branco
- Ícones por linha
- Conteúdo dividido em blocos
- Labels discretas
- Valores legíveis

---

> Bloco de timeline

- Fundo branco ou transparente, conforme padrão visual
- Etapas conectadas visualmente
- Estado ativo destacado
- Estado futuro neutro
- Estado concluído visualmente reconhecível

---

> Botão primário

- **Uso:**
  - Acompanhar pedido
  - Abrir rastreamento detalhado

- **Padrão:**
  - Fundo laranja/coral
  - Texto branco
  - Bordas arredondadas
  - Largura ampla
  - Deve ser o principal destaque da área inferior

---

> Botão secundário

- **Uso:**
  - Voltar para o início
  - Links auxiliares

- **Padrão:**
  - Sem preenchimento forte
  - Texto em laranja/coral
  - Visual leve

---

## Componentização Recomendada

> Para Angular 20 + Ionic, recomenda-se quebrar a tela em componentes reutilizáveis.

---

## Componentes sugeridos

- **order-success-header.component**
  > Responsável por:
  - ícone de sucesso
  - título
  - subtítulo

---

- **order-resume-card.component**
  > Responsável por:
  - número do pedido
  - quantidade de itens
  - valor total

---

- **delivery-estimate-card.component**
  > Responsável por:
  - label de previsão
  - horário estimado

---

- **order-tracking-timeline.component**
  > Responsável por:
  - exibir etapas do pedido
  - destacar status atual
  - renderizar horários, quando disponíveis

---

- **delivery-details-card.component**
  > Responsável por:
  - endereço
  - tipo de entrega
  - forma de pagamento

---

- **order-support-entry.component**
  > Responsável por:
  - ação de ajuda
  - navegação para suporte

---

- **order-tracking-actions.component**
  > Responsável por:
  - botão acompanhar pedido
  - voltar para o início

---

## Regras Funcionais

### Exibição da tela

> Esta tela deve ser exibida após a finalização bem-sucedida do pedido

- Deve confirmar visualmente que o pedido foi recebido
- Deve exibir imediatamente os dados principais do pedido
- Deve estar conectada ao status real do pedido no backend

---

### Acompanhamento em tempo real

> O andamento do pedido deve refletir as atualizações reais do sistema operacional da loja

### Status mínimos previstos

- `pedido_recebido`
- `preparando`
- `saiu_para_entrega`
- `entregue`

### Regras funcionais

- O front deve buscar o status atual do pedido ao abrir a tela
- O status deve poder ser atualizado em tempo real por:
  - polling periódico
  - refresh manual
  - websocket / tempo real, se disponível
- A timeline deve sempre refletir o status mais recente
- Horários das etapas devem ser exibidos quando existirem

---

### Resumo do pedido

- Deve refletir os dados finais salvos no sistema
- Deve exibir número do pedido, quantidade total de itens e valor final
- Não deve permitir edição nesta etapa

---

### Previsão de entrega

- Deve vir do backend
- Pode ser atualizada ao longo do processo
- Deve aceitar janela de entrega, e não apenas horário fixo

---

### Detalhes da entrega

- Devem refletir os dados escolhidos no checkout
- Devem incluir:
  - endereço final
  - tipo de recebimento
  - forma de pagamento selecionada

---

### Ajuda

- O bloco `Precisa de ajuda?` deve abrir o canal configurado da loja
- O canal pode ser:
  - WhatsApp
  - Tawk.to
  - chat interno
  - central de atendimento

---

### Navegação

- `ACOMPANHAR PEDIDO` deve abrir a visualização detalhada ou manter o acompanhamento expandido
- `Voltar para o início` deve redirecionar para a home da loja
- O pedido deve continuar acessível posteriormente por histórico ou link de acompanhamento

---

## Acessibilidade

> Requisitos recomendados:

- Botões com `aria-label`
- Contraste adequado entre texto e fundo
- Área de toque mínima de 44x44px
- Timeline com indicação visual clara do status atual
- Ícones com significado claro
- Informações de pedido e entrega com boa legibilidade
- Ordem de navegação consistente

> Exemplos

<aria-label="Pedido enviado com sucesso"
aria-label="Resumo do pedido"
aria-label="Previsão de entrega"
aria-label="Acompanhar andamento do pedido"
aria-label="Detalhes da entrega"
aria-label="Abrir ajuda"
aria-label="Acompanhar pedido"
aria-label="Voltar para o início" />

---

## Responsividade e Comportamento Mobile

**Como a tela é claramente mobile-first:**

### Requisitos

- Layout otimizado para smartphones
- Scroll vertical fluido
- Cards fáceis de ler e tocar
- Timeline legível em telas pequenas
- Botão principal com forte destaque visual
- Compatível com Android/iOS
- Respeitar `ion-safe-area`
- Ajustar espaçamento inferior para não conflitar com rodapé fixo ou safe area

---

## Critérios de Aceite

### Funcionais ✅

- Exibir ícone de sucesso com título e subtítulo de confirmação
- Exibir resumo do pedido com número, quantidade de itens e valor total
- Exibir previsão de entrega
- Exibir timeline com as etapas do pedido
- Destacar o status atual do pedido
- Atualizar o status em tempo real ou por atualização automática
- Exibir detalhes da entrega com endereço, tipo e pagamento
- Exibir ação de ajuda
- Permitir abrir acompanhamento detalhado do pedido
- Permitir voltar para o início

---

### Visuais 🎨

- Manter identidade visual das telas anteriores
- Usar laranja/coral como cor principal
- Usar verde no feedback de sucesso
- Manter fundo claro e cards brancos
- Destacar visualmente a etapa atual da timeline
- Garantir leitura clara da previsão de entrega e valor total

---

### Técnicos ⚙️

- Desenvolvido em Angular 20 + Ionic
- Componentização clara
- Dados preparados para integração via API/backend
- Código reutilizável e escalável
- Compatível com Android e iOS
- Estrutura preparada para atualização em tempo real

---

## Implementação Técnica

### Angular 20

- Preferir componentes standalone
- Usar signals ou RxJS para gerenciamento simples de estado
- Estrutura preparada para atualização reativa do status do pedido

### Ionic

- ion-content
- ion-button
- ion-icon
- ion-card ou estrutura customizada
- ion-grid, se necessário
- ion-refresher, opcional
- integração futura com polling ou websocket

### Estilo

- SCSS modular
- Tokens de cor no tema global
- Componentes isolados por responsabilidade

---

## Resumo Executivo

> A Tela 8 deve funcionar como a etapa de confirmação e acompanhamento em tempo real do pedido, garantindo clareza, segurança e visibilidade do andamento da entrega.

### Resultado esperado

- O usuário entende imediatamente que o pedido foi enviado com sucesso
- O andamento do pedido pode ser acompanhado com clareza
- A previsão de entrega e os detalhes logísticos ficam visíveis
- O cliente consegue buscar ajuda rapidamente se necessário
- O fluxo mantém transparência até a entrega final

## APIs do Backend

### 1. Detalhes do Pedido

```http
GET /api/orders/{orderId}
```
**Autenticado** (`[CustomerOnly]`).

**Response:**
```json
{
  "id": "guid...",
  "code": "HAP-X7K9M2P1",
  "storeId": "guid...",
  "fulfillmentType": 1,
  "status": 3,
  "paymentMethod": 3,
  "subtotal": 63.70,
  "deliveryFee": 5.90,
  "total": 69.60,
  "createdAtUtc": "2026-05-27T12:00:00Z",
  "addressStreet": "Rua Augusta",
  "addressNumber": "1500",
  "addressNeighborhood": "Consolação",
  "addressCity": "São Paulo",
  "addressState": "SP",
  "addressComplement": "Apto 42",
  "notes": "Sem cebola",
  "items": [
    {
      "productName": "Smash Burguer",
      "quantity": 2,
      "unitPrice": 28.90,
      "totalPrice": 57.80
    }
  ],
  "history": [
    {
      "createdAtUtc": "2026-05-27T12:00:00Z",
      "previousStatus": 1,
      "newStatus": 3,
      "changedByUserId": "guid...",
      "notes": "Initial order status"
    }
  ]
}
```

> Os campos `history` contêm a **linha do tempo completa** do pedido — cada transição de status com horário.\
> `address*` campos serão `null` para retirada (`fulfillmentType = 2`).

### 2. Listar Pedidos do Cliente

```http
GET /api/orders/my
```
**Autenticado** (`[CustomerOnly]`).

**Response 200:**
```json
[
  {
    "id": "guid...",
    "code": "HAP-X7K9M2P1",
    "storeId": "guid...",
    "status": 4,
    "total": 69.60,
    "createdAtUtc": "2026-05-27T12:00:00Z"
  }
]
```

### 3. Atualização em Tempo Real (Polling)

Como o backend não possui WebSocket, recomenda-se **polling**:

```typescript
// Exemplo: polling a cada 10 segundos
setInterval(() => {
  this.http.get(`/api/orders/${orderId}`).subscribe(order => {
    this.updateTimeline(order.status, order.history);
  });
}, 10000);
```

> Opcionalmente, usar `ion-refresher` para refresh manual.

### 4. Mapeamento de Status para Timeline

| Status (enum) | Label na Timeline | Ícone |
|--------------|-------------------|-------|
| `2` PendingPayment | Aguardando pagamento | ⏳ |
| `3` Received | Pedido recebido ✅ | checkmark |
| `4` Preparing | Preparando seu pedido 🔄 | time |
| `5` Ready | Pronto para entrega 📦 | cafe |
| `6` OnDelivery | Saiu para entrega 🚚 | bicycle |
| `7` Delivered | Entregue 🎉 | checkmark-circle |

> Para pagamento na entrega (CashOnDelivery/CardOnDelivery), o status inicial é `3` (Received), pulando o `PendingPayment`.

### Fluxo de Dados na Tela
1. Ao abrir a tela, chamar `GET /api/orders/{orderId}` com o `orderId` recebido
2. Preencher:
   - **Header de sucesso:** se status >= 3
   - **Resumo:** `code`, `items.length`, `total`
   - **Previsão:** usar horário do `history` ou campo futuro
   - **Timeline:** `history[]` → converter cada entrada em etapa visual
   - **Detalhes da entrega:** `address*` campos, `fulfillmentType`, `paymentMethod`
3. Iniciar polling de `10s` para atualizar status
4. Quando `status` mudar, animar transição na timeline

### Enums
| Campo | Valores |
|-------|---------|
| `OrderStatus: 1` | Created |
| `OrderStatus: 2` | PendingPayment |
| `OrderStatus: 3` | Received |
| `OrderStatus: 4` | Preparing |
| `OrderStatus: 5` | Ready |
| `OrderStatus: 6` | OnDelivery |
| `OrderStatus: 7` | Delivered |
| `OrderStatus: 8` | Cancelled |
| `FulfillmentType: 1` | Delivery |
| `FulfillmentType: 2` | PickUp |
| `PaymentMethod: 3` | CashOnDelivery |
| `PaymentMethod: 4` | CardOnDelivery |


