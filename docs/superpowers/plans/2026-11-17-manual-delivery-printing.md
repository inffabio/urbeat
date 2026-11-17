# Impressão manual de pedidos por motoboy — Plano de implementação

> **Para agentes de execução:** implemente tarefa por tarefa usando TDD. Preserve alterações locais existentes e não faça commit sem autorização explícita.

**Objetivo:** remover da interface do dashboard o envio de entregas/WhatsApp e permitir que o vendedor imprima manualmente, no card expandido, o pedido atribuído a um motoboy, incluindo o nome dele no recibo.

**Arquitetura:** manter intactos os endpoints, DTOs e serviços backend relacionados a WhatsApp/rotas. No frontend, remover apenas a superfície e o fluxo de envio de links, preservando a atribuição manual do motoboy. A impressão manual reutilizará `SellerPrintingService.printOrderFromDetails`, recebendo uma extensão de dados de impressão para o nome do motoboy, sem ativar `autoPrint`.

**Stack:** Angular 20 standalone, Signals, Ionic/TypeScript strict, `SellerPrintingService`, adaptadores de maquininha/local-agent, Jest.

## Restrições globais

- Não remover nem alterar endpoints backend de WhatsApp, rotas ou entregas.
- Não alterar `print-agent`, APIs públicas ou contratos backend.
- Não introduzir PrimeNG/Bootstrap novo; manter tokens e touch targets existentes.
- A impressão manual deve ser explícita pelo clique do vendedor; não deve depender de `autoPrint`.
- O backend continua autoritativo para atribuição e estado do pedido.
- Preservar alterações locais não relacionadas já existentes.

---

### Tarefa 1: Remover a interface de envio/WhatsApp do dashboard

**Arquivos:**
- Modificar: `frontend/src/app/features/seller-orders/seller-orders-page.component.html`
- Modificar: `frontend/src/app/features/seller-orders/seller-orders-page.component.ts`
- Modificar: `frontend/src/app/features/seller-orders/seller-orders-page.component.scss` somente para regras sem uso
- Testar: `frontend/src/app/features/seller-orders/seller-orders-page.component.spec.ts`

**Interfaces:**
- Preservar `assignDeliveryDriver`, `removeDeliveryDriver`, seleção de motoboy e demais ações manuais.
- Remover da UI e do componente apenas o fluxo `sendDeliveries*`, `openWhatsApp*`, compartilhamento de rota/link e helpers exclusivos dele.

- [ ] **Passo 1: escrever testes de ausência**

Adicionar testes que renderizem a página e confirmem:

```ts
expect(fixture.nativeElement.querySelector('.send-deliveries-trigger')).toBeNull();
expect(fixture.nativeElement.textContent).not.toContain('Enviar entregas');
expect(fixture.nativeElement.textContent).not.toContain('Compartilhar rota');
expect(fixture.nativeElement.textContent).not.toContain('Compartilhar entrega');
expect(fixture.nativeElement.textContent).not.toContain('Abrir WhatsApp');
```

- [ ] **Passo 2: executar o teste e confirmar a falha**

Executar:

```powershell
cd frontend
npx jest --no-coverage src/app/features/seller-orders/seller-orders-page.component.spec.ts -t "WhatsApp|Enviar entregas"
```

Esperado: falha porque a UI atual ainda contém o botão/modal/textos.

- [ ] **Passo 3: remover somente a UI e o estado sem uso**

Remover do template o botão superior, os overlays de envio, resultado, checklists, links, botões WhatsApp e compartilhamento. Remover do componente os sinais, interfaces, imports, métodos e computeds usados exclusivamente por esse fluxo. Não remover `DeliveryRouteService` ou campos de rota se ainda forem necessários para exibir estado existente, a menos que a análise confirme uso exclusivo.

- [ ] **Passo 4: executar os testes**

```powershell
npx jest --no-coverage src/app/features/seller-orders/seller-orders-page.component.spec.ts
```

Esperado: suíte verde, com testes antigos de atribuição e status preservados e testes de WhatsApp ajustados/removidos conforme o novo contrato.

---

### Tarefa 2: Adicionar contexto de motoboy ao modelo de impressão

**Arquivos:**
- Modificar: `frontend/src/app/features/seller-printing/seller-printing.models.ts`
- Modificar: `frontend/src/app/features/seller-printing/seller-printing.service.ts`
- Modificar: `frontend/src/app/features/seller-printing/seller-printing.service.spec.ts`

**Interfaces:**
- `PrintableOrder.deliveryDriverName?: string`
- `SellerPrintingService.printOrderFromDetails(details, operationKey?, options?: { deliveryDriverName?: string }): Promise<PrintResult>`
- Compatibilidade: chamadas atuais sem `options` continuam funcionando e sem nome de motoboy.

- [ ] **Passo 1: criar teste falhando**

```ts
it('includes the assigned delivery driver name in the printable order', async () => {
  await service.printOrderFromDetails(orderDetails, undefined, { deliveryDriverName: 'Carlos Motoboy' });
  expect(printReceiptMock).toHaveBeenCalledWith(expect.objectContaining({
    deliveryDriverName: 'Carlos Motoboy',
  }));
});
```

- [ ] **Passo 2: executar o teste**

```powershell
npx jest --no-coverage src/app/features/seller-printing/seller-printing.service.spec.ts -t "driver name"
```

Esperado: falha porque `PrintableOrder` não possui o campo.

- [ ] **Passo 3: implementar o campo e propagação**

Adicionar o campo opcional ao modelo e fazer `printOrderFromDetails` construir o objeto com o nome recebido. Manter o payload existente e não enviar dados novos ao backend; o nome será usado pelo renderer/adaptador local.

- [ ] **Passo 4: atualizar renderer do recibo**

Localizar o renderer usado por `SellerPrintingService`/adaptadores e incluir uma linha segura `Motoboy: <nome>` somente quando o valor existir. Não imprimir WhatsApp/telefone do entregador. Cobrir o renderer com teste textual ou teste do adaptador existente.

- [ ] **Passo 5: validar**

```powershell
npx jest --no-coverage src/app/features/seller-printing/seller-printing.service.spec.ts
```

Esperado: suíte verde.

---

### Tarefa 3: Botão de impressão manual no card expandido

**Arquivos:**
- Modificar: `frontend/src/app/features/seller-orders/seller-orders-page.component.html`
- Modificar: `frontend/src/app/features/seller-orders/seller-orders-page.component.ts`
- Modificar: `frontend/src/app/features/seller-orders/seller-orders-page.component.spec.ts`

**Interfaces:**
- Injetar `SellerPrintingService`.
- Adicionar estado `printingOrderId = signal<string | null>(null)`.
- Adicionar método `printAssignedOrder(order: OrderSummary): void`.
- O botão aparece somente no card expandido, para pedido de entrega com `deliveryDriverName`/`deliveryDriverId`.

- [ ] **Passo 1: escrever teste falhando**

```ts
it('shows manual print action after a motoboy is assigned and prints with his name', async () => {
  // carregar pedido expandido com deliveryDriverId/name
  const button = fixture.nativeElement.querySelector<HTMLButtonElement>(
    '.print-assigned-order',
  );
  expect(button).not.toBeNull();
  button.click();
  await fixture.whenStable();
  expect(printingServiceMock.printOrderFromDetails).toHaveBeenCalledWith(
    expect.objectContaining({ id: 'order-1' }),
    undefined,
    { deliveryDriverName: 'Carlos Motoboy' },
  );
});
```

- [ ] **Passo 2: confirmar RED**

```powershell
npx jest --no-coverage src/app/features/seller-orders/seller-orders-page.component.spec.ts -t "manual print|motoboy"
```

Esperado: falha por ausência do botão/método.

- [ ] **Passo 3: implementar ação**

No card expandido, adicionar botão:

```html
@if (!isPickupType(order.fulfillmentType) && order.deliveryDriverName) {
  <button
    type="button"
    class="board-action print-assigned-order"
    (click)="printAssignedOrder(order)"
    [disabled]="printingOrderId() === order.id"
  >
    {{ printingOrderId() === order.id ? 'Imprimindo...' : 'Imprimir pedido' }}
  </button>
}
```

O método deve obter os detalhes completos de `orderDetails()`; se não existirem, buscar via `getStoreOrder` antes de imprimir. Deve chamar `printOrderFromDetails` com o nome do motoboy, tratar falha com `ToastService`, e sempre limpar `printingOrderId` em `finally`. Não chamar `enqueueAcceptedOrder` nem alterar `autoPrint`.

- [ ] **Passo 4: testar estados**

Cobrir:

```ts
expect(button.disabled).toBe(true); // enquanto promete de impressão está pendente
expect(fixture.nativeElement.textContent).toContain('Imprimir pedido');
expect(fixture.nativeElement.textContent).not.toContain('WhatsApp');
```

- [ ] **Passo 5: validar**

```powershell
npx jest --no-coverage src/app/features/seller-orders/seller-orders-page.component.spec.ts
```

Esperado: suíte verde.

---

### Tarefa 4: Limpeza, compatibilidade e validação final

**Arquivos:**
- Modificar: somente arquivos listados nas tarefas anteriores e CSS realmente órfão.
- Não modificar: backend, endpoints, `print-agent`, DOCX e especificações administrativas.

- [ ] **Passo 1: verificar referências residuais no dashboard**

```powershell
rg -n "Enviar entregas|Compartilhar rota|Compartilhar entrega|Abrir WhatsApp|whatsApp|sendDeliveries" frontend/src/app/features/seller-orders
```

Esperado: nenhuma referência de UI/fluxo removido; referências de contratos compartilhados só podem permanecer fora do dashboard se necessárias.

- [ ] **Passo 2: executar testes direcionados**

```powershell
npx jest --no-coverage `
  src/app/features/seller-orders/seller-orders-page.component.spec.ts `
  src/app/features/seller-printing/seller-printing.service.spec.ts
```

- [ ] **Passo 3: executar build**

```powershell
npx ng build --configuration production
```

Esperado: build concluído; registrar apenas avisos de budget já existentes.

- [ ] **Passo 4: revisar diff**

```powershell
git diff -- frontend/src/app/features/seller-orders frontend/src/app/features/seller-printing
git status --short
```

Confirmar que endpoints backend permanecem intactos, que não houve remoção de código fora do escopo e que as alterações locais anteriores foram preservadas.
