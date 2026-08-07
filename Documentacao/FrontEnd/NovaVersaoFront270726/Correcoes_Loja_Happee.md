# Correções necessárias na loja Urbeat

## Objetivo

Corrigir os problemas de layout, responsividade, navegação e interação da loja, mantendo a integração já existente com o backend.

O arquivo `Loja.zip` deve ser utilizado somente como **referência visual e funcional**. Não copiar diretamente sua estrutura de CSS, dados estáticos ou lógica baseada em `localStorage`. A implementação final deve utilizar os componentes atuais do projeto Angular/Ionic e os dados reais fornecidos pela API.

Página de referência:

<https://www.urbeat.com.br/multisaborlanches>

---

## 1. Card dos produtos

### 1.1. Corrigir a sobreposição do controle de quantidade

Atualmente, quando um produto simples é adicionado, o botão `+` é substituído pelo controle `− 1 +`. Porém, o card continua reservando somente o espaço destinado ao botão pequeno.

O controle de quantidade ocupa aproximadamente 118 px, enquanto a coluna atual reserva somente 42 px. Isso faz com que o controle cubra o nome, a descrição ou o preço do produto.

No print analisado, parte da palavra “caseiro” ficou escondida e passou a aparentar o texto “ro1”.

#### Correção esperada

- Criar estados distintos para:
  - Produto ainda não adicionado.
  - Produto simples já adicionado.
  - Produto que exige escolha de tamanho.
  - Produto que possui grupos de opções.
  - Produto indisponível.
- Não utilizar uma coluna fixa de 42 px para todos os estados.
- Utilizar uma estrutura flexível, por exemplo:

```css
.product-card {
  display: grid;
  grid-template-columns: 96px minmax(0, 1fr) auto;
  align-items: center;
  gap: 12px;
}

.product-info {
  min-width: 0;
}
```

- Em telas pequenas, permitir que o seletor de quantidade seja posicionado abaixo das informações do produto, caso não exista espaço horizontal suficiente.
- Garantir que nome, descrição, preço e controle de quantidade nunca se sobreponham.

#### Critérios de aceite

- O controle `− 1 +` não pode cobrir nenhum texto.
- O card deve continuar funcionando com títulos e descrições longos.
- A quantidade deve permanecer legível com valores de dois ou mais dígitos.
- Testar em larguras de 320, 360, 375, 390, 412 e 768 px.

---

### 1.2. Diferenciar produtos simples e configuráveis

Produtos simples podem apresentar o botão `+` e permitir adição direta.

Produtos com tamanhos, sabores ou grupos de opções não devem aparentar que podem ser adicionados diretamente. Eles devem exibir uma indicação clara de que o usuário precisa abrir o produto.

#### Correção esperada

- Para produto simples: exibir botão `+`.
- Para produto configurável: exibir seta, botão ou texto como `Escolher opções`.
- O card inteiro pode continuar clicável, mas deve existir uma indicação visual da ação.
- Não mostrar seletor de quantidade no card para um produto que depende de uma configuração ainda não escolhida.

#### Critérios de aceite

- O usuário deve reconhecer visualmente quais produtos podem ser adicionados diretamente.
- O usuário deve reconhecer quais produtos precisam ser configurados.
- A indicação deve funcionar com toque, mouse e teclado.

---

### 1.3. Truncamento dos textos

O nome do produto está sendo cortado de maneira excessiva, como em `Hot Dog Tradici...`.

#### Correção esperada

- Permitir até duas linhas para o nome do produto no mobile.
- Permitir até duas linhas para a descrição.
- Aplicar `line-clamp` somente depois de reservar o espaço correto para texto e ações.
- Não ocultar o preço.
- Evitar cortes que tornem impossível identificar o produto.

---

## 2. Carrinho flutuante

### Problema

O botão `Ver sacola` está assumindo uma largura baseada no conteúdo e ocupa somente parte da área disponível.

Além disso, o carrinho e a navegação inferior não possuem um comportamento claramente definido entre `relative`, `sticky` ou `fixed`.

### Correção esperada

- O carrinho deve ocupar toda a largura útil do conteúdo.
- Remover margens negativas incompatíveis com a largura do componente.
- Utilizar `box-sizing: border-box`.
- Definir explicitamente se o carrinho será:
  - `sticky`, acompanhando o final da tela dentro do conteúdo; ou
  - `fixed`, permanecendo sempre visível.
- Caso seja `fixed`, adicionar espaço inferior suficiente na listagem para que o último produto não fique escondido.
- Considerar a área segura do dispositivo:

```css
padding-bottom: env(safe-area-inset-bottom);
```

#### Exemplo de base

```css
.floating-cart {
  width: 100%;
  box-sizing: border-box;
}
```

#### Critérios de aceite

- O carrinho não pode ficar com metade da largura da tela.
- O último produto deve permanecer totalmente acessível.
- O carrinho não pode se sobrepor à navegação inferior.
- O layout deve funcionar no navegador e quando instalado como PWA.

---

## 3. Responsividade

### Problema

O layout mobile está sendo tratado como uma versão comprimida do desktop. O breakpoint existente não cobre adequadamente diferentes larguras de celulares e mantém a mesma coluna de ação de 42 px.

### Correção esperada

- Criar uma abordagem `mobile first`.
- Definir os componentes considerando primeiro telas entre 320 e 430 px.
- Não depender apenas de um breakpoint em 400 px.
- Usar `minmax(0, 1fr)`, `auto`, `flex-wrap` ou áreas de grid para acomodar o conteúdo.
- Centralizar o conteúdo em telas grandes.
- Manter uma largura máxima no desktop sem deixar a loja colada à esquerda.
- Garantir que a barra horizontal de categorias permaneça dentro dos limites da loja.

#### Critérios de aceite

- No desktop, a loja deve ficar centralizada.
- Não deve existir rolagem horizontal na página inteira.
- Somente a barra de categorias pode ter rolagem horizontal.
- Botões e controles devem possuir área de toque mínima de 44 × 44 px.

---

## 4. Categorias, busca e navegação

### 4.1. Preservar o estado selecionado

Ao abrir um produto e voltar para a loja, a categoria e a busca devem retornar exatamente ao estado anterior.

#### Correção esperada

- Manter categoria, termo de busca e posição da rolagem em uma única fonte de estado.
- Não permitir que componentes diferentes mantenham versões independentes do filtro.
- Persistir o estado por serviço, store ou parâmetros de rota.
- Restaurar a posição da listagem ao voltar da página de detalhes.

#### Critérios de aceite

- Selecionar `Todos`, abrir um produto e voltar deve manter `Todos`.
- Selecionar uma categoria, abrir um produto e voltar deve manter a mesma categoria.
- O campo de busca deve manter seu conteúdo ao voltar.

---

### 4.2. Criar estado de lista vazia

Atualmente, uma pesquisa sem correspondência pode deixar a tela completamente vazia.

#### Correção esperada

Exibir um estado com:

- Mensagem `Nenhum produto encontrado`.
- Explicação curta.
- Botão `Limpar busca`.
- Botão `Ver todos os produtos`, quando houver uma categoria selecionada.

---

### 4.3. Ordenação das categorias

- Respeitar o campo de ordem definido no cadastro das categorias.
- Ordenar por esse campo antes de renderizar a barra e as seções do cardápio.
- Não utilizar a ordem de retorno da API como regra implícita.
- Definir um desempate estável, como nome ou identificador.
- Categorias inativas ou sem produtos não devem aparecer, conforme a regra de negócio definida.

---

## 5. Página de detalhes do produto

### 5.1. Formas de venda

A página deve interpretar corretamente a forma de venda cadastrada no produto.

Possibilidades previstas:

- Produto único.
- Venda por tamanho.
- Venda por peso.
- Venda por quantidade.
- Outras formas já suportadas pelo backend.

#### Regras

- Produto único não deve exigir seleção de tamanho.
- Produto por tamanho deve exigir uma opção antes de permitir adicionar.
- O preço inicial deve ser calculado conforme a forma de venda.
- Utilizar `A partir de` somente quando realmente existirem variações de preço.
- Ao editar um item do carrinho, restaurar a forma de venda e todas as escolhas anteriores.

---

### 5.2. Grupos de opções

Os grupos devem ser renderizados a partir das configurações reais do backend.

Cada grupo pode possuir:

- Nome.
- Descrição.
- Obrigatoriedade.
- Quantidade mínima.
- Quantidade máxima.
- Ordem de exibição.
- Itens.
- Preço adicional de cada item.
- Disponibilidade.

#### Correção esperada

- Utilizar controles HTML reais:
  - `radio` quando somente uma opção for permitida.
  - `checkbox` quando várias opções forem permitidas.
  - Controle de quantidade quando o mesmo item puder ser escolhido mais de uma vez.
- Não simular inputs somente com `span` e `label`.
- Permitir navegação por teclado.
- Expor nome, estado selecionado e mensagens de erro para leitores de tela.
- Impedir seleções acima da quantidade máxima.
- Impedir a adição ao carrinho enquanto um grupo obrigatório não estiver válido.
- Ordenar grupos e itens pelo indicador de ordem do backend.

#### Texto das regras

O frontend deve transformar os valores do backend em textos compreensíveis:

- Obrigatório, uma opção: `Escolha 1 opção`.
- Obrigatório, várias opções: `Escolha de 1 a 3 opções`.
- Opcional, máximo de uma: `Opcional — escolha até 1 opção`.
- Opcional, várias opções: `Opcional — escolha até 3 opções`.

Não exibir valores padrões incoerentes, como `escolha até 50 opções`, quando o grupo possui somente três itens.

#### Critérios de aceite

- O total deve ser recalculado ao selecionar ou remover opções.
- Os limites mínimo e máximo devem ser respeitados.
- Mensagens de validação devem aparecer próximas ao grupo.
- Ao editar o produto no carrinho, todas as opções devem ser restauradas.
- Opções indisponíveis não podem ser selecionadas.

---

## 6. Acessibilidade

### Correções necessárias

- Todos os botões devem possuir nome acessível.
- Cards clicáveis devem funcionar com `Enter` e `Espaço`.
- Não utilizar somente cor para indicar seleção.
- Controles de quantidade devem anunciar o produto e a quantidade atual.
- Inputs devem possuir `label` associado.
- Adicionar foco visível para navegação por teclado.
- Garantir contraste suficiente entre texto, fundo e bordas.
- Registrar corretamente os ícones utilizados pelo Ionicons.

### Exemplo para quantidade

```html
<button aria-label="Diminuir quantidade de Pudim de leite">−</button>
<span aria-live="polite">1</span>
<button aria-label="Aumentar quantidade de Pudim de leite">+</button>
```

---

## 7. Tipografia

### Objetivo

Criar uma hierarquia tipográfica clara, consistente e responsiva. O usuário deve conseguir diferenciar rapidamente nome da loja, categorias, produtos, descrições, preços, regras dos grupos e ações.

### 7.1. Família tipográfica

- Utilizar uma única família principal em toda a loja.
- Manter uma pilha de fontes de segurança para evitar mudanças bruscas durante o carregamento.
- Caso seja mantida a fonte Inter, utilizar:

```css
font-family: "Inter", system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
```

- Carregar somente os pesos realmente utilizados, preferencialmente 400, 500, 600 e 700.
- Evitar fontes diferentes em componentes isolados.
- Não utilizar ícones ou caracteres especiais como substitutos de texto.

---

### 7.2. Escala tipográfica

Criar uma escala centralizada em variáveis, evitando tamanhos definidos separadamente em cada componente.

#### Sugestão de base

```css
:root {
  --font-size-xs: 0.75rem;    /* 12 px */
  --font-size-sm: 0.875rem;   /* 14 px */
  --font-size-md: 1rem;       /* 16 px */
  --font-size-lg: 1.125rem;   /* 18 px */
  --font-size-xl: 1.25rem;    /* 20 px */
  --font-size-2xl: 1.5rem;    /* 24 px */
  --font-size-3xl: 2rem;      /* 32 px */

  --line-height-tight: 1.2;
  --line-height-normal: 1.4;
  --line-height-relaxed: 1.55;
}
```

Não aplicar o mesmo tamanho de fonte globalmente a títulos, textos, preços e botões.

---

### 7.3. Hierarquia recomendada

| Elemento | Tamanho sugerido | Peso | Entrelinha |
| --- | ---: | ---: | ---: |
| Nome da loja | 28–32 px | 700 | 1.15 |
| Título da página de produto | 24–28 px | 700 | 1.2 |
| Título da categoria | 20–24 px | 700 | 1.2 |
| Nome do produto no card | 17–18 px | 600 ou 700 | 1.25 |
| Descrição do produto | 14–16 px | 400 | 1.4 |
| Preço principal | 18–20 px | 700 | 1.2 |
| Preço adicional | 14–16 px | 500 ou 600 | 1.3 |
| Nome do grupo de opções | 18–20 px | 600 ou 700 | 1.3 |
| Regra do grupo | 13–14 px | 400 ou 500 | 1.4 |
| Botões principais | 15–16 px | 600 ou 700 | 1.2 |
| Textos auxiliares | 12–14 px | 400 | 1.4 |

Os valores devem ser validados visualmente no projeto e podem variar dentro dos intervalos, mantendo a hierarquia indicada.

---

### 7.4. Tipografia responsiva

- A tipografia não deve diminuir excessivamente no celular.
- Textos essenciais, como nome, preço e ações, não devem ficar abaixo de 14 px.
- Utilizar `clamp()` nos títulos que precisam se adaptar entre mobile e desktop.

```css
.store-title {
  font-size: clamp(1.75rem, 4vw, 2rem);
}

.category-title {
  font-size: clamp(1.25rem, 3vw, 1.5rem);
}
```

- Não aumentar todos os textos proporcionalmente no desktop.
- Preservar uma medida de leitura confortável.
- Evitar linhas de texto excessivamente largas na página de detalhes.

---

### 7.5. Nomes, descrições e preços

- O nome do produto deve ter maior destaque do que a descrição.
- A descrição deve utilizar cor secundária com contraste suficiente.
- O preço deve ser facilmente localizado, sem competir com o nome.
- `A partir de` pode possuir peso menor, mas o valor deve continuar destacado.
- Não quebrar `R$` e o valor em linhas diferentes, salvo quando a largura realmente não comportar o conjunto.
- Utilizar espaços inseparáveis ou um elemento próprio para manter moeda e valor juntos.
- Aplicar formatação monetária brasileira:

```text
R$ 12,00
```

- Não utilizar tamanhos diferentes sem motivo entre produtos simples e configuráveis.

---

### 7.6. Truncamento e quebra de linha

- Permitir até duas linhas para nomes de produtos no mobile.
- Permitir até duas linhas para descrições nos cards.
- Em páginas de detalhes, mostrar o nome e a descrição completos.
- Aplicar reticências somente quando houver uma limitação real de espaço.
- Não utilizar largura fixa no texto apenas para acomodar controles posicionados incorretamente.
- Adicionar `min-width: 0` aos elementos de texto dentro de `grid` ou `flex`.
- Evitar palavras isoladas, valores quebrados e títulos cortados antes da informação relevante.

#### Exemplo

```css
.product-name {
  display: -webkit-box;
  overflow: hidden;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
  line-clamp: 2;
}
```

---

### 7.7. Contraste e legibilidade

- Textos principais devem utilizar contraste mínimo de 4,5:1 em relação ao fundo.
- Textos grandes podem utilizar o mínimo de 3:1, conforme os critérios de acessibilidade aplicáveis.
- Não utilizar cinza excessivamente claro em descrições e instruções.
- Estados desabilitados devem continuar legíveis.
- Não depender somente do peso da fonte ou da cor para comunicar obrigatoriedade, erro ou seleção.
- Verificar a legibilidade sobre fundos rosados, imagens e cards destacados.

---

### 7.8. Carregamento da fonte

- Evitar múltiplas importações da mesma fonte.
- Utilizar `font-display: swap`.
- Definir fontes de fallback com métricas semelhantes.
- Evitar que o carregamento altere significativamente o tamanho dos cards.
- Verificar se os pesos solicitados realmente estão disponíveis; caso contrário, o navegador pode simular negrito e comprometer o resultado.

---

### Critérios de aceite da tipografia

- Títulos, descrições, preços, regras e ações devem ser visualmente distinguíveis.
- Nenhum texto essencial deve ficar ilegível ou menor que o tamanho mínimo definido.
- O nome do produto deve suportar duas linhas sem invadir preço ou controles.
- Moeda e valor não devem se separar indevidamente.
- A alteração do tamanho de texto do dispositivo para 200% não pode impedir as operações principais.
- Não deve haver corte vertical de letras, acentos ou valores.
- A hierarquia deve permanecer consistente em listagem, detalhes, carrinho e edição do item.
- A fonte não deve mudar perceptivelmente após o carregamento da página.

---

## 8. Arquitetura do CSS

### Problema

O material de referência contém várias versões de CSS acumuladas, seletores repetidos e uso excessivo de `!important`. Isso torna o resultado imprevisível e faz uma correção quebrar outra tela.

### Correção esperada

- Separar estilos por componente.
- Remover regras antigas e duplicadas.
- Evitar CSS global para componentes específicos.
- Reduzir o uso de `!important`.
- Criar variáveis para:
  - Cores.
  - Tipografia.
  - Espaçamentos.
  - Bordas.
  - Sombras.
  - Larguras máximas.
- Utilizar apenas uma biblioteca principal de ícones.
- Não aplicar um único tamanho de fonte a todos os elementos.
- Não duplicar elementos estruturais, como o título da loja.

---

## 9. Contrato entre frontend e backend

O frontend não deve exibir diretamente valores técnicos ou padrões internos do backend sem interpretá-los.

### Validar os seguintes campos

- Tipo ou forma de venda do produto.
- Preço base.
- Preço mínimo.
- Ordem da categoria.
- Ordem dos grupos.
- Ordem dos itens.
- Quantidade mínima e máxima de cada grupo.
- Obrigatoriedade.
- Disponibilidade.
- Imagem.
- Produto ativo ou inativo.

### Regras

- O backend deve fornecer dados consistentes.
- O frontend deve aplicar textos e apresentação adequados.
- Valores ausentes devem possuir tratamento explícito.
- Não criar silenciosamente limites artificiais.
- Erros da API devem gerar mensagem e opção de tentar novamente.

---

## 10. Padronização dos textos

Revisar nomes e acentuação apresentados ao cliente.

Exemplos:

- `Katchup` → `Ketchup`.
- `Media` → `Média`.
- `Coca cola` → `Coca-Cola`.
- Padronizar `Hot-dog`, `Hot Dog` ou `Hot-Dog`.
- Padronizar `Búrguer`, `Burger` ou o termo escolhido pela marca.

Os dados podem vir do backend, mas o cadastro e a interface devem evitar variações não intencionais.

---

## 11. Testes obrigatórios

Executar testes antes de considerar a tarefa concluída.

### Cenários de card

- Produto simples não adicionado.
- Produto simples com quantidade 1.
- Produto simples com quantidade 10.
- Produto com nome longo.
- Produto sem descrição.
- Produto sem imagem.
- Produto indisponível.
- Produto com tamanhos.
- Produto com grupos de opções.

### Cenários de navegação

- Filtrar por categoria.
- Pesquisar dentro de uma categoria.
- Abrir um produto e voltar.
- Voltar preservando categoria, busca e rolagem.
- Pesquisa sem resultados.

### Cenários de configuração

- Grupo obrigatório sem seleção.
- Grupo obrigatório válido.
- Tentativa de ultrapassar o máximo.
- Opção com preço adicional.
- Vários grupos no mesmo produto.
- Edição de um produto já existente no carrinho.

### Dispositivos

- 320 × 568.
- 360 × 800.
- 375 × 812.
- 390 × 844.
- 412 × 915.
- Tablet.
- Desktop com 1366 px ou mais.

---

### Testes específicos de tipografia

- Nome de produto curto e longo.
- Descrição ausente, curta e longa.
- Preço abaixo e acima de R$ 100,00.
- Quantidade com um e dois dígitos.
- Zoom do navegador em 200%.
- Tamanho de fonte do sistema aumentado.
- Carregamento lento da fonte.
- Fonte externa indisponível, validando o fallback.

---

## 12. Prioridade de implementação

### Prioridade crítica

1. Corrigir a sobreposição do controle de quantidade.
2. Corrigir a largura e o posicionamento do carrinho.
3. Corrigir a validação dos tamanhos e grupos de opções.
4. Preservar filtros e busca ao navegar.
5. Implementar inputs semânticos nos grupos.

### Prioridade alta

1. Criar layout responsivo mobile first.
2. Diferenciar visualmente produtos simples e configuráveis.
3. Criar estado de lista vazia.
4. Centralizar a loja no desktop.
5. Respeitar a ordenação de categorias, grupos e itens.
6. Aplicar a escala e a hierarquia tipográfica.

### Prioridade de melhoria

1. Refatorar e organizar o CSS.
2. Remover regras duplicadas e `!important`.
3. Padronizar os textos.
4. Consolidar a biblioteca de ícones.
5. Melhorar acessibilidade e navegação por teclado.
6. Otimizar o carregamento e os pesos da fonte.

---

## 13. Definição de pronto

A atualização somente deve ser considerada concluída quando:

- Não existir sobreposição de conteúdo em nenhuma largura testada.
- Todos os produtos puderem ser adicionados ou configurados corretamente.
- Tamanhos e grupos de opções respeitarem as regras do backend.
- O total do item e do carrinho for calculado corretamente.
- Categoria, busca e rolagem forem preservadas ao voltar.
- O carrinho não esconder produtos nem a navegação.
- A loja estiver centralizada no desktop.
- A interface for utilizável por toque, mouse e teclado.
- A hierarquia tipográfica for consistente e legível em todas as telas testadas.
- Nomes, preços e descrições não invadirem controles ou outros conteúdos.
- Não existirem erros relevantes no console.
- Os cenários descritos neste documento tiverem sido testados.
