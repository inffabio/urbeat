# 09 — Cadastro de Produto: Grupos de Opções (reformulado)

Especificação técnica do **item 3 — Grupos de opções (Personalizações)** do cadastro de produto, remodelando o fluxo de edição no painel do vendedor (baseado em `ConceitoGrupos.png`) e a renderização dinâmica nos formatos visuais do cliente (baseados em `Aplicacao1.png` e `Aplicacao2.png`).

---

## 1. O painel do vendedor — cadastro de grupos (`ConceitoGrupos.png`)

O lojista gerencia os grupos de opções através de uma lista de **Accordions colapsáveis** chamada **"2. Personalizações"**.

### 1.1 O Accordion colapsado (Visão Geral)

Cada linha de grupo fechada deve conter (da esquerda para a direita):

1. **Drag Handle (`::`)** — Ícone linear para arrastar o container inteiro e reordenar a exibição (atualiza o `displayOrder` do grupo). **Ativo apenas quando o container estiver fechado.**
2. **Ícone Temático do Grupo** — Um ícone colorido identificando visualmente a categoria (ex.: Ícone de Expansão verde para Tamanho, Alerta laranja para Sabor, Mais `+` azul para Adicionais, Gota roxa para Molhos).
3. **Badge Obrigatório / Opcional**:
   - `Obrigatório` (verde/rosa conforme layout do sistema) — exibido quando `minChoices ≥ 1`.
   - `Opcional` (cinza/azul) — exibido quando `minChoices = 0`.
4. **Resumo textual (Título e Regra)**:
   - **Título**: Nome do grupo (ex.: `Escolha o Tamanho`, `Adicionais`).
   - **Subtítulo (Linha de Resumo)**: Lógica automática baseada nos valores de `min` e `max` (ver §1.3).
5. **Indicadores de Limites** — Rótulo sutil `Min: X` e `Máx: Y` exibindo os limites em texto.
6. **Tipo de Exibição** — Rótulo sutil `Tipo: Checkbox`, `Tipo: Botões`, `Tipo: Lista` ou `Tipo: Chips`.
7. **Seta Ionic (Chevron `v` / `^`)** — Abre/fecha o container.
8. **Botão de Exclusão (Lixeira vermelha)** — Abre modal de confirmação ("Deseja mesmo excluir o grupo?").
9. **(Removido)**: O ícone de lápis para editar nome foi eliminado. A edição ocorre diretamente abrindo o container.

### 1.2 O Accordion aberto (Edição de Grupo)

Ao clicar para expandir o Accordion do grupo (ex.: `Editando grupo: Adicionais`), o painel de edição se estende abaixo contendo:

- **Nome do Grupo** — Campo de texto obrigatório.
- **Tipo do Grupo** (Rádio):
   - `Obrigatório` (ao marcar, força `minChoices = 1`).
   - `Opcional` (ao marcar, força `minChoices = 0`).
- **Exibir como** (Listbox/Dropdown):
   - `Checkbox` · `Botões` · `Lista` · `Chips`.
- **Cálculo do preço** (Listbox/Dropdown) — como os itens selecionados afetam o preço do produto:
   - `Somar` (`add`) — cada item marcado **soma** seu valor ao preço-base. Padrão para adicionais/complementos.
   - `Substituir` (`replace`) — o item selecionado **substitui** o preço-base. Usado em **Tamanho** (P/M/G).
   - `Maior valor` (`highest`) — o preço final é o do **item mais caro** entre os selecionados. **Usado em sabores de pizza (meio a meio): vários sabores → paga-se o mais caro.**
   - `Média` (`average`) — média dos preços dos itens selecionados.
- **Quantidade Mínima** — Campo numérico (bloqueado/oculto se o tipo for single-choice).
- **Quantidade Máxima** — Campo numérico (bloqueado em 1 se o tipo for single-choice).
- **Itens do Grupo** — Seção com os itens cadastrados:
  - Cada item é uma linha com **Drag Handle (`::`)** para reordenar, **Nome do Item** (input), **Preço (R$)** (input) e **Lixeira de exclusão** (com confirmação).
  - Botão **`+ Adicionar Item`** no canto superior direito da tabela de itens para criar linhas novas.

### 1.3 Lógica automática para Subtítulos (Linha de Resumo)

O subtítulo abaixo do nome do grupo fechado é gerado dinamicamente:

- Se `M = 1` e `m = 1` → **"Escolha 1 opção"**
- Se `M = 1` e `m = 0` → **"Escolha 1 opção (opcional)"**
- Se `M > 1` e `m = 0` → **"Escolha até {M} opções"**
- Se `M > 1` e `m = M` → **"Escolha {M} opções"**
- Se `M > 1` e `0 < m < M` → **"Escolha de {m} a {M} opções"**

### 1.4 Crítica e Validações no Cadastro
- `maxChoices ≥ 1` sempre.
- `minChoices ≥ 0` e `minChoices ≤ maxChoices`.
- ❌ **Bloqueio Crítico**: Não é permitido `minChoices = 0` e `maxChoices = 0`. Exibir mensagem: *"A quantidade máxima de escolhas deve ser de pelo menos 1."*

---

## 2. A renderização no Front do Cliente (`Aplicacao1.png` e `Aplicacao2.png`)

O componente `product-detail-page` lê o `displayStyle` do grupo e renderiza os itens usando um dos 4 formatos predefinidos. Cada formato define o comportamento de seleção:

### 2.1 formato `Botões` (Seleção Única ou Múltipla)
- **Visual**: Grid de botões com cantos arredondados (ex.: Ponto da carne, Tamanhos de Açaí, Borda Recheada, Sabores de Pizza, Extras da Pizza).
- **Comportamento**:
  - Se for **Seleção Única** (ex.: `Tamanho` ou `Ponto da carne`): Clicar em um botão ativa-o, exibindo uma borda vermelha e uma marca de seleção (check circular vermelho no topo direito). Os outros botões são desmarcados.
  - Se for **Seleção Múltipla** (ex.: `Ingredientes extras` da pizza em grid 2x3 ou `Escolha os sabores` de pizza em grid 2x2): O cliente pode selecionar até o limite `maxChoices`. Cada item clicado fica ativo (borda e check circular vermelho).
  - Se o preço do item for `> 0`, o botão exibe o preço abaixo do nome (ex.: `+ R$ 8,00`).

### 2.2 formato `Checkbox` (Múltipla Escolha)
- **Visual**: Lista vertical confortável. Cada linha mostra um box quadrado com cantos arredondados (check-box) à esquerda, o nome do item e o preço de acréscimo à direita (ex.: `+ R$ 4,50`).
- **Comportamento**: Permite selecionar múltiplos itens. Ao marcar, o quadrado fica preenchido com a cor primária (`#D54A51`) e exibe um checkmark branco. Respeita o limite `maxChoices`.

### 2.3 formato `Lista` (Seleção Única em Lista)
- **Visual**: Lista vertical de opções com botão de rádio circular (radio-dot) à esquerda, nome do item e preço à direita.
- **Comportamento**: Ao selecionar uma opção, o círculo preenche-se com a cor primária e um ponto branco interno, desmarcando a opção anterior.

### 2.4 formato `Chips` (Múltipla Escolha Compacta)
- **Visual**: Fileiras horizontais de chips/pílulas arredondadas que quebram de linha automaticamente (ex.: Frutas, Cremes e caldas, Crocantes, Extras no Açaí).
- **Comportamento**: Ideal para muitos opcionais compactos. Cada chip tem uma borda leve e um pequeno box quadrado interno à esquerda. Ao clicar, o chip ganha fundo suave (`#FDECEE`), borda da cor da marca, e o quadradinho preenche-se de vermelho com um checkmark branco.

---

## 3. Modelo de dados e integração (Backend e Frontend)

Para dar suporte a esse comportamento dinâmico sem quebrar a estrutura existente, estendemos o modelo:

### 3.1 Entidades e DTOs (Backend)

Em `ProductOptionGroup` (adicionar campo):
```csharp
public string DisplayStyle { get; set; } = "checkbox"; // "buttons" | "list" | "checkbox" | "chips"
public string PriceMode   { get; set; } = "add";       // "add" | "replace" | "highest" | "average"
```

O campo `ChoiceType` (`"single" | "multiple"`) é mantido para compatibilidade e é sincronizado automaticamente:
- Se `DisplayStyle` for `"buttons"` ou `"list"` → `ChoiceType = "single"` (e força `maxChoices = 1`).
- Se `DisplayStyle` for `"checkbox"` ou `"chips"` → `ChoiceType = "multiple"`.

O `PriceMode` define como o preço dos itens selecionados compõe o preço do produto (ver §2 e doc 05 §1.1):
- `add` (padrão): soma os itens selecionados.
- `replace`: o item selecionado substitui o preço-base (Tamanho).
- `highest`: o **maior** preço entre os selecionados (**sabores de pizza / meio a meio**).
- `average`: média dos selecionados.

> **Onde o preço é calculado:** a regra `PriceMode` é aplicada tanto no **carrinho** (exibição) quanto — de forma autoritativa — no **checkout do backend** (fonte da verdade / anti-fraude).

### 3.2 Integração no Frontend

O componente de detalhe de produto no cliente renderiza dinamicamente usando um bloco de `@switch` ou `@if` baseado em `group.displayStyle`:

```html
@for (group of product.optionGroups; track group.id) {
  <section class="options-section">
    <div class="options-header">
      <div class="options-title-row">
        <h3>{{ group.name }}</h3>
        <span class="badge" [class.required]="group.isRequired">
          {{ group.isRequired ? 'Obrigatório' : 'Opcional' }}
        </span>
      </div>
      <p class="options-subtitle">{{ getGroupSubtitle(group) }}</p>
    </div>

    @switch (group.displayStyle) {
      @case ('buttons') {
        <div class="grid-buttons">
          @for (item of group.items; track item.id) {
            <button class="btn-option" [class.active]="isSelected(group, item)" (click)="selectItem(group, item)">
              @if (isSelected(group, item)) {
                <span class="check-badge"><i class="bi bi-check"></i></span>
              }
              <strong>{{ item.name }}</strong>
              @if (item.price > 0) {
                <small>+ R$ {{ item.price | brl }}</small>
              }
            </button>
          }
        </div>
      }
      @case ('chips') {
        <div class="chips-container">
          @for (item of group.items; track item.id) {
            <label class="chip-option" [class.checked]="isSelected(group, item)">
              <input type="checkbox" [checked]="isSelected(group, item)" (change)="toggleItem(group, item)">
              <span class="chip-box"></span>
              <span>{{ item.name }}</span>
            </label>
          }
        </div>
      }
      <!-- Outros formatos: checkbox e list -->
    }
  </section>
}
```

---

## 4. Ordem e Fluxo de Interações

1. **Botão Adicionar Grupo** no wizard de produtos do vendedor:
   - Adiciona um novo `ProductOptionGroup` vazio à coleção de sinais.
   - Define o accordion correspondente como **aberto** automaticamente.
   - O lojista define nome, tipo de exibição, min/max (se aplicável).
2. **Botão "+ Adicionar Item"** no grupo:
   - Adiciona uma linha de `ProductOptionItem` com inputs para nome e preço, prontos para digitação.
3. **Reordenação (Drag e Drop)**:
   - Ativo somente nos containers de grupos que estiverem **fechados** (colapsados), utilizando o Drag Handle no início da linha para reposicionar visualmente. Atualiza o array ordenado de opções.
4. **Fechamento**:
   - Ao colapsar o accordion, a linha exibe o resumo imediato (ex.: `Adicionais - Escolha até 5 opções`, badge `Opcional`).

---

## 5. Casos de Uso com o Novo Design

### A) Ponto da carne (Hambúrguer)
- **Exibir como**: `Botões`
- **Tipo**: `Obrigatório` (`min=1`, `max=1`)
- **Itens**: Ao ponto para mal (grátis) / Ao ponto (grátis) / Bem passada (grátis)
- **Front do cliente**: Grid de 3 botões horizontais; clicar em um marca com check e desmarca o outro.

### B) Adicionais (Hambúrguer)
- **Exibir como**: `Checkbox`
- **Tipo**: `Opcional` (`min=0`, `max=5`)
- **Itens**: Bacon (+4,00) / Queijo Extra (+3,00) / Ovo (+2,50) ...
- **Front do cliente**: Lista vertical com checkboxes confortáveis à esquerda e preços adicionais à direita.

### C) Escolha dos sabores (Pizza Grande)
- **Exibir como**: `Botões` (representados como cards maiores de pizza em grid)
- **Cálculo do preço**: `Maior valor` (`highest`) — **ao escolher 2 sabores, o cliente paga o preço do sabor mais caro** (regra meio a meio padrão no Brasil).
- **Tipo**: `Obrigatório` (`min=1`, `max=2`)
- **Itens**: Alho (78,90) / Calabresa (86,90) / Portuguesa (96,90) / Quatro Queijos (97,90)
- **Front do cliente**: Grid 2x2. Permite selecionar até 2 botões simultaneamente. Exemplo: Calabresa (86,90) + Portuguesa (96,90) → produto sai por **R$ 96,90** (o mais caro).

### D) Açaí montável (Ingredientes extras)
- **Exibir como**: `Chips`
- **Tipo**: `Opcional` (`min=0`, `max=5`)
- **Itens**: Banana / Morango / Granola / Leite em pó ...
- **Front do cliente**: Fileira horizontal de chips compactos com emojis, ideais para rolagem rápida e múltipla seleção.
