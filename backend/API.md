# API Urbeat — Documentação para Frontend

Base URL: `https://api.urbeat.com.br`

Autenticação via JWT Bearer Token no header `Authorization: Bearer <token>`.

---

## Índice

- [Autenticação (Auth)](#1-autenticacao)
- [Lojas (Stores - Vendedor)](#2-lojas-vendedor)
- [Produtos (Store Products - Vendedor)](#3-produtos-vendedor)
- [Categorias (Store Categories - Vendedor)](#4-categorias-vendedor)
- [Checkout (Público / Cliente)](#5-checkout)
- [Pedidos (Orders)](#6-pedidos)
- [Pagamentos (Payments)](#7-pagamentos)
- [Webhooks](#8-webhooks)
- [Administração (Admin)](#9-administracao)
- [Cliente (Customer)](#10-cliente)
- [Endereços do Cliente](#11-enderecos-do-cliente)
- [Notificações do Cliente](#12-notificacoes-do-cliente)
- [Vendedor (Seller)](#13-vendedor)
- [Notificações do Vendedor](#14-notificacoes-do-vendedor)
- [Busca de CEP (Address Lookup)](#15-busca-de-cep)
- [Lojas Públicas (Public Stores)](#16-lojas-publicas)
- [Catálogo Público (Public Catalog)](#17-catalogo-publico)
- [Avaliações (Reviews)](#18-avaliacoes)
- [Assinaturas (Subscriptions)](#19-assinaturas)

---

## 1. Autenticação

Todas as rotas públicas (`AllowAnonymous`). Usadas para registro e login de usuários.

### POST `/api/auth/register/customer`
Registrar novo cliente consumidor.

**Request:**
```json
{
  "fullName": "João Silva",
  "email": "joao@email.com",
  "password": "Senha@123",
  "phoneNumber": "(11) 99999-8888"
}
```

**Response 201:**
```json
{
  "succeeded": true,
  "userId": "a1b2c3d4-...",
  "emailConfirmationPending": true
}
```

### POST `/api/auth/register/seller`
Registrar novo vendedor (lojista). Após registrar, deve criar a loja via `POST /api/stores`.

**Request:** `RegisterUserRequestDto`
```json
{
  "fullName": "Maria Loja",
  "email": "maria@loja.com",
  "password": "Senha@123",
  "phoneNumber": "(11) 98888-7777"
}
```

**Response 201:** igual ao de customer.

### POST `/api/auth/login/customer`
Login como cliente.

**Request:**
```json
{
  "email": "joao@email.com",
  "password": "Senha@123"
}
```

**Response 200:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",
  "expiresAtUtc": "2026-05-28T12:00:00Z",
  "refreshToken": "dGhpcyBpcyBhIHJlZnJl...",
  "refreshTokenExpiresAtUtc": "2026-06-27T12:00:00Z"
}
```

### POST `/api/auth/login/seller`
Login como vendedor. Mesmo formato de `LoginRequestDto`.

### POST `/api/auth/login/admin`
Login como administrador. Mesmo formato.

### POST `/api/auth/token`
Gera token genérico (sempre como Customer). Mesmo formato.

### POST `/api/auth/refresh`
Renova o token usando refresh token enviado via cookie `urbeat.refresh_token`.

**Response 200:** `AuthTokenResponseDto`

### POST `/api/auth/email/confirm`
Confirma e-mail com token recebido por e-mail.

**Request:**
```json
{
  "userId": "a1b2c3d4-...",
  "token": "CfDJ8N2..."
}
```

**Response 200:**
```json
{
  "succeeded": true,
  "alreadyConfirmed": false,
  "message": "E-mail confirmado com sucesso."
}
```

### POST `/api/auth/email/resend-confirmation`
Reenvia e-mail de confirmação.

**Request:**
```json
{
  "email": "joao@email.com"
}
```

**Response 200:**
```json
{
  "succeeded": true,
  "alreadyConfirmed": false,
  "message": "E-mail de confirmação reenviado."
}
```

---

## 2. Lojas (Vendedor)

Todas as rotas exigem `[SellerOnly]`. O vendedor precisa estar autenticado.

### GET `/api/stores/cuisine-types`
Lista tipos de culinária ativos.

**Response 200:**
```json
[
  { "id": "guid-1", "name": "Lanches" },
  { "id": "guid-2", "name": "Pizza" },
  { "id": "guid-3", "name": "Japonesa" }
]
```

### POST `/api/stores`
Cria a loja para o vendedor autenticado.

**Request:**
```json
{
  "name": "Burguer do Rafa",
  "slug": "burguer-do-rafa",
  "phoneNumber": "(11) 99999-0001",
  "description": "Hambúrgueres artesanais",
  "cuisineType": "Lanches",
  "bannerUrl": "https://images.com/banner.jpg",
  "logoUrl": "https://images.com/logo.jpg",
  "tawkToPropertyId": "abc123"
}
```

**Response 201:**
```json
{
  "id": "guid...",
  "ownerUserId": "guid...",
  "name": "Burguer do Rafa",
  "slug": "burguer-do-rafa",
  "storePath": "burguer_do_rafa",
  "phoneNumber": "(11) 99999-0001",
  "description": "Hambúrgueres artesanais",
  "cuisineType": "Lanches",
  "bannerUrl": "https://images.com/banner.jpg",
  "logoUrl": "https://images.com/logo.jpg",
  "tawkToPropertyId": "abc123",
  "isOpen": false,
  "isSubscriptionBlocked": false,
  "deliveryFee": 0,
  "minimumOrderValue": 0,
  "averageRating": 0,
  "totalReviews": 0
}
```

> `storePath` é gerado automaticamente a partir do `name`: lowercase, replaces espaços por `_`, remove acentos. Ex: `"Burguer do Rafa"` → `"burguer_do_rafa"`. Usado na URL pública: `https://www.urbeat.com.br/{storePath}`

### GET `/api/stores/my-store`
Retorna a loja do vendedor autenticado.

**Response 200:** `StoreResponseDto` (exemplo acima)

### PUT `/api/stores/{storeId}`
Atualiza dados da loja. Se o `name` for alterado, o `storePath` é recalculado automaticamente.

**Request:** mesmos campos de criação
**Response 200:** `StoreResponseDto`

### GET `/api/stores/{storeId}/address`
Obtém endereço da loja.

**Response 200:**
```json
{
  "storeId": "guid...",
  "street": "Rua Augusta",
  "number": "1500",
  "neighborhood": "Consolação",
  "city": "São Paulo",
  "state": "SP",
  "zipCode": "01304-001",
  "reference": "Próximo ao metrô",
  "latitude": -23.5505,
  "longitude": -46.6333
}
```

### PUT `/api/stores/{storeId}/address`
Cria ou atualiza endereço da loja.

**Request:** `UpdateStoreAddressRequestDto` (mesmos campos do response acima)
**Response 200:** `StoreAddressResponseDto`

### GET `/api/stores/{storeId}/business-hours`
Obtém horários de funcionamento.

**Response 200:**
```json
{
  "storeId": "guid...",
  "items": [
    { "dayOfWeek": 0, "opensAt": "11:00", "closesAt": "23:00" },
    { "dayOfWeek": 1, "opensAt": "11:00", "closesAt": "23:00" }
  ]
}
```

### PUT `/api/stores/{storeId}/business-hours`
Cria ou atualiza horários.

**Request:**
```json
{
  "items": [
    { "dayOfWeek": 0, "opensAt": "11:00", "closesAt": "23:00" },
    { "dayOfWeek": 1, "opensAt": "11:00", "closesAt": "23:00" }
  ]
}
```
**Response 200:** `StoreBusinessHoursResponseDto`

### PATCH `/api/stores/{storeId}/status`
Altera status aberto/fechado.

**Request:**
```json
{ "isOpen": true }
```
**Response 200:** `StoreResponseDto`

### PATCH `/api/stores/{storeId}/delivery-config`
Atualiza taxa de entrega e valor mínimo do pedido.

**Request:**
```json
{
  "deliveryFee": 5.90,
  "minimumOrderValue": 15.00
}
```
**Response 200:** `StoreResponseDto`

### GET `/api/stores/{storeId}/payment-gateway`
Obtém a configuração do gateway de pagamento da loja (Mercado Pago).

**Response 200:**
```json
{
  "storeId": "guid...",
  "gateway": 1,
  "hasAccessToken": true,
  "hasNotificationUrl": true,
  "environment": "Sandbox",
  "isActive": true
}
```

> `hasAccessToken` / `hasNotificationUrl` indicam se os dados estão cadastrados (valores nunca são expostos).
> `gateway`: 1 = MercadoPago

**Response 200 (sem configuração):** `{ "storeId": "guid...", "gateway": 1, "hasAccessToken": false, "hasNotificationUrl": false, "environment": "Sandbox", "isActive": false }`

### PUT `/api/stores/{storeId}/payment-gateway`
Cria ou atualiza a configuração do gateway de pagamento da loja.

**Request:**
```json
{
  "gateway": 1,
  "accessToken": "APP_USR-123456789...",
  "notificationUrl": "https://api.urbeat.com.br/api/webhooks/mercadopago",
  "environment": "Sandbox",
  "isActive": true
}
```

> `accessToken` é armazenado criptografado (AES-256). `environment`: `"Sandbox"` ou `"Production"`.

**Response 200:**
```json
{
  "storeId": "guid...",
  "gateway": 1,
  "hasAccessToken": true,
  "hasNotificationUrl": true,
  "environment": "Sandbox",
  "isActive": true
}
```

**Status:** `200` | `400` | `403` | `404`

### DELETE `/api/stores/{storeId}/payment-gateway`
Remove a configuração do gateway de pagamento da loja.

**Response:** 204 No Content

---

## 3. Produtos (Vendedor)

Todas as rotas exigem `[SellerOnly]`.

### GET `/api/stores/{storeId}/products`
Lista produtos da loja (incluindo inativos/indisponíveis).

**Response 200:**
```json
[
  {
    "id": "guid...",
    "storeId": "guid...",
    "categoryId": "guid...",
    "categoryName": "Hambúrgueres",
    "name": "Smash Burguer",
    "description": "Pão brioche, smash de 120g...",
    "price": 28.90,
    "imageUrl": "https://placehold.co/400x400",
    "isAvailable": true,
    "isFeatured": true,
    "displayOrder": 1,
    "createdAtUtc": "2026-05-27T10:00:00Z"
  }
]
```

### POST `/api/stores/{storeId}/products`
Cria um produto.

**Request:**
```json
{
  "categoryId": "guid...",
  "name": "Smash Burguer",
  "description": "Pão brioche, smash de 120g...",
  "price": 28.90,
  "imageUrl": "https://placehold.co/400x400",
  "isFeatured": true,
  "displayOrder": 1
}
```
**Response 201:** `ProductResponseDto`

### PUT `/api/stores/{storeId}/products/{productId}`
Atualiza produto.

**Request:** `UpdateProductRequestDto` (adiciona `isAvailable`)
**Response 200:** `ProductResponseDto`

### PATCH `/api/stores/{storeId}/products/{productId}/availability`
Altera disponibilidade.

**Request:**
```json
{ "isAvailable": false }
```
**Response 200:** `ProductResponseDto`

### DELETE `/api/stores/{storeId}/products/{productId}**
Exclui produto. **Response:** 204 No Content

### POST `/api/stores/{storeId}/products/{productId}/images`
Upload de imagem do produto (multipart/form-data, max 6MB).

**Request:** `IFormFile` (campo: `file`)
**Response 200:** `ProductResponseDto`

---

## 4. Categorias (Vendedor)

Todas as rotas exigem `[SellerOnly]`.

### GET `/api/stores/{storeId}/categories`
Lista categorias da loja.

**Response 200:**
```json
[
  {
    "id": "guid...",
    "storeId": "guid...",
    "name": "Hambúrgueres",
    "displayOrder": 1,
    "isActive": true,
    "isFeatured": false
  }
]
```

### POST `/api/stores/{storeId}/categories`
Cria categoria.

**Request:**
```json
{
  "name": "Hambúrgueres",
  "displayOrder": 1,
  "isFeatured": false
}
```
**Response 201:** `ProductCategoryResponseDto`

### PUT `/api/stores/{storeId}/categories/{categoryId}`
Atualiza categoria.

**Request:**
```json
{
  "name": "Hambúrgueres",
  "displayOrder": 1,
  "isActive": true,
  "isFeatured": false
}
```
**Response 200:** `ProductCategoryResponseDto`

### DELETE `/api/stores/{storeId}/categories/{categoryId}**
Exclui categoria. **Response:** 204 No Content

---

## 5. Checkout

### POST `/api/checkout/preview`
**Pública** (`AllowAnonymous`). Calcula resumo do pedido sem criar no banco.

**Request:**
```json
{
  "storeId": "guid...",
  "fulfillmentType": 1,
  "customerAddressId": "guid...",
  "paymentMethod": 3,
  "notes": "Sem cebola, por favor",
  "items": [
    {
      "productName": "Smash Burguer",
      "quantity": 2,
      "unitPrice": 28.90
    },
    {
      "productName": "Coca-Cola Lata",
      "quantity": 1,
      "unitPrice": 5.90
    }
  ]
}
```

> `fulfillmentType`: 1 = Delivery, 2 = Retirada (PickUp)\
> `paymentMethod`: 1 = PixOnline, 2 = CardOnline, 3 = CashOnDelivery, 4 = CardOnDelivery\
> `customerAddressId`: obrigatório apenas para Delivery

**Response 200:**
```json
{
  "storeId": "guid...",
  "fulfillmentType": 1,
  "customerAddressId": "guid...",
  "paymentMethod": 3,
  "subtotal": 63.70,
  "deliveryFee": 5.90,
  "minimumOrderValue": 15.00,
  "total": 69.60,
  "storeIsOpen": true
}
```

> Para retirada, `deliveryFee` = 0 e `minimumOrderValue` = 0.

**Response 409 (loja fechada):**
```json
{ "error": "Store is closed." }
```

### POST `/api/checkout/confirm`
**Autenticado** (`[CustomerOnly]`). Confirma o checkout e cria o pedido.

**Request:** mesmo `CheckoutRequestDto`

**Response 201:**
```json
{
  "orderId": "guid...",
  "code": "HAP-X7K9M2P1",
  "fulfillmentType": 1,
  "status": 3,
  "subtotal": 63.70,
  "deliveryFee": 5.90,
  "total": 69.60
}
```

> `status`: 3 = Received (para CashOnDelivery/CardOnDelivery), 2 = PendingPayment (PixOnline/CardOnline)

---

## 6. Pedidos

### GET `/api/orders/store/report`
**`[SellerOnly]`** — Relatório simples de pedidos.

**Query params:** `startDateUtc`, `endDateUtc`

### POST `/api/orders`
**`[CustomerOnly]`** — Cria pedido (mesmo que `checkout/confirm`).

**Request:** `CheckoutRequestDto`
**Response 201:** `CheckoutConfirmResponseDto`

### GET `/api/orders/my`
**`[CustomerOnly]`** — Lista pedidos do cliente.

**Response 200:**
```json
[
  {
    "id": "guid...",
    "code": "HAP-X7K9M2P1",
    "storeId": "guid...",
    "status": 4,
    "total": 69.60,
    "createdAtUtc": "2026-05-27T12:00:00Z"
  }
]
```

### GET `/api/orders/{orderId}`
**`[CustomerOnly]`** — Detalhes de um pedido.

**Response 200:**
```json
{
  "id": "guid...",
  "code": "HAP-X7K9M2P1",
  "customerUserId": "guid...",
  "storeId": "guid...",
  "fulfillmentType": 1,
  "status": 3,
  "paymentMethod": 3,
  "subtotal": 63.70,
  "deliveryFee": 5.90,
  "total": 69.60,
  "createdAtUtc": "2026-05-27T12:00:00Z",
  "addressCep": "01304001",
  "addressStreet": "Rua Augusta",
  "addressNumber": "1500",
  "addressNeighborhood": "Consolação",
  "addressCity": "São Paulo",
  "addressState": "SP",
  "addressComplement": "Apto 42",
  "addressReference": null,
  "notes": "Sem cebola",
  "items": [
    {
      "productName": "Smash Burguer",
      "quantity": 2,
      "unitPrice": 28.90,
      "totalPrice": 57.80
    }
  ],
  "history": [
    {
      "createdAtUtc": "2026-05-27T12:00:00Z",
      "previousStatus": 1,
      "newStatus": 3,
      "changedByUserId": "guid...",
      "notes": "Initial order status"
    }
  ]
}
```

> Campos `address*` serão `null` para pedidos de retirada (`fulfillmentType = 2`).

### GET `/api/orders/store`
**`[SellerOnly]`** — Lista pedidos da loja (paginado).

**Query params:** `page`, `pageSize`, `startDateUtc`, `endDateUtc`, `status`

**Response 200:**
```json
{
  "page": 1,
  "pageSize": 20,
  "totalItems": 5,
  "totalPages": 1,
  "items": [ ... ]
}
```

### GET `/api/orders/store/{orderId}`
**`[SellerOnly]`** — Detalhe de um pedido da loja. Response: `OrderDetailsResponseDto`

### PATCH `/api/orders/{orderId}/status`
**`[SellerOnly]`** — Atualiza status do pedido.

**Request:**
```json
{
  "newStatus": 4,
  "notes": "Iniciando preparo"
}
```

**Transições válidas:**
| Status Atual | Próximos Status |
|---|---|
| Created (1) | Received, Cancelled |
| PendingPayment (2) | Received, Cancelled |
| Received (3) | Preparing, Cancelled |
| Preparing (4) | Ready, Cancelled |
| Ready (5) | OnDelivery, Delivered, Cancelled |
| OnDelivery (6) | Delivered, Cancelled |
| Delivered (7) | — |
| Cancelled (8) | — |

**Response 200:** `OrderDetailsResponseDto`

---

## 7. Pagamentos

Todas as rotas exigem `[CustomerOnly]`.

### POST `/api/payments/order`
Inicia pagamento no gateway (Mercado Pago). Válido apenas para pedidos com status `PendingPayment`.

**Request:**
```json
{
  "orderId": "guid..."
}
```

**Response 200 (pagamento criado):**
```json
{
  "paymentId": "guid...",
  "orderId": "guid...",
  "gateway": 1,
  "gatewayTransactionId": "txn_abc123",
  "gatewayCheckoutUrl": "https://mercadopago.com/checkout?pref_id=123",
  "method": 1,
  "status": 1,
  "amount": 69.60,
  "createdAtUtc": "2026-05-27T12:00:00Z",
  "updatedAtUtc": null,
  "history": []
}
```

> `status: 1` = Pending\
> `gateway: 1` = MercadoPago\
> Retorna `400` para métodos sem checkout online (CashOnDelivery, CardOnDelivery). Retorna `409` se o pedido não estiver em `PendingPayment`.

### GET `/api/payments/order/{orderId}`
Dados do pagamento. Response: `OrderPaymentResponseDto`

### GET `/api/payments/order/{orderId}/history`
Histórico de status do pagamento.

**Response 200:**
```json
[
  {
    "createdAtUtc": "2026-05-27T12:00:00Z",
    "previousStatus": null,
    "newStatus": 1,
    "source": "Checkout",
    "notes": "Initial payment status"
  }
]
```

---

## 8. Webhooks

Ambos `[AllowAnonymous]`.

### POST `/api/webhooks/mercadopago`
Recebe notificações de pagamento do Mercado Pago.

**Request:** raw JSON body (enviado pelo Mercado Pago)
**Response 200:** `{ "received": true }`

### POST `/api/webhooks/asaas`
Recebe notificações de assinatura do Asaas. Valida header `asaas-access-token`.

**Request:** raw JSON body + header
**Response 200:** `{ "received": true }`

---

## 9. Administração

Todas as rotas exigem `[AdminOnly]`.

### GET `/api/admin/dashboard`
Ping de verificação.

**Response 200:**
```json
{ "area": "admin", "message": "Admin authorized." }
```

### GET `/api/admin/plans`
Lista planos de assinatura.

**Response 200:**
```json
[
  {
    "id": "guid...",
    "name": "Plano Básico",
    "amount": 49.90,
    "description": "Ideal para pequenos negócios.",
    "isActive": true
  }
]
```

### POST `/api/admin/plans`
Cria plano.

**Request:**
```json
{
  "name": "Plano Premium",
  "amount": 99.90,
  "description": "Taxa zero por pedido.",
  "isActive": true
}
```
**Response 201:** `PlanResponseDto`

### PUT `/api/admin/plans/{planId}`
Atualiza plano.

**Request:** `UpdatePlanRequestDto` (mesmos campos)
**Response 200:** `PlanResponseDto`

### PATCH `/api/admin/plans/{planId}/status`
Ativa/desativa plano.

**Request:**
```json
{ "isActive": false }
```
**Response 200:** `PlanResponseDto`

### POST `/api/admin/subscriptions/status`
Insere/atualiza status de assinatura manualmente.

### POST `/api/admin/subscriptions/notifications/process`
Processa notificações de assinatura em lote.

### GET `/api/admin/system-parameters`
Lista parâmetros do sistema.

### GET `/api/admin/system-parameters/{key}`
Obtém parâmetro por chave.

### PUT `/api/admin/system-parameters/{key}`
Atualiza parâmetro.

**Request:**
```json
{ "value": "novo-valor" }
```

### DELETE `/api/admin/system-parameters/{key}**
Exclui parâmetro. **Response:** 204

### POST `/api/admin/system-parameters/reload`
Recarrega parâmetros do banco.

---

## 10. Cliente

### GET `/api/customer/home`
**`[CustomerOnly]`** — Ping de verificação.

```json
{ "area": "customer", "message": "Customer authorized." }
```

---

## 11. Endereços do Cliente

Todas as rotas exigem `[CustomerOnly]`.

### GET `/api/customer/addresses`
Lista endereços do cliente.

**Response 200:**
```json
[
  {
    "id": "guid...",
    "cep": "01304001",
    "street": "Rua Augusta",
    "number": "1500",
    "neighborhood": "Consolação",
    "city": "São Paulo",
    "state": "SP",
    "complement": "Apto 42",
    "reference": null,
    "isPrimary": true,
    "latitude": null,
    "longitude": null
  }
]
```

### POST `/api/customer/addresses`
Cria novo endereço (máx. 3).

**Request:**
```json
{
  "cep": "01304001",
  "number": "1500",
  "street": "Rua Augusta",
  "neighborhood": "Consolação",
  "city": "São Paulo",
  "state": "SP",
  "complement": "Apto 42",
  "reference": null,
  "isPrimary": true,
  "latitude": null,
  "longitude": null
}
```
**Response 201:** `CustomerAddressResponseDto`

### PUT `/api/customer/addresses/{addressId}`
Atualiza endereço. **Request:** `UpsertCustomerAddressRequestDto`. **Response 200.**

### DELETE `/api/customer/addresses/{addressId}**
Exclui endereço. **Response:** 204 No Content

---

## 12. Notificações do Cliente

### GET `/api/customer/notifications`
**`[CustomerOnly]`** — Lista notificações.

### PATCH `/api/customer/notifications/{notificationId}/read`
**`[CustomerOnly]`** — Marca como lida. **Response:** 204

---

## 13. Vendedor

### GET `/api/seller/panel`
**`[SellerOnly]`** — Ping de verificação.

```json
{ "area": "seller", "message": "Seller authorized." }
```

---

## 14. Notificações do Vendedor

### GET `/api/seller/notifications`
**`[SellerOnly]`** — Lista notificações.

### PATCH `/api/seller/notifications/{notificationId}/read`
**`[SellerOnly]`** — Marca como lida. **Response:** 204

---

## 15. Busca de CEP

### GET `/api/address-lookup/cep/{cep}`
**`[AllowAnonymous]`** — Busca endereço por CEP (ViaCEP).

**Exemplo:** `GET /api/address-lookup/cep/01304001`

**Response 200:**
```json
{
  "cep": "01304-001",
  "street": "Rua Augusta",
  "neighborhood": "Consolação",
  "city": "São Paulo",
  "state": "SP"
}
```

---

## 16. Lojas Públicas

Todas as rotas são `[AllowAnonymous]`.

### GET `/api/public/stores`
Lista lojas ativas (não bloqueadas por assinatura).

**Query params:** `cuisineType` (opcional)

**Response 200:**
```json
[
  {
    "id": "guid...",
    "name": "Burguer do Rafa",
    "slug": "burguer-do-rafa",
    "storePath": "burguer_do_rafa",
    "cuisineType": "Lanches",
    "isOpen": true,
    "logoUrl": "https://images.com/logo.jpg",
    "deliveryFee": 5.90,
    "minimumOrderValue": 15.00,
    "averageRating": 4.5,
    "totalReviews": 42
  }
]
```

### GET `/api/public/stores/{storeId}`
Detalhes públicos de uma loja por ID.

**Response 200:**
```json
{
  "id": "guid...",
  "name": "Burguer do Rafa",
  "slug": "burguer-do-rafa",
  "storePath": "burguer_do_rafa",
  "phoneNumber": "(11) 99999-0001",
  "description": "Hambúrgueres artesanais",
  "cuisineType": "Lanches",
  "bannerUrl": "https://images.com/banner.jpg",
  "logoUrl": "https://images.com/logo.jpg",
  "tawkToPropertyId": "abc123",
  "isOpen": true,
  "deliveryFee": 5.90,
  "minimumOrderValue": 15.00,
  "address": { ... },
  "businessHours": [ ... ],
  "averageRating": 4.5,
  "totalReviews": 42
}
```

### GET `/api/public/stores/by-slug/{slug}`
Detalhes públicos de uma loja por slug.

**Exemplo:** `GET /api/public/stores/by-slug/burguer-do-rafa`

**Response 200:** `StorePublicDetailsDto` (mesmo formato acima)

### GET `/api/public/stores/by-path/{storePath}`
Detalhes públicos de uma loja pelo `storePath` (URL amigável).  
Endpoint usado pelo frontend para carregar a loja a partir da URL: `https://www.urbeat.com.br/{storePath}`

**Exemplo:** `GET /api/public/stores/by-path/burguer_do_rafa`

**Response 200:**
```json
{
  "id": "guid...",
  "name": "Burguer do Rafa",
  "slug": "burguer-do-rafa",
  "storePath": "burguer_do_rafa",
  "phoneNumber": "(11) 99999-0001",
  "description": "Hambúrgueres artesanais",
  "cuisineType": "Lanches",
  "bannerUrl": "https://images.com/banner.jpg",
  "logoUrl": "https://images.com/logo.jpg",
  "tawkToPropertyId": "abc123",
  "isOpen": true,
  "deliveryFee": 5.90,
  "minimumOrderValue": 15.00,
  "address": { ... },
  "businessHours": [ ... ],
  "averageRating": 4.5,
  "totalReviews": 42
}
```

**Status:** `200` | `404`

---

## 17. Catálogo Público

Todas as rotas são `[AllowAnonymous]`.

### GET `/api/public/stores/{storeId}/catalog/categories`
Lista categorias ativas de uma loja.

**Response 200:**
```json
[
  {
    "id": "guid...",
    "storeId": "guid...",
    "name": "Hambúrgueres",
    "displayOrder": 1,
    "isActive": true,
    "isFeatured": false
  }
]
```

### GET `/api/public/stores/{storeId}/catalog/products`
Lista produtos disponíveis de uma loja.

**Response 200:**
```json
[
  {
    "id": "guid...",
    "storeId": "guid...",
    "categoryId": "guid...",
    "categoryName": "Hambúrgueres",
    "name": "Smash Burguer",
    "description": "Pão brioche, smash de 120g...",
    "price": 28.90,
    "imageUrl": "https://placehold.co/400x400",
    "isAvailable": true,
    "isFeatured": true,
    "displayOrder": 1,
    "createdAtUtc": "2026-05-27T12:00:00Z"
  }
]
```

### GET `/api/public/stores/{storeId}/catalog/products/featured`
Lista apenas produtos em destaque (`isFeatured: true`).

**Response 200:** `IReadOnlyCollection<ProductResponseDto>`

---

## 18. Avaliações

### POST `/api/orders/{orderId}/review`
**`[CustomerOnly]`** — Cria ou atualiza avaliação de um pedido.

**Request:**
```json
{
  "rating": 5,
  "comment": "Hambúrguer incrível!"
}
```

**Response 200:**
```json
{
  "id": "guid...",
  "orderId": "guid...",
  "rating": 5,
  "comment": "Hambúrguer incrível!",
  "createdAtUtc": "2026-05-27T14:00:00Z"
}
```

### GET `/api/orders/{orderId}/review`
**`[Authorize]`** — Obtém avaliação do pedido pelo cliente.

### GET `/api/public/stores/{storeId}/reviews`
**`[AllowAnonymous]`** — Lista avaliações públicas de uma loja.

**Response 200:**
```json
[
  {
    "id": "guid...",
    "orderId": "guid...",
    "customerName": "João S.",
    "rating": 5,
    "comment": "Hambúrguer incrível!",
    "createdAtUtc": "2026-05-27T14:00:00Z"
  }
]
```

---

## 19. Assinaturas

Todas as rotas exigem `[SellerOnly]`.

### GET `/api/subscriptions/plans`
Lista planos de assinatura ativos.

**Response 200:** `IReadOnlyList<PlanResponseDto>`

### POST `/api/subscriptions/contract`
Contrata um plano de assinatura.

**Request:**
```json
{
  "planId": "guid..."
}
```

**Response 201:**
```json
{
  "subscriptionId": "guid...",
  "status": 1,
  "nextBillingDateUtc": "2026-06-27T00:00:00Z"
}
```

### GET `/api/subscriptions/my`
Dados da assinatura do vendedor.

### GET `/api/subscriptions/my/charges`
Histórico de cobranças.

**Response 200:**
```json
[
  {
    "chargeId": "guid...",
    "amount": 49.90,
    "status": 1,
    "dueDateUtc": "2026-05-27T00:00:00Z",
    "paidAtUtc": "2026-05-27T10:00:00Z"
  }
]
```

---

## Enums

### OrderStatus
| Valor | Nome |
|---|---|
| 1 | Created |
| 2 | PendingPayment |
| 3 | Received |
| 4 | Preparing |
| 5 | Ready |
| 6 | OnDelivery |
| 7 | Delivered |
| 8 | Cancelled |

### PaymentMethod
| Valor | Nome |
|---|---|
| 1 | PixOnline |
| 2 | CardOnline |
| 3 | CashOnDelivery (dinheiro na entrega) |
| 4 | CardOnDelivery (máquina cartão na entrega) |

### FulfillmentType
| Valor | Nome |
|---|---|
| 1 | Delivery (entrega) |
| 2 | PickUp (retirada) |

### PaymentStatus
| Valor | Nome |
|---|---|
| 1 | Pending |
| 2 | Paid |
| 3 | Failed |
| 4 | Cancelled |
| 5 | Refunded |

### PaymentGateway
| Valor | Nome |
|---|---|
| 1 | MercadoPago |

### SellerSubscriptionBillingStatus
| Valor | Nome |
|---|---|
| 1 | Active |
| 2 | Overdue |
| 3 | Blocked |

---

## Políticas de Autorização

| Policy | Roles |
|---|---|
| `CustomerOnly` | Customer |
| `SellerOnly` | Seller |
| `AdminOnly` | Admin |

O token JWT deve conter os claims:
- `sub` ou `ClaimTypes.NameIdentifier`: GUID do usuário
- `role`: papel do usuário (Customer, Seller, Admin)

---

## Códigos de Erro Comuns

| Status | Significado |
|---|---|
| 200 | Sucesso |
| 201 | Criado |
| 204 | Sem conteúdo (DELETE/operações sem retorno) |
| 400 | Requisição inválida (validação) |
| 401 | Não autenticado |
| 403 | Proibido (sem permissão) |
| 404 | Não encontrado |
| 409 | Conflito (loja fechada, bloqueada, pedido inválido) |
| 423 | Bloqueado (lockout por muitas tentativas de login) |
