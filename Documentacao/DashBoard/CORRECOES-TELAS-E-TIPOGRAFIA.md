# Correções de interface — Dashboard Brasa Burguer

> Documento para ser enviado à IA responsável por corrigir o projeto existente em Angular 20 + Ionic.
>
> Esta é uma especificação incremental. Não recrie o projeto do zero. Preserve o código e as funcionalidades que não são citados aqui.

---

## 1. Objetivo

Corrigir os problemas identificados nas quatro imagens de revisão:

1. as páginas de Configurações estão com a rolagem vertical travada;
2. a página Bio ficou sem conteúdos auxiliares existentes no projeto original;
3. as páginas de Cardápio perderam o aviso de mensalidade e a navegação interna original;
4. a tipografia implementada não corresponde ao projeto original;
5. a estrutura de Categorias e Produtos não deve ser fundida em uma única tela extensa;
6. um pedido simulado não gerou aviso sonoro, atualizações no painel nem um card no Kanban de Pedidos.

O resultado deve manter Angular 20, Ionic Angular, componentes standalone, SCSS, Signals, Reactive Forms e a arquitetura atual do projeto.

Não faça um novo redesign. O trabalho é de **correção de comportamento e fidelidade visual**.

---

## 2. Regras anteriores que continuam válidas

As correções deste documento não devem desfazer as modificações já solicitadas anteriormente.

Continuam obrigatórias:

- não exibir Entregas no menu lateral;
- não exibir Instalar no menu lateral;
- não criar uma tela dedicada de Entregas;
- não criar uma página dedicada de Instalar;
- não exibir a aba Adicionais no Cardápio;
- manter somente Categorias e Produtos na navegação do Cardápio;
- preservar os grupos de opções existentes dentro do editor de produto;
- não restaurar a seção Redes sociais em Configurações > Informações;
- não restaurar o botão Exportar em Clientes;
- manter o status da loja como informativo, sem seta e sem comportamento de botão.

Mesmo que alguma imagem do projeto original mostre Entregas, Instalar ou Adicionais, as decisões mais recentes acima têm prioridade.

---

## 3. Prioridade 1 — corrigir a rolagem das páginas de Configurações

### 3.1 Problema observado

Nas rotas abaixo, o conteúdo fica preso na altura visível da janela e o usuário não consegue acessar os campos, linhas ou botões que estão mais abaixo:

```text
/app/configuracoes/informacoes
/app/configuracoes/bairros
```

O mesmo erro pode afetar as demais abas de Configurações. A correção deve ser aplicada ao contêiner compartilhado, e não apenas às duas páginas registradas nas imagens.

Rotas que devem ser verificadas:

```text
/app/configuracoes/informacoes
/app/configuracoes/horarios
/app/configuracoes/impressao
/app/configuracoes/bio
/app/configuracoes/bairros
```

### 3.2 Comportamento esperado

- O usuário deve conseguir rolar até o último elemento da página.
- A rolagem deve funcionar com roda do mouse, touchpad, toque, Page Down, setas e barra de rolagem.
- O último campo, a última linha e os botões finais devem ficar totalmente visíveis.
- Deve existir espaço inferior suficiente para que o conteúdo não fique colado nem escondido no limite da janela.
- O menu lateral pode permanecer fixo no desktop, mas não pode impedir a rolagem do conteúdo principal.
- O cabeçalho pode ser fixo ou sticky somente se não reduzir ou bloquear incorretamente a área rolável.
- Não deve haver duas barras verticais competindo entre si.
- Modais e selects não podem bloquear permanentemente o scroll depois de fechados.

### 3.3 Diagnóstico obrigatório

Antes de alterar CSS aleatoriamente, inspecione a cadeia completa de contêineres:

```text
html
body
ion-app
ion-split-pane
ion-router-outlet
app-shell
settings-shell
ion-content ou main
conteúdo da página
```

Procure por:

- `overflow: hidden` aplicado a `body`, shell ou área de conteúdo;
- `height: 100vh` em múltiplos elementos aninhados;
- `position: fixed` no contêiner principal;
- flex/grid sem `min-height: 0`;
- `ion-content` aninhado dentro de outro `ion-content`;
- `ion-content` com `scrollY` desativado;
- elemento sobreposto transparente capturando o wheel/touch;
- classe de modal que mantém `overflow: hidden` depois do fechamento;
- altura calculada sem descontar corretamente header ou abas;
- scroll colocado em um elemento que não recebe altura disponível.

Corrija a causa real. Não use JavaScript para mover manualmente a página nem crie botões “rolar para baixo”.

### 3.4 Estrutura Ionic recomendada

Cada página deve possuir apenas um contêiner principal responsável pela rolagem.

Exemplo:

```html
<ion-content
  class="settings-page-content"
  [fullscreen]="false"
  [scrollY]="true"
>
  <main class="settings-page-container">
    <!-- conteúdo completo -->
  </main>
</ion-content>
```

Base de SCSS:

```scss
:host {
  display: block;
  height: 100%;
  min-height: 0;
}

.settings-page-content {
  --overflow: auto;
}

.settings-page-container {
  width: 100%;
  max-width: 1440px;
  margin-inline: auto;
  padding: 28px 30px 48px;
}

@media (max-width: 767px) {
  .settings-page-container {
    padding: 18px 14px 40px;
  }
}
```

Se a arquitetura usar um `main` HTML em vez de `ion-content`, a área central deve ter uma configuração equivalente:

```scss
.app-shell {
  min-height: 100dvh;
}

.app-main {
  min-width: 0;
  min-height: 0;
  overflow-y: auto;
  overflow-x: hidden;
  overscroll-behavior: contain;
}
```

Não copie simultaneamente as duas soluções. Use a que corresponde à estrutura real do projeto.

### 3.5 Cuidados com flex e grid

Em um shell semelhante a:

```scss
.shell {
  display: flex;
  height: 100dvh;
}
```

o filho que contém a rota precisa permitir encolhimento e scroll:

```scss
.shell__route-area {
  flex: 1 1 auto;
  min-width: 0;
  min-height: 0;
  overflow: hidden;
}

.shell__scroll-area {
  height: 100%;
  min-height: 0;
  overflow-y: auto;
}
```

`overflow: hidden` só pode existir no pai quando um filho claramente definido for o scroller. Nunca bloqueie o pai e o filho ao mesmo tempo.

### 3.6 Validação da correção

Em Informações:

- rolar desde o título até o último campo;
- alcançar Cancelar e Salvar;
- abrir e fechar selects e verificar se a rolagem continua;
- salvar e continuar conseguindo rolar.

Em Bairros:

- rolar até o último bairro;
- alcançar todas as ações da última linha;
- abrir e fechar o editor de bairro;
- conferir que o scroll volta a funcionar após modal/alerta;
- testar lista curta e lista longa.

Em Bio:

- alcançar logo, banner, Dicas, Requisitos de imagem e ações finais.

Testar pelo menos nas resoluções:

```text
1366 × 768
1536 × 864
1920 × 1080
768 × 1024
390 × 844
```

---

## 4. Prioridade 2 — restaurar os conteúdos ausentes em Configurações > Bio

Rota:

```text
/app/configuracoes/bio
```

### 4.1 Problema observado

A implementação não reproduziu todo o conteúdo auxiliar apresentado no projeto original. O bloco de dicas está incompleto ou ausente, especialmente a orientação:

```text
Use uma logo de qualidade e fácil de reconhecer.
```

### 4.2 Card Dicas

Adicionar ou restaurar um card lateral com:

**Título:**

```text
Dicas
```

**Itens, nesta ordem:**

1. Mantenha sua bio curta e objetiva.
2. Use uma logo de qualidade e fácil de reconhecer.
3. Escolha um banner que represente bem seu estilo.

Requisitos visuais:

- usar o mesmo card branco, raio, borda/sombra e espaçamento do original;
- usar ícone de lâmpada no título;
- usar indicador de confirmação em cada item;
- não usar fonte maior ou mais pesada que os demais cards auxiliares;
- permitir quebra de linha natural sem sobrepor ícone ou borda.

### 4.3 Card Requisitos de imagem

Se estiver ausente ou incompleto, restaurar também:

**Título:**

```text
Requisitos de imagem
```

**Conteúdo:**

- Logo: PNG com fundo transparente, máximo de 2 MB.
- Banner: 1200 × 400 px, JPG ou PNG, máximo de 5 MB.

As validações reais do upload devem corresponder aos textos exibidos.

### 4.4 Layout esperado

No desktop:

- coluna principal com texto da bio, logo e banner;
- coluna lateral com prévia, Dicas e Requisitos de imagem;
- alinhamento superior coerente;
- cards laterais em fluxo normal, sem ficar fora da área rolável.

No tablet/mobile:

- empilhar os blocos;
- manter a prévia antes dos cards auxiliares ou logo após o formulário;
- não usar posição absoluta;
- garantir scroll até Salvar alterações.

Estrutura sugerida:

```scss
.bio-layout {
  display: grid;
  grid-template-columns: minmax(0, 2fr) minmax(280px, 1fr);
  gap: 24px;
  align-items: start;
}

.bio-aside {
  display: grid;
  gap: 18px;
}

@media (max-width: 991px) {
  .bio-layout {
    grid-template-columns: 1fr;
  }
}
```

Preservar:

- upload/alteração de logo;
- remoção de logo;
- upload/alteração de banner;
- remoção de banner;
- preview da loja;
- Cancelar;
- Salvar alterações;
- validações de tamanho e tipo.

---

## 5. Prioridade 3 — restaurar a estrutura original do Cardápio

Rotas:

```text
/app/cardapio/categorias
/app/cardapio/produtos
```

### 5.1 Problema observado

A implementação criada pela IA transformou Categorias e cadastro de Produtos em uma única página longa, com uma coluna lateral repetindo produtos. Essa composição não corresponde ao projeto original.

Também ficaram ausentes:

- o aviso de mensalidade;
- a navegação interna do Cardápio;
- a separação clara entre Categorias e Produtos.

### 5.2 Não fundir Categorias e Produtos

Manter duas páginas independentes:

#### Categorias

```text
/app/cardapio/categorias
```

Deve conter:

- título e descrição da página;
- aviso de mensalidade;
- abas do Cardápio;
- lista de categorias cadastradas;
- reordenação;
- status e exibição;
- formulário ou modal Nova categoria;
- ações de editar e excluir conforme o projeto.

Não deve conter o formulário completo de cadastro de produto nem uma coluna “Seus produtos”.

#### Produtos

```text
/app/cardapio/produtos
```

Deve conter:

- título e descrição da página;
- aviso de mensalidade;
- abas do Cardápio;
- métricas Total, Ativos, Inativos e Categorias;
- busca e filtros;
- lista de produtos cadastrados;
- ação Novo produto;
- editor de produto em modal ou fluxo próprio já definido.

Não duplicar a lista de produtos em uma coluna lateral.

### 5.3 Aviso de mensalidade

Restaurar o banner entre o cabeçalho da página e as abas do Cardápio.

Exemplo de conteúdo:

```text
Sua mensalidade está em dia! Próximo vencimento 10/07/26
```

O texto deve vir do estado real/simulado de mensalidade, sem data fixa no template.

Requisitos:

- fundo verde muito claro;
- texto e ícone verdes;
- largura total do conteúdo;
- altura compacta equivalente ao projeto original;
- borda discreta;
- mesmo componente compartilhado usado nas outras telas;
- não transformar em botão se não houver ação.

Estados previstos:

- em dia;
- vencimento próximo;
- pendente/atrasada.

O texto, ícone e cor devem acompanhar o estado, sem depender apenas da cor.

### 5.4 Navegação interna do Cardápio

Adicionar, logo abaixo do banner, um card/faixa de navegação com somente:

```text
Categorias
Produtos
```

Não restaurar Adicionais.

Comportamento:

- Categorias navega para `/app/cardapio/categorias`;
- Produtos navega para `/app/cardapio/produtos`;
- usar `routerLinkActive` ou estado equivalente;
- somente a rota atual fica destacada;
- preservar foco visível;
- funcionar com teclado;
- manter a mesma ordem no desktop e mobile.

Visual:

- contêiner branco e arredondado;
- item ativo com o mesmo roxo do projeto original;
- texto branco no item ativo;
- item inativo com texto escuro/cinza;
- ícones equivalentes aos do original;
- altura e padding compactos;
- não usar vermelho no lugar do roxo apenas porque outras telas atuais usam vermelho.

Exemplo estrutural:

```html
<nav class="menu-tabs" aria-label="Seções do cardápio">
  <a
    routerLink="/app/cardapio/categorias"
    routerLinkActive="is-active"
    [routerLinkActiveOptions]="{ exact: true }"
  >
    <ion-icon name="layers-outline" aria-hidden="true" />
    <span>Categorias</span>
  </a>

  <a
    routerLink="/app/cardapio/produtos"
    routerLinkActive="is-active"
    [routerLinkActiveOptions]="{ exact: true }"
  >
    <ion-icon name="bag-handle-outline" aria-hidden="true" />
    <span>Produtos</span>
  </a>
</nav>
```

Preferir um componente compartilhado, por exemplo:

```text
MenuSectionTabsComponent
```

Esse componente deve ser usado nas duas rotas para evitar diferenças de espaçamento e estado ativo.

### 5.5 Ordem correta dos blocos

Em Categorias e Produtos:

1. cabeçalho da página;
2. aviso de mensalidade;
3. abas Categorias/Produtos;
4. conteúdo específico da rota.

Não inserir cadastro de produto antes das abas e não mover o banner para dentro de um card de formulário.

---

## 6. Prioridade 4 — corrigir a tipografia em todo o projeto

### 6.1 Problema observado

A versão criada pela IA usa uma tipografia visualmente diferente e pesada demais. Títulos, menus e textos estão com família, peso ou tamanho incompatíveis com o dashboard original.

Não basta usar uma fonte “parecida”. É obrigatório reproduzir:

- família tipográfica;
- peso;
- tamanho;
- line-height;
- letter-spacing;
- transformação de caixa;
- hierarquia entre título, subtítulo, card, campo e legenda.

### 6.2 Família tipográfica obrigatória

A fonte do projeto original é:

```text
Plus Jakarta Sans
```

Carregar os pesos necessários:

```text
400 — Regular
500 — Medium
600 — SemiBold
700 — Bold
800 — ExtraBold, somente onde o original realmente usar
```

Não usar como fonte principal:

- Poppins;
- Inter;
- Roboto;
- Arial;
- fonte padrão do Ionic;
- fonte de marca da plataforma atual.

Fallback permitido:

```scss
font-family: 'Plus Jakarta Sans', system-ui, -apple-system, BlinkMacSystemFont,
  'Segoe UI', sans-serif;
```

### 6.3 Aplicação global no Ionic

Definir no tema global:

```scss
:root {
  --ion-font-family: 'Plus Jakarta Sans', system-ui, -apple-system,
    BlinkMacSystemFont, 'Segoe UI', sans-serif;
}

html,
body,
ion-app {
  font-family: var(--ion-font-family);
}

button,
input,
select,
textarea,
ion-button,
ion-input,
ion-select,
ion-textarea,
ion-label {
  font-family: inherit;
}
```

Verifique componentes Ionic com Shadow DOM. Use variáveis e `::part()` somente quando necessário; não espalhe overrides duplicados por página.

### 6.4 O CSS original é a fonte de verdade

Antes de definir a escala final:

1. abra o CSS/SCSS do projeto original;
2. identifique os valores de `.page-title`, subtítulos, cards, métricas, tabs, labels, botões e menu;
3. confira os valores computados no navegador;
4. transfira esses valores para tokens globais;
5. compare lado a lado nas mesmas dimensões de viewport.

Não estime “a olho” se o projeto original estiver disponível.

### 6.5 Escala de referência

Use a escala abaixo como baseline para localizar divergências. Se o CSS original trouxer um valor diferente para o mesmo elemento, o valor do original tem prioridade.

| Elemento | Tamanho de referência | Peso | Line-height |
|---|---:|---:|---:|
| Título principal de página | 28 px | 700 | 1.2 |
| Título de seção principal | 20 px | 700 | 1.25 |
| Título de card | 18 px | 700 | 1.3 |
| Subtítulo/descrição da página | 14 px | 400 ou 500 | 1.5 |
| Item do menu lateral | 14 px | 600 | 1.25 |
| Aba de navegação | 14 px | 600 | 1.25 |
| Botão | 14 px | 600 ou 700 | 1.2 |
| Label de formulário | 13 px | 600 | 1.35 |
| Texto de input/select | 14 px | 500 | 1.4 |
| Texto de tabela/lista | 13–14 px | 400 ou 500 | 1.4 |
| Texto auxiliar | 12–13 px | 400 ou 500 | 1.45 |
| Métrica numérica | 28–32 px | 700 | 1.15 |

Evitar:

- títulos de 32–40 px em páginas administrativas quando o original usa 28 px;
- `font-weight: 900`;
- corpo inteiro em 600 ou 700;
- letras apertadas por `letter-spacing` negativo excessivo;
- labels em caixa alta sem existir no original;
- tamanhos diferentes para o mesmo componente em rotas distintas.

### 6.6 Tokens tipográficos

Centralizar em `theme/tokens.scss` ou arquivo equivalente:

```scss
:root {
  --font-family-app: 'Plus Jakarta Sans', system-ui, sans-serif;

  --font-size-page-title: 1.75rem;
  --font-size-section-title: 1.25rem;
  --font-size-card-title: 1.125rem;
  --font-size-body: 0.875rem;
  --font-size-label: 0.8125rem;
  --font-size-caption: 0.75rem;

  --font-weight-regular: 400;
  --font-weight-medium: 500;
  --font-weight-semibold: 600;
  --font-weight-bold: 700;

  --line-height-tight: 1.2;
  --line-height-title: 1.3;
  --line-height-body: 1.5;
}
```

Não mantenha os valores desse exemplo se a auditoria do original indicar medidas diferentes. O importante é que exista uma única fonte de verdade.

### 6.7 Elementos a revisar individualmente

Comparar com o original:

- nome da loja no menu;
- subtítulo “Painel da loja/restaurante”;
- status “Loja aberta”;
- itens e títulos de grupo do menu;
- título e descrição de todas as páginas;
- abas de Configurações;
- abas Categorias e Produtos;
- aviso de mensalidade;
- títulos de cards;
- labels e valores de formulário;
- placeholders;
- botões;
- cabeçalhos e linhas de tabela;
- métricas;
- mensagens auxiliares;
- toasts, alerts e modais.

### 6.8 Responsividade tipográfica

No mobile:

- reduzir título de página para o valor usado no original mobile, normalmente entre 23 e 26 px;
- não reduzir corpo abaixo de 14 px em campos e ações importantes;
- evitar quebra de palavras no menu e nas abas;
- permitir que descrições quebrem linha naturalmente;
- manter botões legíveis e com área mínima de 44 px.

Não usar `clamp()` que gere títulos maiores que o original no desktop.

### 6.9 Critério visual

A comparação deve ser feita com screenshots lado a lado na mesma resolução. A correção só está concluída quando:

- a densidade de texto se aproxima do original;
- os títulos deixam de parecer excessivamente pesados;
- menu, tabs, cards e formulários compartilham a mesma família;
- a hierarquia é reconhecível sem depender de negrito extremo;
- não há mudança de fonte durante o carregamento após a aplicação estabilizar.

---

## 7. Prioridade 5 — corrigir o recebimento de novos pedidos

### 7.1 Falha identificada no teste funcional

Foi realizado um teste real de funcionamento:

1. um pedido foi simulado;
2. o painel administrativo estava aberto;
3. nenhum aviso sonoro foi reproduzido;
4. não foi criado um card para o pedido no Kanban;
5. o pedido não ficou claramente disponível para aceite e acompanhamento.

Essa falha deve ser tratada como problema funcional crítico. Não basta adicionar um som ou criar um card estático: o evento de novo pedido precisa alimentar todo o estado operacional do painel.

### 7.2 Resultado esperado ao receber um pedido

Quando um novo pedido válido for criado pela simulação, API ou canal de eventos, a aplicação deve, em uma única operação lógica:

1. validar e normalizar os dados recebidos;
2. cadastrar o pedido no store central com status `new` ou equivalente;
3. persistir o pedido no mecanismo atual;
4. criar imediatamente o card na coluna **Novos pedidos** do Kanban;
5. atualizar o badge de pedidos pendentes no menu;
6. atualizar “Pedidos hoje” e demais métricas derivadas;
7. incluir o pedido em “Últimos pedidos” no Dashboard;
8. incluir ou atualizar o resumo “Pedidos em andamento”;
9. exibir uma notificação visual;
10. reproduzir um aviso sonoro quando o som estiver habilitado e liberado pelo navegador.

Nenhuma dessas atualizações deve exigir recarregar a página.

### 7.3 Fonte única de verdade

O simulador, Dashboard e Kanban devem usar o mesmo store/serviço de pedidos.

Não manter listas independentes como:

```text
dashboardOrders
kanbanOrders
simulatedOrders
```

que precisam ser sincronizadas manualmente.

Estrutura conceitual recomendada:

```ts
@Injectable({ providedIn: 'root' })
export class OrdersStore {
  private readonly state = signal<Order[]>([]);

  readonly orders = this.state.asReadonly();
  readonly newOrders = computed(() =>
    this.state().filter((order) => order.status === 'new'),
  );
  readonly preparingOrders = computed(() =>
    this.state().filter((order) => order.status === 'preparing'),
  );
  readonly pendingCount = computed(() => this.newOrders().length);

  receive(order: Order): ReceiveOrderResult {
    // validar, impedir duplicidade, inserir, persistir e retornar o resultado
  }
}
```

O componente que simula o pedido deve chamar a mesma entrada de domínio usada por eventos reais:

```ts
this.ordersFacade.receiveNewOrder(orderPayload);
```

Ele não deve apenas mostrar uma mensagem de sucesso na tela de simulação.

Se existir backend, WebSocket, SSE ou polling, encapsular a origem em um adaptador, por exemplo:

```text
OrderEventsPort
MockOrderEventsAdapter
ApiOrderEventsAdapter
```

O restante da aplicação não deve depender da tecnologia usada para receber o evento.

### 7.4 Criação do card no Kanban

O pedido novo deve aparecer na rota:

```text
/app/pedidos
```

na coluna:

```text
Novos pedidos
```

O card deve apresentar, no mínimo:

- número do pedido;
- horário de recebimento;
- cliente;
- tipo: entrega ou retirada;
- forma/situação do pagamento;
- itens e quantidades;
- observações do cliente, quando houver;
- taxa de entrega;
- desconto, quando houver;
- valor total;
- ação “Aceitar pedido”.

Regras:

- inserir o card no topo da coluna;
- não duplicar pedidos com o mesmo ID/número;
- ordenar pedidos novos do mais recente para o mais antigo;
- destacar visualmente o card ainda não reconhecido;
- persistir o estado para que o pedido continue no Kanban após recarregar;
- remover o destaque de “não reconhecido” após o usuário abrir, aceitar ou confirmar ciência;
- manter as transições de status já definidas.

Fluxo mínimo:

```text
Novo → Aceitar pedido → Em preparação
Em preparação → Marcar como pronto → Pronto
Pronto → Saiu para entrega/Pronto para retirada
Em entrega/retirada → Concluído
```

A remoção anterior da tela dedicada de Entregas não altera esse fluxo dentro de Pedidos.

### 7.5 Atualização do Dashboard

O Dashboard não precisa receber um segundo Kanban completo se o quadro operacional já existe em `/app/pedidos`. Porém, ao receber um pedido, deve atualizar em tempo real:

- Pedidos hoje;
- Faturamento, conforme a regra atual;
- Ticket médio;
- Pedidos em andamento;
- Últimos pedidos;
- badge no menu;
- qualquer resumo de pedidos novos existente.

Ao clicar no pedido ou no indicador, navegar para o card correspondente no Kanban, quando essa navegação já fizer parte da arquitetura.

### 7.6 Aviso visual obrigatório

O áudio nunca pode ser o único aviso.

Ao receber um pedido, exibir uma notificação visual persistente o suficiente para ser percebida, por exemplo:

```text
Novo pedido #1234 recebido
```

Com ações opcionais:

```text
Ver pedido
Aceitar
```

Requisitos:

- usar toast, banner ou popover integrado ao Ionic;
- anunciar com `aria-live="assertive"` ou região equivalente;
- mostrar número e valor do pedido;
- não fechar antes que o usuário consiga ler;
- não criar várias camadas sobrepostas se chegarem pedidos em sequência;
- agrupar ou enfileirar notificações quando necessário.

### 7.7 Aviso sonoro

Usar um som curto, claro e não agressivo, armazenado nos assets do próprio projeto.

Exemplo de local:

```text
src/assets/audio/new-order.mp3
```

O som deve ser disparado apenas quando:

- o evento representa um pedido realmente novo;
- o pedido foi inserido com sucesso;
- a preferência de som está ativada;
- o navegador já autorizou reprodução de áudio.

Não tocar som:

- ao carregar pedidos antigos do armazenamento;
- ao recarregar a página;
- ao receber novamente o mesmo ID;
- ao mudar o status de um pedido existente;
- em loop infinito;
- para dados seed na inicialização.

### 7.8 Controle “Som ligado/desligado”

O controle mostrado no topo do painel deve ser funcional.

Estados esperados:

```text
Som ligado
Som desligado
Ativar som
```

Regras:

- o texto e o ícone devem refletir o estado real;
- clicar deve ativar/desativar a preferência;
- persistir a preferência no armazenamento local;
- permitir teste do som ao ativá-lo;
- exibir feedback se o navegador bloquear a reprodução;
- não mostrar “Som ligado” se o `AudioContext` estiver suspenso ou o `play()` falhar;
- preservar foco e nome acessível.

Os navegadores normalmente bloqueiam autoplay antes de uma interação do usuário. Na primeira sessão, apresentar uma ação explícita “Ativar som”. Dentro desse clique:

1. criar ou retomar o `AudioContext`, ou executar `audio.play()`;
2. confirmar que a Promise foi resolvida;
3. salvar a preferência;
4. atualizar o rótulo para “Som ligado”.

Não tentar contornar as políticas do navegador. Se o áudio estiver bloqueado, manter o aviso visual e explicar como ativar o som.

### 7.9 Serviço de som recomendado

Centralizar a reprodução para evitar áudios duplicados em várias páginas:

```ts
@Injectable({ providedIn: 'root' })
export class OrderNotificationSoundService {
  private readonly enabled = signal(false);
  private audio?: HTMLAudioElement;

  readonly isEnabled = this.enabled.asReadonly();

  async enable(): Promise<boolean> {
    // carregar/desbloquear o áudio dentro da interação do usuário
  }

  disable(): void {
    // persistir preferência e interromper reprodução pendente
  }

  async playForNewOrder(orderId: string): Promise<void> {
    // tocar uma vez e impedir repetição do mesmo evento
  }
}
```

O listener global de novos pedidos deve ficar em um serviço/facade ou no `AppShellComponent`, permanecendo ativo enquanto o usuário estiver autenticado. Não depender de o usuário estar especificamente na tela Pedidos.

### 7.10 Idempotência e pedidos simultâneos

Usar o ID do pedido como chave de idempotência.

Se o mesmo evento chegar duas vezes:

- manter um único card;
- não somar duas vezes nas métricas;
- não tocar o som novamente;
- não duplicar toast.

Se chegarem vários pedidos diferentes em sequência:

- criar todos os cards;
- manter ordenação;
- atualizar o contador corretamente;
- enfileirar ou agrupar alertas;
- impedir sobreposição caótica de áudio.

### 7.11 Tratamento de erro

Se o pedido recebido for inválido ou a persistência falhar:

- registrar erro técnico sem dados sensíveis;
- mostrar mensagem humana;
- não tocar confirmação sonora de sucesso;
- não incrementar métricas incorretamente;
- oferecer tentar novamente quando aplicável.

Mensagem sugerida:

```text
Não foi possível registrar o novo pedido. Tente novamente.
```

---

## 8. Componentes compartilhados recomendados

Evitar repetir correções em várias rotas. Reutilizar ou criar:

```text
SubscriptionBannerComponent
MenuSectionTabsComponent
SettingsTabsComponent
PageHeaderComponent
AppShellComponent
OrderNotificationSoundService
NewOrderNotificationComponent
```

Responsabilidades:

### `SubscriptionBannerComponent`

- recebe status e próxima data;
- renderiza texto e semântica;
- usado em Categorias e Produtos;
- não contém data hardcoded.

### `MenuSectionTabsComponent`

- mostra apenas Categorias e Produtos;
- controla rota ativa;
- centraliza tipografia e espaçamento.

### `SettingsTabsComponent`

- permanece fora do conteúdo específico das abas;
- não cria um segundo scroller;
- mantém Informações, Horários, Impressão, Bio e Bairros.

### `AppShellComponent`

- define qual elemento é responsável pela rolagem;
- não bloqueia o scroll das rotas;
- preserva menu lateral no desktop e drawer no mobile.

### `OrderNotificationSoundService`

- controla desbloqueio e preferência de áudio;
- reproduz o alerta uma única vez por pedido;
- não depende da rota atualmente aberta;
- informa quando o navegador bloqueia o som.

### `NewOrderNotificationComponent`

- oferece alternativa visual ao som;
- mostra número e resumo do pedido;
- permite navegar para o Kanban;
- trata fila de múltiplos pedidos.

---

## 9. Testes automatizados mínimos

### Rolagem

Criar teste E2E com viewport de 1366 × 768:

1. abrir Informações;
2. registrar `scrollHeight` e `clientHeight` do scroller correto;
3. confirmar que `scrollHeight > clientHeight` quando há conteúdo longo;
4. rolar até o fim;
5. confirmar que o último botão está visível;
6. repetir em Bairros;
7. abrir/fechar um modal e repetir a rolagem.

Não testar o `window.scrollY` se o projeto usa `ion-content`; identifique o elemento realmente rolável.

### Bio

- Dicas contém os três textos;
- a segunda dica é exatamente “Use uma logo de qualidade e fácil de reconhecer.”;
- Requisitos de imagem mostra limites de logo e banner;
- cards são alcançáveis por scroll.

### Cardápio

- Categorias mostra banner de mensalidade;
- Produtos mostra banner de mensalidade;
- as duas páginas mostram somente Categorias e Produtos nas abas;
- não aparece Adicionais;
- rota ativa recebe destaque correto;
- Categorias não contém o formulário completo de Produto;
- Produtos mantém métricas, filtros e lista.

### Tipografia

Testar pelo menos os estilos computados de componentes críticos:

```ts
expect(getComputedStyle(pageTitle).fontFamily)
  .toContain('Plus Jakarta Sans');

expect(getComputedStyle(pageTitle).fontWeight)
  .toBe('700');
```

Não crie testes frágeis para cada parágrafo. Cubra tokens e componentes compartilhados.

### Recebimento de pedido

Testes unitários:

- inserir evento novo cria um único pedido no store;
- `newOrders` passa a conter o pedido;
- `pendingCount` é incrementado;
- evento duplicado não cria outro pedido;
- evento duplicado não solicita novo som;
- mudança de status move o pedido entre as colunas;
- recarregar o estado preserva o pedido sem tocar som novamente;
- som desativado não chama `play()`;
- som ativado chama `play()` uma vez;
- falha de `play()` mantém aviso visual e estado coerente.

Teste E2E:

1. entrar no painel;
2. ativar o som por interação explícita;
3. executar a simulação de um pedido com ID único;
4. confirmar o aviso visual;
5. confirmar que o método de áudio foi chamado;
6. confirmar atualização de badge e métricas;
7. abrir `/app/pedidos`;
8. confirmar o card no topo de Novos pedidos;
9. aceitar o pedido;
10. confirmar sua movimentação para Em preparação;
11. recarregar e confirmar a persistência;
12. repetir o mesmo evento e confirmar que não houve duplicidade.

O E2E pode verificar a chamada de `HTMLMediaElement.play()` com spy. A reprodução audível deve também ser confirmada manualmente em navegador real.

---

## 10. Validação manual obrigatória

### Configurações

- [ ] Informações rola até o final.
- [ ] Horários rola até o final.
- [ ] Impressão rola até o final.
- [ ] Bio rola até o final.
- [ ] Bairros rola até o final.
- [ ] Abrir e fechar modal não trava a página.
- [ ] Botões finais não ficam escondidos.
- [ ] Existe apenas uma barra vertical principal.

### Bio

- [ ] Dicas está presente.
- [ ] As três dicas estão presentes e na ordem correta.
- [ ] Requisitos de imagem está presente.
- [ ] Logo e banner continuam editáveis.
- [ ] Preview continua funcionando.

### Cardápio

- [ ] Banner de mensalidade aparece em Categorias.
- [ ] Banner de mensalidade aparece em Produtos.
- [ ] Abas Categorias e Produtos aparecem nas duas rotas.
- [ ] Adicionais não aparece.
- [ ] A aba ativa corresponde à URL.
- [ ] Categorias e Produtos são páginas separadas.
- [ ] Não existe coluna lateral duplicando produtos.

### Tipografia

- [ ] Plus Jakarta Sans está carregada.
- [ ] Não existe outra família sobrescrevendo o app.
- [ ] Pesos 400, 500, 600 e 700 carregam corretamente.
- [ ] Títulos correspondem ao tamanho do original.
- [ ] Títulos não usam peso 900.
- [ ] Menu e tabs usam o mesmo padrão do original.
- [ ] Labels e inputs correspondem ao original.
- [ ] Desktop e mobile mantêm hierarquia coerente.

### Novo pedido

- [ ] O controle permite ativar e desativar o som.
- [ ] O rótulo informa o estado real do som.
- [ ] O som de teste funciona após a ativação.
- [ ] Simular um pedido produz aviso sonoro quando habilitado.
- [ ] Simular um pedido sempre produz aviso visual.
- [ ] O badge de pedidos é atualizado sem refresh.
- [ ] As métricas do Dashboard são atualizadas.
- [ ] O pedido aparece em Últimos pedidos.
- [ ] Um card é criado no topo de Novos pedidos.
- [ ] O card contém cliente, itens, pagamento e total.
- [ ] Aceitar move o card para Em preparação.
- [ ] Recarregar preserva o pedido e seu status.
- [ ] Repetir o mesmo evento não duplica o card nem o som.
- [ ] O recebimento funciona mesmo quando outra rota está aberta.

---

## 11. Critérios de aceite final

A tarefa só está concluída quando:

1. todas as páginas de Configurações permitem rolagem até o último elemento;
2. a rolagem continua funcionando após abrir e fechar modais, selects e alerts;
3. a página Bio contém as três dicas e os requisitos de imagem;
4. Categorias e Produtos voltaram a ser páginas separadas;
5. o aviso de mensalidade aparece nas duas páginas do Cardápio;
6. as abas Categorias e Produtos aparecem e navegam corretamente;
7. Adicionais continua removido;
8. toda a aplicação utiliza Plus Jakarta Sans;
9. pesos, tamanhos, line-height e hierarquia correspondem ao projeto original;
10. simular um pedido cria imediatamente um card no Kanban em Novos pedidos;
11. o novo pedido atualiza badge, métricas e listas sem recarregar;
12. o alerta visual sempre é exibido;
13. o alerta sonoro é reproduzido quando habilitado e autorizado;
14. o controle de som informa seu estado real e persiste a preferência;
15. eventos duplicados não duplicam card, métricas, toast ou som;
16. o pedido permanece após recarregar e pode avançar no Kanban;
17. o build é concluído sem erro;
18. os testes passam;
19. o console não apresenta erros;
20. não foram reintroduzidas funcionalidades removidas em solicitações anteriores.

---

## 12. Comandos de verificação

Usar os scripts reais do projeto. No mínimo:

```bash
npm run build
npm test
npx ionic serve
```

Se houver lint e E2E configurados:

```bash
npm run lint
npm run e2e
```

Verificar também o console do navegador e a aba Computed do DevTools para confirmar a tipografia realmente aplicada.

---

## 13. Relatório esperado da IA

Ao finalizar, informar:

1. qual elemento passou a controlar a rolagem;
2. quais declarações de `height`/`overflow` causavam o bloqueio;
3. quais páginas de Configurações foram testadas;
4. quais blocos foram restaurados em Bio;
5. como Categorias e Produtos foram separados;
6. onde o banner e as abas compartilhadas foram implementados;
7. onde os tokens tipográficos foram centralizados;
8. quais valores do CSS original foram reutilizados;
9. qual serviço/store recebe o novo pedido;
10. como o pedido alimenta Dashboard e Kanban sem duplicidade;
11. como o desbloqueio e a preferência de som foram implementados;
12. como foi garantida a criação imediata do card em Novos pedidos;
13. testes executados e resultados;
14. arquivos alterados.

Não entregar apenas screenshots. Entregar o código corrigido, testado e funcional.
