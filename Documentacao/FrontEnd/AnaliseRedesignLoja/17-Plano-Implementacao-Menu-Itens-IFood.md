# Menu de Itens Tipo Delivery - Plano de Implementacao

## Objetivo

Remodelar a lista de produtos da loja para uma experiencia mais familiar de delivery: item inteiro abre a tela do produto, o `+` do card e apenas affordance visual, nao acao direta, e a busca nao possui botao lateral de configuracoes.

## Arquivos

- `frontend/src/app/shared/components/product-card/product-card.component.*`: comportamento e visual do item do menu.
- `frontend/src/app/features/store/store-page.component.*`: remover adicao direta pelo card, manter busca sem configuracoes, ajustar lista/categorias.
- `frontend/src/app/features/store/store-page.component.spec.ts`: regressao para clique em `+` abrindo produto via evento `open`/sem adicao direta.
- `Documentacao/FrontEnd/AnaliseRedesignLoja/00-Indice.md`: registrar este plano.

## Passos

1. Escrever teste falhando para garantir que o card emite `open` ao clicar no `+` de produto simples e nao emite `add`.
2. Ajustar `ProductCardComponent`: remover output efetivo de `add/remove` no card do menu, deixar `+` como elemento visual clicavel que chama `open`.
3. Ajustar template da loja para nao passar handlers de `add/remove` nos cards; qualquer clique abre produto.
4. Ajustar SCSS do card para layout mais compacto de delivery, com imagem consistente, separadores leves, acao visual e sem sombras pesadas.
5. Revisar busca para garantir que nao existe botao de configuracoes ao lado do input.
6. Rodar teste focado e build/testes frontend quando possivel.

## Criterios

- Clicar em qualquer ponto do card, inclusive `+`, navega para `/:storePath/produto/:productId`.
- Produto simples nao adiciona direto a sacola pelo card.
- Nenhum botao de configuracoes aparece ao lado da busca.
- Lista fica compacta, escaneavel e sem sobreposicao em mobile.
