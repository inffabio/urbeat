# 05 — Parametrização Avançada no Cadastro de Produtos

O cadastro de produtos **já possui parametrizações** (na aba Produtos do wizard): categorias, preço, imagem, estoque, tags (destaque/mais vendido/novidade) e **grupos de opções** (`ProductOptionGroup`) com tipo de escolha (única/múltipla) e mínimo/máximo. O backend também tem **variações**, **adicionais** e **opções de escolha**. Este documento propõe **expandir** essas parametrizações e, principalmente, **renderizá-las no front do cliente** (que hoje ignora os `optionGroups`).

## 1. O que já existe (base sólida)

- **Grupos de opções** (`ProductOptionGroup` + `ProductOptionItem`): nome, obrigatório, `single`/`multiple`, `minChoices`, `maxChoices`, itens com preço. — **Editável no wizard, mas invisível no app do cliente.**
- **Variações** (`ProductVariation`): tamanhos/variações com preço próprio e preço promocional.
- **Adicionais** (`ProductAdditional`) e **opções de escolha** (`ProductChoiceOption`).
- Estoque (`StockEnabled`/`StockQuantity`), promocional, tags.

> **Ação prioritária #1:** renderizar `optionGroups` na `product-detail-page` do cliente (radio/checkbox/chips conforme `choiceType` e min/max, com badge Obrigatório/Opcional). Isso destrava hambúrguer, açaí, pizza (bordas/extras) e qualquer montagem — reaproveitando o que o lojista já cadastra.

## 1.1 Como um grupo afeta o preço — `priceMode` (conceito fundamental)

Um grupo de opções pode alterar o preço de **quatro** formas. O parâmetro que decide é o **`priceMode`** do grupo:

| `priceMode` | Comportamento | Quando usar |
|---|---|---|
| **`add` (somar)** | Cada opção **soma** seu valor ao preço corrente. | Adicionais (bacon +4,50), borda (+8), extras. Padrão. |
| **`replace` (substituir)** | A opção escolhida **troca** o preço do produto (não soma). | **Tamanho** P/M/G, volume de bebida, tamanho de açaí. |
| **`highest` (maior valor)** | O preço final é o do **item mais caro** entre os selecionados. | **Sabores de pizza (meio a meio):** vários sabores → paga-se o mais caro. |
| **`average` (média)** | Média dos preços dos itens selecionados. | Meio a meio com cobrança proporcional. |

### Efeito no "preço do produto"

Quando o produto tem um grupo **Tamanho em `replace`**, o preço cadastrado deixa de ser fixo e passa a ser exibido como **"a partir de"** — automaticamente igual ao **menor tamanho**. Para sempre haver um preço válido:

- Grupo Tamanho **obrigatório** (`single`, `minChoices = 1`).
- Uma opção marcada como **padrão** (`isDefault`, normalmente o menor), já pré-selecionada.

**Exemplo (`replace`):**
```
Açaí — grupo "Tamanho" (single, replace, obrigatório):
  ○ 300ml → R$ 18,90   (isDefault)
  ○ 500ml → R$ 24,90
  ○ 700ml → R$ 32,90
```
- Cardápio mostra **"a partir de R$ 18,90"**.
- Escolher 700ml → preço vira **R$ 32,90** (substitui, não soma).
- Grupos em `add` (caldas, adicionais) somam **depois**.

> **Nota técnica:** o backend já guarda preço absoluto em `ProductVariation.Price`, mas o front do cliente hoje **soma** a variação ao preço-base (comportamento `add` fixo). Para P/M/G funcionar corretamente, o front precisa respeitar `priceMode = replace`. Ver doc 06.

## 2. Ideias de novas parametrizações (por prioridade)

### 2.1 Layout do grupo de opções (visual)
Parâmetro `displayStyle` no grupo: `radio-list` | `checkbox-list` | `chips` | `stepper-grid` | `card-grid`.
- Açaí usa `chips` (com emoji). Ponto da carne usa `card-grid`. Adicionais usam `checkbox-list`.
- Campo opcional `emoji`/`icon` por item.

### 2.2 Itens grátis por grupo (`freeQuantity`)
"Escolha até 3 acompanhamentos grátis; a partir do 4º, +R$2,00 cada." — muito usado em açaí, marmita, self-service.

### 2.3 Quantidade por item dentro do grupo (`allowItemQuantity`)
Permitir "2x Bacon" no mesmo adicional (stepper por item), com `maxPerItem`.

### 2.4 Meio a meio / múltiplos sabores (pizza)

A pizza deixa de ser "um produto por sabor" e passa a ser **um único produto "Pizza"** com dois grupos que trabalham juntos: **Tamanho** (define o preço-base e quantos sabores cabem) e **Sabores** (aplica a regra de preço).

**a) Grupo "Tamanho" (`replace`)** — cada tamanho define preço-base e `maxFlavors`:

| Tamanho | maxFlavors | fração por sabor |
|---|---|---|
| Broto 25cm | 1 | inteira |
| Média 35cm | 2 | 1/2 |
| Grande 40cm | 2 (ou 3) | 1/2 (ou 1/3) |
| Família 45cm | 4 | 1/4 |

**b) Grupo "Sabores"** — parâmetros:
- `displayStyle: buttons` (cards/botões de sabor) e seleção múltipla até `maxFlavors`.
- `maxChoices` = **herdado do tamanho** (P=1, M/G=2, GG=4).
- `minChoices` — normalmente 1.
- **`priceMode`** (a decisão central do meio a meio — o mesmo campo de §1.1):

| `priceMode` | Cálculo | Uso |
|---|---|---|
| `highest` | preço do **sabor mais caro** entre os escolhidos | **Padrão no Brasil (paga-se o mais caro)** |
| `average` | **média** dos sabores escolhidos | 2ª mais usada |
| `add` | soma dos sabores escolhidos | Cobrança somando os sabores |

**c) Preço do sabor por tamanho** — cada sabor (item do grupo) tem preço por tamanho (matriz **sabor × tamanho**):
```
Calabresa:  Broto 39,90 | Média 49,90 | Grande 59,90 | Família 69,90
Portuguesa: Broto 42,90 | Média 54,90 | Grande 64,90 | Família 74,90
```

**d) Borda / Extras (`add`)** — somam **por cima**, após a regra de sabores. Opcional `appliesTo: whole | perHalf` (padrão `whole`).

**Exemplo (regra `highest`):** Grande, ½ Calabresa (59,90) + ½ Portuguesa (64,90) + borda Catupiry (+8):
```
Sabores (highest) = max(59,90 ; 64,90) = 64,90
+ Borda Catupiry                        =  8,00
= Total                                 = 72,90
```
Com `average`: (59,90 + 64,90) / 2 + 8 = **70,40**.

**Impacto no modelo (backend):** hoje o `OrderItem` guarda **um único** `ChoiceOptionName`. Para meio a meio ele precisa guardar **N sabores** + a fração de cada, e o **cálculo de preço deve ser server-side** (fonte da verdade / anti-fraude):
```
OrderItemFlavor { OrderItemId, FlavorName, FlavorPrice, Fraction (1/2, 1/3...) }
```

**UX de cadastro:** criar produto "Pizza" → definir tamanhos com `maxFlavors` → cadastrar sabores com preço por tamanho (ou planilha) → escolher `priceRule` (1 clique) → adicionar grupos "Borda"/"Extras".

### 2.5 Grupos dependentes / condicionais (`showIf`)
Exibir grupo B só se opção X do grupo A foi escolhida. Ex.: "Tipo de leite" só aparece se escolher "com leite".

### 2.6 Texto livre por grupo (`textInput`)
Mensagem no bolo, nome do aniversariante, observação especial. Limite de caracteres.

### 2.7 Venda por peso / fracionada (comida)
`soldByWeight` + `unit` (kg/g) + `step` (0,1) + `pricePerUnit`. Casos de **comida**: **marmita/comida a quilo/self-service**, açaí por peso, açougue/porções por peso dentro do cardápio.

### 2.8 Combos / kits (bundle)
Produto que agrega outros produtos com desconto. Ex.: "Combo = 1 burger (escolha) + 1 batata + 1 bebida (escolha)". Modelável como grupos `single` obrigatórios que referenciam produtos. **Combos são a categoria de destaque no topo do cardápio** (ver doc 04 §4).

### 2.9 Disponibilidade por horário/dia (`availabilityWindow`)
Produto só no café da manhã, ou marmita só no almoço, ou happy hour. Datas/horas.

### 2.10 Flags de segmento (comida)
- `minimumAge` (18+) — para **bebidas alcoólicas** que complementam a refeição.
- `requiresScheduling` + `leadTimeHours` — **encomenda** (doceria/bolo).

### 2.11 Nutricional / restrições (`dietaryTags`)
Vegano, sem glúten, sem lactose, picante, calorias, porção. Vira badges e filtros.

### 2.12 Limite por pedido (`maxPerOrder`) e mínimo (`minPerOrder`)
Controle de itens promocionais ("máx. 2 por pedido").

## 3. Exemplo: o mesmo modelo cobre todos os tipos de comida

**Hambúrguer**
```
Grupo "Ponto da carne" (single, obrigatório, card-grid): Mal / Ao ponto / Bem passada
Grupo "Adicionais" (multiple, opcional, checkbox): Bacon +4,50 / Cheddar +3,90 ...
Grupo "Molhos" (multiple, máx 2, chips)
Observações (textInput 120)
```

**Açaí**
```
Grupo "Tamanho" (single, replace, obrigatório, isDefault=300ml): 300/500/700ml — substitui o preço
Grupo "Frutas" (multiple, add, freeQuantity=3, chips+emoji)
Grupo "Caldas" (multiple, add, chips+emoji)
Grupo "Crocantes" (multiple, add, chips+emoji)
Observações
```

**Pizza**
```
Grupo "Tamanho" (single, replace, obrigatório): Broto/Média/Grande/Família — define preço-base e maxFlavors
Grupo "Sabores" (flavorSplit, maxFlavors=herdado, priceRule=highest): preço por tamanho (matriz sabor×tamanho)
Grupo "Borda" (single, add, opcional): Catupiry +8 / Cheddar +8
Grupo "Extras" (multiple, add): Pepperoni +5 / Bacon +5 ...
Observações
```

**Marmita / comida a quilo**
```
soldByWeight=true, unit=kg, step=0,1, pricePerUnit
(ou) Grupo "Tamanho" (single): P/M/G + Grupo "Acompanhamentos" (multiple, min/max)
```

**Complemento simples (bebida/sobremesa)**
```
Sem grupos — apenas preço + imagem + observações
```

**Doceria (bolo por encomenda)**
```
Grupo "Tamanho" (single): P/M/G
Grupo "Sabor" (single)
Grupo "Mensagem" (textInput, 40 chars)
requiresScheduling=true, leadTimeHours=48
```

## 4. Melhorias de UX no cadastro (wizard do lojista)

- **Biblioteca de modelos de grupo** conforme o `CuisineType` da loja (1 clique adiciona "Ponto da carne", "Tamanho de açaí", "Borda/Extras", etc.).
- **Pré-visualização em tempo real** de como o produto aparece para o cliente (o wizard já tem "Prévia" — expandir para mostrar os grupos renderizados).
- **Duplicar produto** e **duplicar grupo de opções** entre produtos (evita retrabalho).
- **Validação amigável** já melhorada (preço > 0, imagem obrigatória) — estender para min/max coerentes por `displayStyle`.
- **Categorias de complemento pré-criadas** no onboarding (Combos, Bebidas, Acompanhamentos, Sobremesas).
- **Importação/planilha** para cardápios extensos (o backend já tem `batch`).

## 5. Impacto backend (resumo — detalhe no doc 06)

| Parametrização | Muda modelo? |
|---|---|
| Renderizar `optionGroups` no cliente | **Não** (só front) |
| `priceMode` (replace/add) no grupo + `isDefault` na opção | Sim (campos novos) + front respeitar `replace` |
| `displayStyle`, `emoji`, `freeQuantity`, `maxPerItem` no grupo/item | Sim (campos novos) |
| Meio a meio (`flavorSplit`, `priceRule`, N sabores no item, preço por tamanho) | **Sim** (OrderItem/CheckoutItem + matriz sabor×tamanho) |
| Grupos condicionais (`showIf`) | Sim |
| Venda por peso (comida a quilo) | Sim (Product) |
| Combos | Sim (relacionamento) |
| Disponibilidade por horário | Sim (Product) |
| Flags de comida (`minimumAge`, `requiresScheduling`) | Sim (Product) |

> Estratégia: começar pelo que **não muda o modelo** (renderizar `optionGroups`), depois campos aditivos no grupo/item, e por último as mudanças estruturais (meio a meio, combos).
