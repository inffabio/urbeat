# Cardapio Categorias HTML Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reproduzir no Angular a tela de Categorias conforme `cardapio-categorias.html`, preservando os dados e comportamentos reais.

**Architecture:** O shell existente continua fornecendo sidebar e `main`. A página de Categorias passa a usar as mesmas classes e hierarquia de conteúdo do HTML de referência; o CSS será copiado para o pacote frontend e o SCSS local ficará restrito a bindings e estados Angular.

**Tech Stack:** Angular 20 standalone, Ionic 8, SCSS, Jest, Angular production build.

## Global Constraints

- O HTML de referência é a fonte de verdade visual.
- Não alterar backend nem componentes fora da tela de Categorias nesta etapa.
- Não reverter alterações existentes de outros trabalhos.
- Dados e regras continuam sendo fornecidos pelo backend.
- Manter acessibilidade, estados de carregamento, erro e vazio.

---

### Task 1: Incorporar CSS da Referência

**Files:**
- Create/modify: `frontend/src/assets/css/dashboard-reference.css`
- Modify: `frontend/angular.json` ou `frontend/src/styles.scss`, conforme a forma já usada pelo projeto para assets globais

- [x] **Step 1: Copiar o conteúdo atual de `Documentacao/DashBoard/html/assets/styles.css` para `frontend/src/assets/css/dashboard-reference.css`.**
- [x] **Step 2: Registrar o arquivo como stylesheet global do build Angular.**
- [x] **Step 3: Executar `npx ng build --configuration production` em `frontend` e confirmar que o CSS é incluído sem depender de `Documentacao`.**

### Task 2: Reproduzir Template de Categorias

**Files:**
- Modify: `frontend/src/app/features/seller-categories/seller-categories-page.component.html`
- Modify: `frontend/src/app/features/seller-categories/seller-categories-page.component.scss`

- [x] **Step 1: Preservar ou adicionar testes Jest para título, aviso, tabs, tabela e formulário.**
- [x] **Step 2: Substituir a estrutura visual paralela por `topbar`, `notice`, `menu-tabs`, `row g-3`, `content-card`, `content-head`, `table-responsive`, `table`, `form-card`, `form-label-app` e `form-control-app`, conforme o HTML.**
- [x] **Step 3: Manter os `@if`, `@for`, bindings e handlers existentes dentro dessa estrutura.**
- [x] **Step 4: Remover do SCSS local os tokens roxos e estilos que contradizem `dashboard-reference.css`; manter somente regras de estado Angular e responsividade não coberta.**
- [x] **Step 5: Executar `npx jest --no-coverage src/app/features/seller-categories/seller-categories-page.component.spec.ts`.**

### Task 3: Verificação Visual e Regressão

**Files:**
- Modify only if required: `frontend/src/app/features/seller-categories/*`

- [x] **Step 1: Comparar a hierarquia do template Angular com `Documentacao/DashBoard/html/cardapio-categorias.html`.**
- [x] **Step 2: Executar `npx ng build --configuration production`.**
- [x] **Step 3: Executar o detector visual com `node C:\Projetos\urbeat\.opencode\skills\impeccable\scripts\detect.mjs --json frontend/src/app/features/seller-categories/seller-categories-page.component.html frontend/src/app/features/seller-categories/seller-categories-page.component.scss`.**
- [x] **Step 4: Só após a tela passar, preparar a mesma migração para Produtos e Adicionais em planos separados.**
