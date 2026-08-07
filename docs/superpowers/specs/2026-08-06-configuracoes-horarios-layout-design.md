# Configuracoes de Horarios: Design

## Objetivo

Fazer a rota de configuracoes de horarios reproduzir fielmente a composicao de `Documentacao/DashBoard/html/configuracoes-horarios.html`, sem remover recursos reais da aplicacao.

## Escopo Visual

- Manter o cabecalho e a navegacao de configuracoes existentes no shell.
- Usar um card principal branco com titulo, descricao, sete linhas de dias e rodape de acoes.
- Em desktop, organizar cada dia em uma grade horizontal: estado do dia, primeiro turno, rotulo de intervalo, segundo turno e acao de copiar.
- Exibir os campos de abertura e fechamento com largura compacta e constante, sem crescer para preencher o espaco disponivel. No desktop, cada campo deve ocupar aproximadamente 100 a 112px, como na referencia.
- Posicionar a lixeira imediatamente depois do campo `Fechamento` de cada turno removivel, deixando claro qual turno sera apagado.
- Manter `Adicionar turno` como acao textual discreta.
- Fixar a acao de copiar na ultima coluna da linha de cada dia, independente da quantidade de turnos. Ela abre a selecao dos outros dias que receberao os horarios.
- Exibir dia fechado com a mensagem `Loja fechada neste dia` na area dos horarios.
- Manter os quatro cards auxiliares abaixo do card principal, em quatro colunas no desktop e com quebra responsiva equivalente a referencia.
- Manter `Cancelar` e `Salvar alteracoes` alinhados no canto inferior direito.

## Comportamento Preservado

- Carregamento e persistencia dos horarios reais.
- Abertura e fechamento de cada dia.
- Edicao de horarios com validacao de turnos incompletos ou sobrepostos.
- Adicao e remocao de turnos.
- Copia de horarios para outros dias.
- Cancelamento de alteracoes e feedback de salvamento.
- Fluxo alternativo do assistente de configuracao da loja.

## Turnos Dinamicos

Os dois primeiros turnos ocupam a grade principal da referencia. Cada turno forma um grupo visual `Abertura | Fechamento | Lixeira`, e o rotulo `Intervalo` separa o primeiro do segundo. Turnos adicionais permanecem permitidos e quebram para uma faixa complementar dentro do mesmo dia, sem alterar a ordem de foco, esconder controles ou deslocar o botao de copiar da ultima coluna.

## Responsividade

- Desktop: uma linha compacta por dia, seguindo a referencia.
- Tablet: grade em duas colunas, mantendo estado e acoes legiveis.
- Mobile: conteudo empilhado, campos em duas colunas quando houver espaco e botoes com alvo minimo de 44px.
- Nenhum overflow horizontal na pagina ou no card.

## Acessibilidade

- Preservar labels associados aos inputs e nomes acessiveis dos botoes.
- Manter foco visivel em switches, campos e acoes.
- Nao depender apenas de cor para indicar aberto, fechado, erro ou salvamento.
- Respeitar `prefers-reduced-motion` no skeleton e no switch.

## Validacao

- Teste de componente cobrindo a estrutura visual principal e a ordem dos dias.
- Testes existentes de edicao, copia e salvamento continuam passando.
- Build Angular de producao.
- Detector Impeccable nos arquivos alterados.
- Comparacao final da composicao com o HTML de referencia em desktop e mobile.
