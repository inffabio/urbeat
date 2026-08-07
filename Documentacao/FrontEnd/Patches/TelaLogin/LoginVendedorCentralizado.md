# Login do Vendedor Centralizado

## Contexto

A tela `/login-vendedor` tinha uma composicao em duas colunas: formulario a esquerda e painel visual/promocional a direita. O pedido foi simplificar a tela, removendo o lado direito e centralizando o login.

## Mudanca Aplicada

- Removido o bloco visual lateral com blobs e card de metrica.
- Formulario mantido com os mesmos campos, rotas e comportamento de login.
- Login passa a ocupar um card unico centralizado no viewport.
- Fundo usa a base quente do design system (`--app-bg`) e um destaque sutil bordeaux.
- Link de recuperacao de senha saiu de estilo inline para classe dedicada.
- Botao de mostrar/ocultar senha virou um `button type="button"` com `aria-label`, mantendo suporte a teclado e foco visivel.

## Arquivos

- `frontend/src/app/features/seller-login/seller-login-page.component.html`
- `frontend/src/app/features/seller-login/seller-login-page.component.scss`

## Validacao

- `npx jest --no-coverage src/app/features/seller-login/seller-login-page.component.spec.ts`
- `npx ng build --configuration production`
- Detector Impeccable sem achados para HTML/SCSS da tela.
