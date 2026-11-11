# Argon2id Password Hashing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Substituir o hash de novas senhas do Identity por Argon2id, sem pepper, preservando a validação/migração de hashes PBKDF2 existentes durante a transição.

**Architecture:** Implementar um `IPasswordHasher<IdentityUser<Guid>>` composto: novos hashes usam um formato próprio versionado Argon2id; hashes ASP.NET Identity legados são verificados pelo hasher PBKDF2 padrão e retornam `SuccessRehashNeeded`. O hasher será registrado somente para credenciais de usuário; tokens de promoção/confirmação e PINs terão abstrações de hash próprias e não pagarão o custo de memória do Argon2id.

**Tech Stack:** .NET 9, ASP.NET Core Identity, Argon2id, EF Core, xUnit, PostgreSQL/InMemory, Docker.

## Global Constraints

- Usar **Argon2id** como algoritmo principal para credenciais de usuários.
- Não usar pepper.
- O salt será aleatório e exclusivo por hash, armazenado junto do hash no formato versionado.
- Cliente, lojista e administrador usarão o mesmo hasher de credenciais, mas continuarão com credenciais/papéis independentes.
- Tokens de confirmação/promoção continuarão usando o mecanismo próprio de token aleatório e hash rápido; não serão processados por Argon2id.
- PINs de entrega e OTPs continuarão usando seus mecanismos específicos.
- Preservar alterações não commitadas existentes da promoção por e-mail; não incluir arquivos fora deste escopo.
- Não registrar senha, salt, hash completo ou qualquer segredo em logs.

---

### Task 1: Biblioteca, opções e formato Argon2id

**Files:**
- Modify: `backend/src/Urbeat.Infrastructure/Urbeat.Infrastructure.csproj`
- Create: `backend/src/Urbeat.Infrastructure/Identity/Argon2idPasswordHasherOptions.cs`
- Create: `backend/src/Urbeat.Infrastructure/Identity/Argon2idPasswordHasher.cs`
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/Identity/Argon2idPasswordHasherTests.cs`

**Interfaces:**
- Implements `IPasswordHasher<IdentityUser<Guid>>`.
- `HashPassword(IdentityUser<Guid> user, string password): string` emits a versioned Argon2id string containing algorithm/version/parameters/salt/subkey.
- `VerifyHashedPassword(IdentityUser<Guid> user, string hashedPassword, string providedPassword): PasswordVerificationResult` returns `Success`, `SuccessRehashNeeded`, or `Failed` without throwing on malformed input.

- [ ] **Step 1: Add the vetted Argon2id package** to the Infrastructure project with a pinned version and verify license/target framework compatibility.
- [ ] **Step 2: Write failing unit tests** for: random salts produce different hashes; correct password succeeds; wrong password fails; malformed/truncated/unsupported hashes fail safely; current Argon2id parameters return `Success`; lower parameters return `SuccessRehashNeeded`; UTF-8/password boundary cases work.
- [ ] **Step 3: Run the focused tests and confirm expected failures.**
  `dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~Argon2idPasswordHasherTests"`
- [ ] **Step 4: Implement options validation.** Use configurable memory, iterations, parallelism, salt and hash sizes with safe minimum/maximum bounds; reject values that could cause unbounded memory or CPU use. Do not add a pepper option.
- [ ] **Step 5: Implement the versioned Argon2id format.** Encode binary values with unambiguous Base64Url/base64 segments, include parameters needed for verification, use `RandomNumberGenerator` for salt, use constant-time subkey comparison, and reject malformed/oversized input.
- [ ] **Step 6: Run focused tests and confirm PASS.**

### Task 2: PBKDF2 compatibility and Identity registration

**Files:**
- Modify: `backend/src/Urbeat.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Identity/AuthService.cs` only if token hashing is currently coupled to the user password hasher
- Modify: `backend/src/Urbeat.Infrastructure/Services/RedisEmailTokenCache.cs` or token abstraction only if required to isolate token hashing
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/Identity/AuthPasswordHashingIntegrationTests.cs`
- Test: `backend/tests/Urbeat.IntegrationTests/Api/AuthenticationFlowTests.cs`

**Interfaces:**
- Identity’s `UserManager<IdentityUser<Guid>>` resolves the Argon2id-compatible hasher.
- Legacy ASP.NET Identity PBKDF2 hashes verify through the standard `PasswordHasher<IdentityUser<Guid>>`; successful legacy verification returns `SuccessRehashNeeded`.

- [ ] **Step 1: Add failing compatibility tests** that seed a standard ASP.NET Identity PBKDF2 hash, authenticate successfully, and assert the stored hash is replaced with Argon2id after the normal rehash path; test new registration stores Argon2id.
- [ ] **Step 2: Run the focused backend tests and confirm failure.**
- [ ] **Step 3: Implement a composite hasher.** Detect the Argon2id version prefix first; otherwise delegate legacy formats to the built-in Identity hasher. Return `SuccessRehashNeeded` for legacy PBKDF2 or outdated Argon2id parameters.
- [ ] **Step 4: Register the composite hasher after `AddIdentityCore`** without changing Identity password policy, lockout, roles, JWT or credential table contracts.
- [ ] **Step 5: Verify all user-password call sites.** Ensure `UserManager` rehashes `AspNetUsers.PasswordHash` and `UserRoleCredential.PasswordHash` paths use the composite hasher, while promotion/confirmation tokens use their dedicated token hash implementation rather than Argon2id.
- [ ] **Step 6: Run authentication tests and confirm PASS.**

### Task 3: Configuration and environment calibration

**Files:**
- Modify: `backend/src/Urbeat.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`
- Modify: `backend/src/Urbeat.WebApi/appsettings.json` only for non-secret Argon2id parameters
- Modify: `backend/src/Urbeat.WebApi/appsettings.Development.json` or equivalent test configuration only if present
- Modify: `docker-compose.yml` only if a non-secret configuration override is required
- Modify: `DEPLOY.md` with calibration/rollback guidance
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/Identity/Argon2idOptionsTests.cs`

**Interfaces:**
- Options bind from non-secret configuration such as `PasswordHashing:Argon2id:MemorySizeKb`, `Iterations` and `DegreeOfParallelism`.
- No `Pepper`, password or secret value is added to configuration or Vault.

- [ ] **Step 1: Write failing options tests** for valid defaults, too-small/too-large memory, invalid iteration/parallelism, and deterministic binding from configuration.
- [ ] **Step 2: Implement validated options with test-safe values supplied by test configuration.** Keep production defaults strong but bounded; avoid making every test process allocate excessive memory in parallel.
- [ ] **Step 3: Add deployment documentation** explaining calibration, expected login cost, memory pressure, and how to lower parameters safely if the environment cannot sustain the configured cost; state explicitly that no pepper secret is required.
- [ ] **Step 4: Run options tests and a representative authentication test.**

### Task 4: Isolate token/PIN hashing

**Files:**
- Create or modify: `backend/src/Urbeat.Application/Interfaces/ITokenHasher.cs`
- Create or modify: `backend/src/Urbeat.Infrastructure/Identity/TokenHasher.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Identity/RolePromotionChallengeService.cs` and related confirmation services only where they currently use the Identity password hasher for random tokens
- Review: `backend/src/Urbeat.Infrastructure/Services/DeliveryPinCode.cs`
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/Identity/TokenHasherTests.cs`

**Interfaces:**
- `ITokenHasher.Hash(string rawToken): string` and `Verify(string rawToken, string hash): bool` use the existing token-appropriate strategy and constant-time comparison.

- [ ] **Step 1: Add failing tests** proving promotion/email tokens validate without Argon2id memory cost and that an Argon2id user hash is not accepted as a token hash.
- [ ] **Step 2: Implement/register the token hasher** and migrate only token call sites; do not alter delivery PIN behavior unless its existing dedicated design requires a separate change.
- [ ] **Step 3: Run token, promotion and email confirmation tests.**

### Task 5: End-to-end validation

**Files:**
- Review only: files changed in Tasks 1–4

- [ ] **Step 1: Run backend build.**
  `dotnet build backend/Urbeat.sln`
- [ ] **Step 2: Run backend unit tests.**
  `dotnet test backend/tests/Urbeat.UnitTests`
- [ ] **Step 3: Run authentication and promotion integration tests.**
  `dotnet test backend/tests/Urbeat.IntegrationTests --filter "FullyQualifiedName~AuthenticationFlowTests|FullyQualifiedName~AuthRegistrationSecurityTests|FullyQualifiedName~UserRoleCredentialsFlowTests|FullyQualifiedName~RolePromotionFlowTests|FullyQualifiedName~EmailConfirmationFlowTests"`
- [ ] **Step 4: Run frontend tests/build** to ensure auth flows remain compatible.
  From `frontend/`: `npx jest --no-coverage` and `npx ng build --configuration production`.
- [ ] **Step 5: Review security invariants.** Confirm no pepper/Vault secret was added, no plaintext password/logging exists, legacy verification is compatible, new hashes are Argon2id, tokens/PINs are not Argon2id, and test configuration does not silently weaken production configuration.
- [ ] **Step 6: Run `git diff --check` and inspect status.** Ensure only Argon2id files are included and the pending role-promotion changes remain preserved but unstaged/uncommitted.
