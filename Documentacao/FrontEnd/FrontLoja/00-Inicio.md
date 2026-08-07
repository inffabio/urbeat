# Especificação Funcional e Visual — Telas de 1 a 8: Abrir Pagina do cliente até Pagamento na Entrega da Urbeat

## Projeto

- **Stack alvo:** Angular 20 + Ionic
- **Visão do software:** Existe uma empresa chamada Urbeat que controla todos os clientes vendedores que farão cadastro e cada um terá sua página de venda.
- **Objetivo:**  Os clientes da Empresa Urbeat (centralizadora dona do sistema) serão vendedores Lojas que possuem logomarcas e todo aparato de delivery. O sistema controlara a parte visual do front das lojas assim como seu fluxo **mobile first**. Já existe a stack de backend em .net 9 com banco postgree e outras features.

## Icones sugeridos do ionic

- ion-android-time
- ion-android-search
- ion-social-whatsapp
- ion-bag
- ion-card
- ion-cash
- ion-trash-a
- ion-ios-trash-outline
- ion-person-add
- ion-ios-personadd-outline
- ion-ios-telephone
- ion-ios-location
- ion-ios-information-outline
- ion-android-lock
- ion-android-cart
- ion-ios-arrow-back
- ion-ios-arrow-forward
- ion-ios-checkmark-outline

---

## APIs do Backend

### URL Base
- Produção: `https://api.urbeat.com.br`
- Sandbox: `https://sandbox.api.urbeat.com.br`

### Roteamento de Loja
Cada loja tem uma URL pública no formato:
`https://www.urbeat.com.br/{storePath}`

O `storePath` é gerado automaticamente a partir do nome da loja (lowercase, espaços → `_`, sem acentos). Ex: `"Burguer do Rafa"` → `"burguer_do_rafa"`.

```http
GET /api/public/stores/by-path/{storePath}
```

### Autenticação do Cliente
As telas de 1 a 3 são **públicas** (cardápio, carrinho). A autenticação é exigida apenas a partir da confirmação do pedido (telas 4 em diante).

| Fluxo | Endpoint | Quando usar |
|-------|----------|-------------|
| Registro | `POST /api/auth/register/customer` | Ao finalizar checkout (se novo cliente) |
| Login | `POST /api/auth/login/customer` | Ao finalizar checkout (se já cadastrado) |
| Refresh | `POST /api/auth/refresh` | Renovar token expirado |
| Confirmação e-mail | `POST /api/auth/email/confirm` | Confirmar e-mail do cliente |

### Fluxo de Cliente (Telas 1–8)
| Tela | APIs Principais |
|------|----------------|
| 01-TelaInicial.md | Catálogo público, categorias, produtos, avaliações |
| 02-DetalheProduto.md | Dados do produto (via catálogo público) |
| 03-Cart.md | Checkout preview, confirm |
| 04-CadastroCliente.md | Auth (register/login), CEP lookup, endereços |
| 05-Pagamento.md | Checkout confirm (com método selecionado) |
| 06-EfetivarPagamento.md | Checkout confirm, criar pagamento, status |
| 07-PagamentoNaEntrega.md | Checkout confirm (CashOnDelivery/CardOnDelivery) |
| 08-AcompanhamentoPedidoCliente.md | Detalhes do pedido, histórico, polling |

---

## Estrutura de Pastas do Frontend

```
src/
├── app/
│   ├── core/                          # Singleton services, guards, interceptors
│   │   ├── guards/
│   │   │   └── auth.guard.ts          # Protege rotas autenticadas (telas 4-8)
│   │   ├── interceptors/
│   │   │   ├── auth.interceptor.ts    # Injeta JWT Bearer nas requisições
│   │   │   └── error.interceptor.ts   # Trata erros HTTP globalmente
│   │   └── services/
│   │       ├── auth.service.ts        # Register, login, refresh, logout
│   │       ├── cart.service.ts        # Estado global do carrinho (persistência local)
│   │       ├── store.service.ts       # Store lookup (by-path, detalhes)
│   │       ├── catalog.service.ts     # Categorias, produtos, featured
│   │       ├── checkout.service.ts    # Preview + confirm
│   │       ├── payment.service.ts     # Criar pagamento, status, polling
│   │       ├── order.service.ts       # Detalhes, histórico, listar pedidos
│   │       ├── address.service.ts     # CEP lookup, CRUD endereços
│   │       └── review.service.ts      # Avaliações da loja
│   │
│   ├── shared/                        # Componentes, pipes, diretivas reutilizáveis
│   │   ├── components/
│   │   │   ├── store-header/          # Banner + logo da loja
│   │   │   ├── menu-item-card/        # Card de produto (reusado telas 1, 2, 3)
│   │   │   ├── quantity-selector/     # Seletor +/-
│   │   │   ├── order-summary/         # Subtotal, taxa, desconto, total
│   │   │   ├── delivery-address-card/ # Endereço de entrega (reusado telas 5, 6, 7, 8)
│   │   │   ├── loading-overlay/       # Spinner global com overlay
│   │   │   └── floating-actions/      # Botão WhatsApp + carrinho
│   │   ├── pipes/
│   │   │   └── brl-currency.pipe.ts   # Formata valores em R$
│   │   ├── directives/
│   │   │   └── debounce-input.directive.ts
│   │   ├── models/
│   │   │   ├── store.model.ts         # Store, StorePublicDetails
│   │   │   ├── product.model.ts       # Product, ProductCategory
│   │   │   ├── cart-item.model.ts     # CartItem
│   │   │   ├── checkout.model.ts      # CheckoutRequest, CheckoutPreview, CheckoutConfirm
│   │   │   ├── order.model.ts         # Order, OrderHistory
│   │   │   ├── payment.model.ts       # Payment, PaymentMethod
│   │   │   ├── address.model.ts       # Address, CepLookup
│   │   │   └── user.model.ts          # Customer, AuthResponse
│   │   └── enums/
│   │       ├── order-status.enum.ts   # OrderStatus (1-8)
│   │       ├── payment-method.enum.ts # PaymentMethod (1-4)
│   │       ├── fulfillment-type.enum.ts # FulfillmentType (1=Delivery, 2=PickUp)
│   │       └── payment-status.enum.ts # PaymentStatus (1-5)
│   │
│   ├── features/                      # Módulos de tela (standalone components)
│   │   ├── store/                     # Tela 1 — Cardápio da loja
│   │   │   ├── components/
│   │   │   │   ├── store-header.component.ts
│   │   │   │   ├── store-info.component.ts
│   │   │   │   ├── menu-search.component.ts
│   │   │   │   ├── menu-categories.component.ts
│   │   │   │   ├── menu-section-title.component.ts
│   │   │   │   ├── menu-item-card.component.ts
│   │   │   │   └── floating-actions.component.ts
│   │   │   └── store-page.component.ts
│   │   │
│   │   ├── product-detail/            # Tela 2 — Detalhe do produto
│   │   │   ├── components/
│   │   │   │   ├── product-detail-header.component.ts
│   │   │   │   ├── product-hero-image.component.ts
│   │   │   │   ├── product-info.component.ts
│   │   │   │   ├── product-observations.component.ts
│   │   │   │   └── add-to-cart-bar.component.ts
│   │   │   └── product-detail-page.component.ts
│   │   │
│   │   ├── cart/                      # Tela 3 — Carrinho / Revisão
│   │   │   ├── components/
│   │   │   │   ├── cart-header.component.ts
│   │   │   │   ├── cart-item-card.component.ts
│   │   │   │   ├── coupon-entry.component.ts
│   │   │   │   ├── delivery-method-selector.component.ts
│   │   │   │   └── cart-footer-actions.component.ts
│   │   │   └── cart-page.component.ts
│   │   │
│   │   ├── checkout/                  # Telas 4-5 — Cadastro + Pagamento
│   │   │   ├── components/
│   │   │   │   ├── customer-checkout-header.component.ts
│   │   │   │   ├── customer-form.component.ts
│   │   │   │   ├── address-select-field.component.ts
│   │   │   │   └── checkout-continue-action.component.ts
│   │   │   ├── customer-page.component.ts       # Tela 4
│   │   │   ├── payment-header.component.ts
│   │   │   ├── order-payment-summary.component.ts
│   │   │   ├── payment-method-selector.component.ts
│   │   │   ├── payment-order-summary.component.ts
│   │   │   ├── payment-footer-actions.component.ts
│   │   │   └── payment-page.component.ts         # Tela 5
│   │   │
│   │   ├── payment/                   # Telas 6-7 — Efetivar pagamento
│   │   │   ├── online/                           # Tela 6
│   │   │   │   ├── components/
│   │   │   │   │   ├── app-payment-execution-header.component.ts
│   │   │   │   │   ├── app-order-resume-card.component.ts
│   │   │   │   │   ├── app-payment-method-card-list.component.ts
│   │   │   │   │   ├── app-selected-payment-info.component.ts
│   │   │   │   │   ├── app-payment-summary.component.ts
│   │   │   │   │   └── app-payment-submit-actions.component.ts
│   │   │   │   └── online-payment-page.component.ts
│   │   │   └── delivery/                         # Tela 7
│   │   │       ├── components/
│   │   │       │   ├── delivery-payment-header.component.ts
│   │   │       │   ├── delivery-payment-order-summary.component.ts
│   │   │       │   ├── delivery-payment-method-selector.component.ts
│   │   │       │   ├── delivery-payment-extra-info.component.ts
│   │   │       │   ├── delivery-payment-summary.component.ts
│   │   │       │   └── delivery-payment-actions.component.ts
│   │   │       └── delivery-payment-page.component.ts
│   │   │
│   │   └── order-tracking/            # Tela 8 — Acompanhamento
│   │       ├── components/
│   │       │   ├── order-success-header.component.ts
│   │       │   ├── order-resume-card.component.ts
│   │       │   ├── delivery-estimate-card.component.ts
│   │       │   ├── order-tracking-timeline.component.ts
│   │       │   ├── delivery-details-card.component.ts
│   │       │   ├── order-support-entry.component.ts
│   │       │   └── order-tracking-actions.component.ts
│   │       └── tracking-page.component.ts
│   │
│   ├── store/                         # Gerenciamento de estado (Signals ou NgRx)
│   │   ├── cart.store.ts             # Carrinho (itens, quantidades, observações)
│   │   ├── checkout.store.ts         # Fluxo de checkout (endereço, método, etapa)
│   │   └── auth.store.ts             # Token JWT, usuário logado
│   │
│   ├── app.routes.ts                  # Definição de rotas
│   ├── app.component.ts
│   └── app.config.ts
│
├── assets/
│   ├── images/
│   └── icons/
│
├── environments/
│   ├── environment.ts                 # API base URL, configurações
│   └── environment.prod.ts
│
├── theme/
│   ├── variables.scss                 # Variáveis CSS (cores, fontes, bordas)
│   └── global.scss                    # Estilos globais
│
└── index.html
```

### Convenções

- **Standalone components** — sem NgModules, usando `@Component({ standalone: true, ... })`
- **Estado** — Signals para estado local, serviços injetáveis para estado compartilhado
- **SCSS modular** — cada componente com seu próprio arquivo de estilo
- **Models** — interfaces TypeScript que espelham os DTOs do backend
- **Enums** — copiados do backend para evitar valores mágicos
- **Core services** — injetados como `providedIn: 'root'`
- **Feature components** — lazy-loaded nas rotas

### Mapa de Rotas

| Path | Componente | Tela | Auth |
|------|-----------|------|------|
| `/:storePath` | `StorePageComponent` | 01 - Cardápio | ❌ |
| `/:storePath/produto/:productId` | `ProductDetailPageComponent` | 02 - Detalhe | ❌ |
| `/:storePath/carrinho` | `CartPageComponent` | 03 - Carrinho | ❌ |
| `/:storePath/checkout/cadastro` | `CustomerPageComponent` | 04 - Cadastro | ✅ |
| `/:storePath/checkout/pagamento` | `PaymentPageComponent` | 05 - Pagamento | ✅ |
| `/:storePath/checkout/pagar` | `OnlinePaymentPageComponent` | 06 - Pagar app | ✅ |
| `/:storePath/checkout/entrega` | `DeliveryPaymentPageComponent` | 07 - Pagar entrega | ✅ |
| `/:storePath/pedido/:orderId` | `TrackingPageComponent` | 08 - Acompanhar | ✅ |

