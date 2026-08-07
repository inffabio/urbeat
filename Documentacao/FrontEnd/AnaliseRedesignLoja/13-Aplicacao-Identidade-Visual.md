# 13 - Aplicação da Identidade Visual (NovaVersaoFront)

> **Data:** 2026-07-10  
> **Status:** Parcial — Fases 1-5 concluídas, Fase 6 (tracking) pendente  
> **Base:** Protótipos em `Documentacao/FrontEnd/NovaVersaoFront/`

## O que foi pedido

Aplicar a identidade visual dos protótipos `Aplicacao1.jpg`, `Aplicacao2.jpg` e `ConceitoGrupos.png` (em `Documentacao/FrontEnd/NovaVersaoFront/`) no frontend Angular/Ionic (`./frontend`).

## O que foi feito

### Fase 1 — Tema (fundação)

**Arquivos alterados:**

| Arquivo | Mudança |
|---------|---------|
| `src/theme/variables.scss` | Nova paleta vinho/bordeaux: `--app-brand: #D54A51`, `--app-brand-dark: #B63A41`, `--app-brand-soft: #FDECEE`. Background app: `#ede9e3`, superfície: `#fff`. Fonte Ionic: `Inter`. Novos tokens de shadow, radius, spacing alinhados ao protótipo. |
| `src/theme/global.scss` | Fonte **Inter** (400-900) substitui Nunito Sans. App-shell mobile-first com wrapper centralizado. Novas classes globais: `.circle-btn`, `.ghost-btn`, `.page-head`, `.screen-padding`, `.link-orange`, `.dash`, `.row`, `.between`, `.muted`. Scrollbar estilizada vinho. Toast mantido com pastel tones. |
| `src/app/core/icons.ts` | Adicionados: `bagCheckOutline`, `personCircleOutline`, `starOutline`, `receiptOutline`, `optionsOutline`, `createOutline`, `phonePortraitOutline`. |

### Fase 2 — Catálogo (store-page + store-shell)

**Arquivos alterados:**

| Arquivo | Mudança |
|---------|---------|
| `features/store/store-page.component.ts` | Adicionado `goBack()`. Removido `starHtml()` e `deliveryTypes()` (não mais usados). |
| `features/store/store-page.component.html` | **Hero** com imagem + overlay gradiente + botão voltar (`.circle-btn`). **Painel da loja** com borda superior curva (`border-radius: 34px 34px 0 0`), logo centralizada sobrepondo hero (`top: -74px`), nome/tipo de cozinha. **Métricas** em 3 colunas com separadores (status, tempo, pedido mínimo). **Busca** com campo arredondado + botão filtro. **Chips de categoria** com rolagem horizontal, estilo pill ativo/vinho. **Product cards** com grid (imagem 112×96 + info + botão add circular). **Floating cart** sticky com gradiente vinho. |
| `features/store/store-page.component.scss` | Totalmente reescrito com design do protótipo. Sem sombras em cards (`.box-shadow: none`). Hover estado `#FDECEE`. |
| `features/store/store-shell.component.ts` | Adicionado **footer nav** global com 4 ícones (Cardápio, Pedidos, Carrinho, Conta). Navegação `isActive()` e `navigate()`. App-shell wrapper. |

### Fase 3 — Produto (product-detail-page)

**Arquivos alterados:**

| Arquivo | Mudança |
|---------|---------|
| `features/product-detail/product-detail-page.component.html` | **Hero** com overlay + botão voltar. **Product sheet** com borda superior curva (`border-radius: 32px 32px 0 0`). Título + preço lado a lado. Variações e escolhas como **botões grid 3 colunas** com checkmark flutuante. Adicionais como **check-list** com box quadrado. Grupos de opções com suporte aos 3 formatos (`buttons`, `chips`, `checkbox/list`). Observações com ícone lápis. **Sticky bar** inferior (qty + botão adicionar gradiente). Link "Voltar ao cardápio". |
| `features/product-detail/product-detail-page.component.scss` | Totalmente reescrito. Hero com `::after` gradient overlay. Botões de opção com estado ativo vinho + checkmark badge. Chip grid com wrap. Check-list com bordas inferiores. Sticky bar com gradiente de background simulando fade. |

### Fase 4 — Carrinho (cart-page)

**Arquivos alterados:**

| Arquivo | Mudança |
|---------|---------|
| `features/cart/cart-page.component.html` | **Page head** com 3 colunas (voltar, título, lixeira). **Cart product cards** com layout grid (imagem 112×112, info, qty pill, botão remover X). **Receive grid** com 2 cards (entrega/retirada) com ilustração, radio dot, time pill. **Summary card** com subtotal, frete, desconto, total. **Continue button** gradiente com 3 colunas (ícone sacola, texto, chevron). Link "Voltar ao cardápio". |
| `features/cart/cart-page.component.scss` | Totalmente reescrito com design do protótipo. |

### Fase 5 — Pagamento (payment-page + delivery/online)

**Arquivos alterados:**

| Arquivo | Mudança |
|---------|---------|
| `features/checkout/payment-page.component.html` | Page head + resumo do pedido com round-icon. **Pay options**: 2 cards largos com radio, ícone grande (70×70), título, descrição, chevron. Address card. Summary card com dash divider. Link "Voltar ao cardápio". |
| `features/checkout/payment-page.component.scss` | Totalmente reescrito. |
| `features/payment/delivery/delivery-payment-page.component.scss` | Reestilizado com novos tokens. |
| `features/payment/online/online-payment-page.component.scss` | Reestilizado com novos tokens. |

## Design tokens aplicados

| Token | Antes | Depois |
|-------|-------|--------|
| Cor primária | `#f57c52` (laranja coral) | `#D54A51` (vinho bordeaux) |
| Cor primária dark | `#e5673f` | `#B63A41` |
| Cor primária soft | `#fde7dd` | `#FDECEE` |
| Background | `#faf5ef` | `#ede9e3` |
| Fonte | Nunito Sans | Inter (400-900) |
| Sombras | Presentes em todos cards | Removidas na maioria, só em elementos selecionados |
| Cards | Shadow + border-radius 12px | Border 1px solid + border-radius 18px, sem shadow |
| Botões | Shadow, radius 999px | Gradiente (brand → brand-dark), sem shadow |
| Categorias | Tabs com underline | Chips/pills com rolagem horizontal |
| Navegação inferior | Inexistente | Footer nav com 4 itens fixos |

## O que NÃO foi alterado

- Lógica de negócio (mantida intacta em todos os componentes)
- Serviços (cart, checkout, auth, store, etc.)
- Rotas e guards
- Páginas do vendedor (store-config, products)
- Landing page
- Admin
- Tracking page (fase 6 pendente)

## Build

Build produção passa sem erros. Apenas warnings pré-existentes (unused imports, budget).

## Próximos passos

- Fase 6: Redesenhar tracking-page com timeline do protótipo
- Atualizar customer-page (checkout/cadastro) com novo estilo
- Aplicar tema nas páginas do vendedor (store-config wizard)
- Testar responsividade em dispositivos reais
