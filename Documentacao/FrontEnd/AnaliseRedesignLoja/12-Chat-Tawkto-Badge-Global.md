# 12 — Chat de Atendimento (Tawk.to) e Badge Global

> Substitui o WhatsApp como canal de contato cliente↔loja em todas as telas da loja.

## 1. O que foi feito

1. **Substituição do WhatsApp** (removido do front do cliente): store-page não tem mais FAB verde do WhatsApp; tracking-page não tem mais o help fixo com número `5511999999999`.
2. **Tawk.to como único canal de chat** cliente↔loja. O lojista instala o app Tawk.to no celular e atende por lá (push notification).
3. **Badge de chat flutuante** em todas as telas da loja (`:storePath`), com identidade visual (ícone `chatbubble-ellipses`, rótulo "Ajuda", cor da marca `#D54A51`, sombra, animação de entrada).
4. **Campo de configuração na 1ª tela do wizard** da loja (novo card **"3. Chat de atendimento"**, entre Descrição e Identidade visual), com:
   - Input `tawkToPropertyId` (formato `propertyId/widgetId`).
   - Guia de ativação: criar conta em `tawk.to`, entrar no `dashboard.tawk.to`, achar o ID e instalar o app mobile.
5. **Serviço `TawkService`** que carrega o script de embed do Tawk.to, esconde o balão padrão do widget e expõe `open()`.
6. **`StoreShellComponent`** como pai das rotas `:storePath`, carregando a loja uma única vez (`StoreContextService`) e exibindo o `ChatBadgeComponent` persistente entre navegações.

## 2. Decisões

- Loja **sem** `tawkToPropertyId` configurado → badge **oculto** (não há fallback para telefone já que WhatsApp foi removido).
- `TawkToPropertyId` guarda o embed completo `propertyId/widgetId`; se o lojista colar só o property, usa-se `default` como widget.
- O badge **não aparece** no painel do vendedor/landing (apenas nas telas do cliente `:storePath`).

## 3. O que o lojista precisa fazer

1. Criar conta gratuita em [tawk.to](https://www.tawk.to/).
2. No painel ([dashboard.tawk.to](https://dashboard.tawk.to/)), ir em **Administração → Canais → Widget de Chat**.
3. Copiar **Property ID** e **Widget ID** e colar no campo da configuração da loja no formato `propertyId/widgetId`.
4. Instalar o app Tawk.to no celular ([iOS](https://apps.apple.com/app/tawk-to/id907458277) / [Android](https://play.google.com/store/apps/details?id=to.tawk.android)) para atender clientes com notificação push.

## 4. Arquivos afetados

- Frontend: `tawk.service.ts` (novo), `chat-badge.component.ts` (novo), `store-shell.component.ts` (novo), `store-context.service.ts` (novo), `app.routes.ts` (Shell pai das rotas), `store-config-page.component.*` (campo Tawk + renumeração), `store-page.*` (remove WhatsApp), `order-tracking` (usa TawkService), `store.model.ts` (`tawkToPropertyId`).
- Backend: `PublicStoresController` (endpoint `delivery-check`), `CheckoutService` (bloqueio de bairro não coberto), `CustomerNotificationHub` (JoinStore/LeaveStore), `StoreService` (broadcast).
