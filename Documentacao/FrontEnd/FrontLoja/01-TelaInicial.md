# Especificação Funcional e Visual — Tela Inicial de Pedidos Delivery da Urbeat

---

## ⚠️ Atualização 2026-05 — versão `01-Telainicial01.jpeg`

> Esta atualização **prevalece** sobre o que estiver descrito mais abaixo neste documento em caso de conflito visual.

### O que mudou em relação à `01-TelaIncial.jpeg`

| Elemento | Antes | Depois (atual) |
|---|---|---|
| Botão `+` laranja em cada card de produto | Tinha botão `+` à esquerda da imagem do produto que adicionava 1 unidade direto ao carrinho | **REMOVIDO.** Tocar em qualquer parte do card abre a tela `02-DetalheProduto`, onde o usuário define quantidade, observações e adiciona. |
| FAB do carrinho (canto inferior direito) | Botão flutuante circular vermelho com ícone carrinho + badge numérico | **SUBSTITUÍDO** por **pílula laranja** com texto "**Ver sacola**" + ícone carrinho + badge numérico, ocupando ~60% da largura inferior (entre o FAB do WhatsApp à esquerda e a borda direita). |
| FAB do WhatsApp (canto inferior esquerdo) | Botão circular verde | **MANTIDO** sem mudanças. |
| Visibilidade da pílula | FAB do carrinho sempre visível | **A pílula só aparece quando há ao menos 1 item no carrinho** (`@if cart.totalItems() > 0`). |

### Implementação atual (Angular)

```html
<button type="button" class="view-cart-pill" (click)="openCart()">
  <span class="pill-label">Ver sacola</span>
  <span class="pill-cart">
    <ion-icon name="cart"></ion-icon>
    <span class="pill-badge">{{ cart.totalItems() }}</span>
  </span>
</button>
```

```scss
.view-cart-pill {
  position: fixed;
  bottom: calc(var(--space-5) + env(safe-area-inset-bottom));
  right: var(--space-5);
  left: calc(var(--space-5) + 60px + var(--space-4));  /* respeita FAB WhatsApp */
  height: 56px;
  border-radius: var(--radius-full);
  background: var(--app-primary);  /* coral #f57c52 */
  color: #fff;
  display: flex; align-items: center; justify-content: space-between;
}
```

### Impacto nos comportamentos abaixo

- A seção "Tocar no botão +" (linhas ~187-191 abaixo) **não se aplica mais** — não existe mais o botão `+` por produto.
- A seção "Botão Carrinho 🛒" (linhas ~213-232, ~234-264) descreve um FAB que **não existe mais**; substituída pela pílula descrita acima.

---

## Projeto

    - Stack alvo: Angular 20 + Ionic
    - Visão do software: Existe uma empresa chamada Urbeat que controla todos os clientes vendedores que farão cadastro e cada um terá sua página de venda.
    - Objetivo: Criar a tela inicial do cliente

### Visão Geral da Tela

> Esta tela representa a página inicial de um cardápio de delivery mobile do cliente da happe

> Ela tem como principais objetivos:

    - 🍔 Apresentar a identidade visual da loja
    - 🏪 Exibir informações principais do estabelecimento
    - 🔎 Permitir busca de itens do cardápio
    - 📂 Navegar entre categorias
    - ⭐ Mostrar produtos em destaque
    - ➕ Adicionar produtos ao carrinho
    - 💬 Oferecer atalho para contato via WhatsApp
    - 🛒 Mostrar acesso rápido ao carrinho com contador de itens

> Estrutura Geral da Tela

> A tela pode ser dividida em 6 blocos principais:

    - Header com imagem/banner
    - Informações da loja
    - Campo de busca
    - Tabs de categorias
    - Lista de produtos em destaque
    - Ações flutuantes no rodapé

### Estrutura Visual por Seções

> Header / Banner Principal
    - Função Exibir uma imagem de destaque apetitosa reforçar a marca. Virá do backend
    - ocupando a largura da tela
    - Foto de logomarca do cliente como destaque visual
    - Logo circular da loja sobreposta no canto inferior direito da imagem virá do BackEnd

> Comportamento esperado
    - A imagem deve ocupar o topo da tela
    - Deve ter proporção visual de banner
    - O logo deve ficar sobreposto ao banner, com posicionamento absoluto
    - O logo deve ter destaque visual, sem prejudicar a leitura

> Requisitos técincos

    - Usar ion-img ou img com lazy loading
    - Permitir substituir a imagem por URL dinâmica vinda da API
    - Permitir logo dinâmico da loja

> Informações da Loja

    - Função
        - Exibir informações principais do estabelecimento logo abaixo do banner.
        - Campos identificados
        - Nome da loja: buscar do BackEnd
        - Categoria/subtítulo: buscar do BackEnd
        - Horário/status: buscar do BackEnd
        - Previsão de entrega: Previsão: buscar do BackEnd

    - Hierarquia visual esperada
      - Nome da loja com maior destaque
      - Categoria em texto secundário
      - Horário e previsão com estilo menor e discreto

> Campo de Busca

## Função

> Permitir que o usuário pesquise produtos do cardápio.

### Elementos identificados

- Campo de busca horizontal
- Ícone de lupa à esquerda
- Estilo com destaque em cor laranja/coral
    
### Placeholder sugerido

- Embora não esteja explicitamente legível, recomenda-se algo como:
    - Buscar no cardápio
    
### Comportamento esperado

- Filtrar produtos em tempo real
    
### Buscar por

- nome do produto
- descrição
- categoria
- Debounce recomendado para evitar excesso de processamento

### Requisitos técnicos

- Implementar com ion-searchbar ou ion-input customizado
- Debounce entre 300ms e 500ms
- Busca local inicialmente; opcional integração com API backend

## Tabs/Categorias do Cardápio 📂

### Função

- Permitir navegação horizontal entre categorias do cardápio.

- Categorias 
  - Destaques 
     > O backend devera ter um capo para destaque na tabela Categoria
  - Combos
     > O usuario cliente da urbeat definirá os combos
  - Ex. Combos, Hamburgueres, Bebidas eo que mais vier da api

### Estado visual

- Categoria selecionada: Destaques
- Item ativo em laranja/coral
- Itens inativos em cor neutra/escura
- Comportamento esperado
- Ao tocar em uma categoria:
- atualizar estado ativo
- filtrar lista de produtos exibidos
- opcionalmente rolar até a seção correspondente
- Tabs devem suportar scroll horizontal caso haja muitas categorias
- Requisitos técnicos
- Pode ser implementado com:
- ion-segment
- ou barra horizontal customizada
- Deve haver estado ativo bem definido
- Deve suportar categorias vindas de API

## Título da Seção de Produtos ⭐

> Função: Indicar qual grupo de produtos está sendo exibido.

### Elemento identificado

- Título da seção: Destaques

> Comportamento esperado: Atualizar conforme categoria selecionada
    
> Exibir de forma clara acima da lista

> Regra: Sempre refletir a categoria/tab ativa

##  Lista de Produtos

> A tela apresenta pelo menos 2 cards/list items de produtos.

### Campos identificados

- Nome: Ex. Cheese Burguer
- Descrição: Ex. Pão brioche, carne, queijo, alface, tomate e molho especial
- Preço: Ex. R$ 24,90
- Imagem do produto
- Botão de adicionar (+)

### Funcionalidade

- Exibir resumo visual do produto
- Permitir adição rápida ao carrinho

### Cada item deve conter

- 📷 Thumbnail/imagem do produto
- 🏷️ Nome do produto
- 📝 Descrição curta
- 💰 Preço
- ➕ Botão circular de adicionar

### Layout sugerido

- Conteúdo principal alinhado à esquerda
- Imagem do item alinhada à direita
- Botão “+” próximo da imagem ou sobreposto em área de fácil acesso
- Espaçamento confortável entre itens

### Comportamentos esperados

> Tocar no item pode:

- abrir página de detalhes do produto (02-DetalheProduto.jpeg)

> Tocar no botão +:

- adiciona 1 unidade ao carrinho
- atualiza badge do carrinho
- pode exibir feedback visual/toast

> Botão + deve ter área de toque adequada
> Cada item deve ser renderizado via *ngFor / @for
> Lista deve suportar dados dvinda de uma API do backend via json

### Ações Flutuantes Inferiores 

- Botão tawk.to
- Elemento identificado: Ícone do Tawk.to
- Cor verde
- Posicionado no canto inferior esquerdo
- Deve ser configurado para cada loja

> Função: Abrir conversa com a loja, Facilitar atendimento rápido.

- Comportamento esperado

  > Abrir WhatsApp com número pré-configurado

  > Fallback para link web se app não estiver instalado

- Botão Carrinho 🛒
- Elemento identificado: Ícone de carrinho
- Cor: vermelha
- Badge com quantidade: 2
- Posicionado no canto inferior direito

> Função:
    - Levar o usuário ao carrinho
    - Exibir quantidade atual de itens
    - Comportamento esperado
    - Sempre visível
    - Badge atualizado em tempo real

> Ao clicar:
    - navegar para tela do carrinho
    
> Requisitos técnicos
    - O badge deve ocultar se a quantidade for 0
        Valor deve vir de serviço global de carrinho
---

## Botão Carrinho 🛒

### Elemento identificado

- Ícone de carrinho
- cor vermelha
- Badge com quantidade:

- Posicionado no canto inferior direito

> Função:

- Levar o usuário ao carrinho
- Exibir quantidade atual de itens

> Comportamento esperado:

- Sempre visível
- Badge atualizado em tempo real

> Ao clicar:

- navegar para tela do carrinho

### Requisitos técnicos

- O badge deve ocultar se a quantidade for 0
- Valor deve vir de serviço de carrinho

---

## Especificação Visual

### Paleta de Cores

- Cores principais
  - Laranja/Coral: usado em elementos ativos e botões de ação
  - Vermelho: botão do carrinho
  - Verde: botão WhatsApp
  - Bege/Creme claro: fundo geral
  - Cinza escuro / preto: textos principais
  - Cinza médio/claro: textos secundários

<  --app-primary: #f57c52;        // laranja/coral
  --app-primary-dark: #e5673f;
  --app-accent-red: #e53935;     // carrinho
  --app-accent-green: #25d366;   // whatsapp
  --app-bg: #f7f1ea;             // fundo bege claro
  --app-surface: #ffffff;        // cards / inputs
  --app-text-primary: #222222;
  --app-text-secondary: #6b6b6b;
  --app-border-light: #ececec; />


### Tipografia

- Hierarquia recomendada
  - Nome da loja: destaque, semibold/bold
  - Título de seção: semibold
  - Nome do produto: semibold
  - Descrição e informações adicionais: regular
  - Preço: bold ou semibold para destaque
  
- Nome fonte
  - Google Fonts Nunito Sans

---

### Bordas e Formas

- Layout moderno, amigável e arredondado
- Botões flutuantes circulares
- Cards/list items com raio suave

< --radius-sm: 8px;
 --radius-md: 12px;
 --radius-lg: 16px;
 --radius-full: 999px; />

---

### Espaçamentos

<  
--space-1: 4px;
--space-2: 8px;
--space-3: 12px;
--space-4: 16px;
--space-5: 20px;
--space-6: 24px; >

## Componentização Recomendada

> para Angular 20 + Ionic, recomenda-se quebrar a tela em componentes reutilizáveis.

---

## Componentes sugeridos

- store-header.component
  > Responsável por:
- banner
- logo sobreposto

---

- store-info.component

> Responsável por:

- nome da loja
- subtítulo
- horário
- previsão de entrega

---

- menu-search.component

> Responsável por:

- campo de busca
- emissão do termo pesquisado

---

- menu-categories.component

> Responsável por:

- tabs/categorias
- controle do item ativo

---

- menu-section-title.component

> Responsável por:

- título da categoria atual

---

- menu-item-card.component

> Responsável por:

- nome
- descrição
- preço
- imagem
- botão de adicionar

---

- floating-actions.component

> Responsável por:

- botão WhatsApp
- botão carrinho com badge

---

## Regras Funcionais

### Busca

> Deve filtrar os produtos visíveis
> Busca deve considerar:

- nome
- descrição
- Se o termo estiver vazio:
- mostrar produtos da categoria selecionada

---

## Categoria ativa

- Apenas uma categoria ativa por vez

> Ao selecionar categoria:

- atualizar visual
- atualizar lista

---

## Adição ao carrinho

> Ao clicar no botão +:

- adicionar 1 item
- atualizar badge do carrinho
- permitir múltiplas adições do mesmo item

---

## Navegação ao carrinho

> Clique no botão do carrinho deve navegar para tela: (03-Cart.md)

## WhatsApp

> Clique no botão Tawk.to deve abrir uma caixa de chat
> Mensagem inicial opcional configurável

---

## Regras do Carrinho

> Mesmo sendo uma tela inicial, ela já depende de regras básicas do carrinho.

- Requisitos
- Manter estado global do carrinho
- Atualizar badge em tempo real
- Persistência opcional em:
- localStorage
- storage do Ionic
- backend

---

## Acessibilidade

> Requisitos recomendados:

- Botões com aria-label
- Contraste adequado entre texto e fundo
- Área de toque mínima de 44x44px
- Imagens com alt
- Tabs com indicação visual clara de item ativo
- Search bar com label acessível
- Exemplos
<aria-label="Buscar produtos"
aria-label="Adicionar Cheese Burguer ao carrinho"
aria-label="Abrir conversa no WhatsApp"
aria-label="Abrir carrinho" />

---

## Responsividade e Comportamento Mobile

**Como a tela é claramente mobile-first:**

### Requisitos

- Layout otimizado para smartphones
- Componentes empilhados verticalmente
- Tabs com rolagem horizontal, se necessário
- Botões flutuantes fixos acima da safe area
- Compatível com Android/iOS
- Observações Ionic
- Respeitar ion-safe-area
- Ajustar espaçamento inferior por causa dos botões flutuantes

---

## Critérios de Aceite

### Funcionais ✅

- Exibir banner principal com logo da loja
- Exibir nome, subtítulo, horário e previsão de entrega
- Exibir campo de busca funcional
- Exibir categorias navegáveis
- Destacar visualmente categoria ativa
- Exibir lista de produtos da categoria selecionada
- Exibir nome, descrição, preço, imagem e botão + para cada item
- Adicionar item ao carrinho ao clicar em +
- Atualizar badge do carrinho em tempo real
- Abrir carrinho ao tocar no botão flutuante
- Abrir Tawk.to ao tocar no botão

---

### Visuais 🎨

- Fundo em tom claro/bege
- Uso de laranja/coral nos destaques
- Botão do carrinho em vermelho
- Botão WhatsApp em verde
- Layout limpo, moderno e amigável
- Imagens dos produtos com boa visibilidade
- Espaçamentos e hierarquia visual consistentes

### Técnicos ⚙️

- Desenvolvido em Angular 20 + Ionic
- Componentização clara
- Dados preparados para integração via API
- Código reutilizável e escalável
- Compatível com Android e iOS

---

### Implementação Técnica

- Angular 20
- Preferir componentes standalone
- Usar signals ou RxJS para gerenciamento simples de estado local
- @for e @if podem ser usados se desejado

### Ionic

- ion-content
- ion-searchbar
- ion-badge
- ion-icon
- ion-fab ou botões flutuantes customizados

### Estilo

- SCSS modular
- Tokens de cor no tema global
- Componentes isolados por responsabilidade

## APIs do Backend

### 1. Carregar Loja pela URL
Usado ao abrir a página: `https://www.urbeat.com.br/{storePath}`

```http
GET /api/public/stores/by-path/{storePath}
```

**Response (campos relevantes para esta tela):**
```json
{
  "id": "guid...",
  "name": "Burguer do Rafa",
  "cuisineType": "Lanches",
  "bannerUrl": "https://images.com/banner.jpg",
  "logoUrl": "https://images.com/logo.jpg",
  "tawkToPropertyId": "abc123",
  "isOpen": true,
  "deliveryFee": 5.90,
  "minimumOrderValue": 15.00,
  "businessHours": [
    { "dayOfWeek": 0, "opensAt": "11:00", "closesAt": "23:00" }
  ],
  "averageRating": 4.5,
  "totalReviews": 42
}
```

> Os campos `bannerUrl`, `logoUrl` alimentam o banner e logo da tela.\
> `name`, `cuisineType` preenchem as informações da loja.\
> `businessHours` + `isOpen` determinam o status (aberto/fechado).\
> `tawkToPropertyId` é usado para configurar o botão de chat.

### 2. Categorias do Cardápio
```http
GET /api/public/stores/{storeId}/catalog/categories
```

**Response:**
```json
[
  {
    "id": "guid...",
    "storeId": "guid...",
    "name": "Hambúrgueres",
    "displayOrder": 1,
    "isActive": true,
    "isFeatured": false
  }
]
```

> O campo `isFeatured` indica se a categoria deve aparecer como destaque.\
> Ordenar pelo campo `displayOrder`.

### 3. Produtos do Cardápio
```http
GET /api/public/stores/{storeId}/catalog/products
```

**Response:**
```json
[
  {
    "id": "guid...",
    "storeId": "guid...",
    "categoryId": "guid...",
    "categoryName": "Hambúrgueres",
    "name": "Smash Burguer",
    "description": "Pão brioche, smash de 120g...",
    "price": 28.90,
    "imageUrl": "https://placehold.co/400x400",
    "isAvailable": true,
    "isFeatured": true,
    "displayOrder": 1
  }
]
```

> Filtrar por `categoryId` ao selecionar uma categoria.\
> Ordenar por `displayOrder` dentro de cada categoria.

### 4. Produtos em Destaque
```http
GET /api/public/stores/{storeId}/catalog/products/featured
```

**Response:** `IReadOnlyCollection<ProductResponseDto>` (mesmo formato do endpoint de produtos, filtrado por `isFeatured: true`).

### 5. Avaliações da Loja
```http
GET /api/public/stores/{storeId}/reviews
```

**Response:**
```json
[
  {
    "id": "guid...",
    "customerName": "João S.",
    "rating": 5,
    "comment": "Hambúrguer incrível!",
    "createdAtUtc": "2026-05-27T14:00:00Z"
  }
]
```

### Fluxo de Dados na Tela
1. Ao abrir a URL, chamar `GET /api/public/stores/by-path/{storePath}`
2. Com o `id` recebido, chamar em paralelo:
   - `GET /api/public/stores/{storeId}/catalog/categories`
   - `GET /api/public/stores/{storeId}/catalog/products`
   - `GET /api/public/stores/{storeId}/reviews`
3. A busca local filtra os produtos já carregados por nome/descrição/categoria