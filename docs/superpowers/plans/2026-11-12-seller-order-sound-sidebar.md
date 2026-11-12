# Som de novos pedidos na barra lateral — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fazer o som de novos pedidos iniciar ligado para lojistas sem preferência explícita, removendo o atalho superior e concentrando o controle no botão da barra lateral.

**Architecture:** O `OrderSoundAlertService` continuará usando a preferência persistida apenas para escolhas explícitas (`on`/`off`). O `SellerAppShellComponent` usará o botão lateral tanto para liberar o áudio bloqueado pelo navegador quanto para alternar o som depois da liberação. O banner superior do shell principal será removido; a janela separada de pedidos manterá seus controles próprios porque não exibe a barra lateral.

**Tech Stack:** Angular 20 standalone, Signals, Ionic `IonIcon`, Jest e APIs de áudio do navegador.

## Global Constraints

- Quem nunca escolheu uma preferência inicia com `enabled === true`.
- A preferência explícita `off` continua sendo respeitada.
- O som não deve ser ligado automaticamente quando o usuário já escolheu desligá-lo.
- Remover o banner superior `Ativar som de pedidos` do shell principal.
- O botão da barra lateral deve funcionar em desktop e mobile.
- O primeiro toque lateral pode liberar o áudio quando o navegador bloquear autoplay; depois o botão alterna entre ligado e desligado.
- Preservar os controles da rota separada `/app/pedidos-separados`, pois ela oculta a barra lateral.
- Não alterar SignalR, recebimento de pedidos, backend, checkout, autenticação ou preferências de outros recursos.
- Preservar o arquivo não versionado `Documentacao/Prompt_Organizacao_Produtos_Cardapio_Digital.docx` e as alterações não relacionadas já existentes na árvore.

---

### Task 1: Ajustar o comando do botão lateral

**Files:**
- Modify: `frontend/src/app/features/seller-shell/seller-app-shell.component.ts:120-133`
- Test: `frontend/src/app/features/seller-shell/seller-app-shell.component.spec.ts`

**Interfaces:**
- Consumes `SellerShellFacade.soundEnabled()`, `SellerShellFacade.soundNeedsActivation()`, `enableSound()` e `disableSound()`.
- Produces `toggleSound()` com o fluxo: `needsActivation` → `enableSound`; senão `soundEnabled` → `disableSound`; senão `enableSound`.

- [ ] **Step 1: Write failing tests** para os três estados do botão:

```ts
it('activates audio from the sidebar when sound is enabled but needs browser activation', async () => {
  facade.soundEnabled.set(true);
  facade.soundNeedsActivation.set(true);

  component.toggleSound();
  await Promise.resolve();

  expect(facade.enableSound).toHaveBeenCalled();
  expect(facade.disableSound).not.toHaveBeenCalled();
});

it('disables an already activated sound from the sidebar', () => {
  facade.soundEnabled.set(true);
  facade.soundNeedsActivation.set(false);

  component.toggleSound();

  expect(facade.disableSound).toHaveBeenCalled();
});

it('enables sound from the sidebar when it is disabled', () => {
  facade.soundEnabled.set(false);
  facade.soundNeedsActivation.set(false);

  component.toggleSound();

  expect(facade.enableSound).toHaveBeenCalled();
});
```

- [ ] **Step 2: Run the focused shell spec and confirm the activation case fails** because the current method disables whenever `soundEnabled()` is true.

Run from `frontend/`:

```text
npx jest --no-coverage src/app/features/seller-shell/seller-app-shell.component.spec.ts
```

- [ ] **Step 3: Update `toggleSound()`** so activation has precedence:

```ts
toggleSound(): void {
  if (this.facade.soundNeedsActivation()) {
    void this.facade.enableSound();
    return;
  }

  if (this.facade.soundEnabled()) {
    this.facade.disableSound();
    return;
  }

  void this.facade.enableSound();
}
```

- [ ] **Step 4: Run the focused shell spec and confirm all cases pass.**

---

### Task 2: Remove the upper dashboard activation shortcut

**Files:**
- Modify: `frontend/src/app/features/seller-shell/seller-app-shell.component.html:194-196`
- Modify: `frontend/src/app/features/seller-shell/seller-app-shell.component.spec.ts`
- Modify: `frontend/src/app/features/seller-install/seller-install-page.component.html:101-103` only to update stale instructional copy
- Test: `frontend/src/app/features/seller-shell/seller-app-shell.component.spec.ts`
- Test: `frontend/src/app/features/seller-install/seller-install-page.component.spec.ts` if an existing template-copy test covers this instruction

**Interfaces:**
- The normal shell no longer renders `.activation-banner` or the text `Ativar som de pedidos` above routed dashboard content.
- The sidebar button remains rendered in both desktop and mobile sidebar templates through `#sidebarContent`.
- The detached orders bar remains unchanged.

- [ ] **Step 1: Add a failing template assertion** that the shell template does not contain the normal-route activation banner, while it still contains the sidebar `.sound-btn` and the detached route controls.

```ts
const template = readFileSync(
  resolve(__dirname, './seller-app-shell.component.html'),
  'utf8',
);

expect(template).not.toContain('class="activation-banner"');
expect(template).toContain('class="mini-btn sound-btn"');
expect(template).toContain('class="detached-sound-bar"');
```

- [ ] **Step 2: Run the focused shell spec and confirm the new assertion fails** because the banner is still present.
- [ ] **Step 3: Remove only the normal-shell block** guarded by `@if (facade.soundNeedsActivation())` below `.mobile-topbar`. Do not remove the detached sound bar.
- [ ] **Step 4: Update the installation guidance** from `Ative o som de pedidos no topo do painel.` to `Use o botão de som na barra lateral do painel.`
- [ ] **Step 5: Run shell/install focused specs and confirm the banner is absent while the sidebar and detached controls remain.**

---

### Task 3: Verify default preference and browser activation behavior

**Files:**
- Review/modify: `frontend/src/app/features/seller-shell/order-sound-alert.service.ts`
- Test: `frontend/src/app/features/seller-shell/order-sound-alert.service.spec.ts`
- Test: `frontend/src/app/features/seller-shell/seller-app-shell.component.spec.ts`

**Interfaces:**
- `OrderSoundAlertService.enabled` remains `signal<boolean>` and defaults to `storedPreference !== 'off'`.
- `OrderSoundAlertService.needsActivation` remains true for an enabled document until a real `Audio.play()` succeeds.
- `enable()` persists `on` and attempts playback; `disable()` persists `off` and stops playback.

- [ ] **Step 1: Add or retain service tests** for no preference, explicit `off`, explicit `on`, successful activation and blocked playback. Do not change the expected persisted values.
- [ ] **Step 2: Run the focused service spec** and confirm the preference tests pass before changing service code.

Run from `frontend/`:

```text
npx jest --no-coverage src/app/features/seller-shell/order-sound-alert.service.spec.ts
```

- [ ] **Step 3: Keep the service default implementation unchanged** unless the focused tests expose a regression; the desired implementation is:

```ts
readonly enabled = signal(this.storedPreference !== 'off');
readonly needsActivation = signal(this.storedPreference !== 'off');
```

- [ ] **Step 4: Verify that no code writes `off` on initialization** and that only explicit user action calls `disable()`.

---

### Task 4: Full frontend validation and scope review

**Files:**
- Review all files changed in Tasks 1–3.
- Do not modify backend, `print-agent/`, `oci-mcp-server/`, deployment files, or the unrelated uncommitted product-classification feature.

- [ ] **Step 1: Run focused shell, sound-service and installation tests.**

```text
npx jest --no-coverage src/app/features/seller-shell/seller-app-shell.component.spec.ts src/app/features/seller-shell/order-sound-alert.service.spec.ts src/app/features/seller-install/seller-install-page.component.spec.ts
```

- [ ] **Step 2: Run the complete frontend test suite.**

```text
npx jest --no-coverage
```

- [ ] **Step 3: Run the production build.**

```text
npx ng build --configuration production
```

- [ ] **Step 4: Run `git diff --check` and inspect `git diff --name-only`.** Confirm only the shell/service tests and installation copy changed for this request; keep the existing product-classification changes and DOCX untouched.
- [ ] **Step 5: Report the browser autoplay limitation explicitly:** the preference starts enabled, but the first sidebar touch may be required to unlock audio on mobile/desktop browsers.
