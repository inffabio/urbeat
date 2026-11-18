# Experiência pública de planos — Design

## Objetivo

Conectar a landing ao catálogo administrativo de planos e ao cadastro de
lojista, mantendo a seleção de plano e periodicidade antes do cadastro.

## Fluxo

1. A landing carrega planos ativos.
2. Cada card permite selecionar mensal ou anual quando habilitado.
3. O CTA `Começar` navega para `/cadastro` com `planId` e periodicidade.
4. O cadastro mostra o resumo escolhido e envia somente esses identificadores.
5. O servidor valida o catálogo atual, cria o lojista e vincula a assinatura.
6. Após o trial, a cobrança pendente fica marcada como aguardando integração
   de gateway.

O CTA atual `Criar meu cadastro` passa a executar scroll suave para `#plano`,
com fallback acessível para navegação por âncora. A ação não deve perder o
foco nem impedir navegação por teclado.

## Conteúdo condicional

- `plans.length > 1`: **“Escolha o plano ideal para o seu momento”**.
- `plans.length === 1`: **“Crie hoje mesmo sua loja”**.
- `plans.length === 0`: estado seguro sem CTA de contratação e mensagem de
  indisponibilidade.

O título, preços, trial e features vêm do backend. Fallbacks locais somente
podem existir para indisponibilidade temporária e não podem representar
condições divergentes do catálogo autoritativo.

## Acessibilidade e visual

- Preservar o tema `.urbeat-onboarding`.
- O botão `Começar` do card único será apenas um pouco maior que o atual.
- Periodicidades devem usar controles nativos/semânticos e labels claros.
- O card selecionado terá estado visual e `aria-pressed`/radio equivalente.
- O scroll da landing deve respeitar `prefers-reduced-motion`.
- Não introduzir nova biblioteca visual.

## Testes

- Landing com zero, um e múltiplos planos.
- Título correto em cada quantidade.
- CTA carrega `planId` e periodicidade.
- `Criar meu cadastro` rola para a seção.
- Cadastro preserva e envia os parâmetros.
- Plano desativado durante a jornada gera mensagem de revalidação.
