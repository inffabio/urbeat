# Cópia de turno com merge por posição: Design

**Data:** 2026-11-13
**Revisão:** 2026-11-13 — regra de cópia corrigida de "substituir todos os turnos do destino" para "merge por posição".
**Status:** especificação de design (sem alteração de código, sem commit, sem execução de testes)

## Objetivo

A cópia continua partindo de um **turno individual** identificado por índice (1º, 2º, ...). A mudança é o efeito no destino: em vez de substituir todos os turnos do dia, o horário copiado passa a ser aplicado **somente à posição correspondente**, preservando os demais turnos.

Regras da revisão:

1. Para cada dia de destino aberto, **preservar todos os turnos existentes**.
2. Se o turno na posição copiada existir, **substituir apenas o horário** desse turno pelo horário copiado.
3. Se não existir, **criar automaticamente os turnos intermediários** necessários até alcançar a posição; o novo turno recebe o horário copiado.
4. Os intermediários são criados com horários **vazios** (`startTime`/`endTime` em branco) e a validação existente impede salvar até serem preenchidos (ver "Regra dos turnos intermediários").
5. Sobreposição de horários **continua permitida** (frontend e backend) — nada é revertido nesse ponto.
6. O limite de **5 turnos por dia** permanece; se a posição copiada exigir mais que o limite, a cópia é rejeitada com mensagem de limite e **nenhum** destino é alterado.
7. Dias fechados e o dia de origem continuam ignorados.

A mudança é exclusivamente de frontend (estado compartilhado + texto do modal + testes). Backend, contrato HTTP, persistência e migrations não mudam.

## Escopo e histórico

- **Já implementado e commitado (`0f6caa3 feat: secure admin recovery and replace copied shifts`):**
  - remoção das regras de sobreposição no frontend e no validador da API (mesmo dia e cross-day);
  - `confirmCopy()` substituindo **todos** os turnos de cada destino por um único turno copiado;
  - nota `Os horários atuais dos dias selecionados serão substituídos.` nos dois modais.
- **Esta revisão corrige apenas a semântica da cópia**: de substituição total para merge por posição. A remoção da validação de sobreposição é mantida e não é reaberta.
- Fora de escopo: backend de recuperação admin, validador da API, Postman, DOCX, `docker/`, scripts e deploy.

## Comportamento atual (implementado)

### Estado compartilhado (`frontend/src/app/shared/state/store-hours.state.ts`)

- `openCopyModal(dayId, shiftIndex)` valida que o dia e o turno existem, grava o snapshot em `copySourceShift` (`{ dayId, shiftIndex, shift }`) e abre `isCopyModalOpen`. O snapshot é proposital: edições posteriores no dia de origem não mudam a cópia.
- `copySourceShiftNumber` é `shiftIndex + 1`; `copySourceDayLabel` resolve o rótulo do dia.
- `copyTargetDays` lista somente dias **abertos** e diferentes do dia de origem.
- `toggleCopyTarget` mantém `copyTargets` (um `Set<string>` de `dayId`).
- `confirmCopy()` (linhas 389-431) percorre os destinos e:
  - ignora destino fechado (`!day?.isOpen`) ou igual à origem, sem aviso;
  - **substitui** `day.shifts` por `[{ key: createShiftKey(), startTime, endTime }]`, descartando todos os turnos anteriores do destino;
  - chama `markDirty()` quando ao menos um destino foi aplicado.
- `reportCopyResult(shiftIndex, copiedCount)` monta apenas a mensagem de sucesso ou `Nenhum dia recebeu o turno.`; não há mais avisos de `limite`/`sobreposição`.
- `validateDay(dayId)` valida, em ordem: limite de turnos, dia aberto sem turno, turnos incompletos (`Preencha início e fim de todos os turnos.`), formato `HH:mm` / `HH:mm:ss[.f{1,7}]` e início igual ao fim. Não valida sobreposição.
- `collectScheduleErrors()` agrega `validateDay` de todos os dias; é consumido por `saveDraft()` e por `validateScheduleForSubmit()` (usado no `goNext()` do wizard).
- `sortShifts()` é usado no carregamento, em `applyPreset`, em `syncAllDays` e em `buildRequest` (ordenação do payload por `startTime`). **Não** é usado em `confirmCopy()`.
- `isOvernight()`/`isOvernightShift()` continuam usados pelos templates para o selo de turno que vira o dia; tratam horários vazios como não-overnight.

### Backend (já implementado, sem mudança nesta revisão)

- `backend/src/Urbeat.Application/Validators/UpsertStoreBusinessHoursRequestDtoValidator.cs`: `Items` não vazio, `DayOfWeek` único, dia aberto com ao menos um turno, `Shifts.Count <= 5`, `StartTime`/`EndTime` obrigatórios, início diferente do fim. Sem regras de sobreposição.
- `StoreBusinessHoursService.UpsertAsync` substitui o conjunto inteiro da loja (contrato de persistência inalterado).
- `StoresController` (`PUT {storeId}/business-hours`) devolve `ValidationProblem` 400 antes do serviço.

### Interface do modal de cópia

Ambos os templates exibem a mesma estrutura (`store-hours-page.component.html` linhas 176-180; `seller-hours-page.component.html` linhas 191-195): título `Copiar turno`, descrição dinâmica com número do turno e dia de origem, lista de checkboxes de dias e botões `Cancelar` / `Copiar turno`. A nota atual (`.copy-modal-note`) afirma que os horários atuais dos dias selecionados serão substituídos — texto que deixa de ser verdadeiro com o merge por posição.

## Comportamento novo

1. O turno continua sendo escolhido individualmente pelo botão de copiar do turno (`aria-label` `Copiar turno N de <Dia>`).
2. O usuário seleciona um ou mais dias de destino (somente dias abertos e diferentes da origem).
3. Ao confirmar, cada dia de destino selecionado e aberto é alterado **somente na posição `shiftIndex`**:
   - posição existente → o turno daquela posição recebe `startTime`/`endTime` do snapshot, mantendo `key`/`id`;
   - posição inexistente → são anexados turnos até a posição; os intermediários ficam vazios e a posição final recebe o horário copiado.
4. Os turnos existentes do destino em outras posições permanecem intactos, na mesma ordem.
5. Não há rejeição por sobreposição: a operação nunca falha por conflito de horário, no mesmo dia ou entre dias.
6. Não há mais aviso de `limite` por "destino cheio": sobrescrever uma posição não aumenta a quantidade de turnos. O limite só é usado como guarda de segurança da posição copiada (ver abaixo).
7. Destino fechado enquanto o modal está aberto continua sendo ignorado silenciosamente.
8. Dia de origem nunca é destino.
9. Se nenhum destino for aplicado (por exemplo, todos ficaram fechados durante o modal), o rascunho permanece limpo e o toast informa `Nenhum dia recebeu o turno.`
10. Se pelo menos um destino for aplicado, `hasChanges` vira `true` e o toast informa `Turno N copiado para X dia(s).`, sem bloco de avisos.

### Regra dos turnos intermediários

Quando a posição copiada ainda não existe no destino (por exemplo, destino com apenas o 1º turno e cópia do 2º), os turnos necessários são criados **vazios**:

```ts
{ key: createShiftKey(), startTime: '', endTime: '' }
```

Justificativa e efeitos, explícitos:

- **Nunca inventar horários.** Preencher intermediários com valores arbitrários (por exemplo `09:00-18:00`) gravaria dados que o lojista não digitou.
- **A validação existente bloqueia o salvamento.** `validateDay` retorna `Preencha início e fim de todos os turnos.`; no dashboard `saveDraft()` exibe o erro agrupado e não chama a API; no wizard `goNext()` fica bloqueado por `validateScheduleForSubmit()`.
- **Nada vazio é persistido.** `buildRequest()` só é alcançado depois da validação, portanto turnos vazios nunca chegam ao `PUT /api/business-hours`.
- **O usuário vê o que falta.** Os campos de horário aparecem em branco nas posições intermediárias, sinalizando exatamente o que precisa ser preenchido; `isOvernight` retorna `false` para eles.
- **Sem ordenação na cópia.** `sortShifts()` não é aplicado em `confirmCopy()` para não deslocar posições. A ordenação por `startTime` em `buildRequest()` continua valendo na persistência (o mesmo comportamento já existente para turnos digitados fora de ordem), e a posição é apenas um conceito de UI.

Exemplo canônico: origem com 2 turnos; destino com apenas o 1º (`08:00-10:00`); cópia do 2º (`14:00-16:00`). Resultado do destino: posição 1 = `08:00-10:00` (preservada), posição 2 = `14:00-16:00` (copiada); nenhum intermediário vazio foi necessário. Se a origem tivesse 3 turnos e a cópia fosse do 3º, o destino ficaria com posição 1 preservada, posição 2 vazia e posição 3 copiada.

### Guarda do limite de 5 turnos

A posição copiada exige que o destino tenha ao menos `shiftIndex + 1` turnos. Se `shiftIndex + 1 > maxShiftsPerDay` (ou seja, índice acima de 4), a cópia é **rejeitada por inteiro**:

- toast `Máximo de 5 turnos por dia.` (mesma mensagem de `validateDay`);
- nenhum destino é alterado e `hasChanges` não muda;
- o modal permanece aberto, como em `Selecione pelo menos um dia.`, para o usuário cancelar.

Na UI a guarda é defensiva: `openCopyModal` só aceita um índice existente no dia de origem e um dia de origem válido tem no máximo 5 turnos, então o índice máximo real é 4. A guarda protege contra dados legados/inconsistentes carregados com mais de 5 turnos.

### Algoritmo alvo de `confirmCopy()`

```
source = copySourceShift()
if source is null -> closeCopyModal(); return
targets = [...copyTargets()]
if targets.length === 0 -> showInfo('Selecione pelo menos um dia.'); return
if source.shiftIndex + 1 > maxShiftsPerDay -> showInfo('Máximo de 5 turnos por dia.'); return

applied = []
for targetId of targets:
  day = schedule()[targetId]
  if !day?.isOpen || targetId === source.dayId -> continue
  applied.push(targetId)

if applied.length > 0:
  schedule.update(state => {
    next = { ...state }
    for targetId of applied:
      shifts = [...state[targetId].shifts]
      index = source.shiftIndex
      while shifts.length < index:
        shifts.push({ key: createShiftKey(), startTime: '', endTime: '' })
      existing = shifts[index]
      shifts[index] = existing
        ? { ...existing, startTime: source.shift.startTime, endTime: source.shift.endTime }
        : { key: createShiftKey(), startTime: source.shift.startTime, endTime: source.shift.endTime }
      next[targetId] = { isOpen: true, shifts }
    return next
  })
  markDirty()

closeCopyModal()
reportCopyResult(source.shiftIndex, applied.length)
```

Observações de implementação:

- `index` é o `shiftIndex` do snapshot (0-based); a UI mostra `index + 1`.
- `key`/`id` da posição existente são preservados para manter a identidade da linha no template (foco, `@for track`, animação); `createShiftKey()` é usado somente em posições novas.
- O laço `while` cobre o caso de destino aberto com zero turnos e posição copiada > 0, criando apenas os intermediários necessários.
- `isOpen: true` é redundante com o guard; o importante é o dia continuar aberto.
- A cópia continua idempotente em valores: repetir a cópia sobre o mesmo destino reescreve os mesmos horários e marca `hasChanges` novamente (gesto de escrita), sem duplicar turnos.
- `reportCopyResult` mantém a assinatura `(shiftIndex, copiedCount)`; a mensagem de limite é emitida antes da aplicação e não usa `showGrouped`.

### Texto do modal

Substituir a nota atual nos dois templates por:

> O horário substitui apenas o turno de mesmo número em cada dia; os outros turnos são mantidos.

Manter o botão `Copiar turno`, os checkboxes, os `aria-label` atuais, a estrutura do modal e os alvos de toque existentes (>= 44px). Sem mudança de layout, tema (`.urbeat-onboarding` no wizard, bordeaux no dashboard), tokens ou responsividade; apenas o texto e os testes que o verificam mudam.

## Fluxo de dados

1. Botão de copiar do turno → `openCopyModal(dayId, index)` grava o snapshot e abre o modal.
2. Checkboxes → `toggleCopyTarget(dayId)` atualiza `copyTargets` (Signal `Set<string>`).
3. `Copiar turno` → `confirmCopy()`:
   - filtra destinos inválidos/fechados;
   - faz o merge local em memória por posição, criando intermediários vazios quando necessário, **sem chamada HTTP**;
   - `markDirty()` → `hasChanges = true`.
4. `closeCopyModal()` limpa `isCopyModalOpen`, `copySourceShift` e `copyTargets`.
5. Toast via `ToastService.showInfo` (sucesso, nada aplicado ou limite).
6. Persistência continua acontecendo somente em `saveDraft()` (dashboard) ou `saveHours()` via `goNext()` (wizard), por `buildRequest()` → `storeService.upsertStoreBusinessHours(storeId, request)` → `PUT /api/stores/{storeId}/business-hours`. Turnos intermediários vazios bloqueiam esse caminho até serem preenchidos.
7. Backend: `StoresController.UpsertBusinessHours` → `UpsertStoreBusinessHoursRequestDtoValidator` → `StoreBusinessHoursService.UpsertAsync` (remove + reinsere). Resposta `StoreBusinessHoursResponseDto` alimenta a tela no próximo carregamento; não há mapeamento novo.

## Impacto no frontend

Arquivo: `frontend/src/app/shared/state/store-hours.state.ts`

| Elemento | Ação |
| --- | --- |
| `confirmCopy()` | reescrever conforme algoritmo acima: merge por posição, intermediários vazios, guarda de limite |
| `reportCopyResult()` | manter `(shiftIndex, copiedCount)`; nenhuma mudança de assinatura |
| `copySourceShift`, `copySourceShiftNumber`, `copySourceDayLabel`, `copyTargetDays`, `openCopyModal`, `closeCopyModal`, `toggleCopyTarget` | manter inalterados |
| `validateDay()` | manter inalterado (já sem sobreposição); o erro `Preencha início e fim de todos os turnos.` é o que bloqueia intermediários vazios |
| `collectScheduleErrors()`, `validateScheduleForSubmit()`, `saveDraft()` | manter inalterados |
| `sortShifts()` | manter inalterado e **não** chamar em `confirmCopy()` |
| `maxShiftsPerDay`, `canAddShift`, `addShift` | manter inalterados |
| `isOvernight()` / `isOvernightShift()` | manter (tratam horário vazio) |

Arquivo: `frontend/src/app/features/store-config/hours/store-hours-page.component.html`

- Trocar o texto de `.copy-modal-note` pela nova nota; nenhuma mudança estrutural.

Arquivo: `frontend/src/app/features/seller-hours/seller-hours-page.component.html`

- Mesma troca de texto, mantendo as classes do dashboard.

Arquivos de estilo (`frontend/src/app/features/store-config/hours/store-hours-page.component.scss`, `frontend/src/app/features/seller-hours/seller-hours-page.component.scss`)

- Sem alteração: `.copy-modal-note` já existe e o texto novo tem o mesmo tamanho de bloco.

Não há mudança em `store-hours-page.component.ts`, `seller-hours-page.component.ts`, rotas, `WizardFooterComponent`, `FormsModule`, PWA, Capacitor ou assets.

## Impacto no backend

Nenhum. Esta revisão não altera validador, DTOs, serviço, controller, mapeamentos, entidades, `ApplicationDbContext` ou migrations.

Snapshot do validador (já implementado, preservado):

| Elemento | Situação |
| --- | --- |
| `RuleFor(x => x.Items).NotEmpty()` | mantido |
| unicidade de `DayOfWeek` (`"Each day of week can be configured only once."`) | mantido |
| `IsOpen` → `Shifts.NotEmpty()` (`"Open days must have at least one shift."`) | mantido |
| `Shifts.Count <= MaxShiftsPerDay` (`"Maximum 5 shifts per day."`) | mantido |
| `StartTime`/`EndTime` `NotNull` | mantido |
| `HasEqualStartAndEndTimes` (`"Start and end times cannot be equal."`) | mantido |
| sobreposição same-day e overnight cross-day | removido na revisão anterior; continua aceito |

`StoreOpeningHoursCalculator` não muda: turnos sobrepostos são aceitos e o cálculo segue determinístico (`Any(...)` + menor transição candidata). Turnos vazios nunca chegam ao backend.

## Validações mantidas e removidas

### Removidas (revisão anterior, preservadas)

- Frontend: pares de turnos sobrepostos no mesmo dia em `validateDay`.
- Frontend: conflitos cross-day de overnight (`validateCrossDayConflicts`).
- Frontend: bloqueio de cópia por sobreposição (`conflictsWithDay`).
- Backend: `"Shifts cannot overlap within the same day."`.
- Backend: `"Overnight shifts cannot overlap the next day's shifts."`.

### Mantidas

- Frontend: início e fim obrigatórios (`Preencha início e fim de todos os turnos.`) — inclusive para os intermediários criados pela cópia.
- Frontend: formato aceito `HH:mm` e `HH:mm:ss[.f{1,7}]` (contrato `TimeOnly`).
- Frontend: início igual ao fim (`Início e fim do turno não podem ser iguais.`).
- Frontend: dia aberto exige ao menos um turno; máximo de 5 turnos por dia (também como guarda da cópia).
- Frontend: `anyOpen()` no wizard; bloqueio de `addShift` com horário inválido no turno anterior.
- Frontend: apenas dias abertos recebem cópia; origem não é destino.
- Backend: `Items` não vazio, dias únicos, ao menos um turno por dia aberto, máximo de 5 turnos, `StartTime`/`EndTime` obrigatórios, início diferente do fim, turnos de dias fechados ignorados.
- Backend: propriedade da loja (403), loja inexistente (404), auditoria `StoreBusinessHoursUpserted`/`StoreBusinessHoursUpsertForbidden`.
- Overnight continua **permitido** (`end < start`), incluindo `23:59 -> 00:00` e `00:00 -> 23:59`.

## Compatibilidade e histórico

- **Mudança de comportamento conhecida:** o código atual (commit `0f6caa3`) substitui todos os turnos do destino. Esta revisão **corrige** para merge por posição. Não é um novo recurso, e sim um ajuste de contrato funcional do modal de cópia.
- **Contrato HTTP:** inalterado (`PUT /api/stores/{storeId}/business-hours` com `{ items: [{ dayOfWeek, isOpen, shifts: [{ startTime, endTime }] }] }`). O conjunto de payloads aceitos não muda nesta revisão (sobreposição já era aceita).
- **Persistência:** `UpsertAsync` continua substituindo o conjunto inteiro; nenhuma migração, backfill ou reescrita de dados.
- **Dados existentes:** horários já salvos continuam carregando e salvando. Nenhum dado histórico é reinterpretado; a posição só é usada no momento da cópia.
- **Payload:** a ordem enviada continua sendo `sortShifts` de `buildRequest` (por `startTime`). Como a posição é um conceito de UI, o merge não altera o contrato de ordem da API nem a resposta carregada.
- **Intermediários vazios:** nunca persistidos, pois `saveDraft()`/`goNext()` bloqueiam antes de `buildRequest()`. Se o usuário fechar a tela sem salvar, nada é enviado.
- **Wizard:** `goNext()` continua usando `validateScheduleForSubmit()`; nenhuma mudança de navegação.
- **Dashboard:** `saveDraft()` continua com `saveStatus`, `hasUnsavedChanges()` (incluindo `pendingDrafts`) e `cancelChanges()`, que restaura o snapshot anterior — inclusive para desfazer o merge.
- **Storefront:** `IsOpenNow`, `NextOpeningAt` e `NextStatusChangeAtUtc` preservados.
- **Mobile/PWA/Capacitor:** sem mudança de layout, rotas ou alvos de toque (>= 44px). A nota é texto puro.
- **Marca:** os textos novos não citam a marca; o padrão `urbeat` em minúsculas permanece onde já existe.
- **Postman/DOCX/deploy/secrets:** fora de escopo; nada a alterar.

## Testes

### `frontend/src/app/shared/state/store-hours.state.spec.ts`

Ajustar (bloco `individual shift copy`):

- `replaces the destination with the first shift of a day that has two shifts` → `overwrites the matching position and preserves the other destination shifts`: destino com 2 turnos, cópia do índice 0 → `['09:00-12:00', '19:00-21:00']`.
- `replaces the destination with only the shift at index %i` → `overwrites only the position at index %i`, com destino de 3 turnos e asserção do array completo (posição substituída, demais intactas).
- `clones the selected shift with a new key and preserves backend times` → `preserves the destination key and copies backend times into the matching position`: comparar com a `key` capturada antes da cópia e validar `08:00:30.2500000`/`12:30:45.5000000`.
- `uses a snapshot of the shift taken when the modal was opened` → destino tem 2 turnos; só a posição 0 recebe o snapshot `09:00-12:00` e a posição 1 permanece.
- `replaces all shifts of each selected day with the copied shift` → `merges into each selected destination independently` (destinos com quantidades diferentes de turnos).
- `replaces a destination that already has an overnight shift` → `overwrites the first position of a destination that already has an overnight shift`.
- `replaces the destination even when the copied overnight shift overlaps the next day` → `merges a copied overnight shift even when it overlaps the next day`.
- `replaces Monday even when the Sunday overnight shift ends after midnight` → `merges into Monday even when the Sunday overnight shift ends after midnight`.
- `replaces the destination when the previous day overnight shift ends before the copy starts` → `merges when the previous day overnight shift ends before the copy starts`.
- `replaces the destination with an overnight shift when the next day already has a shift` → `merges an overnight shift when the next day already has a shift`.
- `replaces a day that already has five shifts` → `keeps five shifts and overwrites the copied position` (posição 0 substituída, outras 4 intactas, sem aviso).
- `does not show grouped warnings when all destinations are replaced` → `does not show grouped warnings when destinations are merged`.
- `keeps the draft clean and closes the modal when no destination receives the shift` → mantém nome e cenário de destinos fechados durante o modal.
- `accepts a copied shift that overlaps the destination previous overnight shift` → mantém; destino de 1 turno continua com 1 turno.

Adicionar:

- `appends the copied shift as the second shift when the destination has only one`.
- `creates empty intermediate shifts until the copied position` (destino com 1 turno, cópia do índice 2 → 3 turnos: original, vazio, copiado).
- `blocks saving until the intermediate shifts are filled` (`saveDraft()` não chama `upsertStoreBusinessHours`; `showGrouped` recebe `Preencha início e fim de todos os turnos.`).
- `creates the first shift of an open destination that has none`.
- `rejects the copy when the position exceeds the maximum of five shifts` (origem carregada com 6 turnos para alcançar o índice 5; toast `Máximo de 5 turnos por dia.`; modal aberto; `hasChanges` falso; destino inalterado).
- `does not call the API during copy` (`upsertStoreBusinessHours` não chamado até `saveDraft`).
- `preserves the destination shift keys and creates keys only for new positions`.

### `frontend/src/app/features/store-config/hours/store-hours-page.component.spec.ts`

- `copies only the clicked shift to an open target day` → `fills the copied position of an empty destination and leaves earlier positions pending`: destino `terca` com 0 turnos, cópia do índice 1 → `terca.shifts` com posição 0 vazia e posição 1 `11:00-13:00`; `validateDay('terca')` retorna `Preencha início e fim de todos os turnos.`
- `replaces every shift of the destination with the copied shift` → `overwrites only the matching position and keeps the other destination shifts`: `['11:00-13:00', '15:00-18:00']`.
- `tells the user that the selected days will be replaced` → `tells the user that the other shifts are kept`, verificando a nova nota e `class="copy-modal-note"`.

### `frontend/src/app/features/seller-hours/seller-hours-page.component.spec.ts`

- `copies only the first shift of a two-shift day from its row action` → mantém o fluxo e a expectativa `['08:00-10:00']` (destino de 1 turno, índice 0), com `segunda` ainda com 2 turnos.
- `copies the third shift individually and replaces each selected destination` → `copies the third shift individually and fills the third position on each selected destination`: `terca` = `[['15:00','18:00'], ['',''], ['14:00','16:00']]` e `quarta` = `[['20:00','22:00'], ['',''], ['14:00','16:00']]`; `showGrouped` não chamado; `showInfo` com `Turno 3 copiado para 2 dias.`
- `tells the user that the selected days will be replaced` → `tells the user that the other shifts are kept`, com a nova nota.

### `backend/tests/Urbeat.UnitTests/Application/Validators/UpsertStoreBusinessHoursRequestDtoValidatorTests.cs`

Sem mudança nesta revisão: a cobertura da revisão anterior (aceitar sobreposição, rejeitar `Items` vazio, dia duplicado, mais de 5 turnos, tempos nulos e início igual ao fim) permanece válida.

### Comandos de verificação (para a implementação)

```
cd frontend; npx jest --no-coverage src/app/shared/state/store-hours.state.spec.ts
cd frontend; npx jest --no-coverage src/app/features/store-config/hours/store-hours-page.component.spec.ts
cd frontend; npx jest --no-coverage src/app/features/seller-hours/seller-hours-page.component.spec.ts
cd frontend; npx ng build --configuration production
dotnet build backend/Urbeat.sln
```

## Critérios de aceite

- [ ] Copiar o turno 2 de segunda para terça e quarta preserva o 1º turno de cada destino e aplica o horário copiado na 2ª posição.
- [ ] Se o destino já possui turno na posição copiada, somente `startTime`/`endTime` mudam; a `key`/`id` da linha é preservada.
- [ ] Se o destino não possui a posição, os turnos intermediários são criados com horários vazios e o turno copiado ocupa a posição correta.
- [ ] Turnos do destino em outras posições permanecem inalterados, na mesma ordem.
- [ ] Destino com 5 turnos continua com 5 turnos após sobrescrever uma posição, sem toast de limite.
- [ ] Origem carregada com mais de 5 turnos e cópia de índice 5 é rejeitada com `Máximo de 5 turnos por dia.`, sem alterar nenhum destino e sem `hasChanges`.
- [ ] `saveDraft()`/`goNext()` bloqueiam com `Preencha início e fim de todos os turnos.` enquanto houver intermediário vazio, e nenhuma requisição é enviada.
- [ ] O dia de origem permanece inalterado e não pode ser selecionado como destino.
- [ ] Dias fechados não aparecem como destino; fechar um dia com o modal aberto não o modifica.
- [ ] Nenhum toast de `sobreposição` é produzido; em sucesso o usuário vê `Turno N copiado para X dia(s).`
- [ ] Confirmar sem selecionar dia mantém `Selecione pelo menos um dia.` e o modal aberto.
- [ ] `hasChanges` fica `true` quando ao menos um destino é mesclado e o rascunho pode ser revertido por `cancelChanges()`.
- [ ] Os dois modais de cópia (wizard e dashboard) mostram a nota `O horário substitui apenas o turno de mesmo número em cada dia; os outros turnos são mantidos.`
- [ ] `validateDay` continua acusando formato inválido, início igual ao fim, turno incompleto e mais de 5 turnos; não acusa sobreposição.
- [ ] O validador backend permanece sem regras de sobreposição e continua retornando 400 para `Items` vazio, dia duplicado, dia aberto sem turno, mais de 5 turnos, tempos nulos e início igual ao fim.
- [ ] `PUT /api/stores/{storeId}/business-hours` mantém o mesmo contrato e continua substituindo o conjunto inteiro.
- [ ] `StoreOpeningHoursCalculator` não é alterado e os testes de `IsOpenNow` continuam passando.
- [ ] Build de produção Angular e `dotnet build backend/Urbeat.sln` sem novos warnings/erros.
- [ ] Nenhum arquivo fora dos listados nesta especificação é alterado; backend de recuperação, Postman, DOCX, secrets, deploy e `docker/` permanecem intocados.
