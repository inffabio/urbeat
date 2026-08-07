# Especificação para recriar o Dashboard Brasa Burguer em Angular 20 + Ionic

> Documento de implementação destinado a outra IA de desenvolvimento.
>
> Referência visual e funcional: projeto HTML contido em `dashboard(1).zip`.
>
> Idioma da interface: português do Brasil (`pt-BR`).

---

## 1. Missão

Crie uma aplicação web responsiva que reproduza e melhore as telas do dashboard administrativo **Brasa Burguer**, usando:

- Angular **20.3.x**;
- Ionic Angular **8.8.x**;
- componentes standalone;
- TypeScript em modo estrito;
- SCSS;
- Angular Router;
- Angular Signals para estado local;
- Reactive Forms tipados para formulários;
- armazenamento local para permitir simulação completa sem backend;
- Capacitor apenas como camada opcional para futura compilação móvel;
- PWA para que a tela “Instalar” tenha comportamento real quando o navegador oferecer suporte.

A aplicação deve rodar no computador do usuário por meio de `npm install` e `npm start` ou `ionic serve`.

Não usar Bootstrap, jQuery, bibliotecas de dashboard prontas nem copiar as classes Bootstrap do projeto original. O arquivo de referência usa Bootstrap, mas ele deve servir somente para entender conteúdo, hierarquia, aparência e comportamento. No projeto novo, utilizar componentes Ionic e SCSS próprio.

---

## 2. Resultado esperado

Entregar um projeto executável com:

1. todas as rotas descritas neste documento;
2. layout desktop com menu lateral persistente;
3. layout mobile/tablet com menu lateral recolhível;
4. formulários e ações simuladas funcionando;
5. dados de demonstração persistidos no navegador;
6. carregamento, estado vazio, sucesso, erro e confirmação de exclusão;
7. interface acessível por teclado;
8. testes das regras de negócio mais importantes;
9. README com comandos para instalação, execução, testes e build;
10. nenhuma ação principal sem resposta visual.

O projeto não precisa de servidor ou banco de dados real. Organize a camada de dados para que, futuramente, seja possível trocar o armazenamento local por uma API sem reescrever as páginas.

---

## 3. Leitura da referência e decisões de UI/UX

### 3.1 O que deve ser preservado

- Marca “Brasa Burguer”.
- Menu lateral azul-escuro.
- Destaques em laranja para ações prioritárias e item ativo.
- Roxo como cor de apoio para controles e indicadores.
- Fundo geral cinza muito claro.
- Cards brancos com cantos arredondados e sombra suave.
- Tipografia **Plus Jakarta Sans** ou fallback equivalente.
- Cabeçalho com título, descrição, período, atualizar, notificações e data.
- Indicador “Loja aberta / Recebendo pedidos”.
- Aviso de mensalidade.
- Conteúdo, exemplos e categorias presentes nas telas originais.
- Boa densidade de informação no desktop e reorganização em cards no mobile.

### 3.2 Problemas encontrados que devem ser corrigidos

- O projeto de referência repete menu, cabeçalho e estrutura em cada arquivo HTML.
- Há páginas duplicadas ou legadas, como `produtos.html` e `cardapio-produtos.html`, além de `configuracoes.html` e `configuracoes-horarios.html`.
- Algumas ações são apenas links vazios ou botões sem comportamento.
- A página de pedidos possui CSS e estrutura diferentes do restante do painel.
- Há inconsistências de capitalização e acentuação, como “Concluido”.
- Tabelas largas perdem legibilidade em telas pequenas.
- Ausência de estados padronizados de carregamento, erro e confirmação.
- Regras de negócio estão concentradas em JavaScript imperativo.
- Exclusões não têm fluxo consistente de confirmação e desfazer.

### 3.3 Melhorias obrigatórias

- Criar um único `AppShellComponent`.
- Usar rotas canônicas e redirecionar caminhos legados.
- Unificar componentes, espaçamentos, cabeçalhos, filtros, badges e botões.
- Padronizar textos: “Concluído”, “Em preparação”, “Saiu para entrega” etc.
- Em telas estreitas, trocar tabelas por cards sem esconder informação essencial.
- Usar `ion-toast`, `ion-alert`, `ion-modal`, `ion-action-sheet`, `ion-loading` e `ion-refresher` quando apropriado.
- Exibir feedback imediato após criar, editar, excluir, salvar, atualizar ou mudar status.
- Oferecer “Desfazer” em exclusões simuladas sempre que possível.
- Manter foco visível, labels reais, mensagens de erro vinculadas aos campos e áreas clicáveis de no mínimo 44 × 44 px.

---

## 4. Versões e criação do projeto

### 4.1 Pré-requisitos

Para Angular 20.2/20.3, usar uma versão de Node compatível, preferencialmente **Node 20.19+** ou **Node 22.12+**. A matriz oficial de compatibilidade está em [Angular — Version compatibility](https://angular.dev/reference/versions).

Verificar:

```bash
node --version
npm --version
```

### 4.2 Criar e fixar o projeto em Angular 20

Não executar `ng new` com a versão global sem conferir, pois ela pode gerar Angular 21 ou 22.

```bash
npx -p @angular/cli@20.3.26 ng new brasa-burguer-dashboard \
  --standalone \
  --strict \
  --routing \
  --style=scss \
  --skip-git

cd brasa-burguer-dashboard
npx ng add @ionic/angular@8.8.15
```

Se o assistente do Ionic perguntar sobre standalone, selecionar **Standalone**.

Alternativa com Ionic CLI, somente se for possível garantir que os pacotes Angular permanecerão na série 20:

```bash
npm install -g @ionic/cli
ionic start brasa-burguer-dashboard blank --type=angular
```

Depois, conferir o `package.json` e fixar todos os pacotes `@angular/*` em `20.3.x`. A criação pelo Angular CLI fixado é preferível.

Documentação oficial:

- [Angular CLI — Local set-up](https://angular.dev/tools/cli/setup-local)
- [Ionic Angular — Quickstart](https://ionicframework.com/docs/angular/quickstart)
- [Ionic Angular — standalone build option](https://ionicframework.com/docs/angular/build-options)

### 4.3 Dependências permitidas

Obrigatórias:

```json
{
  "@angular/core": "20.3.26",
  "@angular/router": "20.3.26",
  "@angular/forms": "20.3.26",
  "@ionic/angular": "8.8.15"
}
```

O lockfile deve ser versionado. Não usar `latest` depois que a base estiver instalada.

Opcionais:

- `@angular/pwa` para manifest e service worker;
- `@capacitor/core` e `@capacitor/cli` para builds móveis;
- `@ionic/storage-angular` apenas se houver justificativa para substituir o adaptador simples de `localStorage`.

Não instalar NgRx para esta simulação. Signals e serviços por domínio são suficientes.

---

## 5. Diretrizes Angular e Ionic

- Todos os componentes devem ser standalone.
- Importar componentes Ionic individualmente de `@ionic/angular/standalone`.
- Usar o novo controle de fluxo dos templates: `@if`, `@for`, `@switch`, `@empty`.
- Usar `ChangeDetectionStrategy.OnPush`.
- Usar `input()`, `output()`, `signal()`, `computed()` e `effect()` quando fizer sentido.
- Nunca mutar arrays e objetos mantidos por signals; criar novas referências.
- Usar `inject()` para dependências.
- Usar `loadComponent` para lazy loading das páginas.
- Evitar subscriptions manuais; quando inevitáveis, usar `takeUntilDestroyed()`.
- Usar `track` em todo `@for`.
- Formulários de login, produto, categoria, adicional, bairro e configurações devem ser Reactive Forms tipados.
- Não inserir lógica de negócio extensa em templates.
- Não acessar `document` ou `window` diretamente em componentes. Criar adaptadores injetáveis para armazenamento, PWA e plataforma.
- Formatar moeda e datas em `pt-BR`, moeda `BRL` e fuso `America/Sao_Paulo`.

Signals permitem rastrear granularmente o estado usado pela interface: [Angular — Signals](https://angular.dev/guide/signals). Para formulários previsíveis e testáveis, seguir [Angular — Reactive forms](https://angular.dev/guide/forms/reactive-forms).

---

## 6. Estrutura recomendada

```text
src/
├── app/
│   ├── app.component.ts
│   ├── app.config.ts
│   ├── app.routes.ts
│   ├── core/
│   │   ├── auth/
│   │   │   ├── auth.guard.ts
│   │   │   ├── auth.service.ts
│   │   │   └── auth.models.ts
│   │   ├── persistence/
│   │   │   ├── persistence.port.ts
│   │   │   └── local-storage.adapter.ts
│   │   ├── pwa/
│   │   │   └── install-prompt.service.ts
│   │   └── utils/
│   │       ├── brl.util.ts
│   │       ├── date.util.ts
│   │       └── id.util.ts
│   ├── layout/
│   │   ├── app-shell/
│   │   ├── app-sidebar/
│   │   ├── app-topbar/
│   │   └── subscription-banner/
│   ├── shared/
│   │   ├── components/
│   │   │   ├── page-header/
│   │   │   ├── metric-card/
│   │   │   ├── status-chip/
│   │   │   ├── empty-state/
│   │   │   ├── responsive-list/
│   │   │   ├── loading-skeleton/
│   │   │   └── confirm-action/
│   │   ├── models/
│   │   └── pipes/
│   └── features/
│       ├── login/
│       ├── dashboard/
│       ├── orders/
│       ├── menu/
│       │   ├── products/
│       │   ├── categories/
│       │   └── add-ons/
│       ├── customers/
│       ├── deliveries/
│       ├── reviews/
│       ├── subscription/
│       ├── install/
│       └── settings/
│           ├── schedule/
│           ├── information/
│           ├── printing/
│           ├── bio/
│           └── neighborhoods/
├── assets/
│   ├── images/
│   └── icons/
├── theme/
│   ├── variables.scss
│   ├── tokens.scss
│   └── ionic-overrides.scss
└── styles.scss
```

Cada feature deve conter:

```text
feature/
├── pages/
├── components/
├── data-access/
│   └── feature.store.ts
├── models/
└── feature.routes.ts
```

Uma página coordena componentes e fluxo. O store gerencia dados e regras. Componentes de apresentação não devem acessar `localStorage`.

---

## 7. Rotas

Rotas canônicas:

| Rota | Tela | Protegida |
|---|---|---:|
| `/login` | Login | Não |
| `/app/dashboard` | Visão geral | Sim |
| `/app/pedidos` | Pedidos do dia | Sim |
| `/app/cardapio/produtos` | Produtos | Sim |
| `/app/cardapio/categorias` | Categorias | Sim |
| `/app/cardapio/adicionais` | Adicionais | Sim |
| `/app/clientes` | Clientes | Sim |
| `/app/entregas` | Entregas | Sim |
| `/app/avaliacoes` | Avaliações | Sim |
| `/app/mensalidade` | Mensalidade | Sim |
| `/app/instalar` | Instalar aplicativo | Sim |
| `/app/configuracoes/horarios` | Horários | Sim |
| `/app/configuracoes/informacoes` | Informações | Sim |
| `/app/configuracoes/impressao` | Impressão | Sim |
| `/app/configuracoes/bio` | Bio | Sim |
| `/app/configuracoes/bairros` | Bairros | Sim |

Redirecionamentos:

- `/` → `/app/dashboard` quando autenticado, caso contrário `/login`;
- `/app/cardapio` → `/app/cardapio/produtos`;
- `/app/produtos` → `/app/cardapio/produtos`;
- `/app/configuracoes` → `/app/configuracoes/horarios`;
- rota desconhecida → página 404 simples com ação “Voltar ao dashboard”.

Exemplo:

```ts
import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () =>
      import('./features/login/pages/login.page').then((m) => m.LoginPage),
  },
  {
    path: 'app',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./layout/app-shell/app-shell.component')
        .then((m) => m.AppShellComponent),
    children: [
      {
        path: 'dashboard',
        title: 'Dashboard | Brasa Burguer',
        loadComponent: () =>
          import('./features/dashboard/pages/dashboard.page')
            .then((m) => m.DashboardPage),
      },
      {
        path: 'pedidos',
        title: 'Pedidos | Brasa Burguer',
        loadComponent: () =>
          import('./features/orders/pages/orders.page')
            .then((m) => m.OrdersPage),
      },
      {
        path: '',
        pathMatch: 'full',
        redirectTo: 'dashboard',
      },
    ],
  },
  { path: '**', redirectTo: 'app/dashboard' },
];
```

Seguir o Angular Router e manter URLs recarregáveis e compartilháveis: [Angular — Define routes](https://angular.dev/guide/routing/define-routes) e [Ionic Angular — Navigation](https://ionicframework.com/docs/angular/navigation).

---

## 8. App shell e navegação

Usar `ion-split-pane`, `ion-menu` e `ion-router-outlet`.

- Desktop: menu visível a partir de 1200 px, largura de 244 px.
- Abaixo de 1200 px: menu em overlay, aberto por `ion-menu-button`.
- O conteúdo principal deve ocupar o restante da janela.
- O menu deve fechar após selecionar uma rota em modo compacto.
- Indicar item ativo com `routerLinkActive`.
- Badge “12” em Pedidos.
- Rodapé do menu com suporte e sair.
- O conteúdo principal precisa ter ID igual ao `contentId` configurado no menu.

O `ion-split-pane` existe exatamente para adaptar menu e conteúdo conforme a largura e aceita breakpoint ou media query: [Ionic — ion-split-pane](https://ionicframework.com/docs/api/split-pane). O drawer deve seguir a estrutura documentada em [Ionic — ion-menu](https://ionicframework.com/docs/api/menu).

Exemplo estrutural:

```html
<ion-split-pane
  contentId="main-content"
  when="(min-width: 1200px)"
  class="app-split-pane"
>
  <ion-menu
    menuId="main-menu"
    contentId="main-content"
    type="overlay"
    class="app-menu"
  >
    <ion-content>
      <app-sidebar />
    </ion-content>
  </ion-menu>

  <ion-router-outlet id="main-content" />
</ion-split-pane>
```

### 8.1 Itens do menu

Grupo **Menu**:

- Dashboard;
- Pedidos, com badge de quantidade pendente;
- Cardápio, que abre Produtos, Categorias e Adicionais;
- Clientes;
- Entregas.

Grupo **Sistema**:

- Mensalidade;
- Instalar;
- Configurações.

Rodapé:

- card “Precisa de ajuda? Fale com nosso suporte”;
- ação “Sair”.

### 8.2 Cabeçalho das páginas

O cabeçalho compartilhado deve aceitar:

```ts
interface PageHeaderConfig {
  title: string;
  description: string;
  showPeriod?: boolean;
  showRefresh?: boolean;
  primaryAction?: {
    label: string;
    icon: string;
  };
}
```

Elementos:

- botão de menu somente quando o split pane estiver recolhido;
- título e descrição;
- segmento Hoje / Semana / Mês quando aplicável;
- botão Atualizar;
- notificações com badge “5”;
- data;
- banner informando situação da mensalidade.

No mobile:

- título e botão de menu na primeira linha;
- filtros em faixa rolável;
- ação principal fixa no rodapé somente em fluxos longos de formulário.

---

## 9. Design system

### 9.1 Tokens

Criar em `src/theme/tokens.scss`:

```scss
:root {
  --app-page-bg: #f5f6fb;
  --app-surface: #ffffff;
  --app-surface-soft: #faf9ff;
  --app-text: #111426;
  --app-muted: #7d8298;
  --app-line: #e7e9f3;

  --app-primary: #6d5df2;
  --app-primary-strong: #5b4de2;
  --app-primary-soft: #eeeaff;

  --app-orange: #ff4b1f;
  --app-orange-2: #ff763f;
  --app-orange-soft: #fff1ea;

  --app-success: #19b86a;
  --app-success-soft: #e8f8ef;
  --app-danger: #ef4444;
  --app-danger-soft: #feecec;
  --app-info: #3478ff;
  --app-info-soft: #edf4ff;
  --app-warning: #f6a500;
  --app-warning-soft: #fff5df;

  --app-sidebar: #062f6b;
  --app-sidebar-strong: #021b3d;
  --app-sidebar-text: #eaf2ff;
  --app-sidebar-muted: #a9bddc;

  --app-shadow: 0 28px 70px rgb(20 25 50 / 12%);
  --app-shadow-soft: 0 14px 34px rgb(40 44 80 / 7%);

  --app-radius-xl: 28px;
  --app-radius-lg: 22px;
  --app-radius-md: 16px;
  --app-sidebar-width: 244px;
}
```

Mapear também as variáveis Ionic:

```scss
:root {
  --ion-font-family: 'Plus Jakarta Sans', system-ui, sans-serif;
  --ion-background-color: var(--app-page-bg);
  --ion-text-color: var(--app-text);
  --ion-color-primary: #6d5df2;
  --ion-color-primary-rgb: 109, 93, 242;
  --ion-color-primary-contrast: #ffffff;
  --ion-color-primary-contrast-rgb: 255, 255, 255;
  --ion-color-primary-shade: #6052d5;
  --ion-color-primary-tint: #7c6df3;
}
```

### 9.2 Tipografia

- Fonte: Plus Jakarta Sans.
- Título de página: 28–32 px desktop; 23–26 px mobile; peso 700.
- Título de seção: 18–20 px; peso 700.
- Métrica: 26–34 px; peso 700.
- Corpo: 14–16 px.
- Legenda: 12–13 px.
- Altura de linha mínima: 1.4 para textos corridos.

### 9.3 Espaçamento

Usar escala de 4 px:

```text
4, 8, 12, 16, 20, 24, 32, 40, 48
```

- Padding da área principal: 30 px no desktop, 18 px no tablet, 14 px no mobile.
- Espaço entre cards: 18–24 px.
- Padding interno de cards: 20–24 px; 16 px no mobile.

### 9.4 Componentes visuais

- Card padrão: fundo branco, raio 22 px, borda opcional `#e7e9f3`, sombra suave.
- Botão primário: gradiente laranja, texto branco, altura mínima 44 px.
- Botão secundário: fundo branco, borda cinza/roxa, texto escuro.
- Botão destrutivo: vermelho somente para ações realmente destrutivas.
- Badges de status devem combinar cor e texto; não comunicar status apenas por cor.
- Ícones: Ionicons; manter rótulo visível quando o significado não for óbvio.

### 9.5 Breakpoints

| Faixa | Comportamento |
|---|---|
| `< 576 px` | Uma coluna; tabelas viram cards; botões principais ocupam largura disponível |
| `576–767 px` | Uma ou duas colunas conforme conteúdo |
| `768–991 px` | Duas colunas; filtros roláveis |
| `992–1199 px` | Conteúdo amplo, menu ainda em overlay |
| `≥ 1200 px` | Menu persistente e grids completos |

---

## 10. Componentes compartilhados

### `MetricCardComponent`

Entradas:

```ts
type MetricTone = 'primary' | 'orange' | 'success' | 'info' | 'warning';

interface MetricCardData {
  label: string;
  value: string | number;
  helper?: string;
  trend?: number;
  icon: string;
  tone: MetricTone;
}
```

Exibir skeleton durante atualização e texto acessível para tendência.

### `StatusChipComponent`

Mapear domínio para aparência, nunca espalhar classes de cor pelas páginas:

```ts
type SemanticStatus =
  | 'new'
  | 'pending'
  | 'preparing'
  | 'ready'
  | 'on-route'
  | 'delivered'
  | 'active'
  | 'inactive'
  | 'paused'
  | 'late'
  | 'paid'
  | 'cancelled';
```

### `ResponsiveListComponent`

- Desktop: cabeçalho e linhas tabulares.
- Mobile: cards com pares rótulo/valor.
- Não usar scroll horizontal como única solução.
- Ações devem ficar visíveis no final do card.

### `EmptyStateComponent`

Propriedades:

- ícone ou ilustração leve;
- título;
- descrição;
- ação primária opcional;
- ação secundária opcional.

### `ConfirmActionService`

Abrir `ion-alert` com:

- título específico;
- item afetado;
- consequência;
- cancelar como opção inicial segura;
- confirmação com papel destrutivo.

### Outros componentes

- `PageHeaderComponent`;
- `SubscriptionBannerComponent`;
- `SearchFilterBarComponent`;
- `OrderCardComponent`;
- `ProductCardComponent`;
- `ScheduleDayEditorComponent`;
- `ImageUploadFieldComponent`;
- `FormErrorComponent`;
- `LoadingSkeletonComponent`.

---

## 11. Modelos de domínio

Criar interfaces sem `any`.

```ts
export type Id = string;

export interface Store {
  id: Id;
  name: string;
  slug: string;
  open: boolean;
  acceptingOrders: boolean;
  whatsapp: string;
  cnpj: string;
  address: Address;
  social: SocialLinks;
  bio: string;
  logoUrl: string | null;
  bannerUrl: string | null;
  averageDeliveryMinutes: number;
}

export interface Address {
  zipCode: string;
  street: string;
  number: string;
  complement: string;
  neighborhood: string;
  city: string;
  state: string;
}

export interface SocialLinks {
  instagram: string;
  facebook: string;
  tiktok: string;
  website: string;
}

export type OrderStatus =
  | 'new'
  | 'preparing'
  | 'ready'
  | 'onRoute'
  | 'completed'
  | 'cancelled';

export interface Order {
  id: Id;
  number: number;
  customerId: Id;
  customerName: string;
  phone: string;
  deliveryAddress: string;
  service: 'delivery' | 'pickup';
  paymentMethod: 'pix' | 'cash' | 'credit' | 'debit';
  paymentTiming: 'paid' | 'onDelivery';
  items: OrderItem[];
  deliveryFee: number;
  discount: number;
  total: number;
  status: OrderStatus;
  createdAt: string;
  deliveryPersonId?: Id;
  expectedAt?: string;
}

export interface OrderItem {
  id: Id;
  productId: Id;
  name: string;
  quantity: number;
  unitPrice: number;
  notes?: string;
  options: SelectedOption[];
}

export type ProductSaleMode = 'single' | 'size' | 'weight' | 'variable';

export interface Product {
  id: Id;
  name: string;
  description: string;
  categoryId: Id;
  imageUrl: string | null;
  basePrice: number;
  active: boolean;
  saleMode: ProductSaleMode;
  sizes: ProductSize[];
  weight?: {
    pricePerKg: number;
    minimumKg: number;
  };
  variablePrice?: {
    minimumPrice: number;
    note: string;
  };
  optionGroups: ProductOptionGroup[];
  sortOrder: number;
}

export interface ProductSize {
  id: Id;
  name: string;
  price: number;
}

export interface ProductOptionGroup {
  id: Id;
  name: string;
  selection: 'single' | 'multiple';
  required: boolean;
  min: number;
  max: number;
  items: AddOnItem[];
}

export interface Category {
  id: Id;
  name: string;
  description: string;
  active: boolean;
  visibility: 'always' | 'hidden';
  sortOrder: number;
}

export interface AddOnItem {
  id: Id;
  name: string;
  description?: string;
  group: string;
  price: number;
  active: boolean;
}

export interface Customer {
  id: Id;
  name: string;
  phone: string;
  email: string;
  ordersCount: number;
  totalSpent: number;
  lastOrderAt: string;
  active: boolean;
}

export type DeliveryStatus =
  | 'awaitingPickup'
  | 'onRoute'
  | 'delivered'
  | 'late';

export interface Delivery {
  id: Id;
  orderId: Id;
  orderNumber: number;
  customerName: string;
  deliveryPersonId: Id | null;
  deliveryPersonName: string | null;
  address: string;
  status: DeliveryStatus;
  expectedAt: string;
  fee: number;
}

export interface Review {
  id: Id;
  customerName: string;
  rating: 1 | 2 | 3 | 4 | 5;
  comment: string;
  createdAt: string;
  replied: boolean;
}

export interface Neighborhood {
  id: Id;
  name: string;
  deliveryFee: number;
  minimumOrder: number;
  freeDeliveryAbove: number | null;
  active: boolean;
  notes: string;
}

export interface ScheduleShift {
  id: Id;
  start: string;
  end: string;
}

export interface ScheduleDay {
  weekday: 0 | 1 | 2 | 3 | 4 | 5 | 6;
  label: string;
  open: boolean;
  shifts: ScheduleShift[];
}

export interface SubscriptionInvoice {
  id: Id;
  periodStart: string;
  periodEnd: string;
  dueDate: string;
  amount: number;
  status: 'pending' | 'paid' | 'free';
  paidAt?: string;
}

export interface PrinterSettings {
  printerName: string;
  model: string;
  connectionType: 'bluetooth' | 'network' | 'usb';
  identifier: string;
  paperWidth: 58 | 80;
  copies: 1 | 2;
  autoCut: boolean;
  autoPrintNewOrders: boolean;
  printKitchenOrders: boolean;
  printCounterOrders: boolean;
  printCustomerReceipt: boolean;
  printLogo: boolean;
  highlightOrderNumber: boolean;
  footerText: string;
}
```

---

## 12. Persistência e dados simulados

### 12.1 Porta de persistência

```ts
export abstract class PersistencePort {
  abstract read<T>(key: string, fallback: T): T;
  abstract write<T>(key: string, value: T): void;
  abstract remove(key: string): void;
  abstract clearAppData(): void;
}
```

Implementar `LocalStorageAdapter` com:

- prefixo `brasa-dashboard:`;
- `try/catch` em parse e serialização;
- fallback seguro quando o armazenamento estiver indisponível;
- versão do schema, por exemplo `brasa-dashboard:schema-version`;
- migração ou reset controlado quando o schema mudar.

Não armazenar senha real. A sessão simulada pode usar apenas:

```ts
interface MockSession {
  authenticated: boolean;
  userName: string;
  createdAt: string;
}
```

### 12.2 Stores

Cada store deve:

- ter estado privado;
- expor signals somente leitura;
- expor `computed()` para filtros e métricas;
- persistir após mutações;
- simular latência curta, entre 250 e 600 ms, para testar loading;
- tratar erro simulado de forma controlada;
- fornecer `resetDemoData()`.

Exemplo:

```ts
@Injectable({ providedIn: 'root' })
export class ProductsStore {
  private readonly persistence = inject(PersistencePort);
  private readonly state = signal<Product[]>(
    this.persistence.read('products', PRODUCT_SEED),
  );

  readonly products = this.state.asReadonly();
  readonly activeProducts = computed(() =>
    this.state().filter((product) => product.active),
  );

  save(product: Product): void {
    this.state.update((items) => {
      const exists = items.some((item) => item.id === product.id);
      const next = exists
        ? items.map((item) => item.id === product.id ? product : item)
        : [...items, product];

      this.persistence.write('products', next);
      return next;
    });
  }
}
```

### 12.3 Dados iniciais

Popular a aplicação na primeira execução.

Produtos:

- Brasa Burger — Hambúrgueres — R$ 28,90 — ativo;
- Duplo Bacon — Hambúrgueres — R$ 36,90 — ativo;
- Batata Frita — Porções — R$ 12,90 — ativo;
- Combo Brasa — Combos — R$ 39,90 — ativo;
- Refrigerante Lata — Bebidas — R$ 6,50 — inativo;
- Onion Rings — Porções — R$ 14,90 — ativo.

Categorias:

- Burgers;
- Batatas;
- Bebidas;
- Sobremesas;
- Molhos Extras;
- Combos;
- Saladas.

Adicionais:

- Bacon extra — Extras — R$ 5,00;
- Cheddar — Extras — R$ 4,00;
- Molho especial — Molhos — R$ 2,50.

Clientes:

- Elizabeth Souza;
- Larissa Lima;
- Ana Letícia;
- João Pedro;
- Mariana Silva;
- Rafael Costa.

Pedidos novos de demonstração:

- #1233 — João Silva — Pix pago — R$ 87,90;
- #1232 — Pedro Castilho — Pix na entrega — R$ 87,90;
- #1231 — Fábio Alves — Crédito na entrega — R$ 87,90.

Usar datas relativas à data atual na execução, mesmo que a referência visual mostre maio/julho de 2026. Isso evita que a simulação pareça permanentemente desatualizada.

---

## 13. Requisitos por tela

### 13.1 Login — `/login`

Conteúdo:

- marca Brasa Burguer;
- título “Entrar no painel”;
- subtítulo “Acesse sua conta Brasa Burguer”;
- e-mail;
- senha;
- botão “Entrar”.

Comportamento:

- validar e-mail;
- senha com mínimo de 6 caracteres;
- botão desabilitado enquanto o formulário for inválido ou estiver enviando;
- alternar visibilidade da senha;
- aceitar qualquer e-mail válido e senha com 6+ caracteres;
- mostrar loading breve;
- salvar sessão simulada;
- redirecionar para `/app/dashboard`;
- ao voltar para `/login` autenticado, redirecionar para o dashboard;
- mensagem de erro junto ao campo e resumo acessível quando necessário.

Credenciais sugeridas no README:

```text
E-mail: admin@brasaburguer.com.br
Senha: 123456
```

### 13.2 Dashboard — `/app/dashboard`

Título: “Visão geral da loja hoje”.

Métricas:

- Pedidos hoje: 9;
- Faturamento: R$ 246,90;
- Ticket médio: R$ 27,43;
- Pedidos em andamento: 3.

Seções:

- últimos pedidos;
- formas de pagamento;
- atalhos rápidos;
- resumo por serviço: Delivery e Retirada.

Comportamento:

- Hoje / Semana / Mês recalcula métricas com seed específico;
- Atualizar exibe skeleton e atualiza “última atualização”;
- clicar em pedido abre modal com detalhes;
- “Ver todos os pedidos” navega para `/app/pedidos`;
- atalhos navegam para Cardápio, Entregas e Clientes;
- cards de métricas devem aceitar navegação por teclado;
- gráfico não é obrigatório; se adicionado, precisa ter resumo textual acessível.

### 13.3 Pedidos — `/app/pedidos`

Manter a ideia do fluxo visual de pedidos, mas integrá-lo ao design system.

Colunas/status:

1. Novos pedidos;
2. Em preparação;
3. Pronto para retirada;
4. Em entrega;
5. Concluído.

Cada card:

- número;
- cliente;
- contato;
- endereço ou retirada;
- pagamento;
- itens;
- taxa;
- total;
- horário;
- ação contextual.

Transições:

```text
Novo → Aceitar pedido → Em preparação
Em preparação → Marcar como pronto → Pronto
Pronto → Saiu para entrega → Em entrega
Em entrega → Marcar como entregue → Concluído
```

Regras:

- pedido cancelado não pode avançar;
- pedido concluído é somente leitura;
- pedir confirmação antes de cancelar;
- registrar horário da transição;
- atualizar badge do menu e métricas do dashboard;
- mostrar toast após cada mudança;
- persistir estado após recarregar a página.

Responsividade:

- desktop: painel de novos pedidos em destaque e quadro de acompanhamento;
- tablet: colunas com largura mínima e rolagem horizontal controlada;
- mobile: `ion-segment` para selecionar um status e mostrar somente sua lista;
- nunca reduzir card a ponto de cortar endereço, pagamento ou total.

### 13.4 Produtos — `/app/cardapio/produtos`

Abas/segmentos:

- Categorias;
- Produtos;
- Adicionais.

Métricas:

- Total de produtos;
- Ativos;
- Inativos;
- Categorias.

Filtros:

- busca por nome/descrição;
- categoria;
- status;
- limpar filtros;
- contador de resultados.

Lista:

- imagem;
- nome;
- descrição;
- categoria;
- preço;
- status;
- modo de venda;
- editar;
- excluir.

Comportamentos:

- alternar status sem abrir editor;
- criar e editar em `ion-modal`;
- excluir com confirmação;
- paginação simulada ou carregamento incremental;
- estado vazio específico quando não há produtos;
- estado “nenhum resultado” quando o filtro não encontra dados.

#### Editor de produto

Seção “Informações do produto”:

- nome obrigatório, até 80 caracteres;
- categoria obrigatória;
- descrição até 220 caracteres;
- imagem opcional com preview;
- preço base maior ou igual a zero;
- ativo/inativo.

Seção “Forma de venda”:

- Produto único;
- Por tamanho;
- Por peso;
- Preço variável.

Por tamanho:

- lista dinâmica de nome e preço;
- mínimo de um tamanho;
- adicionar e remover.

Por peso:

- preço por kg;
- peso mínimo em kg.

Preço variável:

- preço mínimo;
- observação para atendimento.

Grupos de opções:

- nome;
- seleção única ou múltipla;
- obrigatório;
- mínimo;
- máximo;
- itens com nome e preço adicional;
- adicionar/remover grupo;
- adicionar/remover item.

Validações:

- `max >= min`;
- grupo precisa de nome;
- grupo precisa de ao menos um item;
- item precisa de nome;
- seleção única usa `max = 1`;
- grupo obrigatório usa `min >= 1`;
- impedir salvar dados inválidos e levar foco ao primeiro erro.

Seeds de configuração:

- Brasa Burger: produto único e grupo “Adicionais”, múltiplo, máximo 3;
- Duplo Bacon: por tamanho e grupo obrigatório “Ponto da carne”;
- Batata Frita: por tamanho e grupo “Molhos”;
- Combo Brasa: grupo obrigatório “Escolha a bebida”;
- Refrigerante Lata: grupo obrigatório “Sabor”;
- Onion Rings: por tamanho, sem grupos.

### 13.5 Categorias — `/app/cardapio/categorias`

Lista:

- ordem;
- categoria;
- quantidade de itens;
- status;
- exibição;
- ações.

Editor:

- nome obrigatório;
- descrição opcional;
- status;
- visível ou não no app;
- posição.

Comportamentos:

- reordenar com `ion-reorder-group`;
- também fornecer botões “Mover para cima/baixo” para acessibilidade;
- impedir categorias com nome duplicado, ignorando maiúsculas e espaços;
- ao inativar uma categoria com produtos ativos, avisar a consequência;
- excluir somente categoria vazia ou solicitar transferência de seus produtos.

### 13.6 Adicionais — `/app/cardapio/adicionais`

Lista:

- adicional;
- descrição;
- grupo;
- preço;
- status;
- ações.

Formulário:

- nome obrigatório;
- descrição;
- grupo obrigatório;
- preço;
- status.

Comportamentos:

- criar, editar, duplicar e excluir;
- filtrar por grupo/status;
- mostrar onde o adicional está sendo usado antes de excluir;
- atualizar grupos dos produtos quando necessário.

### 13.7 Clientes — `/app/clientes`

Métricas:

- Total de clientes: 125;
- Clientes ativos: 87;
- Clientes recorrentes: 62;
- Ticket médio: R$ 48,60.

Filtros:

- nome, telefone ou e-mail;
- status;
- ordenação por mais pedidos, maior gasto, nome ou último pedido.

Lista:

- avatar com iniciais;
- cliente;
- contato;
- quantidade de pedidos;
- total gasto;
- último pedido;
- status;
- ações.

Comportamento:

- abrir detalhes em modal;
- exibir histórico resumido;
- ação de copiar telefone/e-mail;
- exportar CSV gerado no navegador;
- paginação;
- dados pessoais devem ter labels claros e não aparecer em logs.

### 13.8 Entregas — `/app/entregas`

Métricas:

- Entregas do dia: 18;
- Em rota: 6;
- Entregues: 10;
- Tempo médio: 32 min.

Filtros:

- busca por pedido, cliente ou entregador;
- status;
- entregador.

Lista:

- pedido;
- cliente;
- entregador;
- endereço;
- status;
- previsão;
- taxa;
- ações.

Comportamentos:

- atribuir/trocar entregador;
- marcar coleta;
- marcar saída;
- concluir entrega;
- sinalizar atraso automaticamente se previsão passar e status não for entregue;
- toast e persistência;
- botão “Ver rota” pode abrir URL de mapa com endereço codificado, sem exigir API.

### 13.9 Avaliações — `/app/avaliacoes`

A referência contém um placeholder. Melhorar para uma tela preparada para dados.

Quando não houver avaliações:

- ícone de estrela;
- “Ainda não há avaliações”;
- explicação curta;
- ação “Atualizar”.

Quando houver seed opcional:

- média geral;
- distribuição de 1 a 5 estrelas;
- lista com cliente, nota, data e comentário;
- filtro por nota;
- responder/marcar como respondida;
- não permitir editar texto do cliente.

### 13.10 Mensalidade — `/app/mensalidade`

Topo:

- confirmação “Sua mensalidade está em dia, obrigado!”;
- próxima data de vencimento;
- plano atual;
- valor mensal.

Tabela/lista:

- período;
- vencimento;
- valor;
- status;
- data de pagamento;
- ação.

Comportamento:

- “Pagar” abre modal de simulação;
- escolher Pix ou cartão fictício;
- nunca solicitar ou armazenar dados reais de cartão;
- confirmar pagamento muda status para pago;
- atualizar o banner global;
- disponibilizar recibo textual para download.

### 13.11 Instalar — `/app/instalar`

Conteúdo:

- “Instalar aplicativo”;
- benefício de acesso rápido;
- compatibilidade;
- instruções específicas por plataforma;
- botão “Instalar agora”.

Comportamento:

- capturar `beforeinstallprompt` em um serviço;
- habilitar botão apenas quando o prompt estiver disponível;
- se já estiver instalado, mostrar “Aplicativo instalado”;
- no Safari/iOS, mostrar instruções “Compartilhar → Adicionar à Tela de Início”;
- se o browser não suportar, explicar sem exibir erro técnico.

PWA exige pelo menos manifest e service worker: [Capacitor — Building Progressive Web Apps](https://capacitorjs.com/docs/web/progressive-web-apps).

### 13.12 Configurações — shell comum

As telas abaixo compartilham:

- título geral de Configurações;
- abas horizontais: Horários, Informações, Impressão, Bio, Bairros;
- rota própria por aba;
- alterações não salvas protegidas por `CanDeactivate`;
- botões Cancelar e Salvar;
- feedback de salvamento;
- segmentos roláveis no mobile.

#### Horários — `/app/configuracoes/horarios`

Para cada dia:

- aberto/fechado;
- um ou mais turnos;
- início e fim;
- remover turno;
- adicionar turno;
- copiar horários;
- indicar “dia seguinte” quando o fechamento atravessar meia-noite.

Recursos:

- preset “Modelo restaurante”;
- copiar um dia para outros dias abertos;
- resumo semanal de horas;
- cancelar restaura último estado salvo;
- salvar somente quando válido;
- fuso `America/Sao_Paulo`;
- tempo médio de entrega;
- antecedência para pedidos;
- opção de aplicar horários em feriados.

Validação de intervalos:

```ts
function minutesFromTime(value: string): number {
  const [hours, minutes] = value.split(':').map(Number);
  return hours * 60 + minutes;
}

function normalizedInterval(shift: ScheduleShift): {
  start: number;
  end: number;
} {
  const start = minutesFromTime(shift.start);
  let end = minutesFromTime(shift.end);

  if (end <= start) {
    end += 24 * 60;
  }

  return { start, end };
}

function intervalsOverlap(
  first: ScheduleShift,
  second: ScheduleShift,
): boolean {
  const a = normalizedInterval(first);
  const b = normalizedInterval(second);

  return [-1440, 0, 1440].some((offset) => {
    const shiftedStart = b.start + offset;
    const shiftedEnd = b.end + offset;
    return a.start < shiftedEnd && shiftedStart < a.end;
  });
}
```

Mensagens:

- “Informe início e fim do turno.”
- “Há turnos sobrepostos neste dia.”
- “Adicione pelo menos um turno ou marque o dia como fechado.”
- “Pelo menos um dia da semana deve estar aberto.”

#### Informações — `/app/configuracoes/informacoes`

Campos:

- nome;
- slug/link de acesso;
- WhatsApp;
- CNPJ;
- CEP;
- rua;
- número;
- complemento;
- bairro;
- cidade;
- estado;
- Instagram;
- Facebook;
- TikTok;
- site.

Comportamentos:

- máscaras visuais para WhatsApp, CNPJ e CEP sem misturar valor formatado com modelo;
- validar URL e identificadores;
- slug em minúsculas, sem espaços e com hífens;
- preview do link;
- salvar e cancelar;
- bloco de status e última atualização.

Não consultar CEP externo nesta simulação. Se criar adaptador futuro, manter fallback manual.

#### Impressão — `/app/configuracoes/impressao`

Campos:

- nome/modelo da impressora;
- Bluetooth, rede ou USB;
- endereço/identificador;
- papel 80 mm ou 58 mm;
- 1 ou 2 cópias;
- corte automático;
- impressão automática;
- cozinha;
- balcão;
- cliente;
- logo;
- número do pedido em destaque;
- rodapé.

Painéis:

- prévia do cupom;
- status da impressora;
- último teste;
- papel;
- fila.

Ações:

- testar impressão: simular loading e sucesso;
- reconectar;
- salvar como padrão;
- salvar alterações.

Não tentar acessar hardware real. Criar `PrinterPort` e uma implementação `MockPrinterAdapter` para futura substituição por plugin Capacitor.

#### Bio — `/app/configuracoes/bio`

Campos:

- texto da bio, máximo 160 caracteres;
- logo;
- banner.

Comportamentos:

- contador de caracteres;
- preview em tempo real;
- upload local com `URL.createObjectURL`;
- validar tipo e tamanho;
- logo: PNG, até 2 MB;
- banner: JPG/PNG, até 5 MB, proporção recomendada 1200 × 400;
- remover e substituir;
- revogar object URLs quando não forem mais usados.

O preview deve mostrar marca, bio, banner e botão “Ver cardápio”.

#### Bairros — `/app/configuracoes/bairros`

Lista:

- nome;
- taxa;
- pedido mínimo;
- frete grátis acima de;
- status;
- ações.

Seeds:

- Centro;
- Bela Vista;
- Jardim Paulista;
- Vila Mariana;
- Consolação;
- Liberdade.

Editor:

- nome;
- taxa de entrega;
- pedido mínimo;
- frete grátis acima de;
- atender bairro;
- observações até 200 caracteres.

Comportamentos:

- busca e filtro;
- criar, editar, pausar e excluir;
- não aceitar nome duplicado;
- valores monetários não negativos;
- resumo com bairros ativos, taxa média, pedido mínimo médio e frete grátis médio;
- métricas calculadas, não hardcoded.

---

## 14. Formulários

Padrão:

- label sempre visível;
- placeholder não substitui label;
- marcar campo obrigatório visualmente e no `aria-required`;
- mensagem específica abaixo do campo;
- erro somente após toque ou tentativa de envio;
- foco no primeiro erro ao salvar;
- desabilitar salvar durante processamento;
- preservar valores se um erro ocorrer;
- confirmar navegação quando houver alterações não salvas.

Exemplo tipado:

```ts
readonly form = new FormGroup({
  name: new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(80)],
  }),
  categoryId: new FormControl('', {
    nonNullable: true,
    validators: [Validators.required],
  }),
  description: new FormControl('', {
    nonNullable: true,
    validators: [Validators.maxLength(220)],
  }),
  basePrice: new FormControl(0, {
    nonNullable: true,
    validators: [Validators.required, Validators.min(0)],
  }),
  active: new FormControl(true, { nonNullable: true }),
});
```

Usar `FormArray` para turnos, tamanhos, grupos e opções.

---

## 15. Feedback e estados

Toda página de dados deve implementar:

1. **carregando:** skeleton com dimensão semelhante ao conteúdo;
2. **conteúdo:** lista ou formulário;
3. **vazio:** texto e próxima ação;
4. **sem resultados:** informar filtros aplicados e botão Limpar;
5. **erro:** mensagem humana e Tentar novamente;
6. **sucesso:** toast curto;
7. **offline:** banner discreto se `navigator.onLine` for falso.

Toasts sugeridos:

- “Produto salvo com sucesso.”
- “Categoria reordenada.”
- “Pedido #1233 movido para Em preparação.”
- “Horários atualizados.”
- “Bairro pausado.”
- “Alteração desfeita.”

Nunca exibir `Error`, stack trace ou JSON bruto ao usuário.

---

## 16. Acessibilidade

Atender pelo menos WCAG 2.1 AA:

- contraste mínimo adequado;
- foco visível;
- navegação completa por teclado;
- ordem de tabulação coerente;
- `aria-label` em botões só de ícone;
- `aria-current="page"` na rota ativa;
- `aria-live="polite"` para confirmações e alterações de status;
- `role="status"` em loading não bloqueante;
- headings em ordem lógica;
- modal deve prender foco e devolver foco ao acionador;
- ícones decorativos com `aria-hidden="true"`;
- não usar apenas vermelho/verde para diferenciar status;
- áreas clicáveis de no mínimo 44 px;
- respeitar `prefers-reduced-motion`;
- imagens com texto alternativo;
- tabelas desktop com cabeçalhos semanticamente associados.

No quadro de pedidos, oferecer ações por botão. Drag and drop não pode ser o único meio de alterar status.

---

## 17. Responsividade detalhada

### Desktop

- menu lateral fixo;
- quatro métricas por linha;
- dashboard em grid de 12 colunas;
- tabela completa;
- editor de produto em modal largo, máximo 960 px;
- horários em cards por dia.

### Tablet

- menu overlay;
- duas métricas por linha;
- filtros podem quebrar em duas linhas;
- quadro de pedidos com rolagem por colunas;
- modal ocupa aproximadamente 90% da largura.

### Mobile

- uma métrica por linha, ou duas apenas quando valores couberem;
- cabeçalho compacto;
- lista em cards;
- filtros em modal ou faixa rolável;
- botões “Salvar” e “Cancelar” facilmente alcançáveis;
- `ion-modal` com breakpoint/sheet apenas em fluxos curtos;
- editor de produto longo deve abrir em tela inteira;
- segmentos não devem cortar rótulos sem oferecer rolagem.

Testar em pelo menos:

- 360 × 800;
- 390 × 844;
- 768 × 1024;
- 1024 × 768;
- 1366 × 768;
- 1440 × 900.

---

## 18. PWA e execução local

Adicionar PWA:

```bash
npx ng add @angular/pwa@20.3.26
```

Configurar:

- nome: Brasa Burguer — Painel;
- nome curto: Brasa Painel;
- `theme_color`: `#062f6b`;
- `background_color`: `#f5f6fb`;
- ícones adequados;
- estratégia segura para assets;
- aviso quando houver versão nova.

Service worker só funciona em build de produção servido por HTTP:

```bash
npm run build
npx http-server dist/brasa-burguer-dashboard/browser -p 8080
```

No README, explicar:

```bash
npm install
npm start
```

e:

```bash
npx ionic serve
```

Se Capacitor for configurado:

```bash
npx cap add android
npx cap sync
npx cap open android
```

Build nativo é opcional e não faz parte da aceitação mínima.

---

## 19. Testes

Usar a infraestrutura de testes compatível com o Angular CLI 20 criado no projeto. Não trocar o test runner sem necessidade.

### Testes unitários obrigatórios

- cálculo das métricas;
- filtro de produtos;
- validação de grupos de opções;
- transições válidas e inválidas de pedido;
- normalização de turno que atravessa meia-noite;
- detecção de sobreposição de horários;
- cálculo das médias de bairros;
- persistência e recuperação do estado;
- auth guard;
- formatação BRL.

### Testes de componentes

- login inválido e válido;
- modal de produto;
- confirmação de exclusão;
- mudança de status de pedido;
- estado vazio de avaliações;
- salvamento de horários.

### Fluxos E2E prioritários

1. entrar no painel;
2. criar produto;
3. editar produto e adicionar grupo de opções;
4. mover pedido do estado novo até concluído;
5. configurar turnos atravessando meia-noite;
6. criar e pausar bairro;
7. recarregar e confirmar persistência;
8. sair e confirmar bloqueio das rotas protegidas.

Ionic recomenda manter testes unitários e de fluxo como em uma aplicação Angular: [Ionic Angular — Testing](https://ionicframework.com/docs/angular/testing).

---

## 20. Critérios de qualidade de código

- `npm run build` sem erros.
- `npm test` sem falhas.
- Sem erros no console durante os fluxos principais.
- Sem `any` injustificado.
- Sem `subscribe()` aninhado.
- Sem lógica duplicada de status, moeda ou persistência.
- Sem componentes com centenas de linhas contendo múltiplas responsabilidades.
- Sem arquivos CSS globais usados para corrigir uma única página.
- Sem IDs de negócio baseados em índice de array.
- Sem datas tratadas como texto localizado no estado; armazenar ISO e formatar na UI.
- Sem valores monetários tratados por strings formatadas; manter `number`.
- Sem senha, token ou credencial real no repositório.
- Sem dependência de internet depois de `npm install`, exceto fonte externa se não for empacotada. Preferir empacotar a fonte ou usar fallback.

---

## 21. Plano de implementação para a IA

Executar nesta ordem:

### Fase 1 — Fundação

1. criar projeto fixado em Angular 20;
2. adicionar Ionic 8;
3. configurar standalone, tema, locale `pt-BR` e rotas;
4. criar persistência e seeds;
5. configurar PWA;
6. criar README inicial.

### Fase 2 — Design system e shell

1. adicionar tokens;
2. criar menu, topbar e banner;
3. criar componentes compartilhados;
4. validar desktop/mobile;
5. criar login e auth guard.

### Fase 3 — Operação

1. Dashboard;
2. Pedidos;
3. Entregas;
4. Clientes.

### Fase 4 — Cardápio

1. Produtos;
2. editor de produto;
3. Categorias;
4. Adicionais;
5. integração entre os domínios.

### Fase 5 — Configurações

1. Horários;
2. Informações;
3. Bio;
4. Bairros;
5. Impressão.

### Fase 6 — Sistema

1. Mensalidade;
2. Instalar;
3. Avaliações;
4. 404;
5. reset de dados de demonstração.

### Fase 7 — Qualidade

1. testes;
2. acessibilidade;
3. responsividade;
4. estados vazios/erro/loading;
5. build de produção;
6. revisão de console;
7. documentação final.

Ao concluir cada fase:

- executar build;
- testar navegação;
- verificar console;
- testar 390 px e 1366 px;
- não avançar deixando botões principais sem comportamento.

---

## 22. Checklist de aceite

### Geral

- [ ] Projeto usa Angular 20.3.x.
- [ ] Projeto usa Ionic Angular 8.8.x.
- [ ] Componentes são standalone.
- [ ] TypeScript strict está ativo.
- [ ] Projeto roda localmente.
- [ ] Build de produção funciona.
- [ ] Todas as rotas existem.
- [ ] Menu responsivo funciona.
- [ ] Rotas protegidas exigem sessão.
- [ ] Sair encerra sessão.

### Visual

- [ ] Identidade azul, laranja e roxa preservada.
- [ ] Cards, tipografia e espaçamentos são consistentes.
- [ ] Desktop mantém densidade do dashboard original.
- [ ] Mobile não corta conteúdo essencial.
- [ ] Estados têm texto e cor coerentes.
- [ ] Modais e formulários mantêm foco correto.

### Funcional

- [ ] Filtros alteram resultados.
- [ ] Atualizar mostra loading.
- [ ] CRUD de produtos funciona.
- [ ] CRUD de categorias funciona.
- [ ] CRUD de adicionais funciona.
- [ ] CRUD de bairros funciona.
- [ ] Alteração de status dos pedidos funciona.
- [ ] Atribuição/status de entregas funciona.
- [ ] Horários aceitam múltiplos turnos.
- [ ] Horários detectam sobreposição.
- [ ] Turnos após meia-noite funcionam.
- [ ] Bio e imagens possuem preview.
- [ ] Impressão é simulada.
- [ ] Mensalidade pode ser paga em modo fictício.
- [ ] Instalação PWA apresenta estado compatível com o browser.
- [ ] Dados permanecem após recarregar.
- [ ] Existe ação para restaurar dados de demonstração.

### Qualidade

- [ ] Erros de formulário são claros.
- [ ] Exclusões pedem confirmação.
- [ ] Sucessos geram toast.
- [ ] Estado vazio e sem resultados são diferentes.
- [ ] Fluxos críticos têm testes.
- [ ] Não há erros no console.
- [ ] Navegação por teclado funciona.
- [ ] Contraste e foco atendem acessibilidade.

---

## 23. Instrução final para a IA executora

Implemente o projeto completo, e não somente protótipos estáticos. Use o conteúdo e a direção visual do `dashboard(1).zip` como referência, mas converta a arquitetura para Angular 20 + Ionic de forma idiomática.

Antes de considerar uma tela concluída:

1. confira visualmente sua correspondência com a referência;
2. teste todos os botões;
3. teste o fluxo no mobile e no desktop;
4. teste estado vazio, loading e erro;
5. recarregue o navegador para conferir persistência;
6. verifique acessibilidade básica;
7. execute os testes relacionados;
8. execute o build.

Quando uma decisão não estiver explicitamente definida:

- priorize clareza;
- preserve consistência com o restante do painel;
- prefira componentes Ionic;
- evite adicionar bibliotecas;
- mantenha a simulação local;
- registre a decisão no README.

O resultado deve parecer um produto administrativo real e utilizável, não uma coleção de páginas isoladas.
