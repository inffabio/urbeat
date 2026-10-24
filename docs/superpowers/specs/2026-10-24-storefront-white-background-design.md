# Storefront com fundo branco

## Objetivo

Substituir o fundo creme pelo branco em todas as páginas do storefront público, mantendo intactos os temas do dashboard, autenticação e onboarding.

## Escopo

Inclui as rotas públicas de loja, detalhes de produto, carrinho, checkout, confirmação e acompanhamento do pedido. A alteração é exclusivamente visual; não modifica contratos, estado, APIs ou persistência.

## Solução

Aplicar o fundo branco no contexto compartilhado do shell do storefront e em sua área roteada, evitando alterar o token global `--app-bg`, que também é usado por outras áreas do produto. Corrigir usos específicos de `var(--app-bg)` nas páginas do storefront quando eles mantiverem o creme visível.

Superfícies intencionalmente próprias — hero, cards, modais, banners, footer, estados de erro e componentes de ação — devem conservar suas cores atuais.

## Arquivos prováveis

- `frontend/src/app/features/store/store-shell.component.ts`
- `frontend/src/app/features/store/store-page.component.scss`
- `frontend/src/theme/global.scss` ou estilo compartilhado do shell, somente se necessário
- Specs existentes do storefront, caso seja necessário adicionar uma verificação de regressão

## Critérios de aceite

1. Nenhuma área de fundo do storefront exibe o creme.
2. O canvas e a lista de itens usam branco consistente.
3. Hero, cards, modais, footer e banners preservam seus estilos próprios.
4. Dashboard, login, cadastro e onboarding não sofrem alteração.
5. O build de produção Angular passa.

## Validação

Executar a suíte frontend relevante e `npx ng build --configuration production` a partir de `frontend/`.
