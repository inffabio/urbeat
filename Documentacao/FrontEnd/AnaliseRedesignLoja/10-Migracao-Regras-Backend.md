# 10 — Migração de Regras de Negócio: Frontend → Backend

Rastreamento da migração das regras de negócio que hoje vivem (ou são duplicadas) no frontend para o backend, conforme a **Architecture Policy** do `AGENTS.md`.

> **Princípio:** backend = fonte única da verdade (preços, totais, validações, elegibilidade). Frontend = apresentação; pode **espelhar** uma regra só para feedback imediato (ex.: preço estimado, desabilitar botão), nunca como autoridade. O cliente envia **seleções/inputs (ids)**, não resultados calculados.

Legenda status: 🔴 pendente · 🟡 em andamento · 🟢 concluído.

---

## Prioridade 1 — 🟢 Preço do pedido no checkout (autoridade + segurança) — CONCLUÍDO

**Problema (resolvido):** o `CheckoutService` confiava no `UnitPrice` enviado pelo cliente.

**Implementado:**
- `CheckoutItemRequestDto` agora envia **`ProductId`** + seleções por id (`VariationId`, `ChoiceOptionId`, `AdditionalIds`, `OptionGroups[{ GroupId, ItemIds }]`) + `Quantity`/`Notes`. **Removidos** `ProductName` e `UnitPrice`.
- Novo **`IPricingService`/`PricingService`**: calcula o preço unitário autoritativo a partir do `Product` persistido + seleções, aplicando `PriceMode` (`add`/`replace`/`highest`/`average`) e validando grupos (min/máx, itens válidos).
- `CheckoutService` (preview + confirm) carrega os produtos da loja, recomputa `UnitPrice`/`Subtotal`/`Total` e monta o snapshot (`ProductName`, nomes de opções) **no servidor**. Produto inexistente/indisponível ou seleção inválida → `400` com mensagem.
- `CheckoutRequestDtoValidator`: item exige `ProductId` + `Quantity > 0`.
- Frontend: `CartService.toCheckoutItems()` envia só ids; `product-detail` guarda `itemIds` das seleções; carrinho/checkout/pagamentos usam o helper. O preço no cliente permanece apenas como estimativa de UX.
- **Testes:** `PricingServiceTests` (7 casos: base, adicionais, highest/meio-a-meio, replace/tamanho, obrigatório, máx, id inválido) + 79 testes de integração de checkout/orders/payments atualizados e verdes.

---

## Prioridade 2 — 🔴 Totais do carrinho / pedido mínimo / frete

**Onde no front:** `cart-page.component` e `payment-page.component` calculam `subtotal`, `deliveryFee`, `discount`, `total` e o aviso de **pedido mínimo**.

**Alvo:** consumir o **`POST /api/checkout/preview`** (já retorna `Subtotal`, `DeliveryFee`, `MinimumOrderValue`, `Total`, `StoreIsOpen`, `BelowMinimum`). O front apenas formata/renderiza. Remover o cálculo de total/mínimo do cliente (mantém, no máximo, um espelho visual enquanto o preview não responde).

---

## Prioridade 3 — 🔴 Validação de seleção de opções (add-to-cart)

**Onde no front:** `product-detail.isSelectionValid` (grupos obrigatórios, min/máx) e clamps em `toggleGroupItem`.

**Alvo:** manter no front **apenas como UX** (desabilitar o botão). A validação **autoritativa** ocorre no checkout (Prioridade 1). Documentar que o front é espelho.

---

## Prioridade 4 — 🔴 Status "aberta/fechada" e ETA

**Onde no front:** `store-page.statusText` (calcula aberto/fechado por `businessHours` + `isOpen`) e `etaText`.

**Alvo:** o backend deve expor um campo **`isOpenNow`** (calculado a partir de `IsOpen` + `StoreBusinessHour` + fuso) no `StorePublicDetailsDto`. O front só exibe. Hoje o checkout já valida `store.IsOpen`, mas o "aberto agora por horário" é decidido no cliente.

---

## Prioridade 5 — 🔴 Normalização de slug da loja

**Onde no front:** `store-config.onStoreNameChange` gera o slug (lowercase, sem acento, hífens).

**Alvo:** o backend deve ser dono da normalização/unicidade do slug ao criar/atualizar a loja. O front pode sugerir visualmente, mas o valor persistido é normalizado no servidor.

---

## Já em conformidade (referência)

- 🟢 **Normalização de grupos de opções** (`DisplayStyle`→`ChoiceType`, clamp min/máx, `IsRequired = min≥1`, proibir min0&máx0): feito no `ProductService.BuildOptionGroup` + `ProductOptionGroupDtoValidator`. O front espelha para UX.
- 🟢 **Validação de produto** (preço > 0, imagem obrigatória, nomes): `Create/UpdateProductRequestDtoValidator`. Front espelha.
- 🟢 **Categoria duplicada / reativação**: `ProductCategoryService`.

---

## Itens que PERMANECEM no frontend (apresentação pura)

- Máscaras de input (telefone, CEP, moeda) e formatação BRL (`brl` pipe).
- Filtro visual por categoria / aba "Destaques" (a flag `isFeatured` é dados do backend; filtrar a lista exibida é apresentação).
- Espelhos de validação para **feedback imediato** (desabilitar botão, texto de erro), desde que o backend revalide.
- Ordenação/agrupamento visual do cardápio (a ordem canônica vem de `DisplayOrder`/`IsFeatured` do backend).

---

## Ordem de execução sugerida

1. **Prioridade 1** (checkout pricing) — maior valor + fecha falha de segurança. PR próprio com `PricingService` + testes.
2. **Prioridade 2** (totais via preview) — depende do preview já existente.
3. **Prioridade 4** (`isOpenNow`) — pequeno, alto valor de correção.
4. **Prioridade 5** (slug) e **Prioridade 3** (documentar espelho) — menores.
