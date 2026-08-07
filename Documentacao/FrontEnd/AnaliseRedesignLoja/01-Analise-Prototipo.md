# 01 — Análise do Protótipo NovaVersaoFront

Protótipo estático (HTML/CSS/JS) inspirado no app de delivery **"Brasa Burger / Bucks Burgueria"**. Mobile-first, com "app-shell" centralizado no desktop (60vw, máx. 620–720px, cantos arredondados 34px, sombra). Pasta: `Documentacao/FrontEnd/NovaVersaoFront`.

## 1. Identidade visual e design tokens

### Cores (de `css/styles.css`)

| Token | Valor | Uso |
|-------|-------|-----|
| `--brand` / `--orange` | `#D54A51` | Cor primária (vinho/coral). Botões, preços, chips ativos, ícones de destaque. |
| `--brand-2` | `#B63A41` | Segundo tom do gradiente dos botões. |
| `--brand-soft` | `#FDECEE` | Fundo suave de estados selecionados / hover. |
| `--ink` | `#161616` | Texto principal. |
| `--muted` | `#6f6f76` | Texto secundário. |
| `--line` | `#eadfd6` | Bordas / divisórias (bege quente). |
| `--cream` / `--soft` | `#fbf7f2` / `#fff8f0` | Fundos quentes. |
| `--green` | `#119441` | Sucesso (status "Aberta", confirmação). |
| `--brown` | `#6c4634` | Ícones em contexto (round-icons). |
| Fundo do body | `#ede9e3` | Cinza-bege atrás do app-shell. |

> Observação: o CSS evoluiu do laranja original para o **vinho `#D54A51`** na versão V2.2 (bloco de `:where(...)` com `!important` sobrepõe tudo). A identidade final é **monocromática em torno do `#D54A51`**, com sombras removidas ("flat") e tipografia uniformizada.

### Tipografia

- Fonte única: **Inter** (Google Fonts, pesos 400–900).
- V2.2 força `font-size: 15px` base + `line-height: 1.32` em quase todos os elementos, com **destaques em peso 800**. Ou seja: hierarquia por **peso**, não por tamanho.

### Ícones

- **Bootstrap Icons** (`bi bi-*`) — navegação, metas, ações.
- **Line Icons** + **Font Awesome 6** — ícones lineares (moto de entrega, sacola, pin), estrela, fogo ("mais pedido"), peso (180g).
- SVGs inline para ilustrações de entrega/retirada e Pix.

### Componentes de UI recorrentes

- `app-shell` — container mobile centralizado.
- `statusbar` fake (9:41, sinal, wifi, bateria) — apenas estético.
- `footer-nav` — **navegação global fixa de 4 itens**: Cardápio, Pedidos, Carrinho, Conta (Bootstrap Icons). **Isto não existe no front atual.**
- `card`, `pill`, `chip`, `tab` (chips de categoria com rolagem horizontal), `product-card`, `floating-cart` (barra "Ver sacola").

## 2. Telas do protótipo

### 2.1 `index.html` — Cardápio / Home da loja

- **Hero** (banner 286px) com botão voltar circular sobreposto.
- **store-panel**: logo circular sobreposta (-74px), nome da loja (`Bucks Burgueria`), subtítulo (`Hambúrgueres e Lanches`).
- **store-metrics**: 3 colunas — Status (**Aberta**, verde), Tempo médio (**30–45 min**), Pedido mínimo (**R$ 20,00**).
- **Busca** (`search-box`) + **botão de filtros** (`filter-btn` com ícone `bi bi-sliders`). ⚠️ **O botão de filtros não tem finalidade — recomenda-se remover** (ver §2.1.1 e doc 04 §5).
- **Chips de categorias** com rolagem horizontal por arraste (Todos, Combos, Hambúrgueres, Pizzas, Bebidas, Acompanhamentos, Açaí, Empadas). São o **filtro real** do cardápio.
- **Seções por categoria** com `product-card` (imagem, nome, descrição, preço, botão +).
- **Ordem das seções (Combos/Destaques):** o protótipo lista **Combos primeiro**, depois o produto principal (Hambúrgueres/Pizzas), depois complementos (Bebidas, Acompanhamentos, Açaí, Empadas). Ou seja, **os produtos de maior conversão/destaque vêm no topo** (ver doc 04 §4).
- **Comportamento por tipo**: produtos comuns → `carrinho.html`; **pizzas → tela por tamanho** (`pizza-pequena.html` …); **açaí → `produto-acai.html`**; **empada → `produto-empada.html`**.

#### 2.1.1 Busca e filtros — recomendação

- **Manter a busca por texto** na barra (filtra nome + descrição) como filtro primário.
- **Manter os chips de categoria** como mecanismo de filtragem por seção.
- **Remover o botão `filter-btn` (ícone de configuração/`sliders`)** ao lado da busca: não abre nenhuma tela nem executa ação, e as categorias já filtram. A barra de busca deve ocupar **100% da largura**.
- **floating-cart** "Ver sacola" com contador e total.
- **footer-nav** global.

### 2.2 `produto-hamburguer.html` — Produto com opcionais

- Hero do produto 340px, título + preço, **meta** (⭐4,8 · 🔥Mais pedido · ⏱30–45 min · ⚖180g).
- Seção **"Opcionais"** com:
  - **Ponto da carne** — grid de 3 botões selecionáveis (radio visual, "Ao ponto para mal", "Ao ponto", "Bem passada") com check no canto. **Escolha única.**
  - **Adicionais** — checklist (Bacon +R$4,50, Onion Rings, Catupiry, Cheddar, Molho). **Múltipla escolha.**
  - **Observações** — input com contador 0/120.
- Rodapé fixo: seletor de quantidade (± ) + botão "Adicionar ao carrinho" com preço.

### 2.3 Pizzas — fluxo em 2 níveis

**Nível 1 — `pizza-pequena.html` / `-media` / `-grande` / `-gigante`** (por tamanho):

- Reaproveita o cabeçalho de loja (logo, métricas).
- Título "Pizza Pequena 25 cm - 4 fatias".
- **Lista de sabores** (`pizza-flavor-card`): imagem, nome, descrição de ingredientes, preço, botão +. Cada sabor abre `produto-pizza.html`.
- Rodapé fixo com quantidade + total + adicionar.

**Nível 2 — `produto-pizza.html`** (sabor específico):

- Hero do sabor, título + preço, descrição.
- **Borda recheada** (opcional): Catupiry +R$8, Cheddar +R$8 (múltipla, grid 2 col).
- **Ingredientes extras** (opcional): Pepperoni, Bacon, Cebola Roxa, Azeitona, Tomate Seco (múltipla, grid 3 col).
- **Observações** (0/120).
- Rodapé fixo: quantidade + adicionar.

> **Importante:** o protótipo modela o tamanho como uma **tela/coleção separada** e o sabor como **produto**. Não há meio a meio real (2 sabores no mesmo item) — é 1 sabor por item.

### 2.4 `produto-acai.html` — Montagem guiada (multi-etapas)

Modelo de "montar seu produto" numeradas, cada uma com tag **Obrigatório / Opcional**:

1. **Tamanho** (obrigatório) — 300ml R$18,90 / 500ml R$24,90 / 700ml R$32,90 (radio, muda o preço-base).
2. **Frutas** (opcional) — chips com emoji (🍌🍓🥝🍇).
3. **Cremes e caldas** (opcional) — chips (leite condensado, nutella, calda, pasta de amendoim).
4. **Crocantes** (opcional) — granola, paçoca, castanha, coco, confetes.
5. **Extras** (opcional) — leite em pó, gotas de chocolate.
6. **Observações** (opcional).

Rodapé fixo: quantidade + adicionar (com total).

> Este é o **padrão mais rico e generalizável**: seções numeradas, obrigatório/opcional, single/multiple, chips ou radios — é exatamente o que os `ProductOptionGroup` do backend permitem.

### 2.5 `produto-empada.html` — Produto simples

- Produto sem opções: título, preço, descrição, observações, adicionar. Representa o caso "produto simples" (bebidas, sobremesas e demais complementos).

### 2.6 `carrinho.html` — Carrinho + forma de recebimento

- Lista de itens (`cart-product-card`): imagem, nome, descrição, preço, remover (X), pílula de quantidade ± .
- **"Como deseja receber?"** — 2 cards grandes: **Entrega** (moto, 30–45 min) e **Retirada no local** (loja, 15–20 min), radio.
- **Cupom** — (no protótipo aparece o padrão de linha "adicionar cupom" nos outros fluxos; no atual existe placeholder).
- **Resumo**: Subtotal, Taxa de entrega (com ícone info), Total.
- Botão "Continuar" grande + link "Voltar ao cardápio".
- footer-nav global.

### 2.7 `checkout.html` — Cadastro / endereço

- Logo + nome da loja centralizados.
- **Mini-resumo** do pedido.
- Formulário com ícones por campo (nome, telefone, email, CEP, cidade, bairro, rua, número, complemento).

### 2.8 `pagamento.html` — Escolha do tipo de pagamento

- Resumo do pedido.
- **2 opções grandes**: "Pagar pelo app" (cartão, Pix, saldo) e "Pagar na entrega" (dinheiro/cartão).
- Card de endereço.
- **Resumo com Descontos** (linha verde `- R$ 3,00`) — o protótipo prevê desconto/cupom.
- Botão "CONTINUAR" com cadeado (segurança).

### 2.9 `pagar-app.html` / `pagar-entrega.html` / `pagar.html`

- Sub-fluxos de pagamento online (cartão salvo, Pix com logo local) e pagamento na entrega (troco, etc.).

### 2.10 `confirmado.html` — Pedido enviado + rastreio

- **Check de sucesso** verde grande.
- Card do pedido (#12345, itens, total, previsão "Hoje, entre 18:20 e 18:50").
- **Timeline** de 4 passos: Pedido recebido → Preparando → Saiu para entrega → Entregue (com horários).
- Detalhes da entrega (endereço, tipo, pagamento).
- Card "Precisa de ajuda?".
- Botão "ACOMPANHAR PEDIDO".

## 3. Microinterações e padrões notáveis

- **Hover/seleção** sempre em `#FDECEE` com borda `#D54A51`.
- **Flat design** na V2.2 (sombras removidas via `box-shadow:none!important`).
- **Chips de categoria** com rolagem horizontal por arraste (mouse/trackpad/touch).
- **Rodapé fixo de ação** (quantidade + adicionar) em telas de produto.
- **Tags Obrigatório/Opcional** por grupo de opção (padrão açaí) — excelente para orientar o cliente.
- **Numeração de etapas** ("1. Tamanho", "2. Frutas"…) — reduz carga cognitiva.

## 4. Pontos fortes do protótipo a preservar

1. Telas de produto **específicas por tipo de comida** (mas idealmente dirigidas por dados, não por arquivo).
2. Padrão de **montagem guiada** (açaí) — generalizável para qualquer tipo de comida.
3. **footer-nav global** de 4 abas (Cardápio/Pedidos/Carrinho/Conta).
4. **store-metrics** em 3 colunas (status, tempo, mínimo).
5. Rótulos **Obrigatório/Opcional** + numeração de grupos.
6. **Descontos** no resumo (prevê cupom).
7. **Combos e Destaques no topo do cardápio** — ordem que prioriza conversão (ver doc 04 §4).

## 5. Riscos / pontos de atenção

- CSS com muitos `!important` (V2.1/V2.2) — difícil de portar direto; recomenda-se reconstruir os tokens em SCSS limpo (ver doc 02).
- Telas de pizza por **arquivo** não escalam — precisa ser dirigido por dados no Angular.
- Statusbar fake e navegação por HTML estático não se aplicam ao app real (Ionic já tem shell).
- **Botão de filtros (`sliders`) ao lado da busca não tem função — remover** (ver §2.1.1).
- Meio a meio não está resolvido no protótipo (1 sabor por item).
