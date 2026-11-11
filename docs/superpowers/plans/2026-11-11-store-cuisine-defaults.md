# Categorias padrão de loja Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Substituir as categorias globais padrão por 20 opções em ordem alfabética, preservando categorias antigas usadas por lojas como categorias personalizadas.

**Architecture:** A lista única em `CuisineTypeDefaults` alimentará o seeder relacional, a configuração EF e os testes. O `StoreConfigPageComponent`, herdado pelo dashboard `SellerStoreInfoPageComponent`, continuará sendo o único fluxo de seleção frontend; ele ordena a união de padrões e categorias privadas em `pt-BR`.

**Tech Stack:** .NET 9, EF Core/PostgreSQL, Angular 20 standalone/Signals, Jest.

## Global Constraints

- A lista final deve conter exatamente os 20 nomes aprovados, sem numeração visível.
- Categorias antigas vinculadas a lojas devem ser copiadas como privadas antes da remoção do global.
- Categorias antigas sem vínculo devem ser removidas do conjunto global.
- Categorias personalizadas já existentes não podem ser removidas.
- Não alterar `print-agent/`, `oci-mcp-server/` ou funcionalidades não relacionadas.
- Timestamps persistidos devem continuar em UTC.
- Startup continua aplicando migrations e executando seeders em banco relacional.

---

### Task 1: Atualizar fonte única e persistência das categorias padrão

**Files:**
- Modify: `backend/src/Urbeat.Infrastructure/Persistence/CuisineTypeDefaults.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Persistence/ApplicationDbContext.cs:175-215`
- Modify: `backend/src/Urbeat.Infrastructure/Persistence/CuisineTypeSeeder.cs`
- Create: migration EF em `backend/src/Urbeat.Infrastructure/Persistence/Migrations/` com nome `UpdateDefaultCuisineTypes`
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/Persistence/CuisineTypeDefaultsTests.cs`
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/Persistence/CuisineTypeSeederTests.cs`
- Test: `backend/tests/Urbeat.UnitTests/Infrastructure/Persistence/CuisineTypeMigrationTests.cs`

**Interfaces:**
- `CuisineTypeDefaults.Names` passa a retornar exatamente: `Açaíteria`, `Batata Recheada`, `Cachorro-quente (Hot Dog)`, `Cafeteria`, `Caldos e Sopas`, `Comida Japonesa`, `Comida Saudável (Fitness)`, `Confeitaria`, `Creperia`, `Doceria`, `Espetinhos e Churrasco`, `Hamburgueria`, `Marmitaria`, `Massas e Lasanhas`, `Padaria`, `Pastelaria`, `Pizzaria`, `Salgaderia`, `Sorveteria`, `Tapiocaria`.
- `CuisineTypeSeeder.SeedAsync` permanece idempotente e preserva o vínculo das lojas antigas por cópia privada.

- [ ] **Step 1: Atualizar testes de defaults primeiro.** Substituir a expectativa antiga pelos 20 nomes exatos, verificar que a lista é alfabética em `pt-BR` e atualizar casos de `IsDefaultName`/`IsDefaultNormalizedName` para nomes removidos e novos nomes com acentos/parênteses.
- [ ] **Step 2: Executar os testes de defaults para confirmar falha antes da implementação.**
  `dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~CuisineTypeDefaultsTests"`.
- [ ] **Step 3: Alterar `CuisineTypeDefaults.Names`.** Manter a normalização atual e não inserir numeração nos nomes.
- [ ] **Step 4: Atualizar `ApplicationDbContext.HasData`.** Usar IDs estáveis para as novas categorias e manter IDs existentes quando o mesmo nome normalizado continuar existindo; evitar duplicidade global por `NormalizedName`.
- [ ] **Step 5: Gerar a migration a partir de `backend/`.** Executar `dotnet ef migrations add UpdateDefaultCuisineTypes --startup-project src/Urbeat.WebApi --project src/Urbeat.Infrastructure`.
- [ ] **Step 6: Tornar a migration segura para dados existentes.** Antes de qualquer remoção de global antigo, usar SQL idempotente para copiar cada categoria global não aprovada que esteja referenciada para uma categoria privada da loja e atualizar `Stores.CuisineTypeId`; só depois remover globais órfãos. Categorias novas devem ser inseridas com `NOT EXISTS`. Não usar `DeleteData` cego para IDs antigos referenciados e não usar `dotnet ef database update` em banco de implantação.
- [ ] **Step 7: Executar defaults, migration e seeder tests.** Confirmar PASS e verificar que categorias antigas vinculadas permanecem privadas e categorias órfãs são removidas.
- [ ] **Step 8: Commitar backend e migration.**
  `git add backend/src/Urbeat.Infrastructure/Persistence/CuisineTypeDefaults.cs backend/src/Urbeat.Infrastructure/Persistence/ApplicationDbContext.cs backend/src/Urbeat.Infrastructure/Persistence/CuisineTypeSeeder.cs backend/src/Urbeat.Infrastructure/Persistence/Migrations backend/tests/Urbeat.UnitTests/Infrastructure/Persistence/CuisineTypeDefaultsTests.cs backend/tests/Urbeat.UnitTests/Infrastructure/Persistence/CuisineTypeSeederTests.cs backend/tests/Urbeat.UnitTests/Infrastructure/Persistence/CuisineTypeMigrationTests.cs; git commit -m "feat: update default store cuisine types"`

### Task 2: Validar o seletor compartilhado no cadastro e dashboard

**Files:**
- Modify: `frontend/src/app/features/store-config/store-config-page.component.spec.ts`
- Review: `frontend/src/app/features/seller-store-info/seller-store-info-page.component.ts` para confirmar a herança, sem duplicar a lista

**Interfaces:**
- `StoreConfigPageComponent.sortedCuisineTypes` permanece a fonte da ordenação visual.
- `SellerStoreInfoPageComponent` continua herdando o mesmo comportamento, sem duplicar a lista no dashboard.

- [ ] **Step 1: Atualizar fixture `DEFAULT_CUISINE_TYPE_NAMES`.** Usar os 20 nomes na ordem alfabética aprovada.
- [ ] **Step 2: Adicionar teste de contrato visual.** Verificar que as opções do select aparecem exatamente sem prefixos numéricos ou texto adicional.
- [ ] **Step 3: Adicionar teste de compatibilidade.** Simular uma categoria antiga/customizada retornada para uma loja e verificar que ela permanece na união e ordenada junto aos padrões.
- [ ] **Step 4: Verificar o dashboard.** Confirmar que a página derivada usa o mesmo componente/base e não possui lista paralela ou ordem diferente.
- [ ] **Step 5: Rodar os specs direcionados.**
  `npx jest --no-coverage src/app/features/store-config/store-config-page.component.spec.ts src/app/features/seller-store-info/seller-store-info-page.component.spec.ts`.
- [ ] **Step 6: Commitar a cobertura frontend.**
  `git add frontend/src/app/features/store-config/store-config-page.component.spec.ts; git commit -m "test: cover default store cuisine options"`

### Task 3: Validação completa e revisão do escopo

**Files:**
- Review only: arquivos alterados nas tarefas 1 e 2

- [ ] **Step 1: Revisar `git diff` e confirmar que alterações não relacionadas existentes, incluindo a feature anterior de compressão de imagens, não foram incluídas nos commits desta tarefa.**
- [ ] **Step 2: Executar backend.**
  `dotnet build backend/Urbeat.sln` e `dotnet test backend/tests/Urbeat.UnitTests`.
- [ ] **Step 3: Executar frontend.**
  From `frontend/`: `npx jest --no-coverage` e `npx ng build --configuration production`.
- [ ] **Step 4: Revisar migration e compatibilidade.** Confirmar ordem de operações, preservação de lojas existentes, idempotência do seeder e ausência de categorias globais antigas não aprovadas.
- [ ] **Step 5: Executar `git diff --check` e registrar resultados, warnings preexistentes e qualquer teste condicionado à disponibilidade do Docker.**
