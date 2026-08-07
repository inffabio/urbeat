# 04 — Front da Loja: Parametrização por CuisineType (Delivery de Comida)

> **Escopo:** o Urbeat é um app de **delivery de comida**. Não é multi-segmento (não há farmácia, mercado, petshop). A parametrização do front da loja é dirigida pelo **`CuisineType`** — o **tipo de comida da loja** (Hamburgueria, Pizzaria, Açaiteria, Restaurante, Japonesa, Doceria, etc.).

## 1. Premissa: 1 loja = 1 tipo de comida (CuisineType) + complementos

Cada loja cadastrada tem **um `CuisineType` principal**, que define **o tipo de produto central e suas variações**. Uma loja, a princípio, **não vende pizza e hambúrguer juntos** — ela é uma hamburgueria **ou** uma pizzaria. Porém, toda loja tem **categorias complementares** que acompanham o lanche/refeição:

```
Loja (CuisineType = Hamburgueria)
├── Produto principal: Hambúrgueres (com variações: ponto da carne, adicionais, molhos)
└── Complementos (categorias): Combos · Bebidas · Acompanhamentos · Sobremesas
```

- **Produto principal** → segue o modelo de opções do `CuisineType` (ex.: pizza tem tamanho→sabores→borda; açaí tem tamanho→frutas→caldas).
- **Complementos** → categorias genéricas que **quase toda loja de comida tem**: Bebidas, Acompanhamentos, Sobremesas, e o especial **Combos**.

> O `CuisineType` **não** transforma a loja em outro segmento; ele apenas **sugere o modelo de produto/opções e a terminologia** adequada àquele tipo de comida.

## 2. O que o CuisineType parametriza

| Parâmetro | Como o CuisineType influencia |
|---|---|
| **Modelo de opções do produto principal** | Hamburgueria → "Ponto da carne + Adicionais + Molhos"; Pizzaria → "Tamanho + Sabores + Borda + Extras"; Açaiteria → "Tamanho + Frutas + Caldas + Crocantes". |
| **Terminologia** | "Sabores" (pizza/açaí), "Ponto da carne" (hambúrguer), "Montar" (açaí). |
| **Categorias sugeridas** | Além do principal, já sugere **Combos, Bebidas, Acompanhamentos, Sobremesas**. |
| **Layout do card** | Comida em geral usa lista com imagem grande (apetite). Pizzaria pode agrupar por tamanho. |
| **Modelos de grupo de opções** (no cadastro) | Biblioteca 1-clique conforme o tipo (ver doc 05). |

> Tudo isso é **sugestão/preset editável** — o lojista ajusta livremente. O `CuisineType` continua sendo também o rótulo usado na descoberta/listagem pública da loja.

## 3. Tipos de comida (CuisineType) e seus modelos

Exemplos de tipos de comida e o **modelo de produto principal** de cada um:

### 3.1 Hamburgueria / Lanches
- Produto principal: hambúrguer.
- Opções: **Ponto da carne** (única, obrigatória) · **Adicionais** (múltipla) · **Molhos** (múltipla, máx. N).
- Complementos: Combos, Bebidas, Acompanhamentos (batata, onion), Sobremesas.

### 3.2 Pizzaria
- Produto principal: pizza.
- Fluxo: **Tamanho** → **Sabores** (1 a N conforme tamanho, meio a meio) → **Borda** (única) → **Extras** (múltipla).
- Complementos: Bebidas, Bordas, Sobremesas.

### 3.3 Açaiteria / Sorveteria
- Produto principal: açaí montável.
- Fluxo: **Tamanho** (obrigatório, muda preço-base) → **Frutas** → **Cremes/Caldas** → **Crocantes** → **Extras**.
- Complementos: Bebidas, Adicionais.

### 3.4 Restaurante / Comida caseira / Marmita
- Produto principal: prato/marmita.
- Opções: **Tamanho** (P/M/G ou marmita) · **Acompanhamentos** (múltipla, min/max) · **Ponto/preparo**.
- Complementos: Bebidas, Sobremesas, Entradas.

### 3.5 Japonês / Combinados
- Produto principal: combinado.
- Opções: **Montagem de combinado** (escolha N peças) · adicionais grátis (hashi, shoyu).
- Complementos: Bebidas, Temaki avulso, Sobremesas.

### 3.6 Doceria / Confeitaria / Padaria
- Produto principal: doces/bolos.
- Opções: **Tamanho** · **Sabor** · **Mensagem** (texto no bolo). Pode exigir **agendamento** (encomenda).
- Complementos: Bebidas, Salgados.

> Todos são **comida**. A diferença entre eles é apenas o **modelo de opções do produto principal** — o que já é 100% coberto pelos `ProductOptionGroup` do backend (ver doc 05).

## 4. Combos e Destaques — a ordem do cardápio (análise)

**Sim, isto foi analisado.** É um ponto central da experiência: **quais produtos aparecem primeiro** no cardápio.

### 4.1 Como o protótipo faz
No `index.html`, a ordem das seções é: **Combos → Hambúrgueres → Pizzas → Bebidas → Acompanhamentos → Açaí → Empadas**. Ou seja, **Combos vêm primeiro**, seguidos do produto principal, depois complementos.

### 4.2 Como o front atual faz
- Existe uma **pseudo-categoria "Destaques"** (`isFeatured`): a store-page inicia selecionando a categoria marcada como `isFeatured` (ou a primeira), e ao escolher "Destaques" filtra `p.isFeatured === true` (`store-page.component.ts:43-64, 107-108`).
- O backend expõe endpoint dedicado: `GET /api/public/stores/{storeId}/catalog/products/featured`.
- Produto tem `IsFeatured`, `IsBestSeller`, `IsNew` e `TagPriority` (ordem de exibição das tags) — já cadastráveis no wizard (seção "Organização").

### 4.3 Recomendação (ordem do cardápio)
Definir uma **ordem canônica de exibição** no front da loja:

1. **Destaques** (produtos `isFeatured`) — vitrine no topo.
2. **Combos** — categoria de maior ticket/atalho de conversão.
3. **Produto principal** do `CuisineType` (Hambúrgueres, Pizzas, Açaí…).
4. **Complementos**: Bebidas, Acompanhamentos, Sobremesas.
5. Demais categorias por `DisplayOrder`.

Parametrizações sugeridas:
- **Ordem de categorias** arrastável pelo lojista (já existe `DisplayOrder` em `ProductCategory`).
- **Marcar categoria como "vitrine/topo"** (já existe `IsFeatured` em `ProductCategory`).
- **Seções especiais** ligáveis/desligáveis: Destaques, Mais vendidos, Novidades (mapear `IsBestSeller`/`IsNew`).
- Combos como categoria padrão criada no onboarding (junto com Bebidas/Acompanhamentos/Sobremesas).

## 5. Busca e filtros (análise)

### 5.1 Barra de busca
- **Protótipo e front atual têm busca por texto** que filtra por **nome e descrição** do produto (`store-page.component.ts:56-62`).
- **Recomendação:** manter a **busca direta na barra** como filtro primário — é o comportamento esperado em delivery de comida. Busca deve varrer nome + descrição (e, opcionalmente, categoria).

### 5.2 Chips/tabs de categoria = o "filtro" real
- O filtro natural do cardápio são os **chips de categoria** (Todos, Combos, Hambúrgueres, Bebidas…), com rolagem horizontal. Isso já cobre a necessidade de filtragem.

### 5.3 Botão de filtro (ícone de configuração ao lado da busca) — **REMOVER**
- No protótipo (`index.html`), ao lado da busca existe um **botão `filter-btn` com o ícone `bi bi-sliders`** (ajustes/configuração). **Ele não tem finalidade** — não há tela/ação de filtros avançados, e as categorias já filtram.
- **Ação:** **remover o `filter-btn`** e deixar a **barra de busca ocupar 100% da largura**. Menos poluição, mais clareza.
- Se no futuro houver necessidade real de filtros avançados (ex.: "só promoções", "sem glúten"), aí sim reintroduzir um controle com função definida — não antes.

## 6. Modelo de dados (mínimo, sem multi-segmento)

Não é necessário criar `StoreProfile`/`EstablishmentType`. O suficiente:

- **`CuisineType`** (já existe) — tipo de comida da loja. Pode ganhar um campo opcional para vincular **modelos de grupo de opções sugeridos** (ver doc 05) e a **terminologia**.
- **`ProductCategory`** (já existe) — usar `DisplayOrder` + `IsFeatured` para a ordem/vitrine do cardápio.
- **`Product`** (já existe) — `IsFeatured`, `IsBestSeller`, `IsNew`, `TagPriority` para Destaques/Mais vendidos/Novidades.

Opcional (melhoria de tema visual da loja):
- Cor primária por loja (tema) — **um** campo `PrimaryColorHex` na Store, se quiser permitir personalização de marca. Não é obrigatório para o foco atual.

## 7. Resumo das mudanças neste documento (vs versão anterior)

- ❌ Removido: conceito de **multiestabelecimento** e segmentos não-comida (mercado, farmácia, petshop) e o `StoreProfile/EstablishmentType`.
- ✅ Ajustado: parametrização passa a ser por **`CuisineType`** (tipo de comida), **1 loja = 1 tipo + complementos** (Combos/Bebidas/Acompanhamentos/Sobremesas).
- ✅ Adicionado: análise de **Combos/Destaques** e **ordem do cardápio**.
- ✅ Adicionado: análise de **busca e filtros** e recomendação de **remover o ícone de configuração** ao lado da barra de busca.
