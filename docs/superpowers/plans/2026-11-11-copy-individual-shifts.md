# Cópia individual de turnos Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir copiar qualquer turno individual, inclusive o primeiro, para dias ativos no wizard e no dashboard, sem sobreposição e com aviso por dia conflitante.

**Architecture:** Evoluir a lógica compartilhada de `StoreHoursState` para armazenar dia/índice do turno selecionado e copiar apenas esse turno. Atualizar os dois templates consumidores (`StoreHoursPageComponent` e `SellerHoursPageComponent`) para renderizar o botão por turno; usar a validação de intervalos existente, incluindo turnos que atravessam meia-noite.

**Tech Stack:** Angular 20 standalone, Signals, Ionic, TypeScript strict, Jest.

## Global Constraints

- O primeiro turno é um turno individual; nenhum botão copia o dia inteiro.
- Somente dias ativos podem ser destinos.
- Destinos conflitantes não recebem a cópia; destinos válidos continuam recebendo.
- O aviso deve identificar cada dia com sobreposição ou limite excedido.
- Wizard e dashboard devem obedecer exatamente à mesma regra compartilhada.
- Não alterar contratos backend, persistência, presets, sincronização ou regras de horários não relacionadas.
- Não incluir nos commits alterações não relacionadas já existentes no working tree.

---

### Task 1: Evoluir o estado compartilhado de cópia individual

**Files:**
- Modify: `frontend/src/app/shared/state/store-hours.state.ts`
- Modify: `frontend/src/app/shared/state/store-hours.state.spec.ts`

**Interfaces:**
- Produces `copySourceShift` com `dayId`, `shiftIndex` e snapshot do turno selecionado.
- `openCopyModal(dayId, shiftIndex)` abre a cópia do turno escolhido.
- `copyTargetDays` retorna somente dias abertos, exceto o dia de origem.
- `confirmCopy()` adiciona somente o turno selecionado aos destinos válidos e exibe aviso para conflitos.

- [ ] **Step 1: Escrever testes de estado que falhem.** Cobrir: primeiro turno copia apenas um turno de uma origem com dois; segundo/terceiro turno copia apenas seu índice; destinos fechados não aparecem; origem/índice inválido não copia.
- [ ] **Step 2: Escrever testes de sobreposição.** Cobrir conflito com turno normal, conflito com turno que atravessa meia-noite, conflito entre a cópia e o dia seguinte quando a cópia atravessa meia-noite e destino no limite de `maxShiftsPerDay`.
- [ ] **Step 3: Escrever teste de cópia parcial e aviso.** Configurar dois destinos ativos, um conflitante e um válido; confirmar que só o válido recebe o turno e `ToastService.showError` ou `showGrouped` informa o nome do dia conflitante.
- [ ] **Step 4: Executar o spec para confirmar as falhas esperadas.**
  From `frontend/`: `npx jest --no-coverage src/app/shared/state/store-hours.state.spec.ts`.
- [ ] **Step 5: Substituir a origem por turno individual.** Manter compatibilidade interna apenas onde necessário, mas remover dependência de `copySourceDay` como fonte da cópia.
- [ ] **Step 6: Filtrar destinos ativos.** `copyTargetDays` deve excluir o dia de origem e dias com `isOpen === false` no momento em que o modal é aberto/consultado.
- [ ] **Step 7: Implementar confirmação segura.** Clonar o turno com nova chave, verificar todos os turnos do destino e conflitos de virada para o dia seguinte, respeitar limite máximo e acumular avisos por nome de dia. Aplicar destinos válidos numa única atualização de estado e marcar dirty somente se algo foi adicionado.
- [ ] **Step 8: Atualizar texto do modal e mensagens.** Expor dia e número do turno selecionado e informar conflitos sem apagar alterações já aplicadas.
- [ ] **Step 9: Rodar o spec compartilhado e confirmar PASS.**
- [ ] **Step 10: Commitar a lógica de estado.**
  `git add frontend/src/app/shared/state/store-hours.state.ts frontend/src/app/shared/state/store-hours.state.spec.ts; git commit -m "feat: copy individual store shifts"`

### Task 2: Atualizar o wizard de cadastro de horários

**Files:**
- Modify: `frontend/src/app/features/store-config/hours/store-hours-page.component.html`
- Modify: `frontend/src/app/features/store-config/hours/store-hours-page.component.spec.ts`
- Modify: `frontend/src/app/features/store-config/hours/store-hours-page.component.scss` only for layout needed by per-shift actions

**Interfaces:**
- Cada `.shift-row` renderiza um botão `.btn-copy`/equivalente que chama `openCopyModal(day.id, i)`.
- Não existe mais botão de cópia no nível de `.day-actions`.

- [ ] **Step 1: Atualizar testes de markup antes da implementação.** Verificar botão em primeiro, segundo e terceiro turno; aria-label inclui número do turno e dia; botão de dia inteiro não existe.
- [ ] **Step 2: Executar o spec do wizard para confirmar falha.**
  `npx jest --no-coverage src/app/features/store-config/hours/store-hours-page.component.spec.ts`.
- [ ] **Step 3: Mover o botão para cada turno.** Remover `.day-actions` de cópia e renderizar ação junto aos inputs e remoção em cada linha; preservar add/remove/overnight.
- [ ] **Step 4: Ajustar acessibilidade e responsividade.** Usar `type="button"`, título e aria-label contextualizados; manter alvo de toque mínimo e não quebrar o layout mobile.
- [ ] **Step 5: Rodar o spec do wizard e confirmar PASS.**
- [ ] **Step 6: Commitar o template do wizard.**
  `git add frontend/src/app/features/store-config/hours/store-hours-page.component.html frontend/src/app/features/store-config/hours/store-hours-page.component.scss frontend/src/app/features/store-config/hours/store-hours-page.component.spec.ts; git commit -m "feat: add per-shift copy action to wizard"`

### Task 3: Atualizar o dashboard de horários

**Files:**
- Modify: `frontend/src/app/features/seller-hours/seller-hours-page.component.html`
- Modify: `frontend/src/app/features/seller-hours/seller-hours-page.component.spec.ts`
- Modify: `frontend/src/app/features/seller-hours/seller-hours-page.component.scss` only for per-shift action layout

**Interfaces:**
- O dashboard chama a mesma API `openCopyModal(day.id, i)` e usa o mesmo modal/estado compartilhado.
- Não haverá implementação paralela de conflito ou clonagem no dashboard.

- [ ] **Step 1: Atualizar testes do dashboard antes da implementação.** Cobrir botão no primeiro e demais turnos, remoção do antigo comportamento de primeiro turno copiando o dia e aria-label contextualizado.
- [ ] **Step 2: Executar o spec do dashboard para confirmar falha.**
  `npx jest --no-coverage src/app/features/seller-hours/seller-hours-page.component.spec.ts`.
- [ ] **Step 3: Renderizar botão individual em cada `shift-group`.** Remover o `@if (i === 0)` que só mostrava cópia no primeiro; inserir a ação para todo índice e manter remoção ao lado.
- [ ] **Step 4: Atualizar a descrição do modal.** Mostrar “Copiar turno X de [dia] para:” e manter seleção somente de dias ativos.
- [ ] **Step 5: Ajustar SCSS somente onde necessário.** Preservar o layout atual do editor de horários e o comportamento dos componentes `SellerHoursTimeFieldComponent`.
- [ ] **Step 6: Rodar o spec do dashboard e confirmar PASS.**
- [ ] **Step 7: Commitar o dashboard.**
  `git add frontend/src/app/features/seller-hours/seller-hours-page.component.html frontend/src/app/features/seller-hours/seller-hours-page.component.scss frontend/src/app/features/seller-hours/seller-hours-page.component.spec.ts; git commit -m "feat: apply individual shift copy in dashboard"`

### Task 4: Validação final

**Files:**
- Review only: arquivos alterados nas tarefas 1–3

- [ ] **Step 1: Revisar diff e garantir que alterações de compressão, categorias e bairros já existentes não sejam incluídas nos commits desta tarefa.**
- [ ] **Step 2: Rodar specs direcionados.**
  `npx jest --no-coverage src/app/shared/state/store-hours.state.spec.ts src/app/features/store-config/hours/store-hours-page.component.spec.ts src/app/features/seller-hours/seller-hours-page.component.spec.ts`.
- [ ] **Step 3: Rodar suíte frontend completa.**
  From `frontend/`: `npx jest --no-coverage`.
- [ ] **Step 4: Rodar build de produção.**
  From `frontend/`: `npx ng build --configuration production`.
- [ ] **Step 5: Revisar aceite manual.** Confirmar primeiro/segundo/terceiro turno individual, apenas dias ativos, cópia parcial, aviso dos dias conflitantes, overnight e regra idêntica no wizard/dashboard.
- [ ] **Step 6: Executar `git diff --check` e registrar warnings preexistentes.**
