# Backend Security And Payment Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven development or executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Corrigir autorização cross-store, autenticação, pagamentos, recuperação de conta, CORS e lacunas operacionais do backend.

**Architecture:** Preservar as camadas atuais `Domain -> Application -> Infrastructure -> WebApi`. Adicionar o desafio temporário de e-mail à resposta/fluxo de cadastro, centralizar autorização de proprietário nos serviços existentes, validar totais no início e no retorno do pagamento e configurar políticas de segurança a partir de configuração.

**Tech Stack:** .NET 9, ASP.NET Core, Identity, EF Core, FluentValidation, xUnit, PostgreSQL/InMemory.

## Global Constraints

- Backend continua sendo a autoridade de validação, preços, frete, checkout e transições.
- Não expor valores de segredo, tokens ou credenciais em logs, testes ou arquivos rastreados.
- Não alterar o fluxo de migrations de startup.
- Preservar autorização por proprietário e testes existentes.

### Task 1: Secure Email Change Flow

**Files:**
- Modify: `backend/src/Urbeat.Application/DTOs/AuthRequestDtos.cs` and response DTOs as located by the implementer
- Modify: `backend/src/Urbeat.Infrastructure/Identity/AuthService.cs`
- Modify: `backend/src/Urbeat.WebApi/Controllers/AuthController.cs`
- Modify: seller registration and email-confirmation contracts
- Test: existing AuthService/controller tests

- [ ] Write failing tests proving an anonymous caller cannot change an arbitrary account email and a valid short-lived registration challenge can change the pending email.
- [ ] Run focused auth tests and confirm the expected failures.
- [ ] Implement a signed, expiring, one-use challenge bound to user id and current email without storing raw challenge secrets.
- [ ] Require the challenge on update-email and remove authorization from `UserId/currentEmail` alone.
- [ ] Run focused auth tests and confirm the flow passes.

### Task 2: Enforce Store Ownership

**Files:**
- Modify: `backend/src/Urbeat.WebApi/Controllers/StoresController.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Services/StoreService.cs`
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/Services/StoreService*Tests.cs` and controller/integration authorization tests

- [ ] Add failing cross-store tests for delivery time and neighborhood mutations.
- [ ] Pass authenticated owner id into service methods and return forbidden/not found for another owner.
- [ ] Restrict global neighborhood creation to admin or replace it with a store-scoped mutation.
- [ ] Run unit and integration authorization tests.

### Task 3: Validate Online Payment Amounts

**Files:**
- Modify: `backend/src/Urbeat.Infrastructure/Services/Payments/MercadoPagoOrderPaymentStrategy.cs`
- Modify: Mercado Pago adapter/request models and webhook/payment service as required
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/Services/MercadoPagoOrderPaymentStrategyTests.cs` and payment webhook tests

- [ ] Add failing tests for delivery fee inclusion and gateway amount mismatch.
- [ ] Send the authoritative delivery fee in the gateway checkout and compare callback amount/currency with the persisted order total.
- [ ] Keep mismatched payments pending/failed and never advance the order.
- [ ] Run payment tests and integration flow tests.

### Task 4: Harden Reset, Recovery And CORS

**Files:**
- Modify: `backend/src/Urbeat.Infrastructure/Identity/AuthService.cs`
- Modify: `backend/src/Urbeat.WebApi/Controllers/AuthController.cs`
- Modify: `backend/src/Urbeat.WebApi/DependencyInjection/WebApiServiceCollectionExtensions.cs`
- Modify: configuration examples for allowed origins and rate limits
- Test: auth, web API and integration security tests

- [ ] Add failing tests for refresh token revocation after password reset and uniform forgot-password responses.
- [ ] Revoke active refresh tokens after reset and return a generic recovery response.
- [ ] Add bounded rate limiting for password recovery and sensitive auth endpoints.
- [ ] Replace wildcard CORS with configured production/development origins while preserving SignalR credentials.
- [ ] Run backend unit tests, integration tests and solution build.

### Task 5: Review And Verification

**Files:**
- No production files unless verification exposes a regression.

- [ ] Run `dotnet test backend/tests/Urbeat.UnitTests`.
- [ ] Run `dotnet test backend/tests/Urbeat.IntegrationTests`.
- [ ] Run `dotnet build backend/Urbeat.sln`.
- [ ] Review the final diff for secrets, authorization gaps and unintended files.
