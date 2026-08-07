# 11 — Frete por Região, Frete Grátis e Loja Fechada

Regras de negócio de **entrega** e **estado da loja**, todas com autoridade no **backend** (ver Architecture Policy no `AGENTS.md`). O frontend apenas exibe/mira.

## 1. O que foi pedido

1. **Loja fechada → não pode comprar.** Nas telas que mostram o status (Aberta/Fechada), quando fechada, bloquear a compra.
2. **Tempo médio** (ex.: 40-60 min) vindo da configuração da loja. *(já existia — `initialMinute`/`finalMinute`)*
3. **Pedido mínimo** vindo da configuração da loja. *(já existia — via `preview`)*
4. **Frete grátis a partir de R$ X**, calculado no backend.
5. **Frete por região**: a taxa de entrega é precificada por região (bairro).

## 2. Decisões (confirmadas com o usuário)

1. **Bairro sem área de entrega cadastrada** (`StoreDeliveryArea` não casa com o bairro do cliente) → **taxa de entrega = R$ 0,00**.
2. **Frete grátis** zera a taxa em **qualquer região** quando `subtotal >= FreeShippingThreshold`.
3. **No carrinho (antes do endereço)** → não exibir taxa; mostrar **"calculado na próxima etapa"** (a região só é conhecida após o endereço).

## 3. Modelo (já existente no banco)

- `Store.FreeShippingThreshold` (decimal?) — limite para frete grátis.
- `Store.DeliveryFee` (decimal) — taxa padrão (mantida, mas o cálculo passa a priorizar região).
- `StoreDeliveryArea { StoreId, Neighborhood, DeliveryFee }` — taxa por bairro.
- `Store.InitialMinute` / `FinalMinute` — tempo médio.
- `Store.MinimumOrderValue` — pedido mínimo.
- `Store.IsOpen` — loja aberta/fechada.

## 4. Regra de cálculo da entrega (backend — `CheckoutService`)

Aplicada em `preview` **e** `confirm` (fonte da verdade):

```
se NÃO for delivery                          → deliveryFee = 0
senão:
  se FreeShippingThreshold > 0 e subtotal >= FreeShippingThreshold
        → deliveryFee = 0 ; freeShippingApplied = true
  senão se há endereço (CustomerAddressId):
        bairro do cliente casa com StoreDeliveryArea (normalizado, sem acento/caixa)?
           sim → deliveryFee = área.DeliveryFee
           não → deliveryFee = 0            (decisão 1)
  senão (delivery sem endereço, ex.: carrinho)
        → deliveryFee = 0 (a UI mostra "calculado na próxima etapa")
total = subtotal + deliveryFee
```

- O endereço passa a ser carregado também no **preview** quando `CustomerAddressId` vier (hoje só no confirm).
- A obrigatoriedade de endereço permanece apenas no **confirm** (persistência).
- `Store.DeliveryAreas` passa a ser incluído no carregamento da loja no checkout.

## 5. Contrato exposto

- **`CheckoutSummaryResponseDto`** ganha: `FreeShippingThreshold` (decimal?) e `FreeShippingApplied` (bool). `DeliveryFee` já vem regional.
- **`StorePublicDetailsDto`** ganha: `FreeShippingThreshold` (decimal?) — para o texto "Frete grátis a partir de R$ X".
- Models do frontend (`StorePublicDetails`, `CheckoutPreviewResponse`) atualizados com os novos campos.

## 6. Frontend (apresentação)

- **Loja fechada:**
  - `store-page`: aviso "Loja fechada no momento" + desabilitar "Ver sacola" e o clique de adicionar produto.
  - `cart`: desabilitar "Continuar" + aviso quando `!store.isOpen`.
  - O backend continua sendo a trava autoritativa (retorna 409/erro no checkout).
- **Frete grátis / região:**
  - `payment-page`: passa `customerAddressId` no `preview` (para a taxa regional aparecer com o endereço).
  - `cart`: exibe **"Frete grátis a partir de R$ X"** (quando a loja tem threshold) e **"calculado na próxima etapa"** no lugar da taxa.
  - `cart`/`payment`: quando `freeShippingApplied`, mostrar linha **"Frete grátis"** e taxa 0.
- **Tempo médio / pedido mínimo:** continuam vindos da config (sem mudança estrutural).

## 7. Testes

- **Unit** (cálculo de frete): sem delivery → 0; frete grátis acima do limite → 0; bairro com área → taxa da área; bairro sem área → 0.
- **Integração** (checkout): `preview`/`confirm` refletindo a taxa por bairro e o frete grátis; loja fechada → 409.

## 8. Arquivos afetados

- Backend: `CheckoutService`, `CheckoutSummaryResponseDto`, `StorePublicDetailsDto`, `StoreReadRepository` (mapa de detalhes públicos), testes de checkout.
- Frontend: `checkout.model.ts`, `store.model.ts`, `cart-page`, `payment-page`, `store-page`.
