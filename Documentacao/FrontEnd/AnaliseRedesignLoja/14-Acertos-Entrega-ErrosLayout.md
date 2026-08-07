# 14 - Acertos de Layout na Tela de Configuração de Entrega

> **Data:** 2026-07-10  
> **Status:** Concluído  
> **Base:** Screenshots em `Documentacao/FrontEnd/NovaVersaoFront/ErrosLayout/` (acertosp1..Acertosp7)

## O que foi pedido

Corrigir problemas de layout e UX na tela de Configuração de Entrega (`/configurar-loja/entrega`), conforme documentado nos 7 screenshots da pasta `ErrosLayout/`.

## Mudanças realizadas

### 1. Renomeação de botão
- "Aplicar frete a todos" → **"Taxa Única"**  
  **Arquivo:** `store-delivery-page.component.html:60`

### 2. Inserção de bairro manual inline
- Adicionada uma **linha inline** no final da lista de bairros com campo de texto + campo de taxa + botão "Adicionar" (já existia a lógica `addInline()` no TS, mas o HTML não a renderizava).
- **Arquivos:** `store-delivery-page.component.html` (linha inline adicionada), `store-delivery-page.component.scss` (`.btn-add-inline`)

### 3. Modal de seleção de bairros — correções
- **Gap entre checkbox e nome:** `margin-right: 14px` nos checkboxes dos `ion-item`, classe `.nb-name` com `margin-left: 4px`
- **Botão Confirmar:** Renomeado de "Adicionar selecionados" para **"Confirmar seleção"**, estilizado como `.btn-confirm` (fundo vinho, ícone checkmark), mais visível e proeminente
- **Seleção:** Já era single-click (mantido), múltipla seleção com toggle-all disponível
- **Arquivos:** `store-delivery-page.component.html`, `store-delivery-page.component.scss`

### 4. Nova feature: "Frete grátis hoje"

Toggle na seção 2 do card de entrega. Quando ativado, **todo pedido do dia tem frete grátis**, independente do valor mínimo.

**Frontend:**
- Toggle switch estilizado (`.toggle-switch`) com estados on/off
- `freeShippingToday` signal no componente
- `toggleFreeShippingToday()` método
- Incluído no `persistConfig()` e carregado do backend no `loadExistingConfig()`
- Adicionado a `UpdateDeliveryConfigRequest` e `StoreResponse` models

**Backend:**
| Arquivo | Mudança |
|---------|---------|
| `Store.cs` | Novo campo `FreeShippingToday` (bool) |
| `StoreResponseDto.cs` | Novo campo `FreeShippingToday` |
| `StorePublicDetailsDto.cs` | Novo campo `FreeShippingToday` |
| `UpdateStoreDeliveryConfigRequestDto.cs` | Novo campo `FreeShippingToday` |
| `IStoreService.cs` | `UpdateDeliveryConfigAsync` assinatura atualizada |
| `StoreService.cs` | `store.FreeShippingToday = freeShippingToday` |
| `StoresController.cs` | Passa `request.FreeShippingToday` |
| `CheckoutService.cs` | `store.FreeShippingToday` check: se true → frete grátis |
| Migration | `AddFreeShippingTodayToStore` (gerado por EF) |

## Arquivos alterados

### Frontend (5 arquivos)
- `frontend/src/app/features/store-config/delivery/store-delivery-page.component.html`
- `frontend/src/app/features/store-config/delivery/store-delivery-page.component.ts`
- `frontend/src/app/features/store-config/delivery/store-delivery-page.component.scss`
- `frontend/src/app/shared/models/store.model.ts`

### Backend (9 arquivos)
- `backend/src/Urbeat.Domain/Entities/Store.cs`
- `backend/src/Urbeat.Application/DTOs/StoreResponseDto.cs`
- `backend/src/Urbeat.Application/DTOs/StorePublicDetailsDto.cs`
- `backend/src/Urbeat.Application/DTOs/UpdateStoreDeliveryConfigRequestDto.cs`
- `backend/src/Urbeat.Application/Interfaces/IStoreService.cs`
- `backend/src/Urbeat.Infrastructure/Services/StoreService.cs`
- `backend/src/Urbeat.Infrastructure/Services/CheckoutService.cs`
- `backend/src/Urbeat.WebApi/Controllers/StoresController.cs`
- `backend/src/Urbeat.Infrastructure/Persistence/Migrations/*_AddFreeShippingTodayToStore.cs`

## Build
Backend: `Build succeeded. 0 Error(s)`  
Frontend: `Output location: dist/frontend`
