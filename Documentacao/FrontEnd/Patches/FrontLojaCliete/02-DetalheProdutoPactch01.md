# 🍔 Especificação Funcional e Técnica — Tela de Detalhe do Produto com Personalizações
## Base visual inspirada na tela antiga + evolução para nova tela com customizações dinâmicas

---

# 🎯 Objetivo

Implementar a **Tela de Detalhe do Produto** no frontend do delivery, mantendo a estética da tela mobile já existente e evoluindo a experiência para suportar as personalizações cadastradas pela loja no painel administrativo.

A nova versão da tela deve partir do modelo antigo, que possuía apenas:

- foto do produto;
- nome;
- descrição curta;
- preço;
- campo de observação;
- seletor de quantidade;
- botão `Adicionar ao carrinho`;

e evoluir para a nova versão, que além disso passa a exibir, quando existirem:

- **Sabores e Variações**;
- **Opções de escolha**;
- **Adicionais**.

Tudo isso deve ser exibido de forma elegante, clara, responsiva e compatível com a estética visual já usada na tela mobile.

---

# 🧭 Contexto funcional

A tela será usada no app/site de vendas do cliente final, após o usuário clicar em um item do cardápio.

Essa tela deve refletir exatamente o que foi cadastrado pela loja no módulo administrativo de produtos.

Ou seja, as seções exibidas no detalhe do produto dependem do cadastro feito pelo lojista:

- se o produto possui **variações**, mostrar a seção;
- se o produto possui **opções de escolha**, mostrar a seção;
- se o produto possui **adicionais**, mostrar a seção;
- se não possuir alguma dessas estruturas, a seção correspondente não deve ser renderizada.

---

# 🆚 Evolução da tela antiga para a nova tela

## 1. Tela antiga
A versão anterior possuía estrutura simples com:

- imagem do produto;
- nome;
- descrição;
- preço;
- observações;
- quantidade;
- CTA de adicionar ao carrinho.

## 2. Nova tela
A nova versão mantém toda a base anterior e adiciona blocos dinâmicos de personalização:

- **Sabores e Variações** *(quando houver)*;
- **Opções de escolha** *(quando houver)*;
- **Adicionais** *(quando houver)*.

## 3. Regra central de compatibilidade
Se o produto não possuir personalizações cadastradas, a tela continua funcionando como a versão antiga, sem quebrar layout e sem exibir áreas vazias.

---

# 🎨 Diretriz visual e estética

## 1. Estilo geral
A tela deve seguir a estética visual mobile moderna já existente:

- fundo branco;
- boa hierarquia tipográfica;
- imagem grande no topo;
- elementos com cantos arredondados;
- uso de laranja como cor primária;
- destaque visual em seleção ativa;
- separação clara entre seções;
- sensação de app de delivery premium.

## 2. Elementos visuais principais
- imagem grande do produto no topo;
- header sobreposto com:
  - voltar;
  - compartilhar;
  - favorito, se existir no escopo;
- bloco informativo do produto abaixo da imagem;
- cards ou blocos para personalizações;
- rodapé fixo com quantidade + botão de adicionar ao carrinho.

## 3. Cores sugeridas
- **Primária:** laranja/laranja-avermelhado;
- **Texto principal:** preto/cinza escuro;
- **Texto secundário:** cinza médio;
- **Bordas:** cinza claro;
- **Itens selecionados:** borda e fundo suave na cor primária;
- **Obrigatório:** selo ou texto em destaque discreto.

## 4. Sensação desejada
A tela deve transmitir que o cliente está montando seu pedido de forma simples, visual e confiável.

---

# 🧱 Estrutura da tela

A tela deve ser composta pelos seguintes blocos, nesta ordem:

1. **Header flutuante sobre a imagem**
2. **Imagem principal do produto**
3. **Informações principais do produto**
4. **Seção de Sabores e Variações** *(quando houver)*
5. **Seção de Opções de escolha** *(quando houver)*
6. **Seção de Adicionais** *(quando houver)*
7. **Campo de observações**
8. **Rodapé fixo com quantidade e botão de adicionar ao carrinho**

---

# 1. Header da tela

## Objetivo
Permitir ações rápidas no topo da tela do produto.

## Elementos
- botão voltar;
- botão compartilhar;
- botão favorito/curtir *(opcional se já existir no projeto)*;
- indicador de galeria como `1/4` *(somente se houver múltiplas imagens futuramente)*.

## Regras
- o botão voltar deve retornar para a tela/lista anterior;
- os botões do topo devem ficar sobrepostos à imagem;
- devem possuir contraste suficiente para leitura sobre a foto.

---

# 2. Imagem principal do produto

## Objetivo
Destacar o produto com forte apelo visual.

## Regras
- exibir a imagem principal cadastrada no produto;
- usar proporção visual destacada no topo;
- aplicar `object-fit: cover`;
- manter arredondamento e acabamento consistente com a identidade visual.

## Comportamento
- se houver apenas uma imagem, mostrar a imagem sem carrossel real;
- se futuramente existir múltiplas imagens, a tela já pode estar preparada para paginação;
- se não houver imagem válida, usar placeholder elegante.

---

# 3. Informações principais do produto

## Campos exibidos
- nome do produto;
- preço inicial;
- descrição curta;
- avaliação mockada/opcional, se existir na tela pública;
- número de avaliações mockado/opcional.

## Regras

### Nome
- obrigatório;
- deve aparecer com destaque tipográfico forte.

### Descrição
- texto cadastrado pela loja;
- pode ser curta ou média;
- se ultrapassar muito espaço, permitir expansão futura.

### Preço
A exibição do preço depende da estrutura do produto:

#### Caso 1 — produto sem variações
Mostrar o preço base do produto:
- ex.: `R$ 24,90`

#### Caso 2 — produto com variações
Mostrar o menor preço inicial ou o preço da opção selecionada por padrão.

### Regra sugerida
Se existir variação marcada ou definida como primeira opção:
- mostrar o preço dessa variação inicialmente.

---

# 4. Seção de Sabores e Variações

## Objetivo
Permitir ao cliente selecionar uma variação do produto, como tamanho, peso, versão simples/dupla/tripla ou sabor.

## Quando exibir
Renderizar esta seção apenas se o produto possuir `variations` cadastradas.

## Título sugerido
- `Tamanho`
- `Sabores e variações`
- ou o nome do grupo definido no backend, se houver suporte futuro.

## Subtítulo
- `Obrigatório`, quando a seleção for obrigatória;
- texto de apoio explicando que o cliente precisa escolher uma opção.

## Exemplo visual
- Simples — R$ 24,90
- Duplo — R$ 32,90
- Triplo — R$ 39,90

## Comportamento
- exibir opções em cards, botões ou linhas selecionáveis;
- permitir somente uma seleção por grupo na V1;
- destacar visualmente a opção escolhida;
- atualizar o preço total imediatamente.

## Regras funcionais
- se a variação for obrigatória, o usuário não pode adicionar ao carrinho sem escolher;
- se houver uma variação padrão definida, ela pode vir pré-selecionada;
- se não houver padrão e a escolha for obrigatória, o botão de adicionar ao carrinho só habilita após seleção.

## Validação
- não permitir continuar sem seleção quando a seção for obrigatória;
- exibir mensagem amigável:
  - `Selecione uma opção obrigatória.`

## Estrutura sugerida no frontend
```ts
type ProductVariationOption = {
  id: string;
  name: string;
  price: number;
  isDefault?: boolean;
};

