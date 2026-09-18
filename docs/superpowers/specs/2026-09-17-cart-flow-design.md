# Storefront Cart Flow Design

## Goal

Corrigir o fluxo da pagina de carrinho para que a lixeira confirme a remocao total e o CTA de avancar fique visivel acima do footer menu, sem misturar a sessao do cliente entre lojas.

## Approved Behavior

- O botao `X` de cada item remove somente aquele item.
- A remocao individual recalcula subtotal e total por meio dos sinais existentes do `CartService`.
- A lixeira abre um dialogo central com a pergunta `Deseja apagar todos os itens do carrinho?`.
- `Cancelar` fecha o dialogo sem alterar o carrinho.
- `Sim` limpa todos os itens, fecha o dialogo e navega para o cardapio da loja atual.
- O CTA vermelho do carrinho fica acima do footer menu, respeitando a clearance e a safe area existentes.
- O CTA fica desabilitado quando a loja esta fechada ou o pedido esta abaixo do minimo.
- Cliente sem sessao segue para o cadastro do storefront atual.
- Cliente cadastrado segue para pagamento.
- O cadastro cria a sessao segura existente e a persiste pelo mecanismo atual de cookie/token.
- A identidade do cliente nao sera duplicada por loja. Carrinho e checkout continuam vinculados a loja atual.

## Architecture

O `CartPageComponent` continua dono das acoes de limpar e navegar. O `CartService` permanece responsavel apenas pelo estado do carrinho e seus calculos reativos. O `StickyActionBarComponent` recebera uma variante visual explicita, aplicada somente no carrinho, preservando as instancias de cadastro e pagamento.

O posicionamento fixo atual sera preservado: a barra usa `--store-footer-clearance` para ficar acima do footer global. A cor primaria sera aplicada por classe/variante usando os tokens `--app-brand` e `--app-brand-dark`, sem novo valor de cor.

## Modal and Navigation

O dialogo atual sera mantido como overlay centralizado, com `role="dialog"`, `aria-modal="true"` e fechamento por `Cancelar` ou clique fora. A confirmacao chamara `cart.clear()` antes de `goToMenu()`, preservando o contexto de rota necessario para voltar a loja correta.

O CTA continuara chamando `continueCheckout()`. A regra existente permanece: cliente autenticado com endereco salvo vai para `checkout/pagamento`; caso contrario, vai para `checkout/cadastro`. O cadastro existente, ao concluir, segue para `checkout/pagamento`.

## Testing

- Verificar dialogo central, texto da pergunta e botoes `Sim`/`Cancelar`.
- Verificar que cancelar preserva itens e nao navega.
- Verificar que confirmar limpa itens e navega para o cardapio correto.
- Verificar que `X` remove somente o item clicado.
- Verificar que a instancia do carrinho usa a variante primaria/vermelha.
- Verificar que a variante padrao do `StickyActionBar` permanece inalterada para checkout.
- Executar testes focados, suite completa e build de producao.

## Out of Scope

- Criar contas ou tokens diferentes por loja.
- Alterar o contrato de autenticacao ou o cookie de refresh.
- Alterar `FooterNav`, `StoreShell` ou as rotas de checkout alem do necessario para este fluxo.
- Adicionar selecao individual de itens: no carrinho atual, `X` remove um item e a lixeira remove todos.
