# Especificação Funcional e Visual — Tela 5: Pagamento da Urbeat 

---

## ⚠️ Atualização 2026-05

> Esta atualização **prevalece** em caso de conflito com o conteúdo abaixo.

### Opção de pagamento pré-selecionada

| Item | Especificação |
|---|---|
| Opção inicial marcada | **"Pagar na entrega"** (`selected` inicializa como `'delivery'`) |
| Justificativa | Fluxo mais comum em delivery local; permite avançar com 1 clique no botão CONTINUAR sem precisar marcar nada. |
| Usuário pode trocar | Sim — tocar em "Pagar pelo app" marca outra opção (radio behavior). |

### Implementação (Angular)

```ts
// frontend/src/app/features/checkout/payment-page.component.ts
type PayCategory = 'app' | 'delivery';

// Inicia com "Pagar na entrega" pré-selecionado
readonly selected = signal<PayCategory | null>('delivery');
```

### Impacto

- Botão **CONTINUAR** já fica habilitado ao entrar na tela.
- Tocar em CONTINUAR sem mudar nada → navega para `07-PagamentoNaEntrega.jpeg`.

---

## Projeto

- **Stack alvo:** Angular 20 + Ionic
- **Visão do software:** Existe uma empresa chamada Urbeat que controla todos os clientes vendedores que farão cadastro e cada um terá sua página de venda.
- **Objetivo:** Criar a tela de pagamento do cliente, responsável por permitir a escolha da forma de pagamento e revisar os dados finais do pedido antes da confirmação.

---

## Visão Geral da Tela

> Esta tela representa a etapa de pagamento no fluxo de delivery mobile do cliente do estabelecimento cadastrado na happe.

> Ela tem como principais objetivos:

- 💳 Permitir escolher a forma de pagamento **pix ou mercado pago**
- 🧾 Exibir um resumo rápido do pedido
- 📍 Exibir o endereço de entrega selecionado
- 💰 Exibir subtotal, taxa, descontos e total
- ✅ Permitir seguir para a confirmação final do pedido
- ↩️ Permitir voltar ao cardápio sem perder o carrinho

---

## Estrutura Geral da Tela

> A tela pode ser dividida em 6 blocos principais:

- Header de navegação
- Resumo do pedido
- Seleção da forma de pagamento
- Endereço de entrega
- Resumo financeiro
- Ações finais

---

## Estrutura Visual por Seções

> Header

- **Função:** Exibir contexto da etapa atual e permitir retorno à tela anterior (03-cart.md)
- **Elementos identificados:**
  - Botão voltar à esquerda
  - Título: `Pagamento`
  - Subtítulo: `Escolha como deseja pagar` **fonte pequena**

> Comportamento esperado

- O botão voltar deve retornar para a tela anterior do fluxo (03-cart.md)
- O título e subtítulo devem deixar clara a etapa atual do checkout
- O header deve manter consistência visual com as telas anteriores

---

> Resumo do Pedido

- **Função:** Exibir um resumo rápido do pedido antes da escolha do pagamento

### Elementos identificados

- Ícone de sacola/pedido
- Quantidade de itens
- Número do pedido
- Valor total resumido
- Ação `Ver detalhes`

### Comportamento esperado

- Deve exibir a quantidade total de itens do pedido
- Deve exibir o identificador do pedido, quando já existir no fluxo
- Deve exibir o valor total resumido
- Ao tocar em `Ver detalhes`, pode:
  - expandir os itens
  - abrir modal
  - navegar para visualização resumida do carrinho
  - Crie o modal de resumo com o mesmo padrão eos itens do carrinho com Quantidade, preço, preço total e um icone X no canto superior direito para fechar e u botão fechar.


---

> Forma de Pagamento

- **Função:** Permitir ao usuário escolher como deseja pagar

### Título da seção

- `Como deseja pagar?`

### Subtitulo (Opções de Pagamento)

- `Escolha a opção que for mais conveniente para você` **fonte pequena**

---

### Opções previstas

### Titulo

- `Pagar pelo app`

### Subtitulo (Pagar pelo app)

- `Pagar com cartão, Pix ou carteira de forma rápida e segura.` **fonte pequena**

### Icone

(./images/payApp.svg)

---

### Titulo

- `Pagar na entrega`

### Subtitulo (Pagar na entrega)

- `Pagar em dinheiro cartão ou pix` **fonte pequena**

### Icone

(./images/payEntrega.svg)

---

### Conteúdo mínimo por opção

- Ícone ilustrativo
- Título
- Descrição curta
- Indicador visual de seleção
- Seta indicando possibilidade de detalhamento

### Comportamento esperado

- Apenas uma opção pode ficar ativa por vez
- Ao selecionar `Pagar pelo app`:
  - destacar a opção visualmente
  - permitir fluxo posterior com cartão, Pix ou carteira digital
- Ao selecionar `Pagar na entrega`:
  - destacar a opção visualmente
  - permitir fluxo posterior com dinheiro, cartão ou Pix na entrega


### Padronização visual

- Card clicável
- Opção ativa com borda laranja/coral
- Opção inativa com borda neutra/cinza
- Indicador visual claro de seleção
- Texto descritivo em estilo secundário

---

> Endereço de Entrega

- **Função:** Exibir o endereço informado pelo cliente na etapa anterior (04-CadastroCliente.md)

### Elementos identificados

- Ícone de localização
- Título: `Endereço de entrega`
- Rua e número
- Bairro, cidade e estado

### Comportamento esperado

- Os dados devem vir da tela de cadastro/endereço
- O endereço deve ser exibido de forma legível e resumida
- Opcionalmente, pode permitir edição futura por ação secundária

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
- **Taxa de entrega:** definida na etapa de entrega/retirada
- **Descontos:** valor aplicado por cupom ou campanha
- **Total:** subtotal + taxa - descontos

### Comportamento esperado

- Atualização em tempo real caso algum dado financeiro mude no fluxo
- O campo `Total` deve possuir o maior destaque visual da seção
- O campo `Descontos` pode usar cor verde para reforçar valor abatido

---

> Ações Finais

### Botão principal

- **Elemento identificado:** Botão largo com texto `CONTINUAR`
- **Função:** Levar o usuário para a etapa final de revisão/confirmação do pedido

### Comportamento esperado

- Deve ser a ação de maior destaque da tela
- Deve validar se uma forma de pagamento foi selecionada
- Se não houver forma de pagamento definida, o botão deve permanecer desabilitado ou exibir validação

---

### Ação secundária

- **Elemento identificado:** Texto ou botão `Voltar ao cardápio`
- **Função:** Permitir retornar à listagem de produtos (Telainicial.md)

### Comportamento esperado

- Não deve limpar o carrinho
- Deve possuir estilo secundário, sem competir com o botão principal

---

## Especificação Visual

### Paleta de Cores

- **Laranja/Coral:** usado em botões principais, seleção ativa, links de ação e destaque de total
- **Verde:** usado para destaque de descontos, quando aplicável
- **Bege/Creme claro:** fundo geral
- **Branco:** cards e superfícies
- **Cinza escuro / preto:** textos principais
- **Cinza médio/claro:** descrições, bordas, divisores e estados inativos

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
  - Título das opções de pagamento: semibold
  - Descrições: regular
  - Valores financeiros: semibold/bold
  - Total: maior destaque da área financeira

- **Nome fonte**
  - Google Fonts Nunito Sans

---

### Bordas e Formas

- Layout moderno, amigável e arredondado
- Cards com cantos suaves
- Botões com bordas arredondadas
- Opções de pagamento com borda destacada no estado ativo

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
- Boa separação entre cards e blocos financeiros
- Respiro visual entre endereço, pagamento e ações finais
- Área inferior suficiente para não conflitar com safe area

---

## Padronização de Cards e Botões

> Card de resumo

- Fundo branco
- Cantos arredondados
- Ícone à esquerda (ionic icon sacola)
- Informação principal com boa leitura
- Link de ação em laranja/coral

---

> Card de seleção

- Usado para forma de pagamento
- Card clicável
- Borda neutra no estado padrão
- Borda laranja/coral no estado ativo
- Indicador visual de seleção
- Ícone e seta lateral

---

> Botão primário

- **Uso:**
  - Continuar
  - Confirmar avanço no checkout

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

> Botão de ícone

- **Uso:**
  - Voltar
  - Ver detalhes
  - Ações auxiliares

- **Padrão:**
  - Área de toque confortável
  - Ícone claro
  - Feedback visual ao toque

---

## Componentização Recomendada

> Para Angular 20 + Ionic, recomenda-se quebrar a tela em componentes reutilizáveis.

---

## Componentes sugeridos

- **payment-header.component**
  > Responsável por:
  - botão voltar
  - título
  - subtítulo

---

- **order-payment-summary.component**
  > Responsável por:
  - quantidade de itens
  - número do pedido
  - valor resumido
  - ação ver detalhes

---

- **payment-method-selector.component**
  > Responsável por:
  - pagar pelo app
  - pagar na entrega
  - controle da opção ativa

---

- **delivery-address-card.component**
  > Responsável por:
  - exibir rua, número, bairro, cidade e estado

---

- **payment-order-summary.component**
  > Responsável por:
  - subtotal
  - taxa
  - desconto
  - total

---

- **payment-footer-actions.component**
  > Responsável por:
  - botão continuar
  - voltar ao cardápio

---

## Regras Funcionais

### Pagamento

> Deve permitir escolher apenas uma forma de pagamento por vez
> Deve manter a opção selecionada durante a navegação no checkout

---

### Resumo do pedido

- Deve refletir os dados atuais do carrinho
- Deve exibir total consistente com as etapas anteriores
- `Ver detalhes` deve permitir acesso rápido às informações resumidas do pedido

---

### Endereço

- Deve refletir os dados preenchidos na tela anterior
- Deve ser exibido em formato resumido e legível

---

### Continuidade do fluxo

- Clique no botão `CONTINUAR` deve validar a escolha da forma de pagamento
- Se a seleção estiver válida, deve salvar os dados e seguir para a próxima etapa
- Clique em `Voltar ao cardápio` deve manter o carrinho salvo

---

## Acessibilidade

> Requisitos recomendados:

- Botões com `aria-label`
- Contraste adequado entre texto e fundo
- Área de toque mínima de 44x44px
- Indicador visual claro da opção selecionada
- Informações financeiras com boa legibilidade
- Ordem de navegação consistente

> Exemplos

<aria-label="Voltar"
aria-label="Ver detalhes do pedido"
aria-label="Selecionar pagar pelo app"
aria-label="Selecionar pagar na entrega"
aria-label="Endereço de entrega"
aria-label="Continuar para próxima etapa" />

---

## Responsividade e Comportamento Mobile

**Como a tela é claramente mobile-first:**

### Requisitos

- Layout otimizado para smartphones
- Scroll vertical fluido
- Cards fáceis de tocar e selecionar
- Botão principal com forte destaque visual
- Compatível com Android/iOS
- Respeitar `ion-safe-area`
- Ajustar espaçamento inferior para não conflitar com rodapé fixo ou safe area

---

## Critérios de Aceite

### Funcionais ✅

- Exibir header com voltar, título e subtítulo
- Exibir resumo do pedido com quantidade de itens, número do pedido e valor
- Permitir visualizar detalhes do pedido
- Exibir opções de pagamento com seleção única
- Permitir selecionar `Pagar pelo app`
- Permitir selecionar `Pagar na entrega`
- Exibir endereço de entrega preenchido anteriormente
- Exibir subtotal, taxa, descontos e total
- Validar seleção de pagamento antes de continuar
- Permitir continuar para a próxima etapa do checkout
- Permitir voltar ao cardápio sem perder o carrinho

---

### Visuais 🎨

- Manter identidade visual das telas anteriores
- Usar laranja/coral como cor principal
- Manter fundo claro/bege
- Padronizar cards, estados selecionados e botão principal
- Destacar visualmente o valor total
- Destacar visualmente a opção de pagamento selecionada

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
- Estrutura preparada para persistir método de pagamento durante o checkout

### Ionic

- ion-content
- ion-button
- ion-icon
- ion-radio ou seleção customizada
- ion-card ou estrutura customizada
- ion-footer ou rodapé fixo, se necessário

### Estilo

- SCSS modular
- Tokens de cor no tema global
- Componentes isolados por responsabilidade

---

## Resumo Executivo

> A Tela 5 deve funcionar como a etapa de escolha da forma de pagamento, garantindo clareza, consistência visual e continuidade no fluxo de compra.

### Resultado esperado

- O usuário consegue escolher a forma de pagamento com facilidade
- O pedido e o endereço continuam visíveis para conferência
- Os valores finais ficam claros antes da confirmação
- O fluxo para a próxima etapa acontece sem fricção

## APIs do Backend

### 1. Preview (opcional — recalcular com método selecionado)

```http
POST /api/checkout/preview
```

**Request (incluindo método de pagamento):**
```json
{
  "storeId": "guid...",
  "fulfillmentType": 1,
  "customerAddressId": "guid...",
  "paymentMethod": 1,
  "items": [ ... ]
}
```

**Response 200:** `CheckoutPreviewResponseDto` (subtotal, deliveryFee, total)

> Útil se a tela precisar recalcular valores com base no método escolhido (ex: taxa extra para cartão).

### 2. Confirmar Pedido (após escolher método)

```http
POST /api/checkout/confirm
```
**Autenticado** (`[CustomerOnly]`). Cria o pedido no banco.

**Request:** mesmo payload do preview
```json
{
  "storeId": "guid...",
  "fulfillmentType": 1,
  "customerAddressId": "guid...",
  "paymentMethod": 1,
  "notes": "Sem cebola, por favor",
  "items": [
    { "productName": "Smash Burguer", "quantity": 2, "unitPrice": 28.90 },
    { "productName": "Coca-Cola Lata", "quantity": 1, "unitPrice": 5.90 }
  ]
}
```

**Response 201:**
```json
{
  "orderId": "guid...",
  "code": "HAP-X7K9M2P1",
  "fulfillmentType": 1,
  "status": 3,
  "subtotal": 63.70,
  "deliveryFee": 5.90,
  "total": 69.60
}
```

> **Após confirmar**, a navegação depende do método escolhido:
> - `PixOnline` (1) ou `CardOnline` (2) → tela `06-EfetivarPagamento.md`
> - `CashOnDelivery` (3) ou `CardOnDelivery` (4) → tela `07-PagamentoNaEntrega.md`

### Fluxo de Dados na Tela
1. Tela exibe dados do carrinho + endereço (já preenchidos em etapas anteriores)
2. Usuário seleciona forma de pagamento
3. Ao clicar em CONTINUAR:
   - Chamar `POST /api/checkout/confirm` com `paymentMethod` selecionado
   - Redirecionar conforme o método escolhido

### Enums
| Campo | Valor | Tela de destino |
|-------|-------|----------------|
| `paymentMethod: 1` | PixOnline (`Pagar pelo app > Pix`) | 06-EfetivarPagamento.md |
| `paymentMethod: 2` | CardOnline (`Pagar pelo app > Mercado Pago`) | 06-EfetivarPagamento.md |
| `paymentMethod: 3` | CashOnDelivery (`Pagar na entrega > Dinheiro`) | 07-PagamentoNaEntrega.md |
| `paymentMethod: 4` | CardOnDelivery (`Pagar na entrega > Cartão/Pix`) | 07-PagamentoNaEntrega.md |


