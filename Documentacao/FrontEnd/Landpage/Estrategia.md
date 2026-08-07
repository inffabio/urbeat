# 📄 `INSTRUCOES_LANDING_ANGULAR_IONIC.md`

```md
# 🚀 Integração de Landing Page HTML/CSS/JS em Projeto Angular 20 + Ionic

## 🎯 Objetivo

Integrar uma landing page já existente, composta por:

- `index.html`
- arquivo CSS próprio
- arquivo JavaScript próprio para efeitos/interações
- imagens/assets

dentro de um projeto usando:

- Angular 20
- Ionic
- TypeScript
- Angular Router

A landing page será usada como página inicial do projeto, enquanto a aplicação Ionic principal ficará separada em outra rota, por exemplo `/app`.

---

# ✅ Estratégia Recomendada

A melhor estratégia é **converter a landing page em uma página Angular/Ionic dedicada**, em vez de colocar o HTML diretamente no `src/index.html`.

## Estrutura final desejada

```txt
src/
  app/
    pages/
      landing/
        landing.page.html
        landing.page.scss
        landing.page.ts
        landing-effects.ts

  assets/
    landing/
      images/
        hero.png
        demo-1.png
        demo-2.png
      landing.js // opcional, se decidir manter JS externo temporariamente

src/
  styles.scss
```

---

# ⚠️ O que NÃO fazer

Não inserir todo o conteúdo da landing page dentro de:

```txt
src/index.html
```

O arquivo `src/index.html` do Angular deve continuar sendo apenas o shell principal da aplicação.

## Exemplo errado

```html
<body>
  <app-root></app-root>

  <!-- NÃO colocar a landing inteira aqui -->
</body>
```

Isso pode causar problemas com:

- roteamento do Angular
- Ionic navigation
- scripts duplicados
- SEO
- ciclo de vida dos componentes
- manutenção do projeto

---

# 🧱 Passo 1 — Criar a página Landing

Rodar o comando:

```bash
ionic generate page pages/landing
```

Ou criar manualmente:

```txt
src/app/pages/landing/
```

Com os arquivos:

```txt
landing.page.html
landing.page.scss
landing.page.ts
landing-effects.ts
```

---

# 🧩 Passo 2 — Migrar o HTML

O arquivo original `index.html` possui esta estrutura:

```html
<!DOCTYPE html>
<html>
<head>
  ...
</head>
<body>
  ...
</body>
</html>
```

No Angular, **não se deve copiar**:

```html
<!DOCTYPE html>
<html>
<head>
<body>
</body>
</html>
```

Para o arquivo:

```txt
landing.page.html
```

deve ser copiado apenas o conteúdo que está dentro do `<body>`.

---

## Exemplo de estrutura correta

```html
<ion-content fullscreen>
  <main class="landing-page">

    <section class="hero">
      <p class="hero-badge">
        Para pequenos deliveries
      </p>

      <h1>
        Seu delivery, com&nbsp;cara de restaurante.
      </h1>

      <p class="hero-description">
        Cardápio digital, pedidos online e painel de gestão num só sistema —
        feito pra quem ainda atende cada cliente pelo nome.
      </p>

      <div class="hero-actions">
        <a class="btn btn-primary" routerLink="/register">
          Começar grátis por 14 dias
        </a>

        <a class="btn btn-secondary" href="#demo">
          Ver demonstração
        </a>
      </div>
    </section>

  </main>
</ion-content>
```

---

# 🎨 Passo 3 — Migrar o CSS

O CSS original da landing page deve ser movido para:

```txt
src/app/pages/landing/landing.page.scss
```

## Exemplo

```scss
.landing-page {
  min-height: 100vh;
  background: #fff;
  color: #111;
  font-family: Inter, sans-serif;
}

.hero {
  padding: 80px 24px;
  text-align: center;
}

.hero h1 {
  font-size: clamp(2.5rem, 6vw, 5rem);
  line-height: 1;
}
```

---

# ⚠️ Atenção com estilos globais

Se o CSS original tiver seletores como:

```css
html {
  scroll-behavior: smooth;
}

body {
  margin: 0;
  font-family: Inter, sans-serif;
}
```

Esses estilos podem afetar o projeto Ionic inteiro.

Existem duas opções.

---

## Opção A — Colocar estilos globais em `src/styles.scss`

Usar apenas para estilos que devem afetar toda a aplicação.

```scss
html {
  scroll-behavior: smooth;
}

body {
  margin: 0;
}
```

---

## Opção B — Escopar os estilos na landing page

Essa é a opção mais segura.

Em vez de:

```css
body {
  font-family: Inter, sans-serif;
}
```

usar:

```scss
.landing-page {
  font-family: Inter, sans-serif;
}
```

Em vez de:

```css
section {
  padding: 80px 0;
}
```

usar:

```scss
.landing-page section {
  padding: 80px 0;
}
```

---

# 🖼️ Passo 4 — Migrar imagens e assets

Criar a pasta:

```txt
src/assets/landing/images/
```

Colocar todas as imagens da landing page dentro dela.

Exemplo:

```txt
src/assets/landing/images/hero.png
src/assets/landing/images/cardapio.png
src/assets/landing/images/produto.png
src/assets/landing/images/carrinho.png
src/assets/landing/images/pagamento.png
src/assets/landing/images/pedido.png
```

---

## Atualizar caminhos das imagens

Se o HTML original usa:

```html
<img src="./images/hero.png">
```

alterar para:

```html
<img src="assets/landing/images/hero.png" alt="Dashboard do urbeat">
```

No Angular/Ionic, assets públicos devem ser referenciados a partir de:

```txt
assets/
```

---

# 🧠 Passo 5 — Migrar JavaScript para TypeScript

A melhor prática é converter o JavaScript da landing page para TypeScript, usando o ciclo de vida do componente Angular.

Criar o arquivo:

```txt
src/app/pages/landing/landing-effects.ts
```

---

## Exemplo de `landing-effects.ts`

```ts
export function initLandingEffects(root: HTMLElement): () => void {
  const cleanups: Array<() => void> = [];

  /**
   * FAQ accordion
   */
  const faqButtons = Array.from(
    root.querySelectorAll<HTMLButtonElement>('[data-faq-button]')
  );

  faqButtons.forEach((button) => {
    const onClick = () => {
      const item = button.closest('[data-faq-item]');
      item?.classList.toggle('active');
    };

    button.addEventListener('click', onClick);

    cleanups.push(() => {
      button.removeEventListener('click', onClick);
    });
  });

  /**
   * Exemplo de botão de scroll para demonstração
   */
  const demoLinks = Array.from(
    root.querySelectorAll<HTMLAnchorElement>('a[href="#demo"]')
  );

  demoLinks.forEach((link) => {
    const onClick = (event: Event) => {
      event.preventDefault();

      const target = root.querySelector('#demo');

      target?.scrollIntoView({
        behavior: 'smooth',
        block: 'start'
      });
    };

    link.addEventListener('click', onClick);

    cleanups.push(() => {
      link.removeEventListener('click', onClick);
    });
  });

  /**
   * Retorna uma função de limpeza.
   * Isso evita listeners duplicados quando o usuário sai e volta da página.
   */
  return () => {
    cleanups.forEach((cleanup) => cleanup());
  };
}
```

---

# 🧩 Passo 6 — Configurar `landing.page.ts`

Exemplo recomendado:

```ts
import {
  AfterViewInit,
  Component,
  ElementRef,
  NgZone,
  OnDestroy,
  ViewEncapsulation
} from '@angular/core';

import { IonicModule } from '@ionic/angular';
import { Meta, Title } from '@angular/platform-browser';
import { RouterModule } from '@angular/router';

import { initLandingEffects } from './landing-effects';

@Component({
  selector: 'app-landing',
  standalone: true,
  imports: [
    IonicModule,
    RouterModule
  ],
  templateUrl: './landing.page.html',
  styleUrls: ['./landing.page.scss'],

  /**
   * Usar None somente se o CSS da landing precisa se comportar como CSS global.
   * Se possível, manter os estilos escopados com .landing-page.
   */
  encapsulation: ViewEncapsulation.None
})
export class LandingPage implements AfterViewInit, OnDestroy {
  private destroyEffects?: () => void;

  constructor(
    private elementRef: ElementRef<HTMLElement>,
    private ngZone: NgZone,
    private title: Title,
    private meta: Meta
  ) {}

  ngAfterViewInit(): void {
    this.title.setTitle("urbeat · Seu delivery, com cara de restaurante.");

    this.meta.updateTag({
      name: 'description',
      content:
        'Cardápio digital, pedidos online e painel de gestão para pequenos deliveries.'
    });

    this.ngZone.runOutsideAngular(() => {
      this.destroyEffects = initLandingEffects(this.elementRef.nativeElement);
    });
  }

  ngOnDestroy(): void {
    this.destroyEffects?.();
  }
}
```

---

# 🛣️ Passo 7 — Configurar rotas

A landing page deve ficar na rota inicial:

```txt
/
```

A aplicação principal Ionic pode ficar em:

```txt
/app
```

Exemplo de `app.routes.ts`:

```ts
import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./pages/landing/landing.page').then(m => m.LandingPage)
  },
  {
    path: 'app',
    loadChildren: () =>
      import('./tabs/tabs.routes').then(m => m.routes)
  },
  {
    path: '**',
    redirectTo: ''
  }
];
```

---

# 🔗 Passo 8 — Ajustar links da landing

Links internos da aplicação Angular devem usar `routerLink`.

Exemplo:

```html
<a class="btn btn-primary" routerLink="/register">
  Começar grátis por 14 dias
</a>
```

Links para seções da própria landing podem usar `href`.

Exemplo:

```html
<a href="#demo">
  Ver demonstração
</a>
```

Se houver scroll suave controlado por JavaScript, tratar no `landing-effects.ts`.

---

# 🧾 Passo 9 — Metatags e título

O título da página não deve ficar manualmente dentro de `landing.page.html`.

Usar o serviço `Title` do Angular:

```ts
this.title.setTitle("urbeat · Seu delivery, com cara de restaurante.");
```

Usar o serviço `Meta` para description:

```ts
this.meta.updateTag({
  name: 'description',
  content:
    'Cardápio digital, pedidos online e painel de gestão para pequenos deliveries.'
});
```

---

# 🧱 Passo 10 — Estrutura HTML recomendada para a landing

A landing deve ser organizada com tags semânticas:

```html
<ion-content fullscreen>
  <main class="landing-page">

    <section class="hero">
      ...
    </section>

    <section class="stats">
      ...
    </section>

    <section class="features">
      ...
    </section>

    <section class="how-it-works">
      ...
    </section>

    <section class="resources">
      ...
    </section>

    <section id="demo" class="demo">
      ...
    </section>

    <section class="pricing">
      ...
    </section>

    <section class="testimonials">
      ...
    </section>

    <section class="faq">
      ...
    </section>

    <section class="final-cta">
      ...
    </section>

  </main>
</ion-content>
```

---

# 📌 Mapeamento sugerido do conteúdo da landing

Baseado no HTML original, organizar assim:

## Hero

Conteúdo:

- "Para pequenos deliveries"
- "+1.200 lojas ativas"
- título principal
- descrição
- CTA "Começar grátis por 14 dias"
- CTA "Ver demonstração"
- benefícios:
  - Sem taxa por pedido
  - Cancela quando quiser
  - Suporte por WhatsApp

---

## Estatísticas

Conteúdo:

- `1.2k+` lojas confiam no urbeat
- `38%` aumento médio em pedidos
- `5min` pra colocar menu no ar
- `0%` taxa por pedido recebido

---

## Gestão

Título:

```txt
Controle, agilidade e organização no seu dia a dia.
```

Cards:

- Pedidos em tempo real
- Status do pedido
- Gestão sem stress
- WhatsApp integrado

---

## Como funciona

Passos:

1. Cadastre seu cardápio
2. Compartilhe seu link
3. Receba os pedidos
4. Prepare e entregue

---

## Recursos

Lista:

- Cardápio digital responsivo
- Painel de pedidos completo
- Histórico e relatórios
- Impressão de pedidos
- Taxa de entrega por bairro
- Promoções e combos
- Produtos e adicionais
- Horário de funcionamento
- Clientes e endereços
- Cupom de desconto

---

## Demonstração

Etapas:

- Cardápio
- Produto
- Carrinho
- Pagamento
- Pedido

---

## Planos

Plano Básico:

- R$ 49,90/mês
- Cardápio digital ilimitado
- Pedidos online ilimitados
- Painel de gestão completo
- WhatsApp integrado
- Suporte por chat
- Taxa por pedido: 8%

Plano Premium:

- R$ 99,90/mês
- Tudo do plano Básico
- Zero taxa por pedido
- Atendimento prioritário
- Relatórios avançados
- Domínio personalizado
- Múltiplos operadores no painel

---

## Depoimentos

Depoimentos existentes:

- Rafael Borges
- Mariana Souto
- Juliana Takeshi
- Carlos Oliveira

---

## FAQ

Perguntas:

- Preciso de cartão de crédito pra começar?
- O urbeat cobra taxa por pedido?
- Funciona com Pix e Mercado Pago?
- Posso usar meu domínio próprio?
- Preciso de equipamento especial?
- E se eu quiser cancelar?

---

## CTA final

Título:

```txt
Seu cardápio merece uma vitrine bonita.
```

CTA:

```txt
Começar agora →
```

---

# 🧪 Passo 11 — Exemplo de FAQ com atributos para JavaScript

No HTML:

```html
<section class="faq">
  <div class="faq-item" data-faq-item>
    <button class="faq-question" type="button" data-faq-button>
      Preciso de cartão de crédito pra começar?
      <span>+</span>
    </button>

    <div class="faq-answer">
      <p>
        Não. Você cadastra seu cardápio, configura sua loja e testa por 14 dias
        sem fornecer dado nenhum de pagamento.
      </p>
    </div>
  </div>
</section>
```

No SCSS:

```scss
.faq-answer {
  display: none;
}

.faq-item.active .faq-answer {
  display: block;
}
```

No TypeScript, o `landing-effects.ts` controla o clique.

---

# 🧠 Alternativa temporária — Manter o JavaScript original

Se for necessário manter o arquivo JavaScript original, mover ele para:

```txt
src/assets/landing/landing.js
```

E carregar dinamicamente dentro do componente Angular.

## Exemplo

```ts
import {
  AfterViewInit,
  Component,
  ElementRef,
  OnDestroy,
  Renderer2
} from '@angular/core';

@Component({
  selector: 'app-landing',
  templateUrl: './landing.page.html',
  styleUrls: ['./landing.page.scss']
})
export class LandingPage implements AfterViewInit, OnDestroy {
  private scriptElement?: HTMLScriptElement;

  constructor(
    private renderer: Renderer2,
    private elementRef: ElementRef
  ) {}

  ngAfterViewInit(): void {
    this.scriptElement = this.renderer.createElement('script');
    this.scriptElement.src = 'assets/landing/landing.js';
    this.scriptElement.async = true;

    this.renderer.appendChild(document.body, this.scriptElement);
  }

  ngOnDestroy(): void {
    if (this.scriptElement) {
      this.renderer.removeChild(document.body, this.scriptElement);
    }
  }
}
```

## Atenção

Essa abordagem funciona, mas não é a ideal.

Problemas possíveis:

- eventos duplicados
- scripts não removidos corretamente
- dependência de `document`
- problemas em SSR/prerender
- manutenção mais difícil

Sempre que possível, converter o JavaScript para TypeScript.

---

# 🧰 Uso de Ionic na landing page

Para landing pages de marketing, não é necessário transformar tudo em componentes Ionic.

Pode usar:

```html
<ion-content fullscreen>
  <main class="landing-page">
    ...
  </main>
</ion-content>
```

Dentro da landing, usar HTML normal:

```html
<header>
<section>
<article>
<footer>
<nav>
```

Usar componentes Ionic apenas quando fizer sentido.

---

# ✅ Checklist final

Antes de finalizar, conferir:

- [ ] A landing está em `src/app/pages/landing/`
- [ ] O conteúdo do `<body>` foi movido para `landing.page.html`
- [ ] O `<!DOCTYPE html>`, `<html>`, `<head>` e `<body>` não foram copiados para o componente
- [ ] O CSS foi movido para `landing.page.scss`
- [ ] Estilos globais foram revisados
- [ ] Imagens estão em `src/assets/landing/images/`
- [ ] Caminhos das imagens usam `assets/landing/images/...`
- [ ] JavaScript foi convertido para `landing-effects.ts`
- [ ] Eventos JS possuem limpeza no `ngOnDestroy`
- [ ] A rota `/` aponta para a landing
- [ ] A rota `/app` aponta para a aplicação Ionic principal
- [ ] Links internos usam `routerLink`
- [ ] Links de seção usam `href="#id"`
- [ ] Título e descrição usam `Title` e `Meta` do Angular
- [ ] A landing funciona em mobile
- [ ] A landing não quebra estilos do Ionic

---

# 🏁 Resultado esperado

A aplicação deve ficar assim:

```txt
https://seudominio.com/        → Landing page urbeat
https://seudominio.com/app     → Aplicação Ionic principal
https://seudominio.com/login   → Login, se existir
https://seudominio.com/register → Cadastro, se existir
```

---

# ⭐ Recomendação final

Para este projeto, a estratégia recomendada é:

```txt
Converter a landing page em uma página Angular dedicada.
```

Arquivos principais:

```txt
landing.page.html      → HTML da landing
landing.page.scss      → CSS/SCSS da landing
landing.page.ts        → ciclo de vida Angular, SEO e inicialização
landing-effects.ts     → interações e efeitos convertidos de JavaScript
assets/landing/images/ → imagens da landing
```

Evitar colocar a landing page diretamente no `src/index.html`.

Isso mantém o projeto limpo, escalável, compatível com Angular/Ionic e mais fácil de manter.
```

