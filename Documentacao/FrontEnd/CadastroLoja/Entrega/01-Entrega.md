# 🚚 Cadastro da Loja — Etapa: Áreas e Taxas
## Especificação detalhada para implementação com Angular 20 + Ionic

---

# 🎯 Objetivo

Implementar a tela de **cadastro de locais de entrega e taxas por bairro** no fluxo de onboarding da loja de delivery.

Esta etapa permite que o lojista:

- defina os **bairros atendidos**;
- informe a **taxa de entrega** de cada bairro;
- adicione e remova bairros dinamicamente;
- configure o valor de **frete grátis a partir de** um pedido mínimo;
- tenha feedback de **salvamento automático** em tempo real;
- prossiga no fluxo de publicação da loja.

---

# 🧭 Contexto no fluxo

A tela faz parte do fluxo de cadastro da loja e exibe o seguinte stepper:

1. `Loja`
2. `Áreas e taxas`
3. `Entrega`
4. `Publicar`

## Estado visual identificado
- etapa atual destacada: `Áreas e taxas`
- progresso exibido: `50% concluído`
- texto complementar: `Continue assim!`

---

# 🛠️ Stack obrigatória

A implementação deve usar obrigatoriamente:

- **Angular 20**
- **Ionic**
- **Ionicons**
- arquitetura compatível com componentes standalone, quando aplicável

## Diretriz obrigatória
Sempre usar:

- componentes visuais do Ionic quando fizer sentido;
- ícones do Ionic / Ionicons;
- padrões de formulário reativos do Angular;
- boas práticas de acessibilidade e responsividade.

---

# 🖼️ Assets / imagens

Todos os arquivos visuais devem ser buscados em:

`public/images`

## Regra de uso de imagens
Procurar nesta pasta por:

- logo da marca;
- ilustrações do onboarding;
- ícones personalizados, se existirem;
- imagens de apoio dos cards;
- elementos gráficos decorativos.

## Regras adicionais
- se houver ícone equivalente em **Ionicons**, priorizar Ionicons;
- usar imagens de `public/images` apenas quando necessário;
- se algum asset não existir, usar:
  - `ion-icon`
  - SVG inline
  - componentes nativos do Ionic

---

# 🧱 Estrutura da tela

---

# 1. Header / Stepper de progresso

## Elementos visíveis
- logo/marca
- stepper horizontal do onboarding
- etapa atual destacada
- indicador de progresso
- texto:
  - `50% concluído`
  - `Continue assim!`

## Requisitos
- destacar visualmente `Áreas e taxas`;
- manter as demais etapas como anteriores/posteriores;
- exibir estrutura compatível com mobile e desktop.

## Sugestão Ionic
- `ion-header`
- `ion-toolbar`
- `ion-progress-bar`
- stepper customizado com `div` + classes ou componente próprio

---

# 2. Bloco de introdução

## Conteúdo visível
**Título:**
`Cadastre os bairros e defina as taxas de entrega`

**Descrição:**
`Informe as áreas que você atende e quanto cobra pela entrega.`

## Objetivo
Explicar claramente ao usuário o que deve ser preenchido nesta etapa.

---

# 3. Indicador de autosave

## Conteúdo visível
- `Salvo automaticamente`
- `Suas alterações são salvas em tempo real.`

## Requisitos funcionais
Toda alteração deve ser salva automaticamente.

## Estados recomendados
- `saving`
- `saved`
- `error`

## Textos recomendados
- `Salvando...`
- `Salvo automaticamente`
- `Erro ao salvar`

## Regra técnica
Usar autosave com debounce para evitar excesso de requisições.

---

# 4. Card principal — Bairros e taxas

A tela possui um card principal com o título:

`1. Adicione os bairros e defina a taxa de entrega`

## Estrutura identificada
Uma tabela/listagem com colunas:

- `Bairro`
  - subtítulo: `Área de entrega`
- `Taxa de entrega`
  - subtítulo: `Valor cobrado do cliente`
- `Ações`

## Funcionalidade principal
Permitir gerenciar uma lista dinâmica de bairros com suas respectivas taxas.

---

# 5. Linhas de bairros

Cada linha da tabela contém:

- alça visual de arraste/reordenação
- campo de nome do bairro
- campo monetário da taxa
- botão de remover

## Estrutura funcional de cada linha
### Campo 1 — Bairro
- input de texto
- placeholder identificado:
  - `Nome do bairro`
- deve aceitar texto livre

### Campo 2 — Taxa de entrega
- input monetário
- prefixo visual:
  - `R$`
- placeholder:
  - `0,00`
- entrada com formatação monetária no padrão brasileiro

### Campo 3 — Ações
- botão de remoção da linha
- ícone visual de lixeira

## Requisitos de interação
- o usuário pode editar livremente os valores;
- ao remover uma linha, ela deve desaparecer imediatamente;
- a persistência deve ocorrer após a alteração.

---

# 6. Linha inline para adição rápida

Existe uma linha especial no final da lista para adição rápida de novo bairro.

## Comportamento identificado
Essa linha possui:

- input de bairro
  - placeholder:
    - `Ex.: Aparecida`
- input de taxa
- botão para adicionar

## Regra funcional
Ao adicionar via linha inline:

- o nome do bairro é obrigatório;
- se o bairro não for preenchido:
  - o foco deve ir para o campo de bairro;
  - a inserção não deve acontecer;
- se a taxa não for informada:
  - usar valor padrão `5,00`;
- após adicionar:
  - inserir nova linha antes da linha inline;
  - limpar os campos da linha inline.

## Interação adicional identificada
- pressionar `Enter` dentro da linha inline também adiciona o bairro.

---

# 7. Botão “Adicionar bairro”

Além da linha inline, existe um botão explícito:

`Adicionar bairro`

## Comportamento identificado
Ao clicar:

- cria uma nova linha vazia editável;
- insere a nova linha antes da linha de adição inline;
- coloca foco no primeiro input da nova linha.

## Requisitos
Essa ação deve permitir adicionar manualmente uma linha vazia, mesmo sem usar a linha inline.

---

# 8. Dica informativa

Existe um bloco de dica com ícone de informação.

## Conteúdo visível
- título: `Dica`
- texto:
  `Seus clientes verão a taxa de entrega antes de finalizar o pedido.`

## Objetivo
Orientar o lojista sobre o impacto da taxa cadastrada.

## Sugestão Ionic
- `ion-icon` com `information-circle-outline`
- card ou box informativo com destaque leve

---

# 9. Card de frete grátis

A tela possui um segundo card com a configuração:

`2. Frete grátis a partir de`

## Descrição visível
`Defina o valor mínimo do pedido para o frete ser grátis.`

## Estrutura
- input monetário com prefixo `R$`
- texto explicativo:
  `Pedidos iguais ou acima desse valor terão frete grátis.`

## Regra funcional
O lojista define um valor mínimo de pedido para ativar frete grátis.

## Requisitos técnicos
- input monetário formatado no padrão BRL;
- permitir valor vazio se a funcionalidade for opcional;
- salvar automaticamente a alteração.

---

# 10. Rodapé de navegação

No final da tela existem:

- botão `Voltar`
- texto:
  `Seus dados estão seguros conosco.`
- botão `Continuar`

## Requisitos funcionais
### Botão Voltar
- retorna para a etapa anterior do fluxo

### Botão Continuar
- avança para a próxima etapa do onboarding

### Mensagem de segurança
- deve ser exibida entre as ações de navegação

---

# 🧠 Funcionalidades identificadas no comportamento

---

# 1. Criação dinâmica de linha

Foi identificada uma lógica equivalente a:

`ts
createRow(name = '', fee = '')`

## Máscara monetária

- Usar Mascar Monetaria

## Ícones recomendados

- trash-outline
- add-outline
- information-circle-outline
- checkmark-circle-outline
- shield-checkmark-outline
- chevron-back-outline
- chevron-forward-outline
- reorder-three-outline

## Responsividade

>Mobile:

- lista em formato de cards ou linhas empilhadas
- cabeçalho adaptado
- inputs em coluna
- botões largos
- boa área de toque

> Tablet:

- manter tabela simplificada
- distribuir melhor campos e ações

> Desktop:

- permitir layout mais tabular
- cabeçalho completo
- ações de rodapé bem posicionadas

Rodapé / confiança
Exibir no rodapé:

> botão Voltar:

**mensagem Seus dados estão seguros conosco.**

- botão Continuar

> Objetivo:

- reforçar segurança e confiança;
- manter coerência com o onboarding;
- permitir navegação clara entre etapas


## Resumo executivo

**Esta etapa do onboarding permite ao lojista cadastrar os bairros atendidos e suas respectivas taxas de entrega, além de configurar um valor mínimo para frete grátis.**

> O comportamento identificado mostra uma experiência centrada em:

- cadastro rápido de bairros;
- edição direta na lista;
- adição inline com fallback de taxa padrão;
- remoção imediata;
- formatação monetária em padrão brasileiro;
- autosave em tempo real;
- navegação simples para continuidade do cadastro.
