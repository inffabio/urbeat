# Especificação Funcional e Visual — Tela 6: Efetivar Pagamento pelo App da Urbeat

## Projeto

- **Stack alvo:** Angular 20 + Ionic
- **Visão do software:** Existe uma empresa chamada Urbeat que controla todos os clientes vendedores que farão cadastro e cada um terá sua página de venda.
- **Objetivo:** Criar a tela de efetivação do pagamento pelo app, responsável por permitir ao cliente escolher o método final de pagamento digital e concluir a cobrança com segurança.

---

## Visão Geral da Tela

> Esta tela representa a etapa de efetivação do pagamento no fluxo de delivery mobile do cliente da Urbeat.

> Ela tem como principais objetivos:

- 💳 Permitir escolher o método final de pagamento pelo app
- 🟦 Permitir pagamento via **Mercado Pago**
- ⚡ Permitir pagamento via **Pix**
- 🧾 Exibir resumo do pedido
- 💰 Exibir resumo financeiro final
- 🔒 Reforçar percepção de pagamento seguro
- ✅ Permitir concluir o pagamento
- ↩️ Permitir voltar ao cardápio sem perder o carrinho, enquanto o pedido não for finalizado

---

## Estrutura Geral da Tela

> A tela pode ser dividida em 7 blocos principais:

- Header de navegação
- Resumo do pedido
- Seleção do método de pagamento
- Bloco complementar do método selecionado
- Resumo do pagamento
- Botão principal para pagar
- Ação secundária para voltar ao cardápio

---

## Estrutura Visual por Seções

> Header

- **Função:** Exibir a etapa atual do fluxo e reforçar que o pagamento será feito de forma rápida e segura
- **Elementos identificados:**
  - Botão voltar à esquerda
  - Título: `Pagar pelo app`
  - Subtítulo: `Pagamento rápido e seguro`

> Comportamento esperado

- O botão voltar deve retornar para a tela anterior do checkout
- O título deve indicar com clareza que esta é a etapa final do pagamento digital
- O subtítulo deve reforçar confiança e segurança
- O header deve manter consistência visual com as telas anteriores

---

> Resumo do Pedido

- **Função:** Exibir um resumo rápido do pedido antes da cobrança

### Elementos identificados

- Ícone de sacola/pedido
- Quantidade de itens
- Número do pedido
- Valor total resumido
- Ação `Ver detalhes`

### Comportamento esperado

- Deve exibir quantidade total de itens
- Deve exibir identificador do pedido, quando já existir
- Deve exibir valor total resumido à direita com destaque
- Ao tocar em `Ver detalhes`, pode:
  - expandir os itens
  - abrir modal
  - navegar para uma visualização resumida do carrinho

### Padronização visual

- Card com fundo branco
- Ícone à esquerda
- Informações centrais em coluna
- Valor total alinhado à direita
- Link `Ver detalhes` em laranja/coral

---

> Escolha do Método de Pagamento

- **Função:** Permitir ao usuário escolher como deseja pagar dentro do app

### Título da seção

- `Escolha o método de pagamento`

### Opções previstas

- `Mercado Pago`
- `Pix`

### Conteúdo mínimo por opção

- Ícone ou logo oficial
- Título
- Descrição curta
- Indicador visual de seleção
- Área clicável completa

### Comportamento esperado

- Apenas uma opção pode ficar ativa por vez
- A opção selecionada deve ter maior destaque visual
- Ao selecionar `Mercado Pago`:
  - exibir bloco complementar do Mercado Pago
  - preparar fluxo externo ou embutido de checkout
- Ao selecionar `Pix`:
  - preparar fluxo de geração de QR Code e/ou código copia e cola na próxima etapa ou em modal

### Padronização visual

- Card clicável
- Borda neutra no estado padrão
- Borda laranja/coral no estado ativo
- Radio button ou indicador de seleção à direita
- Descrição em texto secundário
- Área de toque confortável

---

> Método 1 — Mercado Pago

- **Função:** Oferecer pagamento via gateway Mercado Pago

### Conteúdo esperado

- Logo oficial do **Mercado Pago**
- Título: `Mercado Pago`
- Texto auxiliar sugerido:
  - `Pague com segurança usando os meios disponíveis no Mercado Pago`
  - ou `Finalize com cartão, saldo ou opções disponíveis no checkout Mercado Pago`


### Comportamento esperado

- Ao selecionar esta opção, o sistema deve preparar a integração com o Mercado Pago
- O cliente pode ser direcionado para:
  - redirecionamento controlado
- Buscar no arquivo (../backend/API.md) a api correta para gerar o pagamento pelo mercao pago.
- A finalização deve retornar status de sucesso, falha ou cancelamento
- O pedido só deve ser confirmado após retorno positivo do pagamento

### Regra importante

- Esta opção **substitui** a antiga opção `Cartão de crédito`
- Deve utilizar a **imagem/logo oficial do Mercado Pago**
- Não considerar a opção `Carteira Digital` nesta tela

---

> Método 2 — Pix

- **Função:** Oferecer pagamento instantâneo

### Conteúdo esperado

- Ícone oficial do **Pix**
- Título: `Pix`
- Texto auxiliar sugerido:
  - `Pagamento aprovado na hora`
  - `Escaneie o QR Code ou copie o código Pix`

### Comportamento esperado

- Ao selecionar `Pix`, o sistema deve preparar a cobrança Pix
- Deve abrir próxima etapa com:
  - QR Code
  - código copia e cola
  - temporizador de expiração
- O pedido deve aguardar confirmação do pagamento
- O status do pagamento deve ser atualizado automaticamente ou por consulta manual
- Buscar no arquivo (../backend/API.md) a api correta para gerar o pix.

---

> Bloco Complementar do Método Selecionado

- **Função:** Exibir informações adicionais do método ativo

### Para Mercado Pago

- Exibir card complementar com:
  - logo Mercado Pago
  - texto de segurança
  - indicação de redirecionamento ou finalização segura
- Exemplo de texto:
  - `Você será direcionado para concluir o pagamento com segurança pelo Mercado Pago`

### Para Pix

- Exibir card complementar com:
  - ícone Pix
  - texto curto informando aprovação rápida
- Exemplo de texto:
  - `Após continuar, o QR Code Pix será gerado para pagamento imediato`

### Regra funcional

- Apenas o conteúdo do método ativo deve aparecer
- Se nenhum método estiver selecionado, o bloco complementar pode permanecer oculto

---

> Resumo do Pagamento

- **Função:** Exibir a composição do valor final cobrado

### Campos obrigatórios

- `Subtotal`
- `Taxa de entrega`
- `Descontos`
- `Total`

### Regras funcionais

- **Subtotal:** soma dos itens do carrinho
- **Taxa de entrega:** definida na etapa anterior
- **Descontos:** valor aplicado por cupom ou campanha
- **Total:** subtotal + taxa - descontos

### Comportamento esperado

- Os valores devem refletir exatamente o pedido atual
- O campo `Total` deve possuir o maior destaque visual da seção
- O campo `Descontos` pode ser exibido em verde quando houver abatimento

---

> Botão Principal

- **Elemento identificado:** Botão largo com ícone de cadeado e texto `PAGAR` + Total
- **Função:** Iniciar a cobrança do método selecionado

### Comportamento esperado

- Deve ser a ação de maior destaque da tela
- Deve exibir o valor final já embutido no texto do botão
- Deve validar se há um método de pagamento selecionado
- Deve entrar em loading enquanto o pagamento estiver sendo processado
- Deve evitar múltiplos cliques durante a transação
- Em caso de sucesso, deve avançar para a confirmação final
- Em caso de falha, deve exibir mensagem clara e permitir nova tentativa

### Padrão de segurança

- Ícone de cadeado para reforçar pagamento seguro
- Texto claro e direto
- Feedback visual de processamento obrigatório

---

> Ação Secundária

- **Elemento identificado:** Texto ou botão `Voltar ao cardápio`
- **Função:** Permitir ao usuário sair do checkout sem perder o carrinho, desde que o pedido ainda não tenha sido concluído

### Comportamento esperado

- Não deve apagar os itens do carrinho
- Deve possuir estilo secundário
- Não deve competir visualmente com o botão principal

---

## Especificação Visual

### Paleta de Cores

- **Laranja/Coral:** usado em botões principais, borda da opção selecionada, links de ação e destaque do total
- **Azul Mercado Pago:** usado apenas na logo oficial e, opcionalmente, em detalhes visuais do card do método
- **Verde:** usado para descontos e estados positivos
- **Branco:** cards e superfícies principais
- **Bege/Creme muito claro:** fundo geral, mantendo consistência com o app
- **Cinza escuro / preto:** textos principais
- **Cinza médio/claro:** descrições, divisores e estados inativos

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
  - Título da tela: destaque, semibold/bold
  - Subtítulo: regular ou semibold leve
  - Título do método de pagamento: semibold
  - Descrições auxiliares: regular
  - Valores financeiros: semibold/bold
  - Total: maior destaque da área financeira
  - Texto do botão pagar: bold

- **Nome fonte**
  - Google Fonts Nunito Sans

---

### Bordas e Formas

- Layout moderno, amigável e arredondado
- Cards com cantos suaves
- Botões com bordas arredondadas
- Cards de método de pagamento com borda destacada no estado ativo
- Elementos de resumo com organização vertical clara

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

- Espaçamento confortável entre seções
- Boa separação entre métodos de pagamento
- Respiro visual entre resumo do pedido e resumo financeiro
- Área inferior suficiente para não conflitar com safe area

---

## Sugestões de Imagens e Ícones

> Sugestões visuais para manter consistência, clareza e boa identificação dos métodos:

### Mercado Pago

- Utilizar **logo oficial do Mercado Pago**
- Preferência por:
  - logo horizontal com símbolo + texto
  - ou símbolo oficial em fundo claro
- Sugestão de uso:
  - no card da opção de pagamento
  - no bloco complementar do método selecionado

### Pix

- Utilizar **ícone oficial do Pix**
- Preferência por:
  - símbolo geométrico oficial em verde/teal
- Sugestão de uso:
  - no card da opção Pix
  - em futuras telas de QR Code

### Resumo do pedido

- Ícone sugerido:
  - `shopping-bag-outline`
  - ou sacola customizada em estilo minimalista

### Endpoints visuais auxiliares

- Ícone de cadeado no botão de pagamento:
  - `lock-closed-outline`
- Ícone de seta para ações de detalhe:
  - `chevron-forward-outline`
- Ícone de sucesso futuro:
  - `checkmark-circle-outline`

### Diretriz importante

- Não utilizar a opção `Carteira Digital`
- Substituir completamente a antiga opção `Cartão de crédito` por `Mercado Pago`
- Respeitar identidade visual oficial das marcas externas utilizadas

---

## Padronização de Cards e Botões

> Card de resumo do pedido

- Fundo branco
- Cantos arredondados
- Ícone à esquerda
- Informações principais organizadas em coluna
- Valor total com destaque à direita
- Link de ação em laranja/coral

---

> Card de método de pagamento

- Usado para `Mercado Pago` e `Pix`
- Card clicável
- Borda neutra no estado padrão
- Borda laranja/coral no estado ativo
- Indicador visual claro de seleção
- Ícone/logo à esquerda
- Título e descrição bem organizados

---

> Card complementar do método selecionado

- Fundo branco
- Borda leve
- Conteúdo curto e objetivo
- Pode conter logo maior ou ícone de confiança
- Deve reforçar o próximo passo do usuário

---

> Botão primário

- **Uso:**
  - Pagar
  - Confirmar ação financeira principal

- **Padrão:**
  - Fundo laranja/coral
  - Texto branco
  - Bordas arredondadas
  - Largura ampla
  - Ícone de cadeado opcional/obrigatório nesta tela
  - Deve ser o principal destaque da tela

---

> Botão secundário

- **Uso:**
  - Voltar ao cardápio
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

- **app-payment-execution-header.component**
  > Responsável por:
  - botão voltar
  - título
  - subtítulo

---

- **app-order-resume-card.component**
  > Responsável por:
  - quantidade de itens
  - número do pedido
  - valor resumido
  - ação ver detalhes

---

- **app-payment-method-card-list.component**
  > Responsável por:
  - listar métodos disponíveis
  - controlar seleção única
  - renderizar Mercado Pago e Pix

---

- **app-selected-payment-info.component**
  > Responsável por:
  - exibir conteúdo complementar do método ativo
  - reforçar segurança e próximo passo

---

- **app-payment-summary.component**
  > Responsável por:
  - subtotal
  - taxa
  - desconto
  - total

---

- **app-payment-submit-actions.component**
  > Responsável por:
  - botão pagar
  - voltar ao cardápio

---

## Regras Funcionais

### Método de pagamento

> Deve permitir selecionar apenas um método por vez
> Deve manter a seleção durante a navegação do checkout

- Métodos válidos nesta tela:
  - `Mercado Pago`
  - `Pix`

- Métodos que **não** devem aparecer:
  - `Carteira Digital`
  - `Cartão de crédito` como opção direta

---

### Mercado Pago

- Deve substituir a opção antiga de cartão de crédito
- Deve exibir logo oficial
- Deve preparar integração segura com o gateway
- Deve tratar retorno de:
  - sucesso
  - falha
  - cancelamento
  - pagamento pendente

---

### Pix

- Deve permitir geração de cobrança Pix
- Deve preparar exibição de QR Code ou código copia e cola em etapa seguinte
- Deve permitir acompanhar status do pagamento

---

### Resumo do pedido

- Deve refletir os dados atuais do carrinho
- Deve exibir quantidade de itens, número do pedido e valor total
- `Ver detalhes` deve permitir consulta rápida aos itens do pedido

---

### Resumo financeiro

- Deve manter consistência com valores exibidos nas telas anteriores
- Deve atualizar em tempo real caso haja alteração anterior refletida no checkout

---

### Ação de pagamento

- Clique no botão `PAGAR R$ XX,XX` deve validar:
  - existência de método selecionado
  - consistência do pedido
  - valor total calculado
- Durante o processamento:
  - bloquear múltiplos cliques
  - exibir loading
- Após retorno positivo:
  - efetivar pedido
  - navegar para confirmação/sucesso

---

## Acessibilidade

> Requisitos recomendados:

- Botões com `aria-label`
- Contraste adequado entre texto e fundo
- Área de toque mínima de 44x44px
- Indicador visual claro da opção selecionada
- Logos com `alt` descritivo
- Ordem de navegação consistente
- Feedback textual em caso de erro de pagamento

> Exemplos

<aria-label="Voltar"
aria-label="Ver detalhes do pedido"
aria-label="Selecionar Mercado Pago"
aria-label="Selecionar Pix"
aria-label="Pagar valor total do pedido"
aria-label="Voltar ao cardápio" />

---

## Responsividade e Comportamento Mobile

**Como a tela é claramente mobile-first:**

### Requisitos

- Layout otimizado para smartphones
- Scroll vertical fluido
- Cards de pagamento fáceis de tocar e selecionar
- Botão principal com forte destaque visual
- Compatível com Android/iOS
- Respeitar `ion-safe-area`
- Ajustar espaçamento inferior para não conflitar com rodapé fixo, teclado ou safe area

---

## Critérios de Aceite

### Funcionais ✅

- Exibir header com voltar, título e subtítulo
- Exibir resumo do pedido com quantidade de itens, número do pedido e valor total
- Permitir visualizar detalhes do pedido
- Exibir método `Mercado Pago`
- Exibir método `Pix`
- Permitir seleção única entre os métodos
- Não exibir `Carteira Digital`
- Não exibir `Cartão de crédito` como opção direta
- Exibir bloco complementar do método selecionado
- Exibir subtotal, taxa, descontos e total
- Validar seleção do método antes de pagar
- Processar ação de pagamento com loading
- Permitir seguir para confirmação após retorno positivo
- Permitir voltar ao cardápio sem perder o carrinho, antes da finalização

---

### Visuais 🎨

- Manter identidade visual das telas anteriores
- Usar laranja/coral como cor principal
- Manter fundo claro/bege e cards brancos
- Destacar visualmente a opção selecionada
- Destacar visualmente o valor total
- Utilizar logo oficial do Mercado Pago
- Utilizar ícone oficial do Pix

---

### Técnicos ⚙️

- Desenvolvido em Angular 20 + Ionic
- Componentização clara
- Dados preparados para integração via API/backend/gateway
- Código reutilizável e escalável
- Compatível com Android e iOS

---

## Implementação Técnica

### Angular 20

- Preferir componentes standalone
- Usar signals ou RxJS para gerenciamento simples de estado
- Estrutura preparada para persistir seleção do método de pagamento e status da transação

### Ionic

- ion-content
- ion-button
- ion-icon
- ion-radio ou seleção customizada
- ion-card ou estrutura customizada
- ion-spinner para loading
- ion-footer ou rodapé fixo, se necessário

### Estilo

- SCSS modular
- Tokens de cor no tema global
- Componentes isolados por responsabilidade

---

## Resumo Executivo

> A Tela 6 deve funcionar como a etapa de efetivação do pagamento pelo app, garantindo clareza, segurança e continuidade no fluxo de compra.

### Resultado esperado

- O usuário consegue escolher entre Mercado Pago e Pix com facilidade
- O pedido continua visível para conferência
- O valor final fica claro antes da cobrança
- O botão de pagamento transmite segurança
- O fluxo de conclusão do pedido acontece sem fricção

## APIs do Backend

### 1. Checkout Confirm (já chamado na tela anterior)
Ao chegar nesta tela, o pedido já foi criado via `POST /api/checkout/confirm` (tela 05-Pagamento.md). O front deve ter recebido `orderId` e `code` na resposta.

> **Status do pedido após confirm:** `PendingPayment` (2) para métodos PixOnline/CardOnline.

### 2. Criar Pagamento no Gateway

```http
POST /api/payments/order
```
**Autenticado** (`[CustomerOnly]`). Inicia o pagamento no gateway (Mercado Pago).

**Request:**
```json
{
  "orderId": "guid..."
}
```

**Response 200:**
```json
{
  "paymentId": "guid...",
  "orderId": "guid...",
  "gateway": 1,
  "gatewayTransactionId": "txn_abc123",
  "gatewayCheckoutUrl": "https://mercadopago.com/checkout?pref_id=123",
  "method": 1,
  "status": 1,
  "amount": 69.60,
  "createdAtUtc": "2026-05-27T12:00:00Z"
}
```

> `gateway: 1` = MercadoPago\
> `method: 1` = PixOnline, `method: 2` = CardOnline\
> `status: 1` = Pending

### 3. Comportamento por Método

#### Mercado Pago (CardOnline - `paymentMethod: 2`)
- O campo `gatewayCheckoutUrl` contém a URL do checkout do Mercado Pago
- O front deve abrir esta URL em um **WebView** ou **redirecionamento externo**
- Após o pagamento, o Mercado Pago notifica o backend via webhook (`POST /api/webhooks/mercadopago`)
- O front deve **aguardar/polling** a confirmação via `GET /api/orders/{orderId}`

#### Pix (PixOnline - `paymentMethod: 1`)
- O campo `gatewayCheckoutUrl` contém a URL com os dados Pix
- O front deve exibir **QR Code** e **código copia e cola** extraídos desta URL
- O pagamento é confirmado via webhook + polling

### 4. Consultar Status do Pedido (polling)

```http
GET /api/orders/{orderId}
```
**Autenticado** (`[CustomerOnly]`).

**Response (campos relevantes):**
```json
{
  "id": "guid...",
  "code": "HAP-X7K9M2P1",
  "status": 3,
  "total": 69.60,
  "items": [
    { "productName": "Smash Burguer", "quantity": 2, "unitPrice": 28.90, "totalPrice": 57.80 }
  ],
  "history": [
    {
      "createdAtUtc": "2026-05-27T12:00:00Z",
      "previousStatus": 1,
      "newStatus": 3,
      "notes": "Initial order status"
    }
  ]
}
```

> Quando `status` mudar de `2` (PendingPayment) para `3` (Received), o pagamento foi confirmado. Navegar para `08-AcompanhamentoPedidoCliente.md`.

### 5. Consultar Status do Pagamento

```http
GET /api/payments/order/{orderId}
```
**Autenticado** (`[CustomerOnly]`).

**Response 200:** `OrderPaymentResponseDto` com status do pagamento (Pending/Paid/Failed/Cancelled).

### 6. Histórico do Pagamento

```http
GET /api/payments/order/{orderId}/history
```
**Response 200:** `PaymentHistoryEntryDto[]`

### Fluxo de Dados na Tela
1. Receber `orderId` da tela anterior
2. Usuário escolhe método: **Mercado Pago** (CardOnline) ou **Pix** (PixOnline)
3. Chamar `POST /api/payments/order` com o `orderId`
4. **Se Mercado Pago:** redirecionar para `gatewayCheckoutUrl`
5. **Se Pix:** exibir QR Code + código copia e cola
6. Iniciar polling de `GET /api/orders/{orderId}` a cada 5s
7. Quando `status >= 3` (Received), navegar para tela 08

### Enums
| Campo | Valores |
|-------|---------|
| `PaymentMethod: 1` | PixOnline |
| `PaymentMethod: 2` | CardOnline (Mercado Pago) |
| `OrderStatus: 1` | Created |
| `OrderStatus: 2` | PendingPayment |
| `OrderStatus: 3` | Received (pagamento confirmado) |


