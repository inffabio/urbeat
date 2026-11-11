# Ajustes do wizard de bairros e raio de entrega Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Simplificar a etapa de cadastro de bairros e mover exclusivamente para ela a configuração do raio máximo de entrega.

**Architecture:** Remover o estado/renderização/envio do raio da página base `StoreConfigPageComponent`. Manter o estado e a lógica de raio já existentes em `StoreDeliveryPageComponent`, adicionando apenas o controle ao lado de “Visualizar mapa” e ajustando o markup/estilos do wizard.

**Tech Stack:** Angular 20 standalone, Ionic, Signals, Reactive Forms, Jest, SCSS e tokens existentes de `frontend/src/theme/variables.scss`.

## Global Constraints

- O raio deve aparecer exclusivamente em **Entrega → Cadastro de bairros**.
- A lógica existente de validação, preview, mapa, filtro, remoção e persistência do raio deve ser preservada.
- Usar `var(--app-success-soft)` e tokens existentes; não criar cor fixa nova para o botão verde.
- Manter acessibilidade, alvos de toque de no mínimo 44px e comportamento responsivo/mobile.
- Não alterar backend, contratos, banco, mapa ou regras de cálculo.
- Não incluir nos commits alterações não relacionadas já existentes no working tree.

---

### Task 1: Remover o raio da primeira página Loja

**Files:**
- Modify: `frontend/src/app/features/store-config/store-config-page.component.ts`
- Modify: `frontend/src/app/features/store-config/store-config-page.component.html`
- Modify: `frontend/src/app/features/store-config/store-config-page.component.spec.ts`

**Interfaces:**
- `StoreConfigPageComponent` deixa de manter `maxDeliveryRadiusKm` e deixa de incluir `maxDeliveryRadiusKm` no payload da primeira etapa.
- `StoreDeliveryPageComponent` continua sendo o único consumidor da configuração de raio.

- [ ] **Step 1: Atualizar testes primeiro.** Remover expectativas que o raio apareça/envie na página Loja e adicionar uma asserção de que o markup não contém `maxDeliveryRadiusKm`/“Raio máximo (km)”.
- [ ] **Step 2: Executar o spec para confirmar a falha.**
  From `frontend/`: `npx jest --no-coverage src/app/features/store-config/store-config-page.component.spec.ts`.
- [ ] **Step 3: Remover o campo do template.** Excluir apenas o bloco de label/input de “Raio máximo (km)” da seção de configuração.
- [ ] **Step 4: Remover o estado e hidratação do raio do componente.** Retirar o signal, a atribuição durante carregamento e a validação/payload específicos do raio; preservar demais validações de entrega e o contrato geral da loja.
- [ ] **Step 5: Executar o spec novamente.** Confirmar que o componente continua carregando/salvando a primeira etapa sem referência ao raio.
- [ ] **Step 6: Commitar a remoção isolada.**
  `git add frontend/src/app/features/store-config/store-config-page.component.ts frontend/src/app/features/store-config/store-config-page.component.html frontend/src/app/features/store-config/store-config-page.component.spec.ts; git commit -m "refactor: move delivery radius out of store page"`

### Task 2: Ajustar controles e mensagem da página de bairros

**Files:**
- Modify: `frontend/src/app/features/store-config/delivery/store-delivery-page.component.html`
- Modify: `frontend/src/app/features/store-config/delivery/store-delivery-page.component.scss`
- Modify: `frontend/src/app/features/store-config/delivery/store-delivery-page.component.ts` only if an icon import becomes unused
- Modify: `frontend/src/app/features/store-config/delivery/store-delivery-page.component.spec.ts`

**Interfaces:**
- O botão de abertura do modal continua chamando `openNeighborhoodModal(-1)`, mas seu texto visível será “Selecionar Bairros”.
- O controle de taxa em lote dentro do modal permanece disponível no momento em que bairros são selecionados; o método de alerta `applyBulkFeeToAll()` será removido se ficar sem chamadas após a remoção do botão superior.

- [ ] **Step 1: Atualizar testes de markup e acessibilidade.** Cobrir texto “Selecionar Bairros”, classe/estilo de fundo verde, ausência do botão superior “Aplicar valor a todos”, ausência de `navigate-outline` no estado vazio e mensagem exata solicitada.
- [ ] **Step 2: Executar o spec para confirmar as falhas esperadas.**
  From `frontend/`: `npx jest --no-coverage src/app/features/store-config/delivery/store-delivery-page.component.spec.ts`.
- [ ] **Step 3: Alterar o template.** Renomear o botão, remover somente o botão superior de aplicação em lote, remover o ícone do empty state e trocar a mensagem para `Nenhum bairro cadastrado. Use o botão 'selecionar Bairros' para selecionar`.
- [ ] **Step 4: Aplicar o token visual verde.** Criar uma classe específica, por exemplo `.btn-select-neighborhoods`, com `background: var(--app-success-soft)` e cor/borda de sucesso legíveis, mantendo foco, hover, disabled e toque mínimo; não alterar o botão de confirmação do modal sem necessidade.
- [ ] **Step 5: Limpar código sem chamadas.** Remover `navigate-outline` ou qualquer ícone que deixe de ser usado e remover `applyBulkFeeToAll()` caso não reste nenhum chamador; manter ícones, alertas e controles usados no modal e mapa.
- [ ] **Step 6: Executar o spec novamente e confirmar PASS.**
- [ ] **Step 7: Commitar os ajustes visuais.**
  `git add frontend/src/app/features/store-config/delivery/store-delivery-page.component.html frontend/src/app/features/store-config/delivery/store-delivery-page.component.scss frontend/src/app/features/store-config/delivery/store-delivery-page.component.ts frontend/src/app/features/store-config/delivery/store-delivery-page.component.spec.ts; git commit -m "feat: simplify neighborhood selection controls"`

### Task 3: Mover o campo de raio para a barra de ações

**Files:**
- Modify: `frontend/src/app/features/store-config/delivery/store-delivery-page.component.html`
- Modify: `frontend/src/app/features/store-config/delivery/store-delivery-page.component.scss`
- Modify: `frontend/src/app/features/store-config/delivery/store-delivery-page.component.spec.ts`

**Interfaces:**
- O campo usa os sinais já existentes `maxDeliveryRadiusKm`, `radiusInputValue`, `radiusDescribedBy`, `radiusValidationError`, `radiusPreviewError` e `isRadiusUpdating`.
- O comportamento de `onDeliveryRadiusChange` e `saveDraft` não muda; apenas a localização do controle muda.

- [ ] **Step 1: Adicionar testes de posição e comportamento.** Verificar que o label/input de raio aparece na página de bairros ao lado de “Visualizar mapa”, que hidrata o valor salvo e que edição ainda dispara a atualização/validação existente.
- [ ] **Step 2: Implementar o bloco de raio na barra de ações.** Colocar um controle compacto e responsivo imediatamente junto ao botão “Visualizar mapa”, com label associado, `min="1"`, `step="1"`, valor ligado a `radiusInputValue()` e eventos/ARIA existentes.
- [ ] **Step 3: Preservar estados de atualização e erro.** Exibir feedback junto ao controle sem duplicar mensagens e manter a barra utilizável em mobile; desabilitar somente quando a lógica atual exigir.
- [ ] **Step 4: Executar o spec de entrega.**
  `npx jest --no-coverage src/app/features/store-config/delivery/store-delivery-page.component.spec.ts`.
- [ ] **Step 5: Commitar a movimentação do raio.**
  `git add frontend/src/app/features/store-config/delivery/store-delivery-page.component.html frontend/src/app/features/store-config/delivery/store-delivery-page.component.scss frontend/src/app/features/store-config/delivery/store-delivery-page.component.spec.ts; git commit -m "feat: place delivery radius in neighborhood step"`

### Task 4: Validação final e revisão

**Files:**
- Review only: arquivos alterados nas tarefas 1–3

- [ ] **Step 1: Revisar diff e garantir que alterações anteriores de compressão de imagens e categorias não sejam incluídas nos commits desta tarefa.**
- [ ] **Step 2: Rodar os specs direcionados.**
  `npx jest --no-coverage src/app/features/store-config/store-config-page.component.spec.ts src/app/features/store-config/delivery/store-delivery-page.component.spec.ts`.
- [ ] **Step 3: Rodar a suíte frontend completa.**
  From `frontend/`: `npx jest --no-coverage`.
- [ ] **Step 4: Rodar o build de produção.**
  From `frontend/`: `npx ng build --configuration production`.
- [ ] **Step 5: Revisar manualmente critérios de aceite.** Confirmar raio apenas na etapa Entrega, botão verde claro, ausência do botão superior de aplicação, ausência do ícone central, mensagem exata e responsividade.
- [ ] **Step 6: Executar `git diff --check` e registrar warnings preexistentes sem ampliar o escopo.**
