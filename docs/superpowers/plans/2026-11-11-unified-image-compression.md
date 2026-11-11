# Compressão unificada de imagens Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Comprimir automaticamente todos os uploads raster do Urbeat para aproximadamente 950 KiB, mantendo dimensões e qualidade sempre que possível, com limite autoritativo de 1 MiB no backend.

**Architecture:** Centralizar a compressão adaptativa no utilitário frontend existente e chamá-la nos fluxos de produto, logo e banner. O backend mantém validação independente de extensão, conteúdo e tamanho, com os limites de mídia unificados em 1 MiB; SVG/AVIF permanecem sem reencodificação no navegador.

**Tech Stack:** Angular 20, TypeScript strict, Jest, ASP.NET Core/.NET 9, ImageSharp, Cloudinary, PowerShell.

## Global Constraints

- Usar componentes Angular standalone, Signals, `inject()` e templates com control flow moderno.
- Validações do servidor permanecem autoritativas; valores e limites do cliente não são segurança.
- Preservar os formatos aceitos atuais: AVIF, PNG, SVG, WebP, JPG e JPEG.
- Não alterar `print-agent/`, `oci-mcp-server/` ou recursos Capacitor sem necessidade.
- Manter timestamps e URLs existentes; não registrar conteúdo de imagens ou segredos.
- Usar tokens e padrões existentes; esta tarefa não requer mudança visual.

---

### Task 1: Implementar compressão adaptativa reutilizável

**Files:**
- Modify: `frontend/src/app/shared/utils/image-compression.utils.ts`
- Test: `frontend/src/app/shared/utils/image-compression.utils.spec.ts`

**Interfaces:**
- Produces `compressImage(file, options)` retornando `Promise<File>` e opções para `maxBytes`, `maxWidth`, `quality` e timeout.
- Mantém `canCompressImage` e o fallback seguro para formatos não suportados, canvas ausente, erro de leitura e timeout.

- [ ] **Step 1: Escrever testes para arquivo já abaixo do alvo e formato não reencodável.** Verificar que retornam o mesmo `File` sem criar canvas.
- [ ] **Step 2: Escrever testes para compressão progressiva.** Simular `toBlob` com tamanhos diferentes e verificar que a função reduz qualidade antes de reduzir dimensões e nunca retorna arquivo acima de `maxBytes` quando o encoder consegue atingir o alvo.
- [ ] **Step 3: Escrever testes para proporção, transparência e falhas.** Verificar dimensões proporcionais, MIME coerente, timeout, contexto 2D ausente e blob vazio.
- [ ] **Step 4: Executar o spec isolado e confirmar as falhas esperadas.**
  Run from `frontend/`: `npx jest --no-coverage src/app/shared/utils/image-compression.utils.spec.ts`.
- [ ] **Step 5: Implementar busca adaptativa.** Usar alvo de 950 KiB, tentativas de qualidade decrescentes e, somente depois, redução proporcional de dimensões; arquivos que não puderem ser reduzidos devem retornar o original para que o chamador rejeite-o.
- [ ] **Step 6: Executar novamente o spec isolado e confirmar PASS.**
- [ ] **Step 7: Commitar a unidade.**
  `git add frontend/src/app/shared/utils/image-compression.utils.ts frontend/src/app/shared/utils/image-compression.utils.spec.ts; git commit -m "feat: add adaptive image compression"`

### Task 2: Integrar produtos, logo e banner

**Files:**
- Modify: `frontend/src/app/shared/utils/image-upload.utils.ts`
- Modify: `frontend/src/app/shared/state/store-products.state.ts:1164-1197`
- Modify: `frontend/src/app/features/store-config/store-config-page.component.ts:881-937`
- Test: `frontend/src/app/shared/utils/image-upload.utils.spec.ts`
- Test: `frontend/src/app/features/store-config/products/store-products-page.component.spec.ts`
- Test: `frontend/src/app/features/store-config/store-config-page.component.spec.ts`

**Interfaces:**
- Os três fluxos chamam `compressImage` antes de `uploadImage`.
- `IMAGE_MAX_BYTES`/constante equivalente representa 1 MiB para o resultado final; arquivos raster maiores que o limite original não devem ser rejeitados antes da compressão.

- [ ] **Step 1: Ajustar constantes e testes de validação.** Separar limite-alvo frontend (950 KiB) do limite autoritativo backend (1 MiB), removendo a expectativa antiga de 6 MiB.
- [ ] **Step 2: Atualizar upload de produto.** Validar extensão/MIME, comprimir, rejeitar com mensagem clara se o resultado ainda exceder 1 MiB e enviar o arquivo comprimido.
- [ ] **Step 3: Atualizar upload de logo e banner.** Comprimir dentro de `uploadImageIfNew`, preservando o fluxo assíncrono, preview, troca rápida e tratamento de erro existente.
- [ ] **Step 4: Adicionar testes de integração de fluxo.** Espionar `uploadImage` e verificar que o `File` enviado tem tamanho reduzido; cobrir falha quando não for possível atingir o limite.
- [ ] **Step 5: Rodar os testes frontend direcionados.**
  `npx jest --no-coverage src/app/shared/utils/image-upload.utils.spec.ts` e os specs dos fluxos alterados.
- [ ] **Step 6: Commitar a integração.**
  `git add frontend/src/app/shared/utils frontend/src/app/shared/state/store-products.state.ts frontend/src/app/features/store-config/store-config-page.component.ts; git commit -m "feat: compress all image uploads"`

### Task 3: Tornar o limite do backend autoritativo e uniforme

**Files:**
- Modify: `backend/src/Urbeat.WebApi/Uploads/StoreMediaUploadValidator.cs`
- Modify: `backend/src/Urbeat.Infrastructure/Services/CloudinaryImageUploadService.cs` only if tests show the incoming transformation needs a bounded raster output
- Modify: `backend/tests/Urbeat.UnitTests/WebApi/StoreMediaUploadValidatorTests.cs`
- Modify: `backend/tests/Urbeat.IntegrationTests/Api/StoreImageUploadTests.cs`

**Interfaces:**
- `StoreMediaUploadValidator.Validate(IFormFile file, string type)` rejeita uploads maiores que `1_048_576` bytes para logo, banner, products e store-media.
- Extensão, MIME e conteúdo continuam sendo validados como antes.

- [ ] **Step 1: Atualizar testes backend primeiro.** Cobrir limite de 1 MiB para cada categoria, arquivo acima do limite, formato inválido e conteúdo corrompido.
- [ ] **Step 2: Executar os testes direcionados e confirmar falhas no limite antigo.**
  `dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~StoreMediaUploadValidator"`.
- [ ] **Step 3: Alterar constantes e mensagens do validador.** Centralizar `MaxBytes = 1L * 1024 * 1024` e usar mensagem coerente por tipo ou mensagem comum sem deixar categorias sem limite.
- [ ] **Step 4: Verificar limites de request/configuração.** Confirmar que Kestrel/proxy/Cloudinary continuam aceitando o arquivo pré-compressão dentro do limite de transporte atual; não reduzir limites globais de request sem necessidade.
- [ ] **Step 5: Executar os testes direcionados novamente e confirmar PASS.**
- [ ] **Step 6: Commitar a proteção do servidor.**
  `git add backend/src/Urbeat.WebApi/Uploads/StoreMediaUploadValidator.cs backend/tests; git commit -m "feat: enforce unified image upload limit"`

### Task 4: Validação completa e revisão

**Files:**
- Review only: todos os arquivos modificados nas tarefas anteriores

- [ ] **Step 1: Revisar diff e confirmar que nenhum projeto fora do escopo foi alterado.**
- [ ] **Step 2: Rodar suíte frontend.**
  From `frontend/`: `npx jest --no-coverage`.
- [ ] **Step 3: Rodar build frontend.**
  From `frontend/`: `npx ng build --configuration production`.
- [ ] **Step 4: Rodar build e testes backend.**
  `dotnet build backend/Urbeat.sln` e `dotnet test backend/tests/Urbeat.UnitTests`.
- [ ] **Step 5: Corrigir apenas falhas relacionadas ao escopo, repetir o comando afetado e registrar limitações de ambiente.**
- [ ] **Step 6: Fazer revisão final de compatibilidade: MIME/extensão, transparência, SVG/AVIF, mensagens, 1 MiB e comportamento de preview.**
