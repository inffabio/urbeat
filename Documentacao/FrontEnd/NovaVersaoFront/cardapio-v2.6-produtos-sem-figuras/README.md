# Cardápio Digital — HTML/CSS

Protótipo estático em HTML e CSS baseado nas telas do app de delivery Brasa Burger.

## Páginas

- `index.html` — cardápio/home
- `produto-hamburguer.html` — detalhe e personalização do produto
- `carrinho.html` — carrinho e escolha de entrega/retirada
- `checkout.html` — cadastro/endereço
- `pagamento.html` — escolha do tipo de pagamento
- `pagar-app.html` — pagamento pelo app
- `pagar-entrega.html` — pagamento na entrega
- `confirmado.html` — pedido enviado/acompanhamento

## Como abrir

Abra `index.html` no navegador ou use um servidor local:

```bash
python -m http.server 8000
```

Depois acesse `http://localhost:8000`.

## Responsividade

O layout é mobile-first. Em telas grandes, o app fica centralizado e passa a ocupar 60% da largura disponível, com limite visual para manter proporção de aplicativo mobile.


## Atualização V1.1

- Adicionadas imagens de sabores de pizza em `assets/images/`.

## Atualização V1.2

Foram adicionadas as telas de pizza por tamanho:

- `pizza-media.html` — Pizza Média 35 cm / 6 fatias
- `pizza-grande.html` — Pizza Grande 40 cm / 10 fatias
- `pizza-gigante.html` — Pizza Gigante 45 cm / 12 fatias

A página `index.html` também foi atualizada para direcionar cada tamanho de pizza para sua respectiva tela.

## Atualização V1.3

- Categoria **Pizzas** incluída na barra de categorias da página inicial.
- Produtos comuns da página inicial apontam para `carrinho.html`.
- Cards de tamanhos de pizza têm hover em laranja claro.
- Links dos tamanhos de pizza apontam para `pizza-pequena.html`, `pizza-media.html`, `pizza-grande.html` e `pizza-gigante.html`.


## V1.5

- Removidas as seções de Borda recheada, Ingredientes extras e Observações das páginas pizza-pequena, pizza-media, pizza-grande e pizza-gigante.
- Criada a página `produto-pizza.html` com a tela de produto de pizza no padrão visual do projeto.


## Atualização V1.5

- Página `index.html` ajustada com pizzas em lista vertical no padrão enviado.
- Categoria `Pizzas` mantida na barra de menu.
- Produtos não pizza apontam para `carrinho.html`.
- Linhas de pizza abrem `pizza-pequena.html`, `pizza-media.html`, `pizza-grande.html` e `pizza-gigante.html`.
- Hover em laranja claro nos boxes de pizza.


## Atualização V1.7
- Hover laranja claro em todos os produtos do index.
- Cards de produtos não-pizza clicáveis para carrinho.html.
- Link "Voltar ao cardápio" nas páginas de produto e pizza.


## V1.7
- Carrinho atualizado com novo demonstrativo de produtos e footer com navegação por ícones.


## V2.0

- Footer global aplicado em todas as páginas com 4 itens: Cardápio, Pedidos, Carrinho e Conta.
- Ícones Bootstrap Icons utilizados no footer.


## V2.0
- Barra de categorias em formato de chips no index.
- Adicionados Açaí no copo e Empadas ao menu.
- Badges do index removidos.
- Ícones lineares aplicados nas telas de entrega/pagamento.
- Ícone Pix local em pagar-app.
- produto.html renomeado para produto-hamburguer.html.
- Removidos botões de coração/compartilhar do topo.


## V2.0
- Index com menu de categorias sem sombra e rolagem horizontal.
- Boxes de produtos do index sem sombra, mantendo hover em laranja claro.
- Categorias Açaí e Empadas adicionadas.


## V2.1
- Barra de categorias do index corrigida com rolagem horizontal forçada.
- Categorias Açaí e Empadas visíveis no menu.


## V2.2

- Corrigida a rolagem horizontal da barra de categorias com suporte a mouse, trackpad e arraste.
- Nova identidade visual usando a cor #D54A51 no lugar do laranja.
- Tipografia uniformizada em tamanho único, mantendo destaques em negrito.


## V2.3

- Box de Açaí atualizado no `index.html` com imagem real e link para `produto-acai.html`.
- Box de Empadas atualizado com imagem de empada.
- Criada a página `produto-acai.html` com opções de tamanho, frutas, cremes/caldas, crocantes, extras e observações.

- `produto-empada.html` — página de produto de empada com título, preço, descrição e observações.


## V2.5

- Recriada a página `produto-pizza.html` com seleção de 1 a 2 sabores.
- Incluída a regra: o valor da pizza será o sabor mais caro selecionado.
- Removida a página genérica `pizza.html`.


## V2.6

- Produto pizza sem imagens nos sabores e textos dos sabores em preto.
- Produto açaí sem figuras/emoji ao lado dos itens.
