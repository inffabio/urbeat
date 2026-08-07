# 02 — Design System: Comparativo e Plano de Migração

Comparação entre o **protótipo NovaVersaoFront** e o **front atual** (`frontend/src/theme/variables.scss` + `global.scss`), com recomendação de tokens unificados.

## 1. Comparativo de tokens

### 1.1 Cor primária

| | Protótipo (novo) | Atual |
|---|---|---|
| Primária | `#D54A51` (vinho/coral) | `#f57c52` (laranja) |
| Primária escura | `#B63A41` | `#e5673f` |
| Primária suave (fundo seleção) | `#FDECEE` | `#fde7dd` |
| Tint hover | — (usa brand-soft) | `#f78963` |

> **Decisão necessária:** adotar a nova cor `#D54A51` como `--app-primary`? Recomendo **sim** para alinhar à direção de marca do protótipo, mas fazer via **um único token** para permitir troca fácil (inclusive tema por loja — ver doc 04 §6).

### 1.2 Tipografia

| | Protótipo | Atual |
|---|---|---|
| Família | **Inter** | **Nunito Sans** |
| Hierarquia | Por peso (base 15px, destaques 800) | Por tamanho + peso |

> Inter é mais "neutra/tech"; Nunito Sans é mais "amigável/arredondada". Ambas funcionam para delivery. Recomendo **manter uma decisão de marca única** e centralizá-la em `--app-font-family`.

### 1.3 Cores neutras / semânticas

| Papel | Protótipo | Atual |
|---|---|---|
| Texto principal | `#161616` | `#1a1a1a` |
| Texto secundário | `#6f6f76` | `#6b6b6b` |
| Borda | `#eadfd6` (bege quente) | `#ececec` (cinza) |
| Fundo página | `#ede9e3` / cremes | `#faf5ef` (bege) |
| Sucesso | `#119441` | `#2e7d32` |
| Erro/acento | (usa brand) | `#e53935` |
| WhatsApp | — | `#25d366` |

> As paletas são próximas. O protótipo é mais "quente" (beges). Atual já tem bom sistema semântico — **manter a estrutura semântica atual** e só reajustar os valores.

### 1.4 Raios, sombras, espaçamento

| | Protótipo | Atual |
|---|---|---|
| Raios | 9–34px (cartões grandes arredondados) | `--radius-sm..xl` 8/12/16/24 + full 999 |
| Sombras | Removidas na V2.2 (flat) | `--shadow-sm/md/lg` definidas |
| Espaçamento | ad-hoc | escala 4→40 |

> O front atual já tem **escala de design tokens** (raios, sombras, espaçamentos). O protótipo tende ao **flat**. Recomendo manter os tokens atuais e criar uma variante "flat" opcional.

## 2. Tokens unificados propostos

Sugestão de `variables.scss` (estrutura, valores a validar com marca):

```scss
:root {
  /* Marca — token único, tema opcional por loja (ver doc 04 §6) */
  --app-primary:        #D54A51;
  --app-primary-dark:   #B63A41;
  --app-primary-light:  #FDECEE;
  --app-on-primary:     #ffffff;

  /* Neutros */
  --app-text-primary:   #161616;
  --app-text-secondary: #6f6f76;
  --app-text-muted:     #9a9a9a;
  --app-bg:             #f7f2ec;
  --app-surface:        #ffffff;
  --app-border:         #eadfd6;

  /* Semânticas */
  --app-success: #119441;
  --app-danger:  #e53935;
  --app-whatsapp:#25d366;

  /* Tipografia */
  --app-font-family: 'Inter', 'Nunito Sans', system-ui, sans-serif;

  /* Forma */
  --radius-sm: 10px; --radius-md: 14px; --radius-lg: 18px; --radius-xl: 26px; --radius-full: 999px;

  /* Sombra (modo suave; “flat” = none) */
  --shadow-soft: 0 8px 22px rgba(33,20,8,.07);
  --shadow-card: 0 10px 28px rgba(33,20,8,.06);
}
```

## 3. Componentes a criar/ajustar no Angular para paridade com o protótipo

| Componente | Existe hoje? | Ação |
|---|---|---|
| `store-metrics` (3 colunas: status/tempo/mínimo) | Parcial (info na store-page) | Extrair componente reutilizável. |
| `footer-nav` global (4 abas) | **Não** | Criar `ion-tab-bar` ou componente próprio (Cardápio/Pedidos/Carrinho/Conta). |
| `category-chips` com rolagem horizontal | Sim (tabs) | Ajustar visual para chips arredondados. |
| `option-group` dirigido por dados (radio/checkbox/chips, min/max, obrigatório) | **Parcial** (variações/adicionais fixos) | **Refatorar** a tela de produto para renderizar `optionGroups` (ver doc 05). |
| `guided-builder` (montagem numerada tipo açaí) | **Não** | É o mesmo `option-group` com layout "numerado + tags". |
| `qty-stepper` (± ) | Sim | Padronizar visual. |
| `receive-cards` (Entrega/Retirada) | Sim (cart) | Ajustar visual. |
| `order-timeline` (4 passos) | Sim (tracking) | Ajustar visual/labels. |
| `discount-row` no resumo | **Não (stub)** | Ligar a cupom quando backend existir. |

## 4. Estratégia de migração (sem “big bang”)

1. **Fase tokens:** trocar valores em `variables.scss` (primária, fonte, bordas). Efeito imediato e global, baixo risco.
2. **Fase componentes compartilhados:** criar `footer-nav`, `store-metrics`, `option-group`, `guided-builder`.
3. **Fase telas:** aplicar nos fluxos store → produto → carrinho → checkout → pagamento → tracking.
4. **Fase parametrização:** ligar tokens/layout e modelos de opções ao **`CuisineType`** (tipo de comida) e à **ordem do cardápio** (Destaques/Combos) — doc 04.

> Recomenda-se **não** portar o CSS do protótipo diretamente (uso pesado de `!important`). Usar o protótipo como **referência visual** e reconstruir com os tokens/SCSS já existentes no projeto.

## 5. Acessibilidade / responsividade

- O protótipo é mobile-first e centra o app em telas grandes — compatível com Ionic.
- Uniformizar tudo em 15px pode ferir hierarquia/acessibilidade; recomendo manter **uma escala tipográfica real** (ex.: 13/15/17/22/28) em vez de forçar tamanho único.
- Garantir contraste do `#D54A51` sobre branco (AA) em textos pequenos — validar preço/labels.
