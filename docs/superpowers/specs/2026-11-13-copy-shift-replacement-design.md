# Cópia de turno com substituição de horários: Design

**Data:** 2026-11-13
**Status:** especificação de design (sem alteração de código, sem commit, sem execução de testes)

## Objetivo

Alterar a cópia de turno individual para que cada dia de destino tenha **todos** os seus turnos atuais substituídos pelo turno copiado, em vez de receber o turno adicionalmente. Em consequência, a regra de sobreposição de horários deixa de existir no produto (wizard, dashboard e validação autoritativa da API), mas as demais validações de horário continuam ativas.

A mesma mudança de comportamento vale para os dois fluxos que compartilham `StoreHoursState`:

- wizard: `frontend/src/app/features/store-config/hours/store-hours-page.component.ts`;
- dashboard: `frontend/src/app/features/seller-hours/seller-hours-page.component.ts`.

## Comportamento atual

### Estado compartilhado (`frontend/src/app/shared/state/store-hours.state.ts`)

- `openCopyModal(dayId, shiftIndex)` tira um snapshot do turno em `copySourceShift` (`{ dayId, shiftIndex, shift }`) e abre `isCopyModalOpen`. O snapshot é proposital: edições posteriores no dia de origem não mudam a cópia.
- `copyTargetDays` lista somente dias **abertos** e diferentes do dia de origem.
- `toggleCopyTarget` mantém `copyTargets` (um `Set<string>` de `dayId`).
- `confirmCopy()` (linhas 389-442) percorre os destinos e:
  - ignora destino fechado (`!day?.isOpen`) ou igual à origem, sem aviso;
  - se `day.shifts.length >= maxShiftsPerDay`, descarta o destino e acumula em `limited`;
  - se `conflictsWithDay(targetId, candidate)` for verdadeiro, descarta o destino e acumula em `overlapping`;
  - caso contrário, **anexa** o turno: `shifts: this.sortShifts([...copy[dayId].shifts, candidate])`.
- `reportCopyResult(...)` monta um toast agrupado com avisos de `limite` e `sobreposição`, além da mensagem de sucesso.
- `conflictsWithDay(dayId, candidate)` (linhas 479-498) bloqueia a cópia quando o turno candidato sobrepõe um turno do mesmo dia, quando cai dentro da madrugada de um turno overnight do dia anterior, ou quando um overnight do candidato invade o início do dia seguinte. Usa `shiftsOverlap`, `getNextDayId` e `getPreviousDayId`.
- `validateDay(dayId)` (linhas 510-536) valida, em ordem: limite de turnos, dia aberto sem turno, turnos incompletos, formato `HH:mm`, início igual ao fim e, por último, pares sobrepostos (`Turnos X e Y sobrepostos.`).
- `validateCrossDayConflicts()` (linhas 550-572) gera avisos de overnight de um dia invadindo o início do dia seguinte.
- `collectScheduleErrors()` agrega `validateDay` de todos os dias e `validateCrossDayConflicts()`; é consumido por `saveDraft()` e por `validateScheduleForSubmit()` (usado no `goNext()` do wizard).
- `shiftsOverlap` (linhas 538-548) é o comparador usado tanto por `conflictsWithDay` quanto por `validateDay`; com a remoção das duas regras ele deixa de ter consumidores.

### Backend

- `backend/src/Urbeat.Application/Validators/UpsertStoreBusinessHoursRequestDtoValidator.cs`:
  - `RuleFor(x => x.Items).NotEmpty()`;
  - unicidade de `DayOfWeek`;
  - `HaveNoCrossDayOvernightConflicts` → `"Overnight shifts cannot overlap the next day's shifts."`;
  - `RuleForEach` com `When(x => x.IsOpen)`: ao menos um turno, `Shifts.Count <= 5`, `StartTime`/`EndTime` obrigatórios, `HasOverlappingShifts` → `"Shifts cannot overlap within the same day."` e `HasEqualStartAndEndTimes` → `"Start and end times cannot be equal."`.
- `backend/src/Urbeat.WebApi/Controllers/StoresController.cs` (`PUT {storeId}/business-hours`, linhas 547-591) executa o validador e devolve `ValidationProblem` 400 antes de chamar o serviço.
- `backend/src/Urbeat.Infrastructure/Services/StoreBusinessHoursService.cs` (`UpsertAsync`) não valida sobreposição: remove todas as linhas existentes da loja (`RemoveRange(existing)`) e reinsere o payload inteiro. O contrato de persistência já é "substituição total do conjunto", portanto não precisa mudar.
- `backend/src/Urbeat.Domain/Services/StoreOpeningHoursCalculator.cs` calcula `IsOpenNow`/próximas transições com `Any(...)` sobre turnos; sobreposições não quebram o cálculo, apenas tornam o resultado redundante.

### Interface do modal de cópia

Ambos os templates exibem a mesma estrutura (`store-hours-page.component.html` linhas 161-198; `seller-hours-page.component.html` linhas 177-212): título `Copiar turno`, descrição dinâmica com número do turno e dia de origem, lista de checkboxes de dias e botões `Cancelar` / `Copiar turno`. Nenhum texto informa que o destino será substituído.

## Comportamento novo

1. O turno continua sendo escolhido individualmente pelo botão de copiar do turno (`aria-label` `Copiar turno N de <Dia>`).
2. O usuário seleciona um ou mais dias de destino (somente dias abertos e diferentes da origem, como hoje).
3. Ao confirmar, **cada dia de destino selecionado e aberto passa a ter exatamente um turno**: uma cópia do snapshot do turno de origem. Os turnos anteriores do destino são descartados.
4. Não existe mais rejeição por sobreposição: a operação nunca falha por conflito de horário, no mesmo dia ou entre dias. Não existe mais aviso de `sobreposição`.
5. Não existe mais aviso de `limite`: como o destino fica com um único turno, a cópia não pode ultrapassar `maxShiftsPerDay`. O limite continua valendo para `addShift` e para payloads manuais.
6. Destino fechado enquanto o modal está aberto continua sendo ignorado silenciosamente (guard defensivo de corrida de UI, sem aviso).
7. Dia de origem nunca é destino.
8. Se nenhum destino for aplicado (por exemplo, todos ficaram fechados durante o modal), o rascunho permanece limpo e o toast informa `Nenhum dia recebeu o turno.`
9. Se pelo menos um destino for aplicado, `hasChanges` vira `true` e o toast informa `Turno N copiado para X dia(s).`, sem bloco de avisos.

### Algoritmo alvo de `confirmCopy()`

```
source = copySourceShift()
if source is null -> closeCopyModal(); return
targets = [...copyTargets()]
if targets.length === 0 -> showInfo('Selecione pelo menos um dia.'); return

applied = []
for targetId of targets:
  day = schedule()[targetId]
  if !day?.isOpen || targetId === source.dayId -> continue
  applied.push(targetId)

if applied.length > 0:
  schedule.update(state => {
    next = { ...state }
    for targetId of applied:
      next[targetId] = {
        isOpen: true,
        shifts: [{ key: createShiftKey(), startTime: source.shift.startTime, endTime: source.shift.endTime }],
      }
    return next
  })
  markDirty()

closeCopyModal()
reportCopyResult(source.shiftIndex, applied.length)   // sem parâmetros de aviso
```

Observações de implementação:

- `sortShifts` não é necessário no destino (lista de um elemento), mas pode ser mantido sem custo.
- `isOpen: true` é redundante com o guard; manter `...state[targetId]` ou o literal é decisão de implementação, desde que `isOpen` permaneça `true`.
- A cópia é idempotente em valores: copiar para um dia que já tem o mesmo turno continua substituindo e marcando `hasChanges`, porque a chave de UI (`key`) é regenerada.

### Texto do modal

Adicionar, abaixo da descrição existente nos dois templates, uma linha informativa não interativa:

> Os horários atuais dos dias selecionados serão substituídos.

Manter o botão `Copiar turno`, os checkboxes, os `aria-label` atuais e os alvos de toque existentes (>= 44px). Não há mudança de layout, de tema (`.urbeat-onboarding` no wizard, bordeaux no dashboard) nem de responsividade.

## Fluxo de dados

1. Botão de copiar do turno → `openCopyModal(dayId, index)` grava o snapshot e abre o modal.
2. Checkboxes → `toggleCopyTarget(dayId)` atualiza `copyTargets` (Signal `Set<string>`).
3. `Copiar turno` → `confirmCopy()`:
   - filtra destinos inválidos/fechados;
   - substitui `shifts` de cada destino pelo único turno copiado (estado local em memória, sem chamada HTTP);
   - `markDirty()` → `hasChanges = true`.
4. `closeCopyModal()` limpa `isCopyModalOpen`, `copySourceShift` e `copyTargets`.
5. Toast via `ToastService.showInfo` (sucesso) ou `showInfo('Nenhum dia recebeu o turno.')`.
6. Persistência continua acontecendo somente em `saveDraft()` (dashboard) ou `saveHours()` via `goNext()` (wizard), por `buildRequest()` → `storeService.upsertStoreBusinessHours(storeId, request)` → `PUT /api/stores/{storeId}/business-hours`.
7. Backend: `StoresController.UpsertBusinessHours` → `UpsertStoreBusinessHoursRequestDtoValidator` → `StoreBusinessHoursService.UpsertAsync` (remove + reinsere). Resposta `StoreBusinessHoursResponseDto` alimenta a tela no próximo carregamento; não há mapeamento novo.

## Impacto no frontend

Arquivo: `frontend/src/app/shared/state/store-hours.state.ts`

| Elemento | Ação |
| --- | --- |
| `confirmCopy()` | reescrever conforme algoritmo acima; remover `limited`, `overlapping`, `conflictsWithDay` e a concatenação com `day.shifts` |
| `reportCopyResult()` | reduzir assinatura para `(shiftIndex, copiedCount)`; remover montagem de avisos e o caminho `showGrouped` |
| `conflictsWithDay()` | remover (sem consumidores após a mudança) |
| `getNextDayId()` / `getPreviousDayId()` | remover (usados apenas por `conflictsWithDay`) |
| `shiftsOverlap()` | remover (usado apenas por `conflictsWithDay` e pelo par sobreposto de `validateDay`) |
| `validateDay()` | remover o laço `for (i) for (j)` de sobreposição; manter limite, dia aberto sem turno, turnos incompletos, formato `HH:mm` e início igual ao fim, nessa mesma ordem |
| `validateCrossDayConflicts()` | remover |
| `collectScheduleErrors()` | remover a chamada a `validateCrossDayConflicts()` |
| `isOvernight()` / `isOvernightShift()` | manter: `isOvernight` é usado pelos templates para o selo de turno que vira o dia; `isOvernightShift` continua usado por `isOvernight` |
| `maxShiftsPerDay`, `canAddShift`, `addShift` | manter inalterados |
| `copyTargetDays`, `openCopyModal`, `closeCopyModal`, `toggleCopyTarget` | manter inalterados |

Arquivo: `frontend/src/app/features/store-config/hours/store-hours-page.component.html`

- Adicionar a linha `Os horários atuais dos dias selecionados serão substituídos.` no `.copy-modal-body`, sem alterar estrutura de grid/ações.

Arquivo: `frontend/src/app/features/seller-hours/seller-hours-page.component.html`

- Mesma linha informativa, respeitando as classes do dashboard (`copy-modal-desc` adicional ou nota própria, conforme o SCSS local; reutilizar tokens `var(--app-*)` existentes).

Arquivos de estilo (`frontend/src/app/features/seller-hours/seller-hours-page.component.scss` e `frontend/src/app/features/store-config/hours/store-hours-page.component.scss`)

- Apenas se a linha informativa exigir estilo: tipografia secundária com token existente, sem alterar tamanhos de alvo de toque nem breakpoints.

Não há mudança em `store-hours-page.component.ts`, `seller-hours-page.component.ts`, rotas, `WizardFooterComponent`, `FormsModule`, PWA, Capacitor ou assets.

## Impacto no backend

Arquivo: `backend/src/Urbeat.Application/Validators/UpsertStoreBusinessHoursRequestDtoValidator.cs`

| Elemento | Ação |
| --- | --- |
| `RuleFor(x => x.Items).NotEmpty()` | manter |
| regra de unicidade de `DayOfWeek` | manter (`"Each day of week can be configured only once."`) |
| `HaveNoCrossDayOvernightConflicts` + mensagem `"Overnight shifts cannot overlap the next day's shifts."` | **remover** a regra e o método privado |
| `IsOpen` → `Shifts.NotEmpty()` | manter (`"Open days must have at least one shift."`) |
| `Shifts.Count <= MaxShiftsPerDay` | manter (`"Maximum 5 shifts per day."`) |
| `StartTime`/`EndTime` `NotNull` | manter |
| `HasOverlappingShifts` + `"Shifts cannot overlap within the same day."` | **remover** a regra e o método privado |
| `HasEqualStartAndEndTimes` | manter (`"Start and end times cannot be equal."`) |

Não alterar: `UpsertStoreBusinessHoursRequestDto`, `StoreBusinessHourShiftDto`, `StoreBusinessHoursResponseDto`, `IStoreBusinessHoursService`, `StoreBusinessHoursService.UpsertAsync`, `StoresController`, mapeamentos AutoMapper, entidades, `ApplicationDbContext` e migrações.

`StoreOpeningHoursCalculator` não muda. Turnos sobrepostos passam a ser aceitos na persistência e o cálculo continua determinístico porque usa `Any(...)` (aberto se qualquer turno cobre o instante) e, para próxima abertura/fechamento, escolhe o menor instante candidato.

## Validações mantidas e removidas

### Removidas

- Frontend: pares de turnos sobrepostos no mesmo dia em `validateDay`.
- Frontend: conflitos cross-day de overnight em `validateCrossDayConflicts()`.
- Frontend: bloqueio de cópia por sobreposição em `conflictsWithDay()`.
- Backend: `"Shifts cannot overlap within the same day."`.
- Backend: `"Overnight shifts cannot overlap the next day's shifts."`.

### Mantidas

- Frontend: início e fim obrigatórios (`Preencha início e fim de todos os turnos.`).
- Frontend: formato aceito `HH:mm` e `HH:mm:ss[.f{1,7}]` (contrato `TimeOnly`).
- Frontend: início igual ao fim (`Início e fim do turno não podem ser iguais.`) — redundante com o backend, mantido por paridade de mensagem.
- Frontend: dia aberto exige ao menos um turno; máximo de 5 turnos por dia.
- Frontend: `anyOpen()` no wizard; bloqueio de `addShift` com horário inválido no turno anterior.
- Frontend: apenas dias abertos recebem cópia; origem não é destino.
- Backend: `Items` não vazio, dias únicos, ao menos um turno por dia aberto, máximo de 5 turnos, `StartTime`/`EndTime` obrigatórios, início diferente do fim, turnos de dias fechados ignorados.
- Backend: propriedade da loja (403), loja inexistente (404), auditoria `StoreBusinessHoursUpserted`/`StoreBusinessHoursUpsertForbidden`.
- Overnight continua **permitido** (`end < start`), incluindo `23:59 -> 00:00` e `00:00 -> 23:59`, sem qualquer regra de sobreposição. Não há outra regra de overnight no código que precise de ajuste de coerência.

## Compatibilidade

- **Contrato HTTP:** inalterado (`PUT /api/stores/{storeId}/business-hours` com `{ items: [{ dayOfWeek, isOpen, shifts: [{ startTime, endTime }] }] }`). O conjunto de payloads aceitos aumenta: requisições antes rejeitadas com 400 por sobreposição passam a ser aceitas.
- **Persistência:** `UpsertAsync` já substitui o conjunto inteiro; nenhuma migração, backfill ou reescrita de dados.
- **Dados existentes:** horários já salvos que hoje teriam sobreposição continuam carregando e salvando. `validateDay` não os bloqueia mais.
- **Wizard:** `goNext()` continua usando `validateScheduleForSubmit()`, que passa a conter somente as validações mantidas; nenhuma mudança de navegação.
- **Dashboard:** `saveDraft()` continua com `saveStatus`, `hasUnsavedChanges()` (incluindo `pendingDrafts`) e `cancelChanges()`, que restaura o snapshot anterior — inclusive para desfazer uma cópia por substituição.
- **Storefront:** `IsOpenNow`, `NextOpeningAt` e `NextStatusChangeAtUtc` preservados; sobreposição apenas produz resultado redundante, nunca erro.
- **Mobile/PWA/Capacitor:** sem mudança de layout, rotas ou alvos de toque (>= 44px). A linha informativa nova é texto puro.
- **Marca:** os textos novos não citam a marca; o padrão `urbeat` em minúsculas permanece onde já existe.
- **Postman/DOCX/deploy/secrets:** fora de escopo; nada a alterar.

## Testes

### `frontend/src/app/shared/state/store-hours.state.spec.ts`

Ajustar:

- `detects the backend overnight overlap between an early and a late shift on the same day` → passa a esperar `validateDay('segunda')` nulo para `00:30-02:00` + `23:00-01:00` (renomear para indicar que sobreposição é aceita).
- `reports an overnight shift that conflicts with the next day, including Sunday to Monday` → remover junto com `validateCrossDayConflicts()`.
- `does not report a next-day shift that starts after the overnight shift ends` → remover.
- `blocks saveDraft when there is a cross-day conflict` → remover (ou converter em teste de que `saveDraft` envia o payload).
- `copies only the first shift of a day that has two shifts` → esperar `['09:00-12:00']` em terça (substituição, não `['09:00-12:00','19:00-21:00']`).
- `copies only the shift at index %i` → esperar apenas o turno copiado.
- `clones the selected shift with a new key and preserves backend times` → continua válido, mas o dia de destino fica com um único turno.
- `uses a snapshot of the shift taken when the modal was opened` → esperar `['09:00-12:00']`.
- `applies the copy to valid days and warns about each conflicting day` → reescrever: ambos os destinos recebem o turno e não há `showGrouped`/`sobreposição`.
- `blocks a copy that overlaps an overnight shift on the destination day` → reescrever para substituição (`['00:30-02:00']`).
- `blocks a copied overnight shift that conflicts with the next day of the destination` → reescrever para substituição.
- `blocks a normal copy that starts before the previous day overnight shift ends` → reescrever para substituição.
- `blocks a normal copy into Monday when the Sunday overnight shift ends overnight` → reescrever para substituição.
- `copies a normal shift when the previous day overnight shift ends before it starts` → esperar `['02:00-04:00']`.
- `copies an overnight shift when the next day has no conflicting shift` → esperar `['23:00-01:00']`.
- `does not exceed the maximum shifts per day and warns about the day at the limit` → reescrever: destino com 5 turnos passa a ter 1 turno, sem aviso de limite (o limite continua coberto por `limits each day to five shifts` e `canAddShift`).
- `keeps the draft clean and closes the modal when no destination receives the shift` → continua válido apenas no cenário de destinos fechados durante o modal; sem cenário de conflito.

Adicionar:

- `replaces all shifts of each selected day with the copied shift` (dois destinos, vários turnos cada, resultado com 1 turno cada e origem intacta).
- `keeps the destination day open and the origin untouched`.
- `accepts a copied shift that overlaps the destination previous overnight shift` (sem toast de aviso, `hasChanges` true).
- `does not show grouped warnings when all destinations are replaced` (`showGrouped` não chamado).
- `does not call the API during copy` (`upsertStoreBusinessHours` não chamado até `saveDraft`).

### `frontend/src/app/features/seller-hours/seller-hours-page.component.spec.ts`

- Teste `copies the third shift individually and reports the conflicting destination` → renomear e reescrever: `terca` e `quarta` ficam apenas com `14:00-16:00`; `showGrouped` não é chamado; nenhuma mensagem contém `sobreposição`.
- Teste de DOM com `.copy-action` → mantém contagem/aria-labels; o clique em `copyButtons[0]` deve resultar em `['08:00-10:00']` em terça.

### `frontend/src/app/features/store-config/hours/store-hours-page.component.spec.ts`

- `copies only the clicked shift to an open target day` → continua válido; adicionar variação em que o destino tem dois turnos e passa a ter apenas o copiado.

### `backend/tests/Urbeat.UnitTests/Application/Validators/UpsertStoreBusinessHoursRequestDtoValidatorTests.cs`

Remover/ajustar:

- `ValidateAsync_ShouldRejectOverlappingShiftsWithinTheSameDay` → converter em `ValidateAsync_ShouldAcceptOverlappingShiftsWithinTheSameDay` (`IsValid` true, sem a mensagem antiga).
- `ValidateAsync_ShouldReportOnlyUniquenessForDuplicatedDayOfWeek` → remover a asserção sobre a mensagem de overnight (que deixa de existir); manter a unicidade.
- `ValidateAsync_ShouldRejectOvernightShiftConflictWithNextDayStart` → converter em aceite.
- `ValidateAsync_ShouldRejectOvernightShiftConflictFromSundayToMonday` → converter em aceite.
- `ValidateAsync_ShouldRejectOvernightShiftConflictFromSaturdayToSunday` → converter em aceite.
- `ValidateAsync_ShouldAcceptOvernightShiftWhenNextDayStartsAfterItEnds` e `...WhenNextDayIsClosed` → mantêm válidos.

Adicionar:

- `ValidateAsync_ShouldAcceptCopyReplacementPayload` (destino com um único turno igual ao de origem).
- `ValidateAsync_ShouldStillRejectEqualStartAndEndTimes` (garantia de regra mantida após a remoção).
- `ValidateAsync_ShouldStillRejectMoreThanFiveShiftsOnOpenDays` (cobertura já existente, manter).

Não alterar `backend/tests/Urbeat.IntegrationTests/Relational/StoreBusinessHoursServiceRelationalTests.cs`, `backend/tests/Urbeat.IntegrationTests/Api/StoreManagementFlowTests.cs` e `backend/tests/Urbeat.IntegrationTests/Api/PublicStoresFlowTests.cs` — usam payloads sem sobreposição.

### Comandos de verificação (para a implementação)

```
dotnet build backend/Urbeat.sln
dotnet test backend/tests/Urbeat.UnitTests --filter "FullyQualifiedName~UpsertStoreBusinessHoursRequestDtoValidatorTests"
cd frontend; npx jest --no-coverage src/app/shared/state/store-hours.state.spec.ts
cd frontend; npx jest --no-coverage src/app/features/seller-hours/seller-hours-page.component.spec.ts
cd frontend; npx jest --no-coverage src/app/features/store-config/hours/store-hours-page.component.spec.ts
cd frontend; npx ng build --configuration production
```

## Critérios de aceite

- [ ] Copiar o turno 2 de segunda para terça e quarta faz cada um desses dias terminar com exatamente 1 turno, com os mesmos `startTime`/`endTime` do snapshot do turno 2 de segunda.
- [ ] Os demais turnos que existiam em terça e quarta desaparecem do estado e do payload de `saveDraft()`/`goNext()`.
- [ ] O dia de origem permanece inalterado e não pode ser selecionado como destino.
- [ ] Dias fechados não aparecem como destino; fechar um dia com o modal aberto não o modifica.
- [ ] Nenhum toast de `sobreposição` ou `limite` é produzido pela cópia; em sucesso o usuário vê `Turno N copiado para X dia(s).`
- [ ] Confirmar sem selecionar dia mantém `Selecione pelo menos um dia.` e o modal aberto.
- [ ] `hasChanges` fica `true` quando ao menos um destino é substituído e o rascunho pode ser revertido por `cancelChanges()`.
- [ ] O modal de cópia, no wizard e no dashboard, mostra a nota de substituição.
- [ ] `validateDay` continua acusando formato inválido, início igual ao fim, turno incompleto e mais de 5 turnos; não acusa mais sobreposição.
- [ ] `collectScheduleErrors()`/`validateScheduleForSubmit()` não contêm validação cross-day.
- [ ] O validador backend retorna 200 para payloads com turnos sobrepostos no mesmo dia e com overnight invadindo o dia seguinte.
- [ ] O validador backend continua retornando 400 para `Items` vazio, dia duplicado, dia aberto sem turno, mais de 5 turnos, tempos nulos e início igual ao fim.
- [ ] `PUT /api/stores/{storeId}/business-hours` mantém o mesmo contrato e continua substituindo o conjunto inteiro.
- [ ] `StoreOpeningHoursCalculator` não é alterado e os testes de `IsOpenNow` continuam passando.
- [ ] Build de produção Angular e `dotnet build backend/Urbeat.sln` sem novos warnings/erros.
- [ ] Nenhum arquivo fora dos listados nesta especificação é alterado; DOCX, secrets, deploy e `docker/` permanecem intocados.
