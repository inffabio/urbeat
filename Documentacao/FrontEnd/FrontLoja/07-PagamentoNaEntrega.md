# Especificação Funcional e Visual — Tela 7: Pagamento na Entrega da Urbeat

---

## ⚠️ Atualização 2026-05

> Esta atualização **prevalece** em caso de conflito com o conteúdo abaixo.

### Opção de pagamento pré-selecionada

| Item | Especificação |
|---|---|
| Opção inicial marcada | **"Cartão"** (`selected` inicializa como `'card'`) |
| Justificativa | Forma de pagamento mais comum/segura na entrega; reduz fricção. |
| Usuário pode trocar | Sim — tocar em "Dinheiro" marca a outra opção. |
| Quando o usuário escolhe Dinheiro | Mantém o fluxo de pergunta "Precisa de troco?" (já implementado). |

### Implementação (Angular)

```ts
// frontend/src/app/features/payment/delivery/delivery-payment-page.component.ts
type DeliveryPay = 'cash' | 'card';

// Inicia com "Cartão" pré-selecionado
readonly selected = signal<DeliveryPay | null>('card');
```

### Impacto

- Botão **CONTINUAR/FINALIZAR** já fica habilitado ao entrar na tela.
- `canFinalize` retorna `true` direto (não exige seleção manual nem campo de troco).
- Se o usuário tocar em "Dinheiro", o sistema limpa `cardPreference` e mostra o sub-fluxo de troco.

---

## Projeto

- **Stack alvo:** Angular 20 + Ionic
- **Visão do software:** Existe uma empresa chamada Urbeat que controla todos os clientes vendedores que farão cadastro e cada um terá sua página de venda.
- **Objetivo:** Criar a tela de definição do pagamento na entrega, responsável por registrar como o cliente deseja pagar no momento do recebimento do pedido.

---

## Visão Geral da Tela

> Esta tela representa a etapa complementar do checkout exibida quando, na tela de pagamento, o cliente escolhe a opção `Pagar na entrega`.

> Ela tem como principais objetivos:

- 💵 Permitir escolher como será feito o pagamento na entrega
- 💳 Permitir pagamento com cartão na entrega
- ⚡ Permitir pagamento com Pix na entrega
- 💰 Permitir pagamento em dinheiro
- 🧾 Exibir resumo rápido do pedido
- 📍 Exibir endereço de entrega
- ✅ Registrar a forma de pagamento escolhida no sistema
- 🚚 Permitir seguir para a confirmação final do pedido

---

## Estrutura Geral da Tela

> A tela pode ser dividida em 7 blocos principais:

- Header de navegação
- Resumo do pedido
- Seleção do método de pagamento na entrega
- Bloco complementar do método selecionado
- Endereço de entrega
- Resumo financeiro
- Ações finais

---

## Estrutura Visual por Seções

> Header

- **Função:** Exibir a etapa atual do fluxo e contextualizar que o pagamento será realizado no momento da entrega
- **Elementos identificados esperados:**
  - Botão voltar à esquerda
  - Título: `Pagar na entrega`
  - Subtítulo: `Escolha como deseja pagar ao receber`

> Comportamento esperado

- O botão voltar deve retornar para a tela anterior do checkout
- O título deve deixar claro que o pagamento não será processado pelo app nesta etapa
- O subtítulo deve orientar a escolha do método de pagamento presencial
- O header deve manter consistência visual com as telas anteriores

---

> Resumo do Pedido

- **Função:** Exibir um resumo rápido do pedido antes da definição final da forma de pagamento na entrega

### Elementos esperados

- Ícone de sacola/pedido
- Quantidade de itens
- Número do pedido, se já existir no fluxo
- Valor total resumido
- Ação `Ver detalhes`

### Comportamento esperado

- Deve exibir a quantidade total de itens do pedido
- Deve exibir o valor total do pedido com destaque
- Deve permitir ação `Ver detalhes` para revisar os itens
- A ação `Ver detalhes` pode:
  - expandir conteúdo
  - abrir modal
  - navegar para resumo do carrinho

### Padronização visual

- Card com fundo branco
- Ícone à esquerda
- Informações centrais em coluna
- Valor total alinhado à direita
- Link `Ver detalhes` em laranja/coral

---

> Escolha do Método de Pagamento na Entrega

- **Função:** Permitir ao usuário informar como pretende pagar no ato da entrega

### Título da seção

- `Como deseja pagar na entrega?`

### Opções previstas

- `Dinheiro`
- `Cartão na entrega`
- `Pix na entrega`

### Conteúdo mínimo por opção

- Ícone ilustrativo
- Título
- Descrição curta
- Indicador visual de seleção
- Área clicável completa

### Comportamento esperado

- Apenas uma opção pode ficar ativa por vez
- A opção selecionada deve ter maior destaque visual
- A escolha deve ser armazenada no estado do checkout
- A escolha deve ser enviada e registrada no sistema ao finalizar o pedido

### Padronização visual

- Card clicável
- Borda neutra no estado padrão
- Borda laranja/coral no estado ativo
- Indicador visual de seleção
- Descrição em texto secundário
- Área de toque confortável

---

> Método 1 — Dinheiro

- **Função:** Permitir ao cliente informar que pagará em espécie no recebimento

### Conteúdo esperado

- Ícone de dinheiro / cédula / moedas
- Título: `Dinheiro`
- Texto auxiliar sugerido:
  - `Pague em espécie ao receber o pedido`

### Comportamento esperado

- Ao selecionar `Dinheiro`, o sistema deve exibir bloco complementar para informação de troco
- Esta escolha deve ser registrada no sistema como método de pagamento na entrega

### Campo complementar sugerido

- Pergunta: `Precisa de troco?`
- Opções:
  - `Não`
  - `Sim`
- Se `Sim`, exibir campo:
  - `Troco para quanto?`

### Regras funcionais

- O campo de troco só aparece quando a opção for `Dinheiro`
- O valor informado deve ser maior ou igual ao total do pedido
- O valor do troco deve ser registrado no sistema junto com o pedido
- Se o cliente não precisar de troco, registrar como `sem troco`

---

> Método 2 — Cartão na entrega

- **Função:** Permitir ao cliente informar que pagará com cartão ao entregador

### Conteúdo esperado

- Ícone de maquininha ou cartão
- Título: `Cartão na entrega`
- Texto auxiliar sugerido:
  - `Pague com cartão de crédito ou débito no momento da entrega`

### Comportamento esperado

- Ao selecionar esta opção, o sistema deve registrar que o pagamento será feito presencialmente com cartão
- Opcionalmente, pode exibir campo secundário para preferência:
  - `Crédito`
  - `Débito`
  - `Indiferente`

### Regra funcional

- Mesmo que a preferência seja informada, o pagamento só será efetivado presencialmente
- O pedido deve ficar registrado com status de:
  - `pagamento pendente na entrega`

---

> Método 3 — Pix na entrega

- **Função:** Permitir ao cliente informar que pagará por Pix no momento da entrega

### Conteúdo esperado

- Ícone oficial do Pix
- Título: `Pix na entrega`
- Texto auxiliar sugerido:
  - `Faça o Pix no momento do recebimento do pedido`

### Comportamento esperado

- Ao selecionar `Pix na entrega`, o sistema deve registrar esta preferência
- O pagamento não é confirmado nesta tela
- O pedido deve seguir com status de:
  - `pagamento pendente na entrega`

---

> Bloco Complementar do Método Selecionado

- **Função:** Exibir informações adicionais conforme o método escolhido

### Para Dinheiro

- Exibir bloco com:
  - pergunta sobre troco
  - opção `Não preciso de troco`
  - opção `Preciso de troco`
  - campo `Troco para quanto?`, se necessário

### Para Cartão na entrega

- Exibir bloco com:
  - texto explicativo curto
  - opcionalmente preferência entre crédito e débito

### Para Pix na entrega

- Exibir bloco com:
  - texto explicativo curto
  - informação de que o pagamento será realizado no recebimento

### Regra funcional

- Apenas o bloco do método ativo deve ficar visível
- O conteúdo complementar deve ser simples, direto e fácil de preencher no mobile

---

> Endereço de Entrega

- **Função:** Exibir o endereço informado anteriormente no fluxo

### Elementos esperados

- Ícone de localização
- Título: `Endereço de entrega`
- Rua e número
- Bairro, cidade e estado

### Comportamento esperado

- O endereço deve vir da tela de cadastro/endereço
- Deve ser exibido de forma resumida e legível
- Opcionalmente, pode existir ação futura para editar

---

> Resumo Financeiro

- **Função:** Exibir a composição do valor final do pedido

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
- O campo `Descontos` pode aparecer em verde

---

> Ações Finais

### Botão principal

- **Elemento identificado esperado:** Botão largo com texto `FINALIZAR PEDIDO`
- **Variação aceita:** `CONTINUAR` ou `CONFIRMAR PEDIDO`
- **Função:** Confirmar o método de pagamento na entrega e finalizar o pedido

### Comportamento esperado

- Deve ser a ação de maior destaque da tela
- Deve validar se um método foi selecionado
- Se o método for `Dinheiro` com troco, deve validar o valor informado
- Deve salvar no sistema:
  - tipo de pagamento: `na entrega`
  - método escolhido
  - necessidade de troco, quando houver
  - valor para troco, quando houver
- Após concluir, deve seguir para tela de confirmação/sucesso do pedido

---

### Ação secundária

- **Elemento identificado:** Texto ou botão `Voltar ao cardápio`
- **Função:** Permitir sair do checkout sem perder o carrinho, enquanto o pedido não for finalizado

### Comportamento esperado

- Não deve apagar os itens do carrinho
- Deve possuir estilo secundário
- Não deve competir visualmente com o botão principal

---

## Especificação Visual

### Paleta de Cores

- **Laranja/Coral:** usado em botões principais, borda da opção selecionada, links e destaque do total
- **Verde:** usado para descontos e mensagens positivas
- **Bege/Creme claro:** fundo geral, mantendo consistência com o app
- **Branco:** cards e superfícies principais
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
  - Textos auxiliares: regular
  - Valores financeiros: semibold/bold
  - Total: maior destaque da área financeira
  - Texto do botão principal: bold

- **Nome fonte**
  - Google Fonts Nunito Sans

---

### Bordas e Formas

- Layout moderno, amigável e arredondado
- Cards com cantos suaves
- Botões com bordas arredondadas
- Cards de opção com borda destacada no estado ativo
- Campos complementares com o mesmo padrão visual das telas anteriores

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
- Boa separação entre cards
- Respiro visual entre opções, endereço e resumo financeiro
- Área inferior suficiente para não conflitar com safe area

---

## Sugestões de Imagens e Ícones

> Sugestões visuais para facilitar entendimento do método de pagamento na entrega:

### Dinheiro

- Ícone sugerido:
  - cédula
  - moedas
  - dinheiro em mãos
- Sugestão Ionic:
  - `cash-outline`
- Uso:
  - card da opção `Dinheiro`
  - bloco complementar de troco

### Cartão na entrega

- Ícone sugerido:
  - maquininha
  - cartão físico
  - pagamento presencial
- Sugestão Ionic:
  - `card-outline`
  - ou ícone customizado de maquininha
- Uso:
  - card da opção `Cartão na entrega`

### Pix na entrega

- Utilizar **ícone oficial do Pix**
- Sugestão de uso:
  - card da opção `Pix na entrega`
  - bloco complementar

### Resumo do pedido

- Ícone sugerido:
  - `bag-handle-outline`
  - `receipt-outline`
  - ou sacola minimalista customizada

### Endereço de entrega

- Ícone sugerido:
  - `location-outline`

### Ação final

- Ícone sugerido no botão:
  - `checkmark-circle-outline`
  - ou `receipt-outline`

### Diretriz importante

- Os ícones devem ser simples, amigáveis e consistentes com o restante do checkout
- Priorizar leitura rápida em dispositivos móveis
- As marcas externas, como Pix, devem usar identidade oficial

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

- Usado para:
  - `Dinheiro`
  - `Cartão na entrega`
  - `Pix na entrega`
- Card clicável
- Borda neutra no estado padrão
- Borda laranja/coral no estado ativo
- Indicador visual claro de seleção
- Ícone à esquerda
- Título e descrição organizados

---

> Card complementar

- Fundo branco
- Borda leve
- Conteúdo curto e objetivo
- Exibir somente quando houver método selecionado
- Deve conter os campos adicionais necessários

---

> Botão primário

- **Uso:**
  - Finalizar pedido
  - Confirmar pagamento na entrega

- **Padrão:**
  - Fundo laranja/coral
  - Texto branco
  - Bordas arredondadas
  - Largura ampla
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

- **delivery-payment-header.component**
  > Responsável por:
  - botão voltar
  - título
  - subtítulo

---

- **delivery-payment-order-summary.component**
  > Responsável por:
  - quantidade de itens
  - número do pedido
  - valor resumido
  - ação ver detalhes

---

- **delivery-payment-method-selector.component**
  > Responsável por:
  - listar métodos disponíveis
  - controlar seleção única
  - renderizar dinheiro, cartão na entrega e Pix na entrega

---

- **delivery-payment-extra-info.component**
  > Responsável por:
  - troco
  - preferência de cartão
  - textos auxiliares do método ativo

---

- **delivery-address-card.component**
  > Responsável por:
  - exibir endereço de entrega

---

- **delivery-payment-summary.component**
  > Responsável por:
  - subtotal
  - taxa
  - desconto
  - total

---

- **delivery-payment-actions.component**
  > Responsável por:
  - botão finalizar pedido
  - voltar ao cardápio

---

## Regras Funcionais

### Exibição da tela

> Esta tela só deve ser exibida quando o usuário escolher `Pagar na entrega` na etapa anterior

- Se o usuário escolher `Pagar pelo app`, seguir fluxo digital
- Se o usuário escolher `Pagar na entrega`, abrir esta tela complementar

---

### Registro no sistema

> A escolha feita nesta tela deve ser registrada no sistema obrigatoriamente

### Dados mínimos que devem ser salvos

- tipo de pagamento: `na_entrega`
- método na entrega:
  - `dinheiro`
  - `cartao_na_entrega`
  - `pix_na_entrega`
- precisa de troco:
  - `sim`
  - `nao`
- valor para troco, quando informado
- preferência de cartão, se existir
- status inicial do pagamento:
  - `pendente_na_entrega`

---

### Seleção de método

- Deve permitir apenas uma opção ativa por vez
- Deve manter a escolha durante a navegação do checkout
- Deve refletir visualmente o estado selecionado

---

### Dinheiro

- Pode exigir informação de troco
- Se `troco para` for preenchido:
  - validar valor maior ou igual ao total
- Registrar observação financeira no pedido

---

### Cartão na entrega

- Registrar que o cliente pagará presencialmente
- Opcionalmente registrar preferência de crédito/débito
- Não processar cobrança online nesta etapa

---

### Pix na entrega

- Registrar que o cliente pagará por Pix no recebimento
- Não marcar pedido como pago nesta etapa
- Manter status como pendente até confirmação operacional

---

### Resumo do pedido

- Deve refletir os dados atuais do carrinho
- Deve exibir quantidade de itens, número do pedido e valor total
- `Ver detalhes` deve permitir consulta rápida

---

### Finalização

- Clique no botão principal deve validar:
  - existência de método selecionado
  - troco preenchido corretamente, quando necessário
  - consistência dos dados do pedido
- Após validação:
  - salvar dados no sistema
  - finalizar pedido
  - navegar para confirmação/sucesso

---

## Acessibilidade

> Requisitos recomendados:

- Botões com `aria-label`
- Contraste adequado entre texto e fundo
- Área de toque mínima de 44x44px
- Indicador visual claro da opção selecionada
- Campos de troco com label visível
- Ordem de navegação consistente
- Mensagens de erro objetivas

> Exemplos

<aria-label="Voltar"
aria-label="Ver detalhes do pedido"
aria-label="Selecionar dinheiro"
aria-label="Selecionar cartão na entrega"
aria-label="Selecionar Pix na entrega"
aria-label="Informar troco"
aria-label="Finalizar pedido"
aria-label="Voltar ao cardápio" />

---

## Responsividade e Comportamento Mobile

**Como a tela é claramente mobile-first:**

### Requisitos

- Layout otimizado para smartphones
- Scroll vertical fluido
- Cards fáceis de tocar e selecionar
- Campos complementares simples e confortáveis
- Botão principal com forte destaque visual
- Compatível com Android/iOS
- Respeitar `ion-safe-area`
- Ajustar espaçamento inferior para não conflitar com teclado, rodapé fixo ou safe area

---

## Critérios de Aceite

### Funcionais ✅

- Exibir header com voltar, título e subtítulo
- Exibir resumo do pedido com quantidade de itens, número do pedido e valor total
- Permitir visualizar detalhes do pedido
- Exibir métodos `Dinheiro`, `Cartão na entrega` e `Pix na entrega`
- Permitir seleção única entre os métodos
- Exibir bloco complementar conforme método selecionado
- Permitir informar troco quando o método for dinheiro
- Validar o valor de troco informado
- Exibir endereço de entrega
- Exibir subtotal, taxa, descontos e total
- Registrar no sistema o método escolhido
- Registrar no sistema a necessidade de troco, quando houver
- Finalizar pedido após validação correta
- Permitir voltar ao cardápio sem perder o carrinho antes da finalização

---

### Visuais 🎨

- Manter identidade visual das telas anteriores
- Usar laranja/coral como cor principal
- Manter fundo claro/bege e cards brancos
- Destacar visualmente a opção selecionada
- Destacar visualmente o valor total
- Usar ícones claros e consistentes para cada opção

---

### Técnicos ⚙️

- Desenvolvido em Angular 20 + Ionic
- Componentização clara
- Dados preparados para integração via API/backend
- Código reutilizável e escalável
- Compatível com Android e iOS

---

## Implementação Técnica

### Angular 20

- Preferir componentes standalone
- Usar signals ou RxJS para gerenciamento simples de estado
- Estrutura preparada para persistir seleção do método e dados complementares do pagamento na entrega

### Ionic

- ion-content
- ion-button
- ion-icon
- ion-input
- ion-radio ou seleção customizada
- ion-card ou estrutura customizada
- ion-footer, se necessário

### Estilo

- SCSS modular
- Tokens de cor no tema global
- Componentes isolados por responsabilidade

---

## Resumo Executivo

> A Tela 7 deve funcionar como a etapa de definição do pagamento na entrega, garantindo clareza, praticidade e registro correto da escolha no sistema.

### Resultado esperado

- O usuário consegue informar com facilidade como pagará no recebimento
- O sistema registra corretamente a forma de pagamento na entrega
- O pedido segue com informações completas para operação e entrega
- O fluxo de finalização acontece sem fricção

## APIs do Backend

### 1. Checkout Confirm (criação do pedido)
Esta tela só é exibida quando o usuário escolheu `CashOnDelivery` (3) ou `CardOnDelivery` (4) na tela 05-Pagamento.md. O pedido **já foi criado** via `POST /api/checkout/confirm` na tela anterior.

> **Status do pedido após confirm com pagamento na entrega:** `Received` (3) — não passa por `PendingPayment`.

### 2. Atualizar Pedido com Dados de Pagamento na Entrega
O backend aceita `paymentMethod: 3` (CashOnDelivery) e `paymentMethod: 4` (CardOnDelivery). O pedido já é criado com o método correto. Os detalhes de troco e preferência são armazenados no campo `notes` do pedido.

```http
POST /api/checkout/confirm
```

**Request (exemplo com CashOnDelivery + troco):**
```json
{
  "storeId": "guid...",
  "fulfillmentType": 1,
  "customerAddressId": "guid...",
  "paymentMethod": 3,
  "notes": "Pagamento: dinheiro na entrega. Precisa de troco para R$ 100,00.",
  "items": [ ... ]
}
```

**Request (exemplo com CardOnDelivery):**
```json
{
  "storeId": "guid...",
  "fulfillmentType": 1,
  "customerAddressId": "guid...",
  "paymentMethod": 4,
  "notes": "Pagamento: cartão na entrega. Preferência: crédito.",
  "items": [ ... ]
}
```

> **Observação:** O backend não possui campos específicos para `troco` ou `preferenciaCartao`. Esses dados devem ser enviados no campo `notes` (string livre) como parte do pedido.

### 3. Consultar Pedido (opcional — pós-criação)

```http
GET /api/orders/{orderId}
```

### Fluxo de Dados na Tela
1. Receber `orderId` da tela anterior (05-Pagamento.md)
2. Pedido já está criado com `status: Received` (3)
3. Tela coleta detalhes adicionais:
   - **Dinheiro:** precisa de troco? Troco para quanto?
   - **Cartão:** preferência crédito/débito?
4. Os detalhes são registrados via `PATCH /api/orders/{orderId}/status` com notes adicional (se necessário) ou já enviados no confirm
5. Botão FINALIZAR PEDIDO → navega para `08-AcompanhamentoPedidoCliente.md`

### Enums
| Campo | Valor | Significado |
|-------|-------|-------------|
| `paymentMethod: 3` | CashOnDelivery | Dinheiro na entrega |
| `paymentMethod: 4` | CardOnDelivery | Cartão (máquina) na entrega |
| `OrderStatus: 3` | Received | Pedido recebido (após confirm) |

### Diferença entre Pagamento Online e na Entrega
| Característica | Pagamento Online (Tela 6) | Pagamento na Entrega (Tela 7) |
|----------------|--------------------------|------------------------------|
| `paymentMethod` | 1 (PixOnline) / 2 (CardOnline) | 3 (CashOnDelivery) / 4 (CardOnDelivery) |
| `status` inicial | PendingPayment (2) | Received (3) |
| Criação de pagamento | `POST /api/payments/order` | Não há |
| Confirmação | Webhook + polling | Imediata (presencial) |


