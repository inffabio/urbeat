
# Tela de Detalhe do Produto - Tela do produto Delivery da Urbeat

---

## ⚠️ Atualização 2026-05 — versão `02-DetalheProduto01.jpeg`

> Esta atualização **prevalece** sobre o que estiver descrito mais abaixo neste documento em caso de conflito.

### Campo "Observações" — comportamento atual

| Item | Especificação |
|---|---|
| Placeholder | `Ex: sem cebola, molho à parte ...` (cor `--app-text-muted`, opacidade 0.85) |
| Limite de caracteres | **250** (`maxlength` no textarea + `[maxlength]` no model) |
| Contador | Texto **decremental** `"{N} caracteres restantes"` |
| Posição do contador | **Dentro do textarea**, canto inferior direito, sobre fundo branco semi-opaco |
| Tamanho do contador | `font-size: 10px`, cor `--app-text-muted` |
| Padding extra do textarea | `padding-bottom` ajustado para 14px adicionais, garantindo que o contador não sobreponha o texto digitado |
| Acessibilidade | Contador tem `aria-live="polite"` para leitores de tela |

### Implementação (Angular)

```html
<div class="obs-wrap">
  <textarea
    id="obs"
    class="obs-input"
    rows="4"
    placeholder="Ex: sem cebola, molho à parte ..."
    [ngModel]="notes()"
    (ngModelChange)="onNotesChange($event)"
    [maxlength]="maxNotes"
    aria-label="Observações do pedido"
  ></textarea>
  <span class="obs-counter" aria-live="polite">
    {{ notesLeft() }} caracteres restantes
  </span>
</div>
```

```scss
.obs-wrap { position: relative; }

.obs-input {
  padding: var(--space-3) var(--space-4) calc(var(--space-3) + 14px);
  /* ... */
}

.obs-counter {
  position: absolute;
  right: var(--space-3);
  bottom: var(--space-2);
  font-size: 10px;
  color: var(--app-text-muted);
  background: var(--app-surface);
  padding: 2px 4px;
  border-radius: var(--radius-sm);
  pointer-events: none;
}
```

---

## Sequencia (01-TelaInicial.md)

## Visão Geral da Tela

- Esta tela representa a página de detalhamento de um produto do cardápio, aberta após o usuário selecionar um item da lista inicial

## Objetivos principais

- 🍔 Exibir o produto com mais destaque visual
- 📝 Apresentar descrição completa
- 💰 Mostrar preço do item
- ✍️ Permitir observações do pedido
- ➖➕ Permitir ajustar quantidade
- 🛒 Adicionar o item configurado ao carrinho
- ⬅️ Permitir voltar para a tela anterior (01-TelaInicial.md)

---

## Estrutura Geral da Tela

- Header com botão de voltar
- Imagem principal do produto
- Informações do produto
- Campo de observações
- Seletor de quantidade
- Botão principal “Adicionar ao carrinho”

---

## Análise Visual e Funcional por Seção

### Header / Navegação Superior

>Função:

- Permitir que o usuário retorne para a tela anterior.

> Elementos identificados :

- Botão de voltar no canto superior esquerdo
- Ícone simples de seta para esquerda
- Posicionado sobre a área superior da tela, com visual limpo
- Comportamento esperado

> Ao tocar no botão:

- navegar para a tela anterior
- ou fechar modal, caso a tela seja aberta como modal
- Deve respeitar a navegação padrão do app

### Requisitos técnicos

> Em Ionic, pode ser implementado com:

- ion-back-button
- ou botão customizado com ion-icon
- Deve respeitar safe-area-top

---

### Imagem Principal do Produto

> Função:

- Dar destaque visual ao item selecionado e reforçar o apelo de compra.

> Elementos identificados:

- Imagem grande da selecão na tela (01-TelaInicial.md)
- Foto centralizada
- Fundo claro, com foco total no produto
- Imagem em alta relevância visual
- Comportamento esperado
- Imagem deve ocupar boa parte da área superior
- Deve ser carregada dinamicamente por URL
- Deve manter proporção adequada sem distorção
- Pode ter fallback caso a imagem não carregue

> Requisitos técnicos:

- Usar ion-img ou img
- Lazy loading recomendado
- object-fit: contain ou cover, conforme a arte disponível

---

### Informações do Produto 🏷️

> Função:

- Exibir os dados principais do item para decisão de compra.

> Campos identificados:

- Nome do produto:  (01-TelaInicial.md)
- Descrição longa: Vindo por API no jason da (01-TelaInicial.md)
- Preço: Ex: R$ 24,90 (01-TelaInicial.md)

> Hierarquia visual esperada:

- Nome com maior destaque
- Descrição em texto secundário
- Preço com destaque visual forte
- Comportamento esperado
- Dados devem ser dinâmicos
- A descrição pode variar de tamanho
- O preço deve ser formatado no padrão BRL

> Requisitos técnicos:

- Usar CurrencyPipe ou formatação customizada
- Permitir descrições longas sem quebrar layout
- Nome e preço devem ser facilmente escaneáveis

---

### Seção “Observações”

> Função:

- Permitir que o usuário informe observações personalizadas para o pedido.

> Elementos identificados:

- Título: Observações
- Campo de texto vazio logo abaixo
- Área com estilo discreto e integrada ao layout
- Exemplos de uso
    - “Sem tomate”
    - “Molho à parte”
    - “Carne ao ponto”
    - “Retirar cebola”

> Comportamento esperado:

- Campo opcional
- Permitir texto livre
- Observação deve acompanhar o item no carrinho
- Limite maximo de 250 caracteres

> Requisitos técnicos:

- Pode ser implementado com:
- ion-textarea
- ou ion-input multilinha customizado

### Seletor de Quantidade ➖➕

> Função:
 - Permitir ajustar quantas unidades do produto serão adicionadas ao carrinho.

> Elementos identificados:

- Botão de menos
- Valor numérico ao centro
- Botão de mais
- Estilo com contorno laranja/coral

> Estado observado

- Quantidade inicial: 1
- Comportamento esperado
- + incrementa a quantidade em 1
- - decrementa a quantidade em 1
- Quantidade mínima deve ser 1
- Quantidade deve refletir imediatamente na interface

> Regras funcionais:

- Não permitir quantidade menor que 1

> Requisitos técnicos:

- Botões com área mínima de toque adequada
- Atualização reativa de estado
- Se desejado, atualizar também o valor total do item

---

### Botão Principal “Adicionar ao carrinho”

> Função:

- Confirmar a seleção do produto com quantidade e observações, adicionando ao carrinho.

> Elementos identificados:

- Botão largo
- Cor laranja/coral
- Label: Adicionar ao carrinho

> omportamento esperado
> Ao clicar:

- adicionar item ao carrinho
- incluir quantidade selecionada
- incluir observações digitadas
- navegar de volta ou exibir feedback visual
- Deve ter destaque máximo na tela

> Regras funcionais:

- Pode mostrar loading enquanto envia ao serviço/carrinho

> Ações  após adicionar

- ✅ Exibir toast: “Item adicionado ao carrinho”
- ✅ Atualizar contador global do carrinho

---

## Componentização Recomendada

### Componentes

- product-detail-header.component

> Responsável por:

- botão voltar
- espaçamento da safe area

---

- product-hero-image.component

> Responsável por:

- imagem principal do produto

---

- product-info.component

> Responsável por:

- nome
- descrição
- preço

---

- product-observations.component

> Responsável por:

- label “Observações”
- textarea/input

---

- quantity-selector.component

> Responsável por:

- botão menos
- quantidade atual
- botão mais

---

- add-to-cart-bar.component

> Responsável por:

- botão principal
- loading state

---

## Regras Funcionais

> Carregamento da tela:

- A tela deve receber o id do produto pela rota
- Deve carregar os dados do item selecionado
- Deve exibir loading spin

---

> Voltar para tela anterior:

- O botão de voltar deve retornar à (01-TelaInicial.md)
- Não deve perder o estado do carrinho global

---

### Adicionar ao carrinho

> Ao clicar em Adicionar ao carrinho, o sistema deve:

- validar quantidade
- coletar observações
- montar payload do item
- enviar ao CartService
- atualizar badge global do carrinho
- dar feedback ao usuário

---


## Responsividade e Comportamento Mobile

**Esta tela é claramente mobile-first.**

> Requisitos:

- Imagem principal grande e centralizada
- Conteúdo com leitura confortável
- CTA facilmente acessível com o polegar
- Respeito à safe area inferior
- Compatível com Android/iOS

---

## Continuidade com a Tela Anterior

> Para manter consistência com a home já especificada, esta tela deve reutilizar:

- 🎨 a mesma paleta de cores
- 🧩 o mesmo padrão de componentização
- 🛒 o mesmo CartService
- 📡 o mesmo MenuService
- 🔤 a mesma tipografia e escala de espaçamento
- 📱 a mesma abordagem mobile-first em Ionic

---

## APIs do Backend

### Observação: Não há endpoint de "detalhe de um único produto"
O backend não possui um endpoint `GET /api/products/{productId}` público. O detalhamento deve ser feito a partir dos dados já carregados no catálogo da tela anterior (01-TelaInicial.md).

### 1. Dados do Produto
Os dados vêm do endpoint já chamado na tela inicial:

```http
GET /api/public/stores/{storeId}/catalog/products
```

**Campos relevantes para esta tela (por produto):**
```json
{
  "id": "guid...",
  "name": "Smash Burguer",
  "description": "Pão brioche, smash de 120g, queijo cheddar, alface, tomate e molho especial",
  "price": 28.90,
  "imageUrl": "https://placehold.co/400x400",
  "isAvailable": true
}
```

> Como o produto já foi carregado na listagem da tela inicial, o front deve passar o objeto completo via estado/params para esta tela, evitando uma nova requisição.

### 2. Inclusão no Carrinho (lado cliente)
O payload do item no carrinho deve conter:
```json
{
  "productId": "guid...",
  "productName": "Smash Burguer",
  "quantity": 2,
  "unitPrice": 28.90,
  "notes": "Sem tomate"
}
```

> Este item é armazenado localmente (CartService) e enviado ao backend apenas no `POST /api/checkout/confirm` (tela 03-Cart.md).