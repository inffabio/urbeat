# Cópia integral de produto Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fazer o botão Copiar do cardápio criar um novo rascunho com todos os dados editáveis do produto de origem, incluindo imagem, identidade, tags, variações, opções, adicionais e configurações de peso.

**Architecture:** O produto de origem continua sendo persistido, mas a cópia será um novo rascunho com `selectedId = null`. `StoreProductsState` manterá os dados copiados no estado do rascunho; o payload de criação usará esse estado, nunca `currentProduct` derivado do produto selecionado, pois ele deixa de existir após iniciar a cópia. IDs persistidos e IDs de entidades aninhadas serão substituídos por novos IDs locais quando necessário.

**Tech Stack:** Angular 20 standalone, Signals, TypeScript strict, Ionic, Jest.

## Global Constraints

- A origem pode ser persistida, mas o destino da cópia deve permanecer `selectedId = null` até o usuário salvar.
- Copiar foto, nome, descrição, preço, categoria, disponibilidade, tags, modo de venda, peso, variações, grupos/opções, adicionais e `choiceOptions`.
- O novo item nunca pode reutilizar o ID do produto original nem IDs persistidos de entidades aninhadas que devam ser criadas novamente.
- O usuário deve poder editar ou remover livremente qualquer dado copiado antes de salvar.
- O payload deve ser validado e montado no frontend a partir do rascunho; o backend continua autoritativo.
- Aplicar a mesma regra no wizard e no dashboard, sem alterar backend, contratos ou funcionalidades não relacionadas.
- Preservar alterações não commitadas anteriores no working tree; não incluir arquivos fora do escopo.

---

### Task 1: Implementar cópia integral no estado compartilhado

**Files:**
- Modify: `frontend/src/app/shared/state/store-products.state.ts`
- Test: `frontend/src/app/features/store-config/products/store-products-page.component.spec.ts`

**Interfaces:**
- `copyProductOptions(product: Product): void` continuará sendo o entrypoint usado pelos dois fluxos, mas passará a copiar o produto completo.
- O estado do rascunho deverá preservar `additionals` e `choiceOptions` independentemente de `selectedId`.
- `persistProduct()` deverá montar `body.additionals` e `body.choiceOptions` usando os dados atuais do rascunho.

- [ ] **Step 1: Escrever testes falhando para cópia integral.** Use um `Product` de origem com nome, descrição, imagem, categoria, preço, disponibilidade, tags, modo de venda, `additionals`, `choiceOptions`, variações, grupos/opções e peso. Verifique que cada campo é refletido no rascunho, que `selectedId()` é `null`, que a imagem e seu preview são preservados e que IDs da origem não são reutilizados.
- [ ] **Step 2: Escrever teste de edição independente.** Depois da cópia, altere nome, descrição, categoria, imagem, tags, adicionais/opções e uma entidade aninhada; confirme que a origem em `products()` permanece inalterada e que o rascunho aceita os novos valores.
- [ ] **Step 3: Escrever teste do payload de criação.** Configure uma cópia, invoque o fluxo de salvamento com mocks do serviço e confirme que a requisição de criação contém `additionals` e `choiceOptions` copiados, além dos campos básicos, mesmo com `selectedId = null`.
- [ ] **Step 4: Executar o spec para confirmar as falhas.** Em `frontend/`, execute `npx jest --no-coverage src/app/features/store-config/products/store-products-page.component.spec.ts`.
- [ ] **Step 5: Adicionar estado de rascunho para adicionais e choice options.** Inicialize-o vazio em `newProduct`/reset e mantenha-o separado do produto selecionado; não use `currentProduct` como fallback para cópia nova.
- [ ] **Step 6: Reescrever `copyProductOptions` como cópia completa.** Copie os campos simples para os signals existentes, preserve `productImagePreview`, copie os arrays com novos objetos/IDs locais e normalize os formatos já usados pelo editor. Não compartilhar referências mutáveis com `product`.
- [ ] **Step 7: Ajustar `persistProduct`.** Montar `additionals` e `choiceOptions` a partir do estado do rascunho, mantendo o comportamento de edição de produtos existentes e sem enviar IDs do produto original como ID do novo produto.
- [ ] **Step 8: Atualizar mensagem de sucesso.** Substituir “Opcoes copiadas...” por texto informando que o produto completo foi copiado para um novo produto.
- [ ] **Step 9: Rodar o spec do wizard e confirmar PASS.**
- [ ] **Step 10: Rodar revisão de diff do estado e dos testes.** Garantir que a lógica existente de seleção, criação, edição, imagem, variações e validação não foi enfraquecida.

### Task 2: Cobrir o dashboard de produtos

**Files:**
- Modify: `frontend/src/app/features/seller-products/seller-products-page.component.spec.ts`
- Review only: `frontend/src/app/features/seller-products/seller-products-page.component.ts`

**Interfaces:**
- O dashboard continuará chamando `copyProductOptions(product)` e herdará a mesma implementação compartilhada.

- [ ] **Step 1: Atualizar o teste do dashboard.** Usar produto completo e verificar que a cópia no dashboard preserva nome, imagem, categoria, tags, adicionais, choice options, variações e grupos, mantendo `selectedId = null`.
- [ ] **Step 2: Verificar que não há lógica paralela.** Confirmar que o componente não monta payload próprio nem sobrescreve os sinais copiados.
- [ ] **Step 3: Executar o spec do dashboard.** `npx jest --no-coverage src/app/features/seller-products/seller-products-page.component.spec.ts`.

### Task 3: Validação final

**Files:**
- Review only: arquivos alterados nas tarefas 1–2

- [ ] **Step 1: Executar specs direcionados.**
  `npx jest --no-coverage src/app/features/store-config/products/store-products-page.component.spec.ts src/app/features/seller-products/seller-products-page.component.spec.ts`
- [ ] **Step 2: Executar suíte frontend completa.**
  `npx jest --no-coverage`
- [ ] **Step 3: Executar build de produção.**
  `npx ng build --configuration production`
- [ ] **Step 4: Executar `git diff --check`.** Registrar apenas warnings de conversão de fim de linha preexistentes, se houver.
- [ ] **Step 5: Revisar isolamento do diff.** Confirmar que somente `store-products.state.ts` e os testes de produtos foram alterados por esta tarefa; não incluir as mudanças anteriores de horários, imagens, categorias, bairros ou backend.
