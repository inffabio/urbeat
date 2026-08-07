# Seller Dashboard Layout Design

**Goal**

Remodelar o painel do lojista para ficar visualmente alinhado aos HTMLs em `Documentacao/DashBoard/html/`, preservando o wizard `configurar-loja` como um fluxo separado, com layout e navegação próprios.

**Decision**

- O layout de dashboard e o layout de wizard são contextos distintos.
- Rotas `/app/*` nao devem reutilizar layout de onboarding/wizard.
- O wizard `configurar-loja/*` continua existindo sem adotar o shell administrativo.
- O dashboard deve usar apenas recursos reais ja existentes no backend.

## Source Of Truth

- Referencia visual: `Documentacao/DashBoard/html/*.html`
- Rotas atuais: `frontend/src/app/app.routes.ts`
- Shell atual do seller: `frontend/src/app/features/seller-shell/*`
- Servicos e APIs existentes: `frontend/src/app/core/services/*` e `backend/src/Urbeat.WebApi/Controllers/*`

## Route Mapping

Cada pagina HTML da documentacao deve corresponder a uma rota Angular real:

- `index.html` -> `/app/dashboard`
- `pedidos.html` -> `/app/pedidos`
- `clientes.html` -> `/app/clientes`
- `cardapio-categorias.html` -> `/app/cardapio/categorias`
- `cardapio-produtos.html` -> `/app/cardapio/produtos`
- `cardapio-adicionais.html` -> `/app/cardapio/adicionais`
- `configuracoes-informacoes.html` -> `/app/configuracoes/informacoes`
- `configuracoes-bio.html` -> `/app/configuracoes/bio`
- `configuracoes-horarios.html` -> `/app/configuracoes/horarios`
- `configuracoes-bairros.html` -> `/app/configuracoes/bairros`
- `configuracoes-impressao.html` -> `/app/configuracoes/impressao`
- `mensalidade.html` -> `/app/mensalidade`
- `instalar.html` -> `/app/instalar`
- `entregas.html` -> `/app/entregas`
- `login.html` -> `/login-vendedor`

## Required Route Corrections

As seguintes rotas atuais devem deixar de redirecionar e passar a renderizar telas reais do painel:

- `/app/entregas` deve usar `seller-deliveries`, nao redirecionar para `/app/pedidos`
- `/app/instalar` deve usar `seller-install`, nao redirecionar para `/app/dashboard`
- `/app/cardapio/adicionais` deve usar `seller-additionals`, nao redirecionar para `/app/cardapio/produtos`

## Layout Boundaries

### Wizard

Mantem o layout atual de fluxo guiado:

- `/configurar-loja`
- `/configurar-loja/horarios`
- `/configurar-loja/entrega`
- `/configurar-loja/produtos`
- `/configurar-loja/publicar`

### Dashboard

Usa shell administrativo proprio com sidebar, topbar, badges, cards, tabelas e secoes conforme os HTMLs de `Documentacao/DashBoard/html/`:

- todas as rotas `/app/*`

## Architecture Direction

### 1. Seller shell stays, but must match documented dashboard navigation

O `seller-shell` continua como contorno do painel, mas deve ser aproximado do HTML documentado:

- menu lateral com mesmas entradas e agrupamentos do HTML
- suporte para rotas ativas equivalentes aos links do HTML
- acoes inferiores de suporte e saida
- estado visual de loja aberta/fechada

### 2. Seller pages must stop reusing wizard presentation

Paginas do painel nao devem continuar apontando para componentes de onboarding:

- `/app/cardapio/produtos`
- `/app/configuracoes/informacoes`
- `/app/configuracoes/horarios`
- `/app/configuracoes/bairros`

Essas rotas devem ter componentes do contexto seller dashboard, mesmo quando reaproveitarem a mesma camada de servicos e os mesmos dados.

### 3. Backend contracts stay unchanged

Nao ha mudanca de regra de negocio nem novos contratos obrigatorios nesta fase. O frontend deve se adaptar aos recursos existentes.

## Backend Resource Reuse

Recursos existentes que sustentam as telas:

- dashboard: pedidos, relatorio por periodo, assinatura, configuracao de impressao
- pedidos: listagem, detalhe, atualizacao de status
- clientes: listagem de clientes da loja
- categorias: CRUD e reorder
- produtos: CRUD, upload de imagem, categorias
- bio/informacoes: dados da loja, endereco, imagem
- horarios: horarios de funcionamento
- bairros/entrega: configuracao de entrega e neighborhoods
- impressao: presets e configuracao da loja
- mensalidade: assinatura e cobrancas
- entregas: resumo/listagem de entregas
- instalar: sem backend; depende do `InstallPromptService`

## Known Gaps

- `cardapio-adicionais` nao tem API standalone. A tela deve continuar baseada nos `optionGroups` dos produtos existentes.
- `instalar` e frontend-only.
- o HTML de referencia pode sugerir numeros estaticos ou microinteracoes que nao existem no backend; esses pontos devem usar dados reais ou estados neutros, nunca mock permanente escondido.

## UX/Implementation Rules

- reproduzir o layout do HTML da documentacao o mais fielmente possivel dentro do stack Angular + Ionic existente
- preservar responsividade mobile e desktop do painel
- manter copy, hierarquia visual e links equivalentes ao HTML sempre que isso nao conflitar com o backend real
- nao mover regras de negocio para o frontend
- nao quebrar o wizard ao separar os layouts
- preferir componentes compartilhados quando fizer sentido visualmente, mas sem forcar reutilizacao de telas do wizard

## Screen Strategy

### Keep and remodel existing seller pages

- `seller-dashboard`
- `seller-orders`
- `seller-customers`
- `seller-categories`
- `seller-bio`
- `seller-printing`
- `seller-subscription`
- `seller-deliveries`
- `seller-install`
- `seller-additionals`

### Replace seller-side reuse of wizard pages

Criar paginas seller dedicadas para substituir o reuse visual atual de:

- produtos do cardapio
- configuracoes de informacoes
- configuracoes de horarios
- configuracoes de bairros/entrega

Essas novas paginas podem reaproveitar logica, models e services ja existentes, mas devem ser visivelmente paginas do dashboard.

## Expected File Impact

- `frontend/src/app/app.routes.ts`
- `frontend/src/app/features/seller-shell/*`
- `frontend/src/app/features/seller-dashboard/*`
- `frontend/src/app/features/seller-orders/*`
- `frontend/src/app/features/seller-customers/*`
- `frontend/src/app/features/seller-categories/*`
- `frontend/src/app/features/seller-additionals/*`
- `frontend/src/app/features/seller-deliveries/*`
- `frontend/src/app/features/seller-install/*`
- `frontend/src/app/features/seller-subscription/*`
- `frontend/src/app/features/seller-printing/*`
- `frontend/src/app/features/seller-bio/*`
- novos componentes seller para produtos, informacoes, horarios e bairros
- possiveis componentes compartilhados visuais em `frontend/src/app/shared/components/`

## Verification

- `npx ng build --configuration production` em `frontend/`
- testes Jest focados nas paginas/servicos alterados
- verificacao manual de navegacao entre todos os links equivalentes aos HTMLs documentados
- verificacao de que `configurar-loja/*` continua com layout de wizard, sem herdar o shell do dashboard

## Non-Goals

- reescrever backend
- criar novos contratos de API sem necessidade comprovada
- unificar dashboard e wizard em um unico layout
- manter redirects atuais que escondem telas existentes do seller
