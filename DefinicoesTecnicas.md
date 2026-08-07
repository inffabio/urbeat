
# Estrutura de Diretórios do Projeto

```
src/
  backend/        # API .NET 9 (Clean Architecture)
  frontend/       # Angular 20 + PrimeNG
  shared/         # (Opcional) Código compartilhado
tests/            # Testes automatizados (unitários/integrados)
docker/           # Dockerfiles e configs de container
scripts/          # Scripts utilitários
.github/          # Workflows de CI/CD
Documentacao/     # Requisitos e docs funcionais
DefinicoesTecnicas.md
```

## backend
- Microsoft dotnet core 9.0


## Banco de dados
- Banco de dados postgresql 18.3
- Utilzar Entity Framework core para Gravação de dados
  - Utilizar o padrão repository e unit of work
  - Chaves Primarias utilizar GUIID versao 7
- Para consultas utilizar Dapper
  - Utilizar o padrão repository e unit of work
- Criar Migrations versionadas

## Boas práticas
- criar índices nas tabelas mais consultadas
- usar constraints
- manter relacionamento claro
- versionar o banco com migrations

## Tabelas técnicas mínimas recomendadas
- `RefreshTokens`
- `AuditLogs`
   

### Autenticação
- [ ] ASP.NET Core Identity
- [ ] Admin principal (seed inicial) com exceção de confirmação de e-mail
- [ ] JWT access token de 15 min
- [ ] Refresh token rotativo de 7 dias
- [ ] Refresh token em cookie HttpOnly
- [ ] Angular Interceptor + Guards
- [ ] Roles + Policies
- [ ] Validação de ownership no backend
	

## Estrutura sugerida para src/frontend

```
src/frontend/
  src/                # Código-fonte Angular
  e2e/                # Testes end-to-end
  angular.json        # Configuração do Angular
  package.json        # Dependências e scripts
  README.md           # Instruções do frontend
```

- Utilizar Angular versão 20
- PrimeNG para Angular 20
  #  🏗️ Infraestrutura Backend Simplificada

  > **Observação:**
  > A estrutura acima é um ponto de partida. Ajuste conforme o projeto evoluir (ex: adicionar pasta docs, separar testes, etc).

   
## Em pagamentos

- Mercado Pago Checkout Pro para pagamento do pedido
- Asaas para assinatura mensal do vendedor
- Webhook como verdade final
- Idempotência
- Persistência do payload bruto
- Bloqueio operacional por inadimplência
- Utilize o padrão adapter para 

## Arquitetura Hexagonal/ Clean Arhitecture no backend

 - Sempre que possivel utilzar os padrão de projeto 
    - Strategy
	- Adapter
	- Factory
 -  use estes padroes para 
    - Interface comum para pagamentos
    - Strategy para comportamento
    - Adater para integração externa
    - Factory para resolução do provider
    - Hexagonal/Clean para desacoplamento
	
## Auditoria
   - Salvar no banco de dados
     - request enviado
	 - response recebido
	 - webhook bruto
	 - transaction id externo
	 
## Logs
  - utlizar Serilog e  OpenTelemetry. gravar em banco de dados separado UrbeatLogs
     - Erros
	 - Auditoria
	 - Login
	 - User events

#  🏗️ Infraestrutura Backend Simplificada

> Documento base para o backend em **.NET 9**, com foco em:
> - simplicidade
> - escalabilidade inicial
> - testabilidade
> - rastreabilidade
> - organização com Clean Architecture

---

# 🎯 Objetivo

Criar uma base simples e organizada para o backend do sistema, permitindo:

- evoluir o projeto sem bagunça 📈
- testar regras importantes com segurança 🧪
- registrar erros e eventos relevantes 🔎
- manter separação entre regra de negócio e infraestrutura 🧱


# ✅ Stack recomendada

## Backend
- **ASP.NET Core Web API (.NET 9)**
- **Entity Framework Core**  para inserções
- ** Dapper**  para consultas
- **PostgreSQL**
- **JWT + Refresh Token**
- **FluentValidation**
- **Serilog**
- **Swagger / OpenAPI**
- **Docker**
- **Hangfire** para tarefas agendadas simples

# 🧭 Princípios para manter o sistema simples

- controllers leves
- regras de negócio fora dos controllers
- acesso a banco fora da API
- integração com serviços externos via interfaces
- logs estruturados
- tratamento global de erros
- autenticação por token
- testes automatizados nas partes críticas

# 📈 Escalabilidade inicial

## Objetivo
Permitir que o sistema cresça sem reescrever tudo.

## Regras práticas
- API deve ser **stateless**
- usar **JWT** para autenticação
- evitar guardar estado da sessão no servidor
- usar paginação em listagens
- criar índices no banco nas tabelas mais acessadas
- separar responsabilidades em camadas
- deixar integrações externas isoladas

## O que isso significa na prática
- a API pode crescer sem depender de sessão em memória
- é mais fácil adicionar novos módulos depois
- é mais fácil trocar gateway de pagamento ou storage de imagem

# 🧪 Testabilidade

## Objetivo
Garantir que o sistema possa ser validado com testes sem depender de tudo estar rodando ao mesmo tempo.

## Tipos de teste

### Testes unitários
Validam:
- regras de negócio
- validações
- serviços de aplicação
- cálculos e regras do pedido

### Testes de integração
Validam:
- API + banco
- autenticação
- criação de pedido
- integração com persistência

## Ferramentas sugeridas
- **xUnit**
- **FluentAssertions**
- **Moq**
- **WebApplicationFactory**

## Regras práticas para facilitar testes
- usar interfaces para serviços externos
- evitar lógica dentro de controller
- não acoplar regra de negócio ao EF diretamente
- criar casos de uso claros

---# 🔎 Rastreabilidade

## Objetivo
Saber:
- qual erro aconteceu
- em qual operação aconteceu
- qual usuário executou a ação
- quando aconteceu

---

# 1. Logs de erro

## Recomendação
Usar **Serilog** com logs estruturados.

## Registrar em logs
- erros inesperados
- exceções de integração
- falhas de autenticação
- falhas em pagamento
- falhas em upload
- falhas de banco

## Campos mínimos no log
- data/hora
- nível do log
- mensagem
- nome da aplicação
- ambiente
- endpoint
- usuário autenticado, se houver
- id da loja, se houver
- exception

 2. Logs de eventos de usuário

## Registrar eventos importantes
- login
- logout
- cadastro de usuário
- criação da loja
- edição da loja
- criação de produto
- edição de produto
- criação de pedido
- alteração de status do pedido
- contratação de assinatura
- bloqueio de loja

## Exemplo de eventos
- `USER_LOGGED_IN`
- `STORE_CREATED`
- `PRODUCT_CREATED`
- `ORDER_CREATED`
- `ORDER_STATUS_CHANGED`

---

# 3. Auditoria simples

## Objetivo
Guardar um histórico básico das ações importantes do sistema.

## Sugestão de tabela `AuditLogs`
Campos:
- `Id`
- `CreatedAt`
- `UserId`
- `UserRole`
- `EventType`
- `EntityName`
- `EntityId`
- `Description`
- `IpAddress`

## Exemplo
- usuário vendedor alterou o status do pedido 123 para `Preparing`
- admin bloqueou a loja 45
- cliente criou pedido 999

## Exemplo de log estruturado de erro
- json
 
  {
    "timestamp": "2026-05-14T12:00:00Z",
    "level": "Error",
    "message": "Erro ao processar pagamento do pedido",
    "application": "DeliveryApp.API",
    "environment": "Production",
    "traceId": "b2f8f4...",
    "correlationId": "req-12345",
    "userId": "user-789",
    "storeId": "store-456",
    "orderId": "order-999",
    "provider": "MercadoPago",
    "exception": "TimeoutException",
    "sourceContext": "DeliveryApp.Infrastructure.Payments.MercadoPago.MercadoPagoGateway"
  }
---

## Exemplo de log de evento de usuário
 - json
  
    {
      "timestamp": "2026-05-14T12:01:00Z",
      "level": "Information",
      "eventType": "ORDER_STATUS_CHANGED",
      "message": "Status do pedido alterado",
      "userId": "seller-1",
      "storeId": "store-10",
      "entityName": "Order",
      "entityId": "order-200",
      "oldStatus": "Preparing",
      "newStatus": "Ready",
      "correlationId": "req-67890"
    }
---

# 4. Tratamento global de erros

## Recomendação
Criar um middleware global para exceções.

## Objetivo
- evitar try/catch em todo controller
- padronizar resposta de erro
- registrar o erro no log

## Benefícios
- código mais limpo
- rastreamento melhor
- resposta consistente para o frontend

# 🔐 Segurança mínima

## Regras obrigatórias
- HTTPS
- JWT com expiração curta
- Refresh Token
- senhas com ASP.NET Core Identity
- secrets fora do código
- CORS configurado corretamente
- logs sem dados sensíveis

# 🌐 API

## Recomendado
- controllers finos
- validação com FluentValidation
- respostas padronizadas
- Swagger habilitado
- autenticação por JWT
- middleware global de erro

## Endpoints técnicos úteis
- `/swagger`
- `/health`

## AutoMapper and MediatR

- chave para configuracao do Autommaper e do MediatR
   eyJhbGciOiJSUzI1NiIsImtpZCI6Ikx1Y2t5UGVubnlTb2Z0d2FyZUxpY2Vuc2VLZXkvYmJiMTNhY2I1OTkwNGQ4OWI0Y2IxYzg1ZjA4OGNjZjkiLCJ0eXAiOiJKV1QifQ.eyJpc3MiOiJodHRwczovL2x1Y2t5cGVubnlzb2Z0d2FyZS5jb20iLCJhdWQiOiJMdWNreVBlbm55U29mdHdhcmUiLCJleHAiOiIxODEwMzM5MjAwIiwiaWF0IjoiMTc3ODg1OTgyMiIsImFjY291bnRfaWQiOiIwMTllMmM0ODRiMDU3YmZjOGJmOGU1OGVmYjI3Y2FkNSIsImN1c3RvbWVyX2lkIjoiY3RtXzAxa3JwNGpkYWg4d2NzYmVmNGc5MnhheXBuIiwic3ViX2lkIjoiLSIsImVkaXRpb24iOiIwIiwidHlwZSI6IjIifQ.Y6ATtAeXoazGbWNOKGTbieWPiEIUh6VfvWI-uPXNRRlcLIKRWWVzY8RZGbYfL0DQvDTenaCJqJppm-AgeZq-gCt5rsQ6PB07JViW7mkACfPLgD1DMThwUozfez6iASf-C0-Y7hXdw96em6apXTVTPdXDvZFPjGf_bN7jLhs_yFF5HIJoBfnoGri9O9uox3E6RmDSHNE1FvRQOc3_HlsymwNjivkJDT0ZeLCTEUUkyEJlKcG4B3BYeU9l37hGM7V4NB7HZRnnzx4kMLoSBcX1m45UDBsyPiSvXdZe-Acm0uGIIW92mAvEY18O420-6k2RqiD7cML7CLnCm3n0GPJ3ZA


## a estrutura de pastas do BackEnd



Para logomarcas e fotos dos produtos:
Cloudinary


Cloudinary costuma ser muito prático
🧩 O que mais você precisa além de Angular e .NET
Além do front e back, você vai precisar pensar nestes blocos:

1. 👥 Perfis de usuário
Você tem pelo menos 3 perfis:

Administrador da plataforma
Vendedor / estabelecimento
Consumidor final


Ou seja:

cada vendedor terá sua própria loja
seus próprios produtos
seus próprios pedidos
suas próprias configurações
Isso precisa estar bem definido desde o início no banco e nas permissões.

3. 📍 Geolocalização e endereço
Você vai precisar decidir:

busca por bairro, cidade ou CEP
área de entrega por:
raio em km
bairros atendidos
CEP
cálculo de taxa de entrega
Integrações úteis:

Google Maps API
ViaCEP
OpenStreetMap para reduzir custo
4. 🔔 Notificações
Muito importante no delivery.

Você pode usar:


Você disse que o vendedor pagará mensalidade.

Então você precisa de:

plano ativo/inativo
vencimento
renovação
bloqueio por inadimplência
período de teste opcional
emissão de cobrança
8. 🧾 Painel administrativo
Você precisará de pelo menos 2 áreas administrativas:

Admin da plataforma
gerenciar vendedores
aprovar/rejeitar cadastros
ver pagamentos da mensalidade
gerenciar planos
relatórios gerais
suporte
Admin do vendedor
cadastrar loja
editar perfil
cadastrar categorias
cadastrar produtos
ver pedidos
atualizar status
ver relatórios
ver assinatura/plano
9. 📊 Relatórios
Desde o início vale pensar em relatórios simples:

Para o vendedor
total de pedidos
faturamento por período
produto mais vendido
pedidos cancelados
Para a plataforma
total de lojas ativas
MRR/receita mensal recorrente
inadimplência
quantidade de pedidos na plataforma
tipos de culinária mais buscados
10. 🔐 Segurança
Essencial para qualquer app com pagamento.

Você vai precisar de:

autenticação JWT
senhas com hash forte
autorização por perfil
rate limiting
proteção contra upload malicioso
logs de auditoria
HTTPS
LGPD
✅ Levantamento de requisitos funcionais
Abaixo está uma proposta inicial de requisitos funcionais.



Backend em camadas
API
Application
Domain
Infrastructure
Componentes principais
Auth Service
User Service
Store Service
Catalog Service
Order Service
Payment Service
Subscription Service
Notification Service
Banco
Principais entidades:

Usuário
Perfil
Loja
Categoria
Produto
ProdutoImagem
Pedido
ItemPedido
Endereço
Pagamento
Assinatura
Plano
Notificação

 └── DeliveryApp.Shared

3. 🗄️ Modelagem inicial do banco de dados
Abaixo está uma proposta inicial de entidades.

👤 Usuários
Users
Id
Name
Email
Phone
PasswordHash
Role
Admin
Seller
Customer
IsActive
CreatedAt
UpdatedAt


🏪 Loja / vendedor
Stores
  Id
  OwnerUserId
  Name
  Slug
  LogoUrl
  BannerUrl
  Description
  CuisineTypeId
  Phone
  Whatsapp
  Email
  DocumentNumber
  DeliveryFee
  MinimumOrderValue
  IsOpen
  IsActive
  CreatedAt

CuisineTypes
Id
Name

📍 Endereços
      Addresses
      Id
      UserId nullable
      StoreId nullable
      Street
      Number
      Complement
      Neighborhood
      City
      State
      ZipCode
      Reference
      Latitude
      Longitude
      Pode servir tanto para cliente quanto para loja.

🧾 Catálogo
      ProductCategories
      Id
      StoreId
      Name
      DisplayOrder
      IsActive
      Products
      Id
      StoreId
      CategoryId
      Name
      Description
      Price
      ImageUrl
      IsAvailable
      PreparationTimeMinutes
      CreatedAt



🛒 Carrinho e pedido
      Orders
        Id
        CustomerUserId
        StoreId
        DeliveryAddressId
        Status
        Subtotal
        DeliveryFee
        Discount
        Total
        PaymentMethod
        PaymentStatus
        Notes
        CreatedAt
        ConfirmedAt
        DeliveredAt
        OrderItems
        Id
        OrderId
        ProductId
        ProductName
        UnitPrice
        Quantity
        TotalPrice
        Observation
       
💳 Pagamentos
        Payments
          Id
          OrderId nullable
          SubscriptionId nullable
          Gateway
          GatewayTransactionId
          Amount
          Method
          Status
          PaidAt
          CreatedAt
          RawResponseJson

📅 Assinatura do vendedor
        Plans
          Id
          Name
          Price
          Description
          IsActive
          Subscriptions
          Id
          StoreId
          PlanId
          Status
          StartDate
          EndDate
          NextBillingDate
          GatewayCustomerId
          GatewaySubscriptionId

🔔 Notificações
Notifications
Id
UserId
Title
Message
Type
IsRead
CreatedAt
📝 Auditoria / logs de negócio
AuditLogs
Id
UserId
EntityName
EntityId
Action
Data
CreatedAt
4. 🔗 Relacionamentos principais
Relações
1 User Seller → 1 ou N Stores
no começo você pode permitir 1 loja por vendedor

1 Store → N ProductCategories
1 Store → N Products
1 Product → N ProductOptionsGroups
1 ProductOptionsGroup → N ProductOptions
1 Customer User → N Orders
1 Order → N OrderItems
1 Store → N Orders
1 Store → 1 Subscription
1 Plan → N Subscriptions



🔐 Auth
    - POST /api/auth/register-customer
   ## cadastra consumidor
    - POST /api/auth/register-seller
  ## cadastra vendedor
    - POST /api/auth/login
  ## login
    - POST /api/auth/refresh-token
   ##  renovar token
    - POST /api/auth/logout
  ## logout

👤 Usuários
   - GET /api/users/me
  ## dados do usuário logado
   - PUT /api/users/me
  ## atualizar perfil

🏪 Lojas
  - POST /api/stores
  ## criar loja
  - GET /api/stores
  ## listar lojas
  - GET /api/stores/{id}
  ## detalhar loja
  - PUT /api/stores/{id}
  ## editar loja
  - PATCH /api/stores/{id}/status
  ## abrir/fechar loja

📂 Categorias de produto
  - POST /api/stores/{storeId}/categories
  ## criar categoria
  - GET /api/stores/{storeId}/categories
  ## listar categorias
  - PUT /api/categories/{id}
  ## editar categoria
  - DELETE /api/categories/{id}
  ## excluir categoria

🍔 Produtos
  - POST /api/stores/{storeId}/products
  ## criar produto
  - GET /api/stores/{storeId}/products
  ## listar produtos da loja
  - GET /api/products/{id}
  ## detalhar produto
  - PUT /api/products/{id}
  ## editar produto
  - PATCH /api/products/{id}/availability
  ## ativar/inativar
  - DELETE /api/products/{id}
  ## excluir produto

🛒 Pedidos
  - POST /api/orders
  ## criar pedido
  - GET /api/orders/my
  ## listar pedidos do consumidor
  - GET /api/orders/store
  ## listar pedidos da loja logada
  - GET /api/orders/{id}
  ## detalhar pedido
  - PATCH /api/orders/{id}/status
  ## atualizar status

📍 Endereços
  - GET /api/addresses
  ## listar endereços do usuário
  - POST /api/addresses
  ## criar endereço
  - PUT /api/addresses/{id}
  ## editar endereço
  - DELETE /api/addresses/{id}
  ## excluir endereço

💳 Pagamentos
  - POST /api/payments/order
  ## iniciar pagamento de pedido
  - POST /api/payments/subscription
  ## iniciar pagamento da assinatura
  - POST /api/webhooks/mercadopago
  ## webhook do pedido
  - POST /api/webhooks/asaas
  ## webhook da assinatura

📅 Assinaturas
  - GET /api/subscriptions/my
  ## ver assinatura da loja
  - POST /api/subscriptions
  ## contratar plano
  - PATCH /api/subscriptions/{id}/cancel
  ## cancelar assinatura

R
Para o seu cenário, eu usaria:

- ASP.NET Core Identity

- JWT Access Token
    - Refresh Token rotativo
    - Autorização por Roles/Policies
    - Cookie HttpOnly para refresh token

- 🔑 Modelo técnico recomendado
      - Access Token
      - tipo: JWT
      - duração: 15 minutos
      - enviado no header:
      - http
 
- Authorization: Bearer {access_token}
    - Refresh Token
    - duração inicial: 7 dias
    - armazenado em:
    - cookie HttpOnly
    - Secure
    - SameSite=Lax ou Strict se o fluxo permitir
    - o refresh token:
    - deve ser salvo no banco
    - deve ter rotação
    - deve poder ser revogado
    - deve expirar

- 🧠  Claims do JWT
     - Claims mínimas:
      - sub → id do usuário
      - email
      - role
      - storeId → quando aplicável
      - tokenVersion → opcional para invalidação global

- 👤 Roles
       - Admin
       - Seller
       - Customer

- 🛡️ Policies
      - RequireAdmin
      - RequireSeller
      - RequireCustomer

- 🔄 Fluxo de autenticação
      - Login
      - usuário envia email + senha
      - backend valida no Identity
      - backend gera:
      - accessToken
      - expiresIn
      - backend grava e devolve o refreshToken em cookie HttpOnly
      - Refresh
      - Angular recebe 401
      - interceptor chama /api/auth/refresh
      - backend valida refresh token
      - gera novo:
      - access token
      - refresh token
      - invalida o refresh token anterior
      - Logout
      - frontend chama /api/auth/logout
      - backend revoga refresh token
      - frontend limpa estado da sessão


- 📦 Angular 20 — como implementar
      - AuthService
      - HttpInterceptor
      - Route Guards
      - Signals para estado simples
      - área pública + área do vendedor + área do admin
      - Armazenamento
      - access token: memória da aplicação
      - refresh token: cookie HttpOnly
      - dados básicos do usuário: sessionStorage opcional
      ***⚠️ Evite guardar refresh token em localStorage.***

- 🔌 Endpoints de autenticação
      - POST /api/auth/register-customer
      - POST /api/auth/register-seller
      - POST /api/auth/login
      - POST /api/auth/refresh
      - POST /api/auth/logout
      - GET /api/auth/me

- 💳 Definição técnica de pagamentos
Você tem dois fluxos diferentes.

1️⃣ Pagamento da mensalidade do vendedor
✅ Gateway recomendado: Asaas
Motivos
excelente para cobrança recorrente
    forte no Brasil
    API simples
    suporta:
    PIX
    boleto
    cartão
    bom para modelo SaaS
    bom para inadimplência e renovação mensal

2️⃣ Pagamento do pedido do cliente

✅ Gateway recomendado: Mercado Pago
      Motivos
      muito forte no Brasil
      suporta:
      PIX
      cartão
      boleto
      integração relativamente simples
      checkout maduro
      bom para MVP

- Mensalidade do lojista: Asaas
- Pagamento do pedido: Mercado Pago
❓ Por que não usar um único gateway?
Você pode usar um só, mas no seu caso:

Asaas é mais forte para assinatura recorrente
Mercado Pago é mais natural para checkout de compra do consumidor
Se no futuro quiser unificar:

alternativa: Pagar.me
💡 Estratégia recomendada para pedido no MVP
Use Mercado Pago Checkout Pro

Porque:

é mais rápido de subir
reduz esforço de compliance
reduz complexidade no frontend
acelera lançamento
Depois, na Fase 2/3
migrar para Checkout Transparente, se quiser UX melhor

🔄 Fluxo recomendado do pagamento do pedido
cliente fecha pedido
backend cria o pedido com status:
PendingPayment
backend cria preferência/transação no Mercado Pago
cliente paga
Mercado Pago envia webhook
backend confirma e atualiza:
PaymentStatus = Paid
OrderStatus = Received
vendedor é notificado

🔄 Fluxo recomendado da assinatura
vendedor escolhe plano
backend cria cliente no Asaas
backend cria assinatura/cobrança recorrente
Asaas envia webhook
backend atualiza:
assinatura ativa
vencida
inadimplente
sistema bloqueia loja se necessário

📌 Status recomendados

- Pedido
    - PendingPayment
    - Paid
    - Received
    - Preparing
    - Ready
    - OnDelivery
    - Delivered
    - Cancelled

- Pagamento
    - Pending
    - Paid
    - Failed
    - Cancelled
    - Refunded
    - Assinatura
    - PendingPayment
    - Active
    - PastDue
    - Suspended
    - Cancelled

- 🛡️ Regras importantes de pagamento
      não confiar só no retorno do frontend
      confirmação final sempre por webhook
      webhook deve ser idempotente
      
      salvar:
      payload bruto
      transaction id
      status mapeado

📚 Estrutura dos cards do MVP
Vou seguir este padrão em todos:

Épico
Fase
Perfil
Prioridade
Descrição
Regras de negócio
Critérios de aceite
Checklist técnico
Dependências
Próximo card sugerido
Observações técnicas quando necessário


O melhor caminho é combinar estes padrões:

🧠 Strategy Pattern
🔌 Adapter Pattern
🏭 Factory Pattern
🧱 Ports and Adapters (Hexagonal Architecture)
👉 Em termos práticos:
A melhor solução é:

Hexagonal/Clean Architecture + Strategy + Adapter + Factory
Isso permite que você:

trocar Mercado Pago por Asaas, Pagar.me, Stripe etc.
evitar espalhar código do gateway pelo sistema inteiro
manter sua regra de negócio independente do provedor
testar melhor

4. 🧱 Ports and Adapters / Hexagonal
Esse é o mais importante no nível arquitetural.

Seu sistema define a porta:

IPaymentGateway
E a infraestrutura implementa os adapters:

MercadoPagoPaymentGateway
AsaasPaymentGateway
Vantagem
desacoplamento real
mais testável
mais sustentável no longo prazo
✅ O melhor desenho para pagamento intercambiável
Modelo ideal
text
 
[Controller/API]
      ↓
[Application Service / Use Case]
      ↓
[IPaymentGateway]  ← porta
      ↓
[Factory escolhe provider]
      ↓
[MercadoPagoAdapter | AsaasAdapter | StripeAdapter]
      ↓
[API externa]

💡 Recomendação prática
Use estas peças juntas:
Interface comum para pagamentos
Strategy para comportamento
Adapter para integração externa
Factory para resolução do provider
Hexagonal/Clean para desacoplamento
📦 Exemplo de modelagem
Enum do provedor
csharp
 
public enum PaymentProvider
{
    MercadoPago = 1,
    Asaas = 2,
    Stripe = 3,
    PagarMe = 4
}

Interface principal
csharp
 
public interface IPaymentGateway
{
    PaymentProvider Provider { get; }

    Task<CreatePaymentResult> CreatePaymentAsync(
        CreatePaymentRequest request,
        CancellationToken cancellationToken);

    Task<PaymentStatusResult> GetPaymentStatusAsync(
        string externalPaymentId,
        CancellationToken cancellationToken);

    Task<CancelPaymentResult> CancelPaymentAsync(
        string externalPaymentId,
        CancellationToken cancellationToken);
}

Exemplo de implementação
csharp
 
public class MercadoPagoPaymentGateway : IPaymentGateway
{
    public PaymentProvider Provider => PaymentProvider.MercadoPago;

    public async Task<CreatePaymentResult> CreatePaymentAsync(
        CreatePaymentRequest request,
        CancellationToken cancellationToken)
    {
        // mapear request interno para request do Mercado Pago
        // chamar API
        // mapear resposta externa para resposta interna
    }

    public async Task<PaymentStatusResult> GetPaymentStatusAsync(
        string externalPaymentId,
        CancellationToken cancellationToken)
    {
        // consultar API
    }

    public async Task<CancelPaymentResult> CancelPaymentAsync(
        string externalPaymentId,
        CancellationToken cancellationToken)
    {
        // cancelar pagamento
    }
}

🧾 Padronize seu modelo interno
Esse ponto é crucial.

Não deixe o sistema depender da linguagem do gateway
Por exemplo, não espalhe no domínio coisas como:

preference_id
qr_code_base64
asaasCustomerId
mercadoPagoStatus
Em vez disso, use modelos internos:

Internamente
PaymentId
ExternalPaymentId
Provider
Status
QrCodeText
CheckoutUrl
ExpiresAt
E mapeie externamente
Cada adapter converte.

🔄 Webhooks também devem seguir esse padrão
Melhor prática
Cada gateway tem um handler próprio, mas todos convertem para um evento interno comum.

Exemplo
MercadoPagoWebhookHandler
AsaasWebhookHandler
Ambos transformam em algo como:

csharp
 
public class PaymentNotification
{
    public PaymentProvider Provider { get; set; }
    public string ExternalPaymentId { get; set; }
    public PaymentStatus Status { get; set; }
    public string RawPayload { get; set; }
}

Depois disso, seu sistema processa tudo do mesmo jeito.

🛡️ Coisas essenciais no módulo de pagamento
Além do padrão de projeto, seu módulo deve ter:

1. 🔁 Idempotência
Se o webhook chegar 2 ou 3 vezes, não pode duplicar ação.

2. 🧾 Auditoria
Salvar:

request enviado
response recebido
webhook bruto
transaction id externo
3. 🔒 Anti-corruption layer
O gateway externo não “contamina” seu domínio.

4. 🧪 Testabilidade
Você consegue mockar IPaymentGateway facilmente.

5. ⚠️ Resiliência
retry controlado
timeout
circuit breaker, se necessário
🧪 Exemplo de uso no Application Service
csharp
 
public class CreateOrderPaymentHandler
{
    private readonly IPaymentGatewayFactory _factory;

    public CreateOrderPaymentHandler(IPaymentGatewayFactory factory)
    {
        _factory = factory;
    }

    public async Task<CreatePaymentResult> HandleAsync(CreateOrderPaymentCommand command, CancellationToken ct)
    {
        var gateway = _factory.GetGateway(command.Provider);

        var request = new CreatePaymentRequest
        {
            OrderId = command.OrderId,
            Amount = command.Amount,
            CustomerName = command.CustomerName,
            CustomerEmail = command.CustomerEmail,
            Method = command.Method
        };

        return await gateway.CreatePaymentAsync(request, ct);
    }
}


🏆 Recomendação final
S
Arquitetura
✅ Clean Architecture / Hexagonal
Padrões
✅ Strategy
✅ Adapter
✅ Factory
Complementos
✅ interface comum para gateway
✅ modelos internos padronizados
✅ webhook handlers por provider
✅ idempotência
✅ auditoria
✅ retry/timeout
📌 Resposta objetiva
Melhor padrão?
Strategy + Adapter + Factory dentro de uma arquitetura Hexagonal/Clean
Essa é a combinação mais segura para:

trocar gateways
manter código limpo
testar bem
evoluir seu app sem acoplamento

---

# 🎯 Objetivo

Definir a infraestrutura mínima e recomendada do backend para que o sistema possa:

- crescer com segurança 📈
- ser testado com confiança 🧪
- permitir auditoria e rastreamento de eventos 🔎
- manter baixo acoplamento técnico 🧱
- facilitar manutenção e evolução contínua ⚙️

---

# 🧭 Princípios arquiteturais

## ✅ Diretrizes principais
- backend **stateless** sempre que possível
- separação clara entre:
  - regras de negócio
  - casos de uso
  - infraestrutura
  - API
- dependências apontando **de fora para dentro**
- integração com serviços externos por **interfaces/ports**
- logs estruturados
- rastreabilidade por **Correlation ID / Trace ID**
- observabilidade desde o início
- testes automatizados em múltiplos níveis

---

# 🧱 Pilares obrigatórios da infraestrutura backend

---

# 1. 📈 Escalabilidade

## Objetivo
Garantir que o backend consiga crescer em carga, número de usuários, lojas, pedidos e integrações sem exigir grande retrabalho.

## Requisitos
- API stateless
- autenticação baseada em token
- processamento assíncrono para tarefas pesadas
- cache para consultas frequentes
- fila/eventos para desacoplamento
- banco preparado para crescimento
- monitoramento de performance
- health checks e readiness/liveness probes

## Recomendado
- **ASP.NET Core Web API (.NET 9)**
- **PostgreSQL**
- **Redis** para cache distribuído
- **Hangfire** para jobs agendados
- **RabbitMQ** ou outro broker para eventos assíncronos, quando necessário
- **Docker**
- **Nginx** como reverse proxy
- **OpenTelemetry** para tracing e métricas

## Estratégias práticas
- manter controllers leves
- mover regras para Application/Domain
- mover integrações para Infrastructure
- evitar sessão em memória no servidor
- usar paginação em listagens
- usar índices no banco
- usar filas para:
  - envio de notificação
  - reprocessamento de webhook
  - geração de relatórios
  - tarefas demoradas
- aplicar rate limiting em endpoints críticos

---

# 2. 🧪 Testabilidade

## Objetivo
Permitir que o sistema seja validado automaticamente com segurança em diferentes camadas.

## Tipos de teste recomendados

### 2.1 Testes unitários
Validam regras de negócio isoladas.

**Devem cobrir:**
- entidades
- value objects
- validações
- services de domínio
- casos de uso puros

### 2.2 Testes de integração
Validam integração entre:
- API + banco
- API + autenticação
- API + filas
- API + serviços externos mockados ou em sandbox

### 2.3 Testes de contrato
Validam contratos entre:
- backend e gateway de pagamento
- backend e front
- backend e webhooks externos

### 2.4 Testes end-to-end técnicos
Validam o fluxo principal:
- login
- criação de loja
- cadastro de produto
- fechamento de pedido
- pagamento
- atualização de status

## Requisitos para boa testabilidade
- usar interfaces nas integrações externas
- evitar lógica dentro de controllers
- usar DTOs e contratos bem definidos
- permitir injeção de dependência
- separar lógica de negócio da infraestrutura
- usar banco isolado para testes de integração
- criar factories/builders de dados de teste

## Ferramentas sugeridas
- **xUnit**
- **FluentAssertions**
- **Moq** ou **NSubstitute**
- **Testcontainers** para PostgreSQL/Redis em testes de integração
- **WebApplicationFactory** para testes da API

---

# 3. 🔎 Rastreabilidade e observabilidade

## Objetivo
Saber:
- o que aconteceu
- quando aconteceu
- com quem aconteceu
- em qual fluxo aconteceu
- por que falhou

---

## 3.1 Logs estruturados

### Recomendação
Usar **Serilog** com logs estruturados em JSON.

### Campos mínimos em todo log
- `Timestamp`
- `Level`
- `Message`
- `Application`
- `Environment`
- `TraceId`
- `CorrelationId`
- `UserId`
- `StoreId` quando aplicável
- `RequestPath`
- `HttpMethod`
- `StatusCode`
- `Exception`
- `SourceContext`

### Tipos de logs
- logs de erro ❌
- logs de warning ⚠️
- logs de informação ℹ️
- logs de auditoria 🧾
- logs de integração 🔌
- logs de segurança 🔐

### Não registrar em log
- senha
- token completo
- CVV
- dados sensíveis sem mascaramento
- payloads com segredos

---

## 3.2 Correlation ID / Trace ID

### Obrigatório
Toda requisição deve carregar um identificador único de rastreamento.

### Objetivo
Permitir seguir um fluxo completo entre:
- request HTTP
- aplicação
- banco
- fila
- job
- webhook
- integração externa

### Implementação recomendada
- middleware que:
  - lê `X-Correlation-Id`
  - se não existir, gera um novo
  - devolve no response
- integrar com OpenTelemetry

---

## 3.3 Auditoria de ações de usuário

### Registrar eventos relevantes
- login
- logout
- cadastro de usuário
- alteração de senha
- criação/edição de loja
- criação/edição de produto
- criação de pedido
- alteração de status do pedido
- contratação de assinatura
- cancelamentos
- bloqueios administrativos

### Campos recomendados para auditoria
- `Id`
- `EventType`
- `OccurredAt`
- `UserId`
- `UserRole`
- `StoreId`
- `EntityName`
- `EntityId`
- `Action`
- `OldValues` opcional
- `NewValues` opcional
- `IpAddress`
- `UserAgent`
- `CorrelationId`

### Exemplos de eventos
- `USER_LOGGED_IN`
- `STORE_CREATED`
- `PRODUCT_UPDATED`
- `ORDER_CREATED`
- `ORDER_STATUS_CHANGED`
- `SUBSCRIPTION_PAYMENT_CONFIRMED`

---

## 3.4 Application Events / Domain Events

### Recomendações
Usar eventos internos para desacoplar ações.

### Exemplos
- `OrderCreatedEvent`
- `OrderPaidEvent`
- `OrderStatusChangedEvent`
- `SubscriptionActivatedEvent`
- `SubscriptionPastDueEvent`

### Benefícios
- baixo acoplamento
- melhor escalabilidade
- mais rastreabilidade
- mais facilidade para expandir integrações

---

## 3.5 Métricas

### Métricas mínimas recomendadas
- total de requests por endpoint
- tempo médio de resposta
- taxa de erro por endpoint
- quantidade de pedidos criados
- quantidade de pagamentos aprovados
- quantidade de pagamentos recusados
- quantidade de webhooks recebidos
- quantidade de jobs com falha
- conexões com banco
- uso de CPU e memória

### Ferramenta
- **OpenTelemetry**


---

## 3.6 Tracing distribuído

### Recomendação
Implementar tracing com **OpenTelemetry**.

### Útil para rastrear
- requests HTTP
- chamadas ao banco
- chamadas HTTP externas
- jobs
- filas
- webhooks

---

## 3.7 Monitoramento de erros

### Recomendado
Centralizar erros em ferramenta própria.

- **Seq** para logs estruturados
---

# 4. 🔐 Segurança mínima de infraestrutura

## Regras obrigatórias
- HTTPS sempre
- CORS restrito
- rate limiting
- headers de segurança
- proteção de secrets
- variáveis sensíveis fora do código
- rotação de credenciais
- hash seguro de senha com Identity
- autenticação JWT com refresh token
- revogação de refresh token
- logs sem dados sensíveis

## Segredos e configuração
Nunca guardar no código-fonte:
- connection strings reais
- chaves JWT reais
- secrets de pagamento
- keys do Cloudinary
- senhas de banco
- secrets de webhook

### Armazenar em
- variáveis de ambiente
- secret manager
- vault

---

# 5. 🗄️ Banco de dados e persistência

## Recomendado
- **PostgreSQL** como banco principal
- **EF Core** como ORM
- migrations versionadas

## Boas práticas
- criar índices para consultas críticas
- usar constraints
- soft delete apenas quando fizer sentido
- versionamento de migrations
- backup automatizado
- restore testado periodicamente
- tabelas separadas para:
  - auditoria
  - pagamentos
  - eventos
  - webhooks processados

## Tabelas técnicas
- `AuditLogs`
- `OutboxMessages`
- `InboxMessages`
- `WebhookEvents`
- `BackgroundJobExecutions` se necessário
- `RefreshTokens`

---

# 6. 📨 Processamento assíncrono

## Quando usar
- envio de notificações
- auditoria de eventos
- reprocessamento de integração
- sincronização com gateway
- relatórios
- limpeza e expiração

## Ferramentas
### Inicialmente
- **Hangfire** para jobs agendados e processamento simples

### Quando crescer
- **RabbitMQ** + consumer(s)

## Jobs recomendados
- expiração de refresh token
- verificação de assinatura vencida
- alerta de cobrança
- reprocessamento de webhook com falha
- limpeza de dados temporários
- geração de relatórios

---

# 7. 🌐 API e infraestrutura web

## Recomendado
- controllers finos
- validação de entrada
- respostas padronizadas
- versionamento da API
- documentação Swagger
- health checks
- readiness e liveness

## Endpoints técnicos r
- `/health`
- `/health/ready`
- `/health/live`
- `/metrics` se aplicável
- `/swagger`

## Middleware 
- exception handling global
- correlation id
- request logging
- authentication
- authorization
- rate limiting

---

# 8. 🧰 Componentes de infraestrutura recomendados

## Essenciais no MVP
- PostgreSQL
- Serilog
- Swagger
- Hangfire
- Cloudinary
- JWT + Refresh Token
- Health Checks
- OpenTelemetry
- Docker
- Nginx

---

# 9. 🧪 Estratégia de testes no pipeline

## Pipeline mínimo
A cada push/pull request:
- restaurar pacotes
- build
- rodar testes unitários
- rodar testes de integração
- validar lint/analyzers
- publicar artefato

## Qualidade recomendada
- cobertura relevante em regras críticas
- foco maior em:
  - autenticação
  - pedidos
  - pagamento
  - assinatura
  - autorização
  - webhooks

---

## Cada ambiente deve ter
- config própria
- banco próprio
- secrets próprios
- logs próprios
- observabilidade própria

## Regras
- não compartilhar banco entre ambientes
- não usar secret de produção em homologação
- migrations controladas no deploy
- rollback planejado

---

# 11. 📦 Organização em Clean Architecture

# 📁 Estrutura simples da Clean Architecture

## Estrutura sugerida para src/backend

```
src/
  Application/    # Casos de uso, regras de negócio, DTOs
  Domain/         # Entidades, agregados, interfaces de repositório
  Infrastructure/ # Implementações (EF, Dapper, pagamentos, email, logs)
  WebApi/         # Controllers, middlewares, configuração
  Migrations/     # Scripts e histórico de migrações
  Tests/          # Testes unitários e de integração do backend
```
    ---

    ## 1. `Urbeat.Domain`

        Contém:
        - entidades
        - enums
        - regras de negócio puras
        - value objects, se necessário


          Urbeat.Domain/
          ├── Entities/
          │    ├── Store.cs
          │    ├── Product.cs
          │    ├── Order.cs
          │    ├── Payment.cs
          │    └── Subscription.cs
          │
          ├── Enums/
          │    ├── OrderStatus.cs
          │    ├── PaymentStatus.cs
          │    └── SubscriptionStatus.cs
          │
          ├── ValueObjects/
          │    └── Address.cs
          │
          └── Exceptions/
                └── DomainException.cs
---

## Urbeat.Application

 - Contém:

    - casos de uso
    - interfaces
    - DTOs
    - validações
    = Criar arquivo DependencyInjection.cs para 

      Urbeat.Application/
      ├── Interfaces/
      │    ├── Repositories/
      │    ├── Services/
      │    └── Payments/
      │
      ├── DTOs/
      │    ├── Auth/
      │    ├── Store/
      │    ├── Product/
      │    ├── Order/
      │    └── Payment/
      │
      ├── Features/
      │    ├── Auth/
      │    ├── Stores/
      │    ├── Products/
      │    ├── Orders/
      │    ├── Payments/
      │    ├── Subscriptions/
      │    └── Admin/
      │
      └── Validators/
---

## Urbeat.Infrastructure

- Contém:

    - EF Core
    - autenticação concreta
    - repositórios
    - integração com pagamento
    - logs
    - storage externo

      Urbeat.Infrastructure/
      ├── Persistence/
      │    ├── AppDbContext.cs
      │    ├── Configurations/
      │    ├── Migrations/
      │    └── Repositories/
      │
      ├── Identity/
      │    ├── ApplicationUser.cs
      │    ├── JwtTokenService.cs
      │    └── RefreshTokenService.cs
      │
      ├── Payments/
      │    ├── MercadoPago/
      │    └── Asaas/
      │
      ├── Logging/
      │    └── AuditService.cs
      │
      ├── Storage/
      │    └── Cloudinary/
      │
      └── DependencyInjection/
            └── ServiceCollectionExtensions.cs

---

## Urbeat.WEbAPI

- Contém:

    - controllers
    - middlewares
    - configuração da aplicação
    - swagger

      Urbeat.WebAPI/
      ├── Controllers/
      │    ├── AuthController.cs
      │    ├── StoresController.cs
      │    ├── ProductsController.cs
      │    ├── OrdersController.cs
      │    ├── PaymentsController.cs
      │    ├── SubscriptionsController.cs
      │    └── AdminController.cs
      │
      ├── Middlewares/
      │    ├── ExceptionHandlingMiddleware.cs
      │    └── RequestLoggingMiddleware.cs
      │
      ├── Extensions/
      │    ├── ServiceCollectionExtensions.cs
      │    └── ApplicationBuilderExtensions.cs
      │
      ├── Program.cs
      └── appsettings.json

---

## Estrutura de testes

      - Testes unitários
          - validação de login
          - criação de pedido
          - cálculo de total
          - mudança de status do pedido
          - bloqueio de loja inadimplente
          - Utilizar a ferramenta ***Bogus*** para criar dados ficticios de teste.
          - Utilizar Bulders para criar Moqs de Entities no CommonTestUtilities para reaproveitamento.
          - Criar testes das validações no Validators

      - Testes de integração testam 
          - autenticação
          - criação de loja
          - cadastro de produto
          - criação de pedido
          - atualização de status

      tests/
      |── CommonTestUtilities/
      ├── Urbeat.UnitTests/
      │    ├── Domain/
      │    ├── Application/
      |    └── Validators/
      │
      └── Urbeat.IntegrationTests/
            ├── WebApi/
            └── Persistence/

---

# 📦 Modelo de Dados (MVP)

## Principais Entidades

- **User** (usuário)
  - Id (GUID)
  - Nome
  - Email (único)
  - Telefone
  - Senha (hash)
  - Role (Customer, Seller, Admin)
  - Ativo

- **Store** (loja)
  - Id (GUID)
  - Nome
  - Telefone
  - Descrição
  - TipoCulinariaId
  - OwnerUserId (vendedor)
  - EnderecoId
  - TaxaEntrega
  - PedidoMinimo
  - LogoUrl
  - Status (Ativa/Inativa/Inadimplente)

- **Endereco**
  - Id (GUID)
  - Rua
  - Número
  - Bairro
  - Cidade
  - Estado
  - CEP
  - PontoReferencia
  - (FK: StoreId ou UserId)

- **CuisineType** (tipo de culinária)
  - Id
  - Nome
  - Ativo

- **ProductCategory**
  - Id (GUID)
  - Nome
  - StoreId
  - DisplayOrder
  - Ativo

- **Product**
  - Id (GUID)
  - Nome
  - Descrição
  - Preço
  - CategoriaId
  - StoreId
  - ImagemUrl
  - IsAvailable
  - DisplayOrder

- **Order**
  - Id (GUID)
  - CustomerId
  - StoreId
  - EnderecoSnapshot
  - Status (PendingPayment, Received, Preparing, Ready, Sent, Delivered, Cancelled)
  - FormaPagamento
  - Total
  - TaxaEntrega
  - PedidoMinimo
  - DataCriacao
  - DataAtualizacao
  - PaymentTransactionId

- **OrderItem**
  - Id (GUID)
  - OrderId
  - ProductSnapshot (nome, preço, imagem)
  - Quantidade
  - Subtotal

- **Plan** (plano de assinatura)
  - Id (GUID)
  - Nome
  - Valor
  - Descrição
  - Status (Ativo/Inativo)

- **Subscription** (assinatura)
  - Id (GUID)
  - StoreId
  - PlanId
  - Status (Ativa/Inadimplente/Cancelada)
  - DataInicio
  - DataFim
  - GatewayCustomerId
  - GatewaySubscriptionId

- **Payment**
  - Id (GUID)
  - OrderId ou SubscriptionId
  - Valor
  - Status (Pending, Paid, Failed, Refunded)
  - GatewayTransactionId
  - DataPagamento
  - Metodo (PIX, Cartão, Boleto)

- **Notification**
  - Id (GUID)
  - UserId
  - PedidoId (opcional)
  - Tipo (NovoPedido, StatusPedido, Assinatura, etc)
  - Mensagem
  - Lida
  - DataCriacao

- **AuditLog**
  - Id (GUID)
  - UserId
  - Evento
  - Entidade
  - EntidadeId
  - Descrição
  - Data
  - IpAddress

## Relacionamentos principais

- 1 vendedor → 1 loja
- 1 loja → N produtos, N categorias, 1 endereço, 1 assinatura
- 1 cliente → N pedidos
- 1 pedido → N itens
- 1 loja → N pedidos
- 1 plano → N assinaturas
- 1 assinatura → 1 loja
- 1 usuário → N notificações

> **Observação:**
> O modelo pode ser expandido conforme o sistema evoluir (ex: cupons, avaliações, histórico de status, etc).

---

## No arquivo Program.cs incluir (configurar)
    
    - Serilog
    - JWT
    - Swagger
    - EF Core
    - tratamento global de erros

---


