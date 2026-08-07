# Especificação Funcional e Visual — Tela 3: Carrinho / Revisão do Pedido da Urbeat

## Projeto

- **Stack alvo:** Angular 20 + Ionic
- **Visão do software:** Existe uma empresa chamada Urbeat que controla todos os clientes vendedores que farão cadastro e cada um terá sua página de venda.
- **Objetivo:** Criar a tela de carrinho do cliente, responsável por revisar os itens escolhidos nas telas anteriores antes de seguir para a próxima etapa do pedido.

---

## Visão Geral da Tela

> Esta tela representa a etapa de revisão do pedido no fluxo de delivery mobile do cliente da Urbeat.

> Ela tem como principais objetivos:

- 🛒 Exibir os itens adicionados ao carrinho
- ➕➖ Permitir ajuste de quantidade
- 🗑️ Permitir remover item individualmente
- 🧹 Permitir limpar o carrinho completo
- 🎟️ Permitir adicionar cupom de desconto
- 🛵 Permitir escolher a forma de recebimento
- 💰 Exibir subtotal, taxa de entrega, desconto e total
- ✅ Permitir seguir para a próxima etapa do checkout
- ↩️ Permitir voltar ao cardápio sem perder o carrinho

---

## Estrutura Geral da Tela

> A tela pode ser dividida em 7 blocos principais:

- Header de navegação
- Lista de itens do pedido
- Ação de cupom
- Seleção de forma de recebimento
- Resumo financeiro
- Botão principal de continuidade
- Ação secundária para voltar ao cardápio

---

## Estrutura Visual por Seções

> Header

- **Função:** Exibir contexto da tela e ações principais de navegação
- **Elementos identificados:**
  - Botão voltar à esquerda
  - Título: `Carrinho`
  - Subtítulo: `Revise seu pedido`
  - Botão de limpar carrinho à direita com ícone de lixeira

> Comportamento esperado

- O botão voltar deve retornar para a tela anterior (Telainicial.md)
- O botão limpar carrinho deve solicitar confirmação antes de apagar todos os itens
- Se o carrinho estiver vazio, o botão de limpar pode ficar oculto ou desabilitado

---

> Lista de Itens do Pedido

- **Função:** Exibir todos os produtos adicionados ao carrinho nas telas anteriores

### Cada item deve conter

- 📷 Imagem do produto
- 🏷️ Nome do produto
- 📝 Descrição curta
- 💰 Preço unitário
- ➕➖ Seletor de quantidade
- 🗑️ Ação para remover item

### Comportamento esperado

- A lista deve refletir exatamente os itens adicionados anteriormente
- Aumentar quantidade deve atualizar os valores em tempo real
- Diminuir quantidade deve respeitar o mínimo de `1`
- Remover item deve atualizar o resumo financeiro imediatamente
- Caso existam observações vindas da tela de detalhe do produto, recomenda-se exibir abaixo da descrição

### Padronização visual

- Card com fundo claro
- Conteúdo organizado horizontalmente
- Nome em destaque
- Descrição em texto secundário
- Preço com destaque médio
- Seletor de quantidade com borda laranja/coral
- Ícone de remover com área de toque confortável

---

> Cupom de Desconto

- **Função:** Permitir adicionar ou aplicar um cupom promocional ao pedido

### Elementos identificados

- Ícone de cupom/ticket
- Texto: `Adicionar cupom de desconto`
- Seta indicativa de ação

### Comportamento esperado

- Ao tocar, deve abrir modal, bottom sheet ou próxima tela
- Deve permitir informar o código manualmente
- Deve validar o cupom
- Deve impactar o valor de desconto no resumo financeiro
- Se houver cupom aplicado, deve permitir remoção posterior

---

> Forma de Recebimento

- **Função:** Permitir ao usuário escolher como deseja receber o pedido

### Título da seção

- `Como deseja receber?`

### Opções previstas

- `Entrega` 
- `Retirada no local`

### Conteúdo mínimo por opção

- Ícone ou ilustração
- Usar imagem (./images/moto.svg) para entrega
- Usar imagem (./images/loja.svg) para retirada no local
- Título
- Descrição curta
- Prazo estimado
- Valor/custo
- Indicador visual de seleção

**ajustar imagens para um bom encaixe**

### Comportamento esperado

- Apenas uma opção pode ficar ativa por vez
- Ao selecionar `Entrega`:
  - considerar taxa de entrega
  - exibir prazo estimado
  - recalcular o total
- Ao selecionar `Retirada no local`:
  - zerar taxa de entrega
  - atualizar o total
  - exibir prazo de retirada

### Padronização visual

- Card clicável
- Opção ativa com borda laranja/coral
- Opção inativa com borda neutra/cinza
- Indicador visual claro de seleção

---

> Resumo Financeiro

- **Função:** Exibir a composição do valor final do pedido

### Campos obrigatórios

- `Subtotal`
- `Taxa de entrega`
- `Descontos`
- `Total`

### Regras funcionais

- **Subtotal:** soma de todos os itens do carrinho
- **Taxa de entrega:** depende da forma de recebimento
- **Descontos:** valor aplicado por cupom ou campanha
- **Total:** subtotal + taxa - descontos

### Comportamento esperado

- Atualização em tempo real ao alterar quantidade, remover item, aplicar cupom ou trocar forma de recebimento
- O campo `Total` deve possuir o maior destaque visual da seção

---

> Ações Finais

### Botão principal

- **Elemento identificado:** Botão largo com texto `Continuar`
- **Subtexto auxiliar:** `Escolha o endereço e o pagamento`
- **Função:** Levar o usuário para a próxima etapa do checkout

### Comportamento esperado

- Deve ser a ação de maior destaque da tela
- Se o carrinho estiver vazio, deve ficar desabilitado

---

### Ação secundária

- **Elemento identificado:** Texto ou botão `Voltar ao cardápio`
- **Função:** Permitir retornar à listagem de produtos (TelaInicial.md)

### Comportamento esperado

- Não deve limpar o carrinho
- Deve possuir estilo secundário, sem competir com o botão principal

---

## Especificação Visual

### Paleta de Cores

- **Laranja/Coral:** usado em botões principais, seleção ativa, destaque de total e seletor de quantidade
- **Vermelho:** usado em ações críticas ou ícones de remoção, quando necessário
- **Bege/Creme claro:** fundo geral
- **Branco:** cards e superfícies
- **Cinza escuro / preto:** textos principais
- **Cinza médio/claro:** descrições, bordas, divisores e estados inativos

<  --app-primary: #f57c52;
   --app-primary-dark: #e5673f;
   --app-accent-red: #e53935;
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
  - Nome do produto: semibold
  - Descrição: regular
  - Preço: semibold/bold
  - Total: maior destaque da área financeira

- **Nome fonte**
  - Google Fonts Nunito Sans

---

### Bordas e Formas

- Layout moderno, amigável e arredondado
- Cards com cantos suaves
- Botões com bordas arredondadas
- Seletor de quantidade pequeno, arredondado e consistente com a tela 2

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

- Espaçamento confortável entre blocos
- Boa separação entre cards
- Respiro visual entre resumo financeiro e ações finais
- Área inferior suficiente para não conflitar com safe area

---

## Padronização de Botões

> Botão primário

- **Uso:**
  - Continuar
  - Confirmar ação principal

- **Padrão:**
  - Fundo laranja/coral
  - Texto branco
  - Bordas arredondadas
  - Largura ampla
  - Pode conter ícone
  - Deve ser o elemento mais evidente da tela

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
  - Limpar carrinho
  - Remover item

- **Padrão:**
  - Área de toque confortável
  - Ícone claro
  - Feedback visual ao toque

---

> Botão de quantidade

- **Uso:**
  - Aumentar e diminuir quantidade

- **Padrão:**
  - Borda laranja/coral
  - Fundo claro
  - Valor centralizado
  - Aparência consistente com a tela de detalhe do produto

---

## Componentização Recomendada

> Para Angular 20 + Ionic, recomenda-se quebrar a tela em componentes reutilizáveis.

---

## Componentes sugeridos

- **cart-header.component**
  > Responsável por:
  - botão voltar
  - título
  - subtítulo
  - limpar carrinho

---

- **cart-item-card.component**
  > Responsável por:
  - imagem
  - nome
  - descrição
  - preço
  - quantidade
  - remover item

---

- **coupon-entry.component**
  > Responsável por:
  - ação para adicionar cupom

---

- **delivery-method-selector.component**
  > Responsável por:
  - entrega
  - retirada no local
  - controle da opção ativa

---

- **order-summary.component**
  > Responsável por:
  - subtotal
  - taxa
  - desconto
  - total

---

- **cart-footer-actions.component**
  > Responsável por:
  - botão continuar
  - voltar ao cardápio

---

## Regras Funcionais

### Carrinho

> Deve manter estado global entre as telas
> Deve refletir os itens adicionados anteriormente
> Deve atualizar valores em tempo real
> Deve permitir persistência local ou backend

---

### Quantidade

- Quantidade mínima por item: `1`

> Ao alterar quantidade:

- atualizar subtotal
- atualizar total
- refletir alteração imediatamente na interface

---

### Remoção

- Remover item individualmente deve atualizar lista e resumo financeiro
- Limpar carrinho deve exigir confirmação antes da exclusão total

---

### Cupom

- Deve ser opcional
- Deve impactar o campo de desconto
- Deve permitir remoção posterior

---

### Recebimento

- Apenas uma opção ativa por vez
- `Entrega` adiciona taxa
- `Retirada no local` remove a taxa de entrega

---

### Navegação

- Clique no botão voltar deve retornar para a tela anterior
- Clique no botão continuar deve navegar para a próxima etapa do checkout
- Clique em voltar ao cardápio deve manter o carrinho salvo

---

## Acessibilidade

> Requisitos recomendados:

- Botões com `aria-label`
- Contraste adequado entre texto e fundo
- Área de toque mínima de 44x44px
- Ícones com significado claro
- Feedback visual para ações de quantidade, remoção e limpeza

> Exemplos

<aria-label="Voltar"
aria-label="Limpar carrinho"
aria-label="Remover item do carrinho"
aria-label="Diminuir quantidade"
aria-label="Aumentar quantidade"
aria-label="Continuar para checkout" />

---

## Responsividade e Comportamento Mobile

**Como a tela é claramente mobile-first:**

### Requisitos

- Layout otimizado para smartphones
- Scroll vertical fluido
- CTA principal com destaque visual
- Compatível com Android/iOS
- Respeitar `ion-safe-area`
- Ajustar espaçamento inferior para não conflitar com rodapé fixo ou safe area

---

## Critérios de Aceite

### Funcionais ✅

- Exibir header com voltar, título, subtítulo e limpar carrinho
- Exibir itens adicionados com imagem, nome, descrição, preço e quantidade
- Permitir alterar quantidade por item
- Permitir remover item individualmente
- Permitir limpar carrinho completo
- Exibir ação para adicionar cupom
- Permitir escolher entre entrega e retirada
- Atualizar taxa e total conforme forma de recebimento
- Exibir subtotal, taxa, descontos e total
- Permitir continuar para a próxima etapa
- Permitir voltar ao cardápio sem perder o carrinho

---

### Visuais 🎨

- Manter identidade visual das telas anteriores
- Usar laranja/coral como cor principal
- Padronizar botões, cards e seletores
- Destacar visualmente a opção selecionada
- Destacar o valor total

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
- Estrutura preparada para carrinho global e atualização reativa

### Ionic

- ion-content
- ion-button
- ion-icon
- ion-list ou estrutura customizada
- ion-radio ou seleção customizada
- ion-footer ou rodapé fixo, se necessário

### Estilo

- SCSS modular
- Tokens de cor no tema global
- Componentes isolados por responsabilidade

---

## Resumo Executivo

> A Tela 3 deve funcionar como a etapa de revisão do pedido, garantindo clareza, rapidez e consistência visual com as telas anteriores.

### Resultado esperado

- O usuário consegue revisar o pedido com facilidade
- Os valores são recalculados em tempo real
- A escolha entre entrega e retirada é clara
- O fluxo para checkout acontece sem fricção

## APIs do Backend

### 1. Preview do Pedido (recomendado ao alterar forma de recebimento)

```http
POST /api/checkout/preview
```
**Pública** (`AllowAnonymous`). Calcula o resumo sem criar no banco.

**Request:**
```json
{
  "storeId": "guid...",
  "fulfillmentType": 1,
  "items": [
    { "productName": "Smash Burguer", "quantity": 2, "unitPrice": 28.90 },
    { "productName": "Coca-Cola Lata", "quantity": 1, "unitPrice": 5.90 }
  ]
}
```

> `fulfillmentType`: `1` = Delivery, `2` = PickUp (Retirada)\
> `customerAddressId` e `paymentMethod` são **opcionais** no preview — podem ser omitidos nesta tela.

**Response 200:**
```json
{
  "storeId": "guid...",
  "fulfillmentType": 1,
  "subtotal": 63.70,
  "deliveryFee": 5.90,
  "minimumOrderValue": 15.00,
  "total": 69.60,
  "storeIsOpen": true
}
```

> **Regra:** Para retirada (`fulfillmentType = 2`), `deliveryFee` = 0 e `minimumOrderValue` = 0.\
> **Regra:** Se a loja estiver fechada, retorna `409 - Store is closed`.

### 2. Preview com Método de Pagamento (opcional)
Quando o usuário também já tiver escolhido o endereço e método de pagamento (em fluxo posterior), enviar:
```json
{
  "storeId": "guid...",
  "fulfillmentType": 1,
  "customerAddressId": "guid...",
  "paymentMethod": 3,
  "items": [ ... ]
}
```

> `paymentMethod`: `1`=PixOnline, `2`=CardOnline, `3`=CashOnDelivery, `4`=CardOnDelivery

### 3. Botão Continuar
O botão `Continuar` da tela **não chama API diretamente**. Ele apenas valida os dados e navega para `04-CadastroCliente.md`. O pedido só será confirmado ao final do fluxo.

### Fluxo de Dados na Tela
1. Carrinho mantido em estado global (CartService com persistência local)
2. Ao selecionar forma de recebimento, chamar `POST /api/checkout/preview` para calcular valores
3. Exibir os valores retornados na tela
4. Cupom de desconto não implementado no backend — manter como UI futura

### Enums
| Campo | Valores |
|-------|---------|
| `FulfillmentType` | `1` = Delivery, `2` = PickUp |


