# 📋 Especificação de Desenvolvimento — Página de Cadastro de Produto (Delivery)

## 🧭 Contexto Geral

- **Stack:** Angular 20 + Ionic + .NET 9
- **Página:** Cadastro/Edição de Produto (`produtos.html`)
- **Status atual:** Seções `1. Informações Básicas`, `2. Estoque` e `Prévia do Produto` já estão implementadas
- **Pendente:** Seção `3. Grupos de Opções` e Seção `4. Organização`

---

## ✅ O que já está pronto

- `1. Informações básicas` — Nome, Categoria, Descrição, Preço base, Foto
- `2. Estoque` — Controle de estoque e quantidade disponível
- `Prévia do produto` — Exibição dinâmica em tempo real do produto

---

## 🔧 3. Grupos de Opções

### 📌 Descrição Geral

Esta seção permite que o lojista crie **grupos de personalização** do produto, como tamanhos, pontos de carne, adicionais, etc. Cada grupo contém **opções** com nome e valor.

---

### 🟢 3.1 — Botão `+ Adicionar grupo`

- Exibido abaixo do título da seção
- Ao clicar, **abre um formulário inline** (ou modal/accordion expansível) para criação de um novo grupo
- O novo grupo é inserido na lista abaixo dos grupos já existentes
- Cada grupo criado exibe seu próprio bloco de configuração

---

### 🟡 3.2 — Estrutura de um Grupo

Cada grupo criado contém os seguintes campos e comportamentos:

#### 🏷️ Cabeçalho do Grupo

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| Nome do grupo | `input text` | ✅ Sim | Ex: "Tamanho", "Adicionais", "Ponto da carne" |
| Obrigatório | `toggle/checkbox` | ✅ Sim | Define se o cliente é obrigado a escolher |

- O cabeçalho exibe:
  - **Nome do grupo**
  - **Badge** indicando `Obrigatório` ou `Opcional` (baseado no toggle)
  - **Botão editar** ✎ — recolhe/expande o grupo para edição
  - **Botão excluir** 🗑 — remove o grupo inteiro (com confirmação)

---

#### 🔘 3.3 — Tipo de Escolha

```
( ) Escolha única     ( ) Escolha múltipla
```

- **Radio buttons** — somente uma opção pode ser selecionada
- **Escolha única:**
  - Campos `Mínimo` e `Máximo` ficam **desabilitados/ocultos**
  - O cliente poderá selecionar apenas **1 opção** do grupo
- **Escolha múltipla:**
  - Campos `Mínimo` e `Máximo` ficam **habilitados**
  - Ver detalhes em `3.4`

---

#### 🔢 3.4 — Campos Mínimo e Máximo (apenas Escolha Múltipla)

| Campo | Tipo | Comportamento |
|---|---|---|
| Mínimo | `select` ou `number input` | Quantidade mínima de opções que o cliente deve selecionar |
| Máximo | `select` ou `number input` | Quantidade máxima de opções que o cliente pode selecionar |

**Regras de validação:**

- `Mínimo >= 0`
- `Máximo >= 1`
- `Máximo >= Mínimo`
- Se `Mínimo > 0` → o grupo é automaticamente marcado como **Obrigatório**
- Se `Mínimo == 0` → o grupo é marcado como **Opcional**
- Os selects devem ser populados dinamicamente com base na quantidade de opções cadastradas no grupo

---

#### 📝 3.5 — Lista de Opções do Grupo

Cada grupo possui uma lista de opções. Cada opção contém:

| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| Nome da opção | `input text` | ✅ Sim | Ex: "Pequeno", "Médio", "Grande" |
| Valor (R$) | `input currency` | ✅ Sim | Valor adicional da opção. Pode ser `R$ 0,00` |

**Comportamentos da lista de opções:**

- Exibe todas as opções do grupo em forma de lista
- Cada linha de opção possui:
  - Campo de nome (editável inline)
  - Campo de valor monetário formatado (`R$ 0,00`)
  - **Botão 🗑 excluir** — remove a opção (com confirmação se for a última)
- Botão **`+ Adicionar opção`** ao final da lista — insere uma nova linha em branco
- Deve haver no mínimo **1 opção** por grupo
- A opção recém-adicionada recebe foco automático no campo de nome

---

#### 🔄 3.6 — Reordenação de Grupos

- Os grupos devem poder ser **reordenados via drag and drop** (usando `@angular/cdk/drag-drop` ou equivalente Ionic)
- Ícone de arrastar (`⠿` ou `≡`) visível no cabeçalho de cada grupo
- A ordem dos grupos reflete a ordem de exibição no front do aplicativo

---

#### 🗑️ 3.7 — Exclusão de Grupo

- Botão 🗑 no cabeçalho do grupo
- Exibe um **alert de confirmação** (`ion-alert`) antes de remover:
  > _"Deseja excluir o grupo '[Nome do grupo]'? Esta ação não pode ser desfeita."_
- Ao confirmar, o grupo é removido da lista e do modelo de dados

---

#### ✏️ 3.8 — Edição de Grupo

- Botão ✎ no cabeçalho do grupo
- Alterna o estado do grupo entre **expandido (editável)** e **recolhido (somente leitura)**
- No estado recolhido, exibe apenas:
  - Nome do grupo
  - Badge Obrigatório/Opcional
  - Quantidade de opções cadastradas (ex: `3 opções`)
  - Botões ✎ e 🗑

---

### 🧩 3.9 — Modelo de Dados (TypeScript Interface)

```typescript
interface OpcaoGrupo {
  id: string;           // UUID gerado no front
  nome: string;
  valor: number;        // em centavos ou float
}

interface GrupoOpcoes {
  id: string;           // UUID gerado no front
  nome: string;
  obrigatorio: boolean;
  tipoEscolha: 'unica' | 'multipla';
  minimo?: number;      // apenas para multipla
  maximo?: number;      // apenas para multipla
  opcoes: OpcaoGrupo[];
  ordem: number;        // posição na lista
}
```

---

### 🔴 3.10 — Validações da Seção

- Um produto pode ter **zero ou mais grupos** (grupos são opcionais)
- Um grupo deve ter **nome preenchido**
- Um grupo deve ter **ao menos 1 opção**
- Cada opção deve ter **nome preenchido**
- Em escolha múltipla: `Máximo >= Mínimo >= 0`
- Exibir erros de validação **inline** ao tentar salvar

---

## 🗂️ 4. Organização

### 📌 Descrição Geral

Esta seção permite ao lojista categorizar o produto em **tags de destaque** que serão usadas para agrupar e exibir produtos no front do aplicativo (ex: seção "Destaques", "Mais Vendidos", "Novidades").

---

### 🏷️ 4.1 — Tags de Destaque Disponíveis

| Tag | Descrição |
|---|---|
| ⭐ Destaque | Produto aparece na seção de destaques do app |
| 🔥 Mais vendido | Produto aparece na seção de mais vendidos |
| 🆕 Novidade | Produto aparece na seção de novidades |

- Cada tag é um **checkbox independente**
- O lojista pode selecionar **nenhuma, uma ou todas** as tags simultaneamente
- Selecionar nenhuma é válido — o produto simplesmente não aparece em nenhuma seção especial

---

### 🔢 4.2 — Ordem de Prioridade (quando múltiplas tags selecionadas)

Quando **mais de uma tag** for selecionada, o sistema deve permitir definir a **prioridade de exibição** de cada tag selecionada.

#### 💡 Solução Proposta — Drag and Drop de Prioridade

Ao selecionar 2 ou mais tags, exibe automaticamente uma **lista ordenável** com as tags selecionadas:

```
Ordem de exibição (arraste para reordenar):

  ⠿  1º  ⭐ Destaque
  ⠿  2º  🔥 Mais vendido
  ⠿  3º  🆕 Novidade
```

- A lista usa **drag and drop** para reordenar
- Ao desmarcar uma tag, ela some da lista de prioridade automaticamente
- Ao marcar uma nova tag, ela é **adicionada ao final** da lista de prioridade
- O número de posição (`1º`, `2º`, `3º`) é recalculado automaticamente

#### 🔄 Comportamento dinâmico

| Ação | Resultado |
|---|---|
| Marcar 1 tag | Lista de prioridade não é exibida (sem necessidade) |
| Marcar 2+ tags | Lista de prioridade aparece com drag and drop |
| Desmarcar uma tag | Removida da lista; demais reordenadas automaticamente |
| Marcar tag novamente | Reinserida no final da lista de prioridade |

---

### 🧩 4.3 — Modelo de Dados (TypeScript Interface)

```typescript
type TagDestaque = 'destaque' | 'mais_vendido' | 'novidade';

interface OrganizacaoProduto {
  tags: TagDestaque[];            // tags selecionadas
  prioridade: TagDestaque[];      // ordem de exibição (index 0 = maior prioridade)
}
```

**Exemplo:**

```typescript
{
  tags: ['destaque', 'novidade'],
  prioridade: ['novidade', 'destaque']  // Novidade aparece antes de Destaque
}
```

---

### 🔴 4.4 — Validações da Seção

- Nenhuma tag selecionada é **válido** — produto sem destaque especial
- Se tags selecionadas, a lista `prioridade` deve conter **exatamente as mesmas tags** que `tags`
- Não há campo de texto para ordem — a prioridade é definida **exclusivamente pelo drag and drop**

---

## 🧱 Estrutura de Componentes Sugerida (Angular/Ionic)

```
produto-form/
├── produto-form.component.ts         ← componente pai (já existe)
├── produto-form.component.html       ← template pai (já existe)
│
├── grupos-opcoes/
│   ├── grupos-opcoes.component.ts    ← lista de grupos + botão adicionar
│   ├── grupos-opcoes.component.html
│   ├── grupo-item/
│   │   ├── grupo-item.component.ts   ← um grupo individual (expansível)
│   │   ├── grupo-item.component.html
│   └── opcao-item/
│       ├── opcao-item.component.ts   ← uma linha de opção
│       └── opcao-item.component.html
│
└── organizacao/
    ├── organizacao.component.ts      ← checkboxes + drag and drop de prioridade
    └── organizacao.component.html
```

---

## 🔗 Integração com .NET 9 (API)

### Endpoint sugerido — `POST /api/produtos` / `PUT /api/produtos/{id}`

O payload deve incluir os grupos e organização:

```json
{
  "nome": "X-Burguer",
  "categoriaId": "uuid",
  "descricao": "...",
  "precoBase": 29.90,
  "foto": "url_ou_base64",
  "controlarEstoque": true,
  "quantidadeEstoque": 50,
  "gruposOpcoes": [
    {
      "nome": "Tamanho",
      "obrigatorio": true,
      "tipoEscolha": "unica",
      "minimo": null,
      "maximo": null,
      "ordem": 1,
      "opcoes": [
        { "nome": "Pequeno", "valor": 0.00 },
        { "nome": "Grande", "valor": 5.00 }
      ]
    }
  ],
  "organizacao": {
    "tags": ["destaque", "novidade"],
    "prioridade": ["novidade", "destaque"]
  }
}
```

---

## 📌 Resumo das Features a Implementar

| # | Feature | Complexidade |
|---|---|---|
| 3.1 | Botão `+ Adicionar grupo` | 🟢 Baixa |
| 3.2 | Cabeçalho do grupo (nome, obrigatório, editar, excluir) | 🟡 Média |
| 3.3 | Tipo de escolha (única / múltipla) | 🟢 Baixa |
| 3.4 | Campos Mínimo e Máximo (condicional) | 🟡 Média |
| 3.5 | Lista de opções com adicionar/excluir | 🟡 Média |
| 3.6 | Reordenação de grupos (drag and drop) | 🔴 Alta |
| 3.7 | Exclusão de grupo com confirmação | 🟢 Baixa |
| 3.8 | Edição inline / accordion do grupo | 🟡 Média |
| 4.1 | Checkboxes de tags de destaque | 🟢 Baixa |
| 4.2 | Drag and drop de prioridade de tags | 🔴 Alta |
| 4.3 | Modelo de dados e integração com API | 🟡 Média |

---

> 💡 **Nota para a IA desenvolvedora:** Utilize `@angular/cdk/drag-drop` para os recursos de arrastar e soltar. Para os alerts de confirmação use `ion-alert` do Ionic. Mantenha consistência visual com os componentes já existentes nas seções 1 e 2 da página. Todos os formulários devem usar **Reactive Forms** do Angular.
