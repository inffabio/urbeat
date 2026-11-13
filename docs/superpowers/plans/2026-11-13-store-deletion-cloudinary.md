# Plano de implementação — exclusão de loja e manutenção Cloudinary

Data: 2026-11-13
Spec: `docs/superpowers/specs/2026-11-13-store-deletion-cloudinary-design.md`

## Escopo entregue

- Backend `Urbeat.Application`, `Urbeat.Infrastructure` e `Urbeat.WebApi`.
- Frontend `frontend/src/app` (serviço, página de configurações da loja, estilos e testes).
- Testes `Urbeat.UnitTests`, `Urbeat.IntegrationTests` e Jest do frontend.
- Nenhuma alteração de print-agent, oci-mcp-server, secrets ou deploy.

## Passo 1 — Contratos e DTOs (Application)

Arquivos:

- `backend/src/Urbeat.Application/Interfaces/IStoreService.cs`
  - adicionar `Task<DeleteStoreResultDto> DeleteForOwnerAsync(Guid ownerUserId, Guid storeId, string? ipAddress, CancellationToken cancellationToken = default);`
- `backend/src/Urbeat.Application/DTOs/DeleteStoreResultDto.cs`
  - `NotFound`, `Forbidden`, `Deleted`, `AssetsAttempted`, `AssetsDeleted`, `AssetsFailed`.
- `backend/src/Urbeat.Application/Interfaces/ICloudinaryImageCatalog.cs`
  - `ListImagesAsync(prefix, maxResults, nextCursor, ct)`;
  - `DestroyAsync(publicId, ct)`.
- `backend/src/Urbeat.Application/Interfaces/IStoreImageMaintenanceService.cs`
  - `ScanAsync(StoreImageOrphanScanRequestDto, ct)`;
  - `CleanupAsync(StoreImageOrphanCleanupRequestDto, ct)`.
- `backend/src/Urbeat.Application/DTOs/CloudinaryImageResourceDto.cs`
  - `CloudinaryImageResourceDto(PublicId, CreatedAtUtc, Format)`;
  - `CloudinaryImagePageDto { Resources, NextCursor }`.
- `backend/src/Urbeat.Application/DTOs/StoreImageMaintenanceDtos.cs`
  - requests/results de scan e cleanup, candidato, falha de deleção.

Validação do passo: `dotnet build backend/Urbeat.sln`.

## Passo 2 — Normalização de public id (Infrastructure)

Arquivo:

- `backend/src/Urbeat.Infrastructure/Services/CloudinaryPublicId.cs`
  - `ExtractFromUrl` (versão, transformações, subpastas, extensão, decode);
  - `IsUnderPrefix` e `NormalizePrefix`;
  - constantes `StorePrefix = "stores/"` e `PendingPrefix = "stores/_pending/"`.

Arquivo alterado:

- `backend/src/Urbeat.Infrastructure/Services/CloudinaryImageUploadService.cs`
  - `DeleteAsync` passa a usar `CloudinaryPublicId.ExtractFromUrl`;
  - retorno `ok`/`not found` é sucesso idempotente;
  - qualquer outro resultado lança `InvalidOperationException` após log, tornando a falha
    auditável. Chamadores atuais já usam `try/catch` best-effort.

## Passo 3 — Catálogo Cloudinary (Infrastructure)

Arquivos:

- `backend/src/Urbeat.Infrastructure/Services/CloudinaryMaintenanceOptions.cs`
  - `SectionName = "CloudinaryMaintenance"`;
  - defaults: 1440 min, 500 assets por scan, 100 deleções por execução;
  - limites rígidos: 2000 assets e 500 deleções.
- `backend/src/Urbeat.Infrastructure/Services/CloudinaryImageCatalog.cs`
  - `ListResourcesByPrefixParams` com `ResourceType.Image`, `Type = "upload"`,
    `MaxResults` limitado a 500 e `NextCursor`;
  - parse de `CreatedAt` como UTC (`DateTimeStyles.AssumeUniversal | AdjustToUniversal`);
  - `DestroyAsync` recusa public id fora de `stores/` e retorna `false` quando o provedor não
    confirma a destruição.

## Passo 4 — Serviço de manutenção de órfãos (Infrastructure)

Arquivo:

- `backend/src/Urbeat.Infrastructure/Services/StoreImageMaintenanceService.cs`
  - `ScanAsync` → `BuildScanAsync` + DTO de varredura;
  - `CleanupAsync` → exige `Confirm`, chama `BuildScanAsync`, limita deleções, revalida prefixo,
    captura falhas individuais e devolve `DeletedPublicIds`/`Failures`;
  - `LoadReferencedPublicIdsAsync` lê `Stores.LogoUrl`, `Stores.BannerUrl`,
    `Products.ImageUrl` e URLs Cloudinary em `LandingPageContents.Value`;
  - `ResolveMinimumAgeMinutes`, `ResolveMaxAssets`, `ResolveMaxDeletions` aplicam defaults e
    limites;
  - log estruturado de varredura/execução sem credenciais; falha exposta apenas com o tipo da
    exceção, sem payload do provedor.

## Passo 5 — Exclusão de loja (Infrastructure)

Arquivo alterado:

- `backend/src/Urbeat.Infrastructure/Services/StoreService.cs`
  - `DeleteForOwnerAsync`: 404, 403 com auditoria, coleta de URLs, transação relacional,
    `DeleteStoreDependentsAsync`, remoção da loja, commit, Cloudinary best-effort pós-commit e
    auditoria `StoreDeleted`;
  - `CollectStoreImageUrlsAsync`: logo, banner e `Products.ImageUrl` deduplicados;
  - `DeleteStoreDependentsAsync`: ordem compatível com FKs `Restrict` (categoria privada,
    rotas antes de motoristas, produtos antes de categorias, adicionais antes de grupos) e
    remoção de linhas store-scoped sem FK (`SellerSubscriptions`, `StorePrinterConfigs`,
    `OrderPrintJobs`);
  - `DeleteWhereAsync<TEntity>`: `ExecuteDeleteAsync` no relacional; `ToListAsync` +
    `RemoveRange` no InMemory.

Dados preservados: `Orders`, `OrderReviews`, `DeliveryNeighborhoods`, `Cities`,
`DeliveryDrivers` e categorias de culinária padrão.

## Passo 6 — Endpoints (WebApi)

Arquivos:

- `backend/src/Urbeat.WebApi/Controllers/StoresController.cs`
  - `[HttpDelete("{storeId:guid}")] DeleteStore` com `SellerOnly` (policy da classe);
  - 204 em sucesso, 403/404 conforme `DeleteStoreResultDto`.
- `backend/src/Urbeat.WebApi/Controllers/AdminCloudinaryController.cs`
  - rota `api/admin/cloudinary`, policy `AdminOnly`;
  - `GET orphan-images` (`maximumAssets`, `minimumAgeMinutes`);
  - `POST orphan-images/cleanup` com checagem de `confirm` e auditoria
    `CloudinaryOrphanCleanup` (somente contagens).

## Passo 7 — DI e configuração

Arquivos:

- `backend/src/Urbeat.Infrastructure/DependencyInjection/InfrastructureServiceCollectionExtensions.cs`
  - `Configure<CloudinaryMaintenanceOptions>`;
  - `ICloudinaryImageCatalog -> CloudinaryImageCatalog`;
  - `IStoreImageMaintenanceService -> StoreImageMaintenanceService`.
- `backend/src/Urbeat.WebApi/appsettings.json`
  - seção não secreta `CloudinaryMaintenance` com os três limites.

Nenhum job Hangfire/hosted service é registrado.

## Passo 8 — Testes

### Unitários

- `backend/tests/Urbeat.UnitTests/Infrastructure/Services/CloudinaryPublicIdTests.cs`
  - extração com e sem versão, transformação, query string, extensão e subpastas;
  - rejeição de URL não Cloudinary;
  - proteção de prefixo (`stores`, `stores-old`, `other/stores`);
  - normalização de prefixo.
- `backend/tests/Urbeat.UnitTests/Infrastructure/Services/StoreServiceDeleteTests.cs`
  - owner remove loja, produtos, categoria privada e assinatura;
  - preserva categoria padrão, `DeliveryNeighborhood`, outra loja e produtos dela;
  - Cloudinary é chamado para logo, banner e imagem de produto;
  - falha Cloudinary mantém banco consistente e reporta `AssetsFailed`;
  - forbidden e not found não mutam nada.
- `backend/tests/Urbeat.UnitTests/Infrastructure/Services/StoreImageMaintenanceServiceTests.cs`
  - scan não apaga e protege referências, recentes, idade desconhecida e conteúdo de landing;
  - cleanup apaga somente órfãos confirmados, incluindo `_pending` antigo;
  - cleanup sem confirmação não contata o provedor;
  - falha parcial não interrompe o lote;
  - paginação reporta `Truncated` e `MaximumDeletions` é respeitado.

### Integração

- `backend/tests/Urbeat.IntegrationTests/Infrastructure/FakeImageUploadService.cs`
  - registra URLs apagadas e permite falha por URL.
- `backend/tests/Urbeat.IntegrationTests/Infrastructure/FakeCloudinaryImageCatalog.cs`
  - lista paginada por prefixo, registra destruições e permite falha por public id.
- `backend/tests/Urbeat.IntegrationTests/Infrastructure/TestWebApplicationFactory.cs`
  - expõe `ImageUploads` e `CloudinaryCatalog` e substitui as implementações reais.
- `backend/tests/Urbeat.IntegrationTests/Api/StoreDeletionFlowTests.cs`
  - exclusão pelo owner (204), limpeza Cloudinary, `my-store` 404 e recriação da loja;
  - 403 para vendedor não-owner;
  - endpoints administrativos bloqueados para seller;
  - scan retorna candidato sem apagar;
  - cleanup sem confirmação retorna 400 e não apaga;
  - cleanup confirmado apaga o órfão.

## Passo 9 — Frontend do vendedor

Arquivos:

- `frontend/src/app/core/services/store.service.ts`
  - `deleteStore(storeId): Observable<void>` → `DELETE /api/stores/{storeId}`, sem corpo.
- `frontend/src/app/features/seller-store-info/seller-store-info-page.component.ts`
  - sinais `deletePanelOpen`, `deleting`, `deleteConfirmation` e `deleteError`;
  - `deleteConfirmed` compara a palavra `excluir` com `trim` e case-insensitive;
  - `canSubmitDeletion` exige a palavra digitada e nenhuma requisição em andamento;
  - `openDeletePanel()`/`cancelDeletion()` controlam o painel; `confirmDeletion()` chama o service;
  - `handleDeletedStore()` (204) e `handleDeleteError()` mapeiam 401/403/404/outros.
- `frontend/src/app/features/seller-store-info/seller-store-info-page.component.html`
  - zona destrutiva após a grade de cartões, renderizada só com `existingStoreId()`;
  - etapa de confirmação com `input` + botão desabilitado até a palavra conferir.
- `frontend/src/app/features/seller-store-info/seller-store-info-page.component.scss`
  - cartão com `--app-error*`, separado do restante; botões com `min-height: 44px`;
  - empilhamento em `max-width: 720px` mantendo alvos de toque.
- `frontend/src/app/features/store-config/store-config-page.component.ts`
  - serviços herdados (`storeService`, `authService`, `router`, `toast`, `sellerShell`) passam de
    `private` para `protected` para a subclasse reutilizá-los; nenhum comportamento mudou.
- `frontend/src/app/features/seller-store-info/seller-store-info-page.component.spec.ts`
  - visibilidade da zona, confirmação obrigatória, cancelamento, sucesso, 401, 403, 404 e 500.
- `frontend/src/app/core/services/store.service.spec.ts`
  - método HTTP `DELETE`, URL sem corpo e propagação do status de erro.

Não há envio de senha, URLs ou public ids: o backend continua dono da limpeza Cloudinary e da
checagem de ownership.

## Passo 10 — Validação obrigatória

Executar na raiz do repositório:

```powershell
dotnet build backend/Urbeat.sln
dotnet test backend/tests/Urbeat.UnitTests
dotnet test backend/tests/Urbeat.IntegrationTests
cd frontend
npx jest --no-coverage src/app/features/seller-store-info/seller-store-info-page.component.spec.ts src/app/core/services/store.service.spec.ts
npx jest --no-coverage src/app/features/store-config/store-config-page.component.spec.ts src/app/branding-consistency.spec.ts src/app/features/seller-shell
npx ng build --configuration production
cd ..
git diff --check
```

Resultado desta implementação:

- `dotnet build backend/Urbeat.sln`: sucesso, 0 erros.
- `dotnet test backend/tests/Urbeat.UnitTests`: 1390 testes, 0 falhas.
- `dotnet test backend/tests/Urbeat.IntegrationTests`: 370 testes, 0 falhas
  (os testes `Relational/*` com Testcontainers se auto-ignoram quando Docker não está
  disponível; não havia Docker neste ambiente).
- `npx jest` (seller-store-info + store.service): 34 testes, 0 falhas.
- `npx jest` (store-config + branding-consistency + seller-shell): 157 testes, 0 falhas.
- `npx ng build --configuration production`: sucesso (somente o aviso já existente de budget do
  bundle inicial).
- `git diff --check`: sem problemas de whitespace.
- Nenhuma limpeza Cloudinary real foi executada.
- `Documentacao/Prompt_Organizacao_Produtos_Cardapio_Digital.docx` permanece untracked e fora do
  diff.


## Runbook operacional

1. **Sempre** começar pelo scan:

   ```http
   GET /api/admin/cloudinary/orphan-images?minimumAgeMinutes=1440&maximumAssets=500
   Authorization: Bearer <token-admin>
   ```

   Conferir `orphans`, `referencedCount`, `recentCount`, `unknownAgeCount` e `truncated`.
   O scan não apaga nada.

2. Revisar os public ids candidatos. `_pending` aparece com `isPending=true` e só entra após a
   idade mínima.

3. Executar em lote pequeno, com confirmação explícita:

   ```http
   POST /api/admin/cloudinary/orphan-images/cleanup
   Authorization: Bearer <token-admin>
   Content-Type: application/json

   {
     "confirm": true,
     "minimumAgeMinutes": 1440,
     "maximumAssets": 500,
     "maximumDeletions": 25
   }
   ```

4. Repetir o scan/execução até `orphanCount=0` ou até o lote desejado. Verificar
   `failures` e repetir apenas os public ids que falharam, se necessário.

5. Nunca executar cleanup sem `confirm=true`, fora do prefixo `stores/` ou sem antes validar o
   scan. Não existe job automático; a rotina é manual.

## Rollback

- A exclusão de loja é transacional no banco; falha antes do commit não deixa estado parcial.
- Falha de Cloudinary após commit não reverte o banco; o asset permanece e pode ser localizado
  pelo próximo scan (não há referência no banco, então vira órfão).
- Não há migration nova nesta entrega.
