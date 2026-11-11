# Layout do catálogo de produtos no wizard — design

## Objetivo

Melhorar a leitura e a operação do catálogo lateral de produtos no wizard de
cadastro, evitando sobreposição entre imagem, nome, preço, ações e filtros em
qualquer largura de tela.

## Escopo

A mudança cobre o catálogo exibido em `store-products-page.component.html` no
wizard `/configurar-loja/produtos`. O dashboard de produtos não terá alteração
de comportamento ou layout, salvo se compartilhar estilos de forma explicitamente
escopada ao wizard.

## Comportamento

### Ação de editar

- O botão de edição usará um ícone visualmente inequívoco de lápis/caneta.
- O botão manterá `aria-label` contextualizado com o nome do produto e `title`
  para mouse/teclado.
- Ao editar, o produto será carregado no formulário como hoje e a página fará
  rolagem suave até a seção **Cadastrar produtos**.
- A rolagem usará `scroll-margin-top` ou equivalente para que o cabeçalho fixo
  do wizard não cubra o início do formulário.
- O comportamento de confirmação de alterações não salvas continuará sendo
  respeitado antes da seleção do produto.

### Nome do produto

- O nome será sempre renderizado como informação primária da linha.
- O layout permitirá quebra controlada ou truncamento seguro, sem deixá-lo sob
  o preço ou os botões.
- Produtos recém-cadastrados e produtos editados continuarão sendo renderizados
  a partir do item atualizado na lista, com fallback textual somente para dados
  realmente ausentes.

## Layout responsivo

- Em telas amplas, a linha usará uma área flexível para imagem/informações e uma
  área independente para ações.
- Em telas estreitas, a linha será organizada em duas faixas: conteúdo do produto
  na primeira e ações na segunda, ocupando toda a largura disponível.
- As ações serão compactas e não competirão com nome/preço; cada botão manterá
  alvo mínimo acessível e foco visível.
- Busca e select de categoria usarão grid responsivo: lado a lado quando houver
  espaço e empilhados quando necessário.
- O ajuste deve funcionar para desktop, tablet e mobile sem overflow horizontal.

## Ações e detalhes

- A ordem visual será: imagem, informações/preço e ações de editar, copiar,
  expandir/recolher e excluir.
- O chevron continuará controlando somente a prévia expandida do produto.
- O preview de grupos de opções, variações e forma de venda terá padding menor
  em relação ao container principal, com tipografia compacta, mantendo legibilidade
  e contraste.
- Nenhuma ação mudará sua semântica: editar abre o formulário, copiar cria um
  novo rascunho, chevron expande detalhes e lixeira exclui.

## Arquivos prováveis

- `frontend/src/app/features/store-config/products/store-products-page.component.html`
- `frontend/src/app/features/store-config/products/store-products-page.component.ts`
- `frontend/src/app/shared/state/store-products.state.ts`, somente se a rolagem
  ou sincronização de seleção exigir uma pequena API compartilhada
- `frontend/src/app/shared/styles/store-products.shared.scss`
- `frontend/src/app/features/store-config/products/store-products-page.component.spec.ts`

## Acessibilidade e compatibilidade

- Usar ícones Ionic existentes ou registrados no componente/state; não usar
  caracteres Unicode como substitutos de ícones.
- Preservar labels, títulos, foco por teclado e áreas de toque de pelo menos 44px.
- Não enfraquecer TypeScript strict nem adicionar dependências.
- Preservar o dashboard e os fluxos de cópia, exclusão, expansão e edição.

## Critérios de aceite

- O botão de editar é reconhecível como lápis/caneta.
- Clicar em editar leva o usuário automaticamente ao formulário sem ficar sob o
  cabeçalho do wizard.
- O nome aparece corretamente após cadastro e edição.
- Nenhum elemento se sobrepõe em telas amplas ou pequenas.
- Busca e filtro permanecem íntegros e organizados.
- Ações e detalhes continuam funcionando, com preview mais compacto.
- Specs direcionados, suíte frontend, build de produção e detector visual passam.
