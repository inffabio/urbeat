# Seller Dashboard Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remodelar o painel do lojista para ficar igual ao layout HTML documentado, separando completamente o contexto visual do dashboard do wizard `configurar-loja`.

**Architecture:** O shell administrativo `/app/*` permanece como contêiner do painel, mas passa a refletir a navegação e a composição visual dos HTMLs da pasta `Documentacao/DashBoard/html/`. As telas do seller deixam de reutilizar a apresentação do wizard e passam a usar componentes próprios de dashboard, reaproveitando apenas serviços, models e endpoints já existentes.

**Tech Stack:** Angular 20 standalone, Ionic 8, Jest, Angular Router, services existentes em `core/services`, backend .NET 9 já existente.

## Global Constraints

- O dashboard é um layout e o wizard é outro layout.
- Rotas `/app/*` não devem reutilizar layout de onboarding/wizard.
- `configurar-loja/*` continua existindo com layout de wizard.
- Usar apenas recursos já existentes do backend.
- Corrigir os redirects atuais de `entregas`, `instalar` e `cardapio/adicionais` para telas reais.
- Manter responsividade mobile e desktop do painel.
- Não mover regras de negócio para o frontend.

---

### Task 1: Alinhar rotas reais do painel ao mapa do HTML

**Files:**
- Modify: `frontend/src/app/app.routes.ts`
- Test: `frontend/src/app/app.routes.ts` via build

**Interfaces:**
- Consumes: componentes seller já existentes e novos componentes seller dedicados
- Produces: rotas `/app/entregas`, `/app/instalar`, `/app/cardapio/adicionais`, `/app/cardapio/produtos`, `/app/configuracoes/informacoes`, `/app/configuracoes/horarios`, `/app/configuracoes/bairros` apontando para páginas do contexto seller

- [ ] Substituir `redirectTo` de `/app/entregas` por `seller-deliveries`
- [ ] Substituir `redirectTo` de `/app/instalar` por `seller-install`
- [ ] Substituir `redirectTo` de `/app/cardapio/adicionais` por `seller-additionals`
- [ ] Trocar o reuse de páginas `store-config/*` em `/app/cardapio/produtos`, `/app/configuracoes/informacoes`, `/app/configuracoes/horarios` e `/app/configuracoes/bairros` por componentes seller dedicados
- [ ] Rodar: `npx ng build --configuration production`

### Task 2: Remodelar o `seller-shell` para ficar fiel ao menu do HTML

**Files:**
- Modify: `frontend/src/app/features/seller-shell/seller-app-shell.component.ts`
- Modify: `frontend/src/app/features/seller-shell/seller-app-shell.component.html`
- Modify: `frontend/src/app/features/seller-shell/seller-app-shell.component.scss`

**Interfaces:**
- Consumes: `SellerShellFacade`, `AuthService`, `Router`
- Produces: shell com navegação equivalente aos links documentados, incluindo Menu e Sistema

- [ ] Atualizar `menuItems` para incluir `Entregas` e refletir os labels do HTML
- [ ] Atualizar `systemItems` para incluir `Mensalidade`, `Instalar` e `Configurações`
- [ ] Ajustar o HTML do shell para espelhar a estrutura visual do dashboard documentado: brand, store-card, blocos de navegação, suporte e sair
- [ ] Ajustar SCSS para aproximar sidebar, topbar, badges e ações inferiores ao `assets/styles.css` da documentação usando tokens do projeto quando possível
- [ ] Verificar navegação manual entre todos os itens do sidebar

### Task 3: Reproduzir o dashboard principal `index.html`

**Files:**
- Modify: `frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.ts`
- Modify: `frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.html`
- Modify: `frontend/src/app/features/seller-dashboard/seller-dashboard-page.component.scss`
- Reuse: `frontend/src/app/shared/components/metric-card/*`
- Reuse: `frontend/src/app/shared/components/subscription-banner/*`

**Interfaces:**
- Consumes: `OrderService`, `SubscriptionService`, `SellerPrintingService`, `SellerShellFacade`
- Produces: dashboard visual equivalente ao HTML com métricas, pedidos recentes, pagamentos, atalhos e resumo por serviço

- [ ] Ajustar o período padrão e labels para refletir o HTML sem perder dados reais
- [ ] Corrigir links dos atalhos rápidos para rotas reais válidas do seller
- [ ] Remover link inválido atual de `/app/configuracoes/entregas` e apontar para `/app/entregas` ou `/app/configuracoes/bairros` conforme o bloco do HTML
- [ ] Ajustar cards, tabelas e badges para ficar visualmente iguais ao HTML documentado
- [ ] Preservar dados reais vindos dos serviços existentes
- [ ] Rodar: `npx jest --no-coverage src/app/features/seller-dashboard/seller-dashboard-page.component.spec.ts`

### Task 4: Reproduzir a tela `pedidos.html`

**Files:**
- Modify: `frontend/src/app/features/seller-orders/seller-orders-page.component.ts`
- Modify: `frontend/src/app/features/seller-orders/seller-orders-page.component.html`
- Modify: `frontend/src/app/features/seller-orders/seller-orders-page.component.scss`

**Interfaces:**
- Consumes: `OrderService`, `ToastService`, `SellerShellFacade`
- Produces: quadro de pedidos e ações visuais alinhadas ao HTML de pedidos

- [ ] Adaptar a hierarquia visual para kanban/cards conforme a documentação
- [ ] Manter as transições reais de status já suportadas pelo backend
- [ ] Preservar confirmação antes de avançar status
- [ ] Garantir que o layout continue funcional no mobile
- [ ] Rodar: `npx jest --no-coverage src/app/features/seller-orders/seller-orders-page.component.spec.ts`

### Task 5: Ativar e remodelar telas já existentes ocultas por redirects

**Files:**
- Modify: `frontend/src/app/features/seller-deliveries/*`
- Modify: `frontend/src/app/features/seller-install/*`
- Modify: `frontend/src/app/features/seller-additionals/*`

**Interfaces:**
- Consumes: `OrderService`, `InstallPromptService`, `StoreService`
- Produces: telas reais para `entregas`, `instalar` e `cardapio/adicionais`

- [ ] Ajustar `seller-deliveries` para refletir o HTML `entregas.html` com dados reais do endpoint de entregas
- [ ] Ajustar `seller-install` para refletir o HTML `instalar.html` com CTA real de instalação
- [ ] Ajustar `seller-additionals` para refletir `cardapio-adicionais.html`, mostrando `optionGroups` dos produtos existentes
- [ ] Verificar se todos os links dessas telas retornam corretamente para as rotas do painel

### Task 6: Remodelar telas seller já existentes com backend suportado

**Files:**
- Modify: `frontend/src/app/features/seller-customers/*`
- Modify: `frontend/src/app/features/seller-categories/*`
- Modify: `frontend/src/app/features/seller-subscription/*`
- Modify: `frontend/src/app/features/seller-printing/*`
- Modify: `frontend/src/app/features/seller-bio/*`

**Interfaces:**
- Consumes: serviços já existentes dessas páginas
- Produces: telas fiéis aos HTMLs `clientes.html`, `cardapio-categorias.html`, `mensalidade.html`, `configuracoes-impressao.html`, `configuracoes-bio.html`

- [ ] Ajustar layout de `seller-customers` para `clientes.html`
- [ ] Ajustar layout de `seller-categories` para `cardapio-categorias.html`
- [ ] Ajustar layout de `seller-subscription` para `mensalidade.html`
- [ ] Ajustar layout de `seller-printing` para `configuracoes-impressao.html`
- [ ] Ajustar layout de `seller-bio` para `configuracoes-bio.html`

### Task 7: Criar páginas seller dedicadas que substituem o reuse visual do wizard

**Files:**
- Create: `frontend/src/app/features/seller-products/seller-products-page.component.ts`
- Create: `frontend/src/app/features/seller-products/seller-products-page.component.html`
- Create: `frontend/src/app/features/seller-products/seller-products-page.component.scss`
- Create: `frontend/src/app/features/seller-store-info/seller-store-info-page.component.ts`
- Create: `frontend/src/app/features/seller-store-info/seller-store-info-page.component.html`
- Create: `frontend/src/app/features/seller-store-info/seller-store-info-page.component.scss`
- Create: `frontend/src/app/features/seller-hours/seller-hours-page.component.ts`
- Create: `frontend/src/app/features/seller-hours/seller-hours-page.component.html`
- Create: `frontend/src/app/features/seller-hours/seller-hours-page.component.scss`
- Create: `frontend/src/app/features/seller-neighborhoods/seller-neighborhoods-page.component.ts`
- Create: `frontend/src/app/features/seller-neighborhoods/seller-neighborhoods-page.component.html`
- Create: `frontend/src/app/features/seller-neighborhoods/seller-neighborhoods-page.component.scss`
- Optionally modify: shared components in `frontend/src/app/shared/components/*`

**Interfaces:**
- Consumes: `StoreService`, `AddressService`, `ToastService`, `SubscriptionService` e models já usados pelo wizard
- Produces: páginas do seller dashboard para `cardapio-produtos`, `configuracoes-informacoes`, `configuracoes-horarios`, `configuracoes-bairros`

- [ ] Criar `seller-products` usando os mesmos dados e operações da página de produtos do wizard, mas com layout do painel documentado
- [ ] Criar `seller-store-info` usando os mesmos dados e operações da página de informações do wizard, mas com layout do painel documentado
- [ ] Criar `seller-hours` usando os mesmos dados e operações da página de horários do wizard, mas com layout do painel documentado
- [ ] Criar `seller-neighborhoods` usando os mesmos dados e operações da página de bairros/entrega do wizard, mas com layout do painel documentado
- [ ] Atualizar `app.routes.ts` para usar esses novos componentes seller

### Task 8: Extrair componentes visuais compartilháveis do dashboard quando isso reduzir duplicação real

**Files:**
- Create or Modify: `frontend/src/app/shared/components/` conforme necessidade mínima

**Interfaces:**
- Consumes: markup recorrente entre telas do painel
- Produces: componentes simples como tabs internas, content cards, table wrappers ou section headers

- [ ] Extrair apenas blocos repetidos que apareçam em 3+ telas
- [ ] Evitar criar um design system paralelo ao já existente
- [ ] Manter os componentes focados e pequenos

### Task 9: Verificação final

**Files:**
- Verify: seller pages and routes affected

**Interfaces:**
- Consumes: todas as mudanças acima
- Produces: build verde e navegação coerente

- [ ] Rodar: `npx ng build --configuration production`
- [ ] Rodar Jest focado nas páginas alteradas que já possuem spec
- [ ] Verificar manualmente o fluxo de links equivalentes aos HTMLs:
  - `/app/dashboard`
  - `/app/pedidos`
  - `/app/clientes`
  - `/app/cardapio/categorias`
  - `/app/cardapio/produtos`
  - `/app/cardapio/adicionais`
  - `/app/entregas`
  - `/app/mensalidade`
  - `/app/instalar`
  - `/app/configuracoes/informacoes`
  - `/app/configuracoes/bio`
  - `/app/configuracoes/horarios`
  - `/app/configuracoes/bairros`
  - `/app/configuracoes/impressao`
- [ ] Confirmar que `/configurar-loja/*` continua com layout de wizard e não herdou o layout do dashboard
