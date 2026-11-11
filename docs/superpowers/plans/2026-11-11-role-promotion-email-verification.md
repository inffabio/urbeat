# Role Promotion Email Verification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir adicionar o segundo papel Customer/Seller sem pedir a senha atual, usando confirmação segura por e-mail e preservando credenciais independentes.

**Architecture:** Identidades continuam únicas por e-mail em `AspNetUsers` e as senhas continuam separadas em `UserRoleCredential`. Para promoção não autenticada, o backend criará uma solicitação pendente persistida, com hash da nova senha e dados necessários para a promoção, enviará um link de uso único com token opaco e só criará o papel/credencial após a confirmação. Usuários já autenticados continuam podendo promover diretamente, pois o bearer token prova a posse da identidade.

**Tech Stack:** .NET 9, ASP.NET Core Identity, EF Core/PostgreSQL, Redis/outbox/e-mail SMTP, Angular 20 standalone/Ionic, Jest, xUnit/integration tests.

## Global Constraints

- O servidor permanece autoritativo para posse, papéis, senha, expiração e emissão de token.
- Nunca emitir token baseado somente em e-mail, senha vazia ou existência de identidade.
- Cliente e lojista mantêm credenciais independentes; não copiar nem compartilhar hashes.
- O e-mail de promoção deve explicar o papel solicitado, ausência de solicitação de senha, validade de 30 minutos, uso único e ação para ignorar.
- Desafios de promoção devem ser distintos de confirmação de cadastro e troca de e-mail.
- Desafio expirado, consumido, inválido ou concorrente não pode criar papel/credencial.
- Respostas não autenticadas não devem revelar se o e-mail possui outro papel.
- Nenhuma senha em claro pode ser persistida, enviada por e-mail ou registrada.
- Preservar migrações existentes, fluxos de cadastro novo, login, recuperação de senha e sessão atual.

---

### Task 1: Modelar solicitação pendente e desafio de promoção

**Files:**
- Create: `backend/src/Urbeat.Domain/Entities/RolePromotionRequest.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Persistence/ApplicationDbContext.cs`
- Create: `backend/src/Urbeat.Infrastructure/Identity/RolePromotionChallengeService.cs`
- Create: `backend/src/Urbeat.Application/Interfaces/IRolePromotionChallengeService.cs`
- Create: `backend/src/Urbeat.Application/DTOs/RolePromotionDtos.cs`
- Create: EF migration under `backend/src/Urbeat.Infrastructure/Persistence/Migrations/`
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/Identity/RolePromotionChallengeServiceTests.cs`

**Interfaces:**
- `CreateAsync(...)` creates/replaces a pending request and returns an opaque confirmation token or correlation id without exposing stored password material.
- `ValidateAndConsumeAsync(token, cancellationToken)` atomically validates purpose, expiry, target role/user and one-time state.
- Pending data contains normalized e-mail/user id, target role, full name/contact/document data needed by the existing promotion path, a password hash created before persistence, token hash, `CreatedAtUtc`, `ExpiresAtUtc`, `ConsumedAtUtc` and concurrency/version fields.

- [ ] **Step 1: Add failing unit tests** for token creation, 30-minute expiry, wrong-purpose token, wrong user/role, one-time consumption, replacement of an older request and concurrent consume.
- [ ] **Step 2: Run the focused unit tests and confirm the expected failures.**
  `dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~RolePromotionChallengeServiceTests"`
- [ ] **Step 3: Implement the entity and EF mapping.** Add unique/indexed lookup fields, UTC timestamps, consumed state and a concurrency token; do not store the raw token or plain password.
- [ ] **Step 4: Implement the challenge service.** Generate cryptographically random token material, store only a hash, enforce a 30-minute UTC expiry, invalidate previous pending requests for the same identity/role and consume atomically.
- [ ] **Step 5: Add and apply a committed EF migration through startup migration only.** Do not run `dotnet ef database update` against deployment databases.
- [ ] **Step 6: Run unit tests and the migration/model validation tests.**

### Task 2: Add promotion e-mail delivery

**Files:**
- Modify: `backend/src/Urbeat.Infrastructure/Services/Email/EmailTemplates.cs`
- Create: `backend/src/Urbeat.Infrastructure/Jobs/SendRolePromotionEmailJob.cs`
- Modify: `backend/src/Urbeat.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Services/Email/EmailConfirmationOptions.cs` only if a dedicated promotion URL option is required
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/Services/Email/EmailTemplatesTests.cs` or existing email template test location

**Interfaces:**
- Job accepts only a correlation/request id and resolves the pending request at execution time; durable job payload must not contain raw token or password hash.
- Template receives the role label, confirmation URL, expiration minutes and optional display name.

- [ ] **Step 1: Add failing template tests** asserting the exact approved Portuguese copy for seller and customer promotion, including “não altera sua senha atual”, “não solicita sua senha por e-mail”, 30 minutes, one-time use and ignore guidance.
- [ ] **Step 2: Implement role-specific HTML/text templates** with subject `Confirme seu novo acesso de lojista no urbeat` or the customer equivalent, safe escaping and a single confirmation CTA.
- [ ] **Step 3: Implement the background job** using the existing `IEmailService`, configured frontend base URL and opaque token/correlation lookup; log only correlation/user/role metadata, never token or password.
- [ ] **Step 4: Register the job and run focused email tests.**

### Task 3: Change backend registration and confirmation contracts

**Files:**
- Modify: `backend/src/Urbeat.Application/DTOs/RegisterUserRequestDto.cs`
- Create/Modify: `backend/src/Urbeat.Application/DTOs/RolePromotionDtos.cs`
- Modify: `backend/src/Urbeat.Application/Interfaces/IAuthService.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Identity/AuthService.cs`
- Modify: `backend/src/Urbeat.WebApi/Controllers/AuthController.cs`
- Modify: `backend/src/Urbeat.WebApi/DependencyInjection/WebApiServiceCollectionExtensions.cs` for rate limiting
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/Identity/AuthServiceTests.cs` or existing auth unit-test location
- Test: `backend/tests/Urbeat.IntegrationTests/Api/AuthRegistrationSecurityTests.cs`
- Test: `backend/tests/Urbeat.IntegrationTests/Api/AuthenticationFlowTests.cs`

**Interfaces:**
- Registration request no longer requires `CurrentPassword` for the new flow; temporary backward compatibility may accept it but must not be required by the UI.
- Add explicit endpoint such as `POST /api/auth/role-promotion/confirm` with `{ token }`, returning role/user status and whether login requires a password step.
- Existing authenticated promotion path uses `authenticatedUserId` as proof and creates the new role credential directly.
- Existing unauthenticated promotion path creates a pending request and queues the role-promotion e-mail, returning a generic pending response.

- [ ] **Step 1: Add failing integration tests** for: authenticated customer adds seller without current password; unauthenticated existing e-mail receives generic pending response; valid link promotes once; expired/replayed link fails; attacker cannot promote with only e-mail; existing same-role registration remains rejected.
- [ ] **Step 2: Add a failing test** proving the new credential password remains independent from the existing role password and is never empty.
- [ ] **Step 3: Refactor `AuthService` promotion branches.** Keep existing role/document checks, use bearer identity proof when available, otherwise create the pending request and queue the job; do not assign role/credential before confirmation.
- [ ] **Step 4: Implement confirmation endpoint.** Validate and consume the challenge, reload the identity, verify target role is still absent, apply claim/role/credential atomically, and return a safe success response. Treat already-completed identical confirmation idempotently without duplicating credentials.
- [ ] **Step 5: Add rate limiting and generic responses.** Use a dedicated policy for promotion requests and confirmation attempts; do not reveal whether an e-mail exists or which other role it owns.
- [ ] **Step 6: Update audit events.** Record request, sent, confirmed, expired, replayed and failed events with user/role/correlation metadata only.
- [ ] **Step 7: Run focused backend unit/integration tests.**

### Task 4: Update seller registration frontend

**Files:**
- Modify: `frontend/src/app/features/seller-register/seller-register-page.component.html`
- Modify: `frontend/src/app/features/seller-register/seller-register-page.component.ts`
- Modify: `frontend/src/app/features/seller-register/seller-register-page.component.spec.ts`
- Modify: `frontend/src/app/shared/models/auth.model.ts`
- Modify: `frontend/src/app/core/services/auth.service.ts`
- Modify: `frontend/src/app/features/email-confirmation/email-confirmation-page.component.ts` and template/spec if reused

**Interfaces:**
- `RegisterCustomerRequest`/seller request no longer sends `currentPassword` from the UI.
- Registration response distinguishes normal email confirmation from pending role promotion with an explicit operation/type and confirmation token context that is safe for the client.
- Confirmation page submits the promotion token to the explicit backend endpoint, not to email-change confirmation.

- [ ] **Step 1: Add failing frontend tests** asserting the current-password textbox, toggle and hint are absent; request payload omits `currentPassword`; new-role password and confirmation remain required.
- [ ] **Step 2: Add tests** for authenticated same-email promotion, pending-email message, confirmation navigation and expired/replayed-link error handling.
- [ ] **Step 3: Remove the current-password UI and state** from seller registration while retaining the new seller password strength/confirmation validation.
- [ ] **Step 4: Update auth models/service** for the explicit role-promotion response and confirmation call.
- [ ] **Step 5: Update confirmation UI copy** to say the new access was confirmed, identify the role, never ask for an existing password and route to the correct login/access screen.
- [ ] **Step 6: Run seller registration and confirmation specs.**

### Task 5: Full validation and security review

**Files:**
- Review only: all files changed in Tasks 1–4

- [ ] **Step 1: Run backend unit tests.**
  `dotnet test backend/tests/Urbeat.UnitTests`
- [ ] **Step 2: Run targeted backend integration tests.**
  `dotnet test backend/tests/Urbeat.IntegrationTests --filter "FullyQualifiedName~AuthenticationFlowTests|FullyQualifiedName~AuthRegistrationSecurityTests|FullyQualifiedName~EmailConfirmationFlowTests|FullyQualifiedName~UserRoleCredentialsFlowTests"`
- [ ] **Step 3: Run frontend targeted tests.**
  `npx jest --no-coverage src/app/features/seller-register/seller-register-page.component.spec.ts src/app/features/email-confirmation/email-confirmation-page.component.spec.ts`
- [ ] **Step 4: Run frontend suite and production build.**
  `npx jest --no-coverage`
  `npx ng build --configuration production`
- [ ] **Step 5: Review security invariants.** Confirm no plain password/token in logs, outbox payloads, URLs beyond the opaque challenge, API responses or local storage; confirm UTC expiry and one-time consume.
- [ ] **Step 6: Run `git diff --check`, inspect migrations and ensure no `.env`, credentials or unrelated files are included.**
