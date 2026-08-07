# Análise do Redesign da Loja (Front do Cliente) — Urbeat

> Documento-mestre da análise comparativa entre o protótipo **NovaVersaoFront** (`Documentacao/FrontEnd/NovaVersaoFront`) e o sistema atual (`./frontend` + `./backend`), com foco no **front da loja do cliente** que é gerado a partir da configuração da loja do vendedor.

## Objetivo

O Urbeat é um app de **delivery de comida**. **Não é multi-segmento** (não há farmácia, mercado, petshop). Cada loja tem **um tipo de comida** (`CuisineType`) — Hamburgueria, Pizzaria, Açaiteria, Restaurante, etc. — que define seu **produto principal e as variações** desse produto. Uma loja, a princípio, **não vende pizza e hambúrguer juntos**, mas sempre tem **complementos** (Combos, Bebidas, Acompanhamentos, Sobremesas).

Esta análise cobre:

1. O que o novo protótipo entrega (visual, cores, tipografia, componentes, telas).
2. Como isso se compara ao front atual em Angular/Ionic.
3. Todas as funcionalidades já cadastradas no sistema (frontend + backend) e onde estão os gaps.
4. Ideias de **parametrização por `CuisineType`** (tipo de comida) para o front da loja, incluindo **Combos/Destaques** (ordem do cardápio) e **busca/filtros**.
5. Ideias de **parametrização no cadastro de produtos** (expandindo o que já existe).
6. Um roadmap de implementação priorizado.

## Índice dos documentos

| # | Arquivo | Conteúdo |
|---|---------|----------|
| 01 | [`01-Analise-Prototipo.md`](./01-Analise-Prototipo.md) | Análise tela a tela do protótipo NovaVersaoFront: layout, cores, tipografia, componentes, ícones e microinterações. |
| 02 | [`02-Design-System-Comparativo.md`](./02-Design-System-Comparativo.md) | Comparativo de design tokens (cores, fontes, sombras, raios) entre novo protótipo e front atual + plano de migração. |
| 03 | [`03-Gap-Funcionalidades.md`](./03-Gap-Funcionalidades.md) | Inventário completo de funcionalidades do sistema (front + back) e matriz de gaps do protótipo. |
| 04 | [`04-Front-Loja-Parametrizacoes.md`](./04-Front-Loja-Parametrizacoes.md) | **(Crítico)** Parametrização do front da loja por `CuisineType` (tipo de comida), Combos/Destaques (ordem do cardápio) e busca/filtros. |
| 05 | [`05-Parametrizacoes-Produto.md`](./05-Parametrizacoes-Produto.md) | Parametrização avançada no cadastro de produtos (grupos de opções, meio a meio, tamanhos, etc.). |
| 06 | [`06-Roadmap-Implementacao.md`](./06-Roadmap-Implementacao.md) | Roadmap faseado, esforço estimado e dependências backend. |
| 07 | [`07-Cadastro-Cliente-e-Mensagens.md`](./07-Cadastro-Cliente-e-Mensagens.md) | Novo cadastro do cliente no checkout (campos/obrigatoriedade, CEP primeiro) e o princípio único de mensagens (toast agrupado com sinal por linha, timer de 20s, sem sobreposição). |
| 08 | [`08-Logo-Centralizado-e-Midia.md`](./08-Logo-Centralizado-e-Midia.md) | Logo da loja centralizado (front do cliente + todos os previews) e política de exclusão de mídia no Cloudinary (inclui exclusão completa da loja). |
| 09 | [`09-Cadastro-Produto-Grupos-de-Opcoes.md`](./09-Cadastro-Produto-Grupos-de-Opcoes.md) | **(Detalhado)** Reformulação dos Grupos de Opções: painel do vendedor (accordion, drag-reorder, listbox "Exibir como", validação min/máx) e os 4 formatos no front do cliente (Botões, Checkbox, Lista, Chips). Baseado em `ConceitoGrupos.png`, `Aplicacao1.png`, `Aplicacao2.png`. |
| 10 | [`10-Migracao-Regras-Backend.md`](./10-Migracao-Regras-Backend.md) | Migração das regras de negócio do frontend para o backend (backlog priorizado; P1 — preço do checkout — concluído). |
| 11 | [`11-Frete-Regiao-FreteGratis-LojaFechada.md`](./11-Frete-Regiao-FreteGratis-LojaFechada.md) | Frete por região (bairro), frete grátis a partir de R$ X e bloqueio de compra com loja fechada. |
| 12 | [`12-Chat-Tawkto-Badge-Global.md`](./12-Chat-Tawkto-Badge-Global.md) | Chat de atendimento (Tawk.to) substitui WhatsApp; badge global em todas as telas da loja; campo de configuração no wizard; auto-liberação de bairro via SignalR. |
| 13 | [`13-Aplicacao-Identidade-Visual.md`](./13-Aplicacao-Identidade-Visual.md) | Aplicação da identidade visual do protótipo NovaVersaoFront no frontend Angular/Ionic: paleta vinho, fonte Inter, novos componentes visuais (hero, chips, product cards, footer nav, sticky bars, payment options). |
| 14 | [`14-Acertos-Entrega-ErrosLayout.md`](./14-Acertos-Entrega-ErrosLayout.md) | Acertos na tela de Configuração de Entrega: botão "Taxa Única", inserção inline de bairro, modal de seleção com checkboxes espaçados e botão Confirmar, nova feature "Frete grátis hoje" com toggle + backend completo. |
| 15 | [`15-Remodelagem-FormaVenda-Grupos.md`](./15-Remodelagem-FormaVenda-Grupos.md) | **(Implementado)** Remodelagem completa do cadastro de produtos: forma de venda (único/tamanho/peso fixo/peso variável), variações com drag-reorder (sem SKU), grupos de opções simplificados (sem displayStyle/priceMode), catálogo lateral com resumo, e reflexo completo no cardápio digital (A partir de R$, R$/kg, stepper de peso, grupos radio/checkbox, Adicionar por R$). |
| 16 | [`16-Rebuild-Fluxo-Cliente-Loja.md`](./16-Rebuild-Fluxo-Cliente-Loja.md) | Spec do rebuild completo do fluxo cliente da loja usando o protótipo `NovaVersaoFront270726/Loja` como referência visual, APIs reais do backend, correções de layout/tipografia/acessibilidade e checkout server-side. |
| 17 | [`17-Plano-Implementacao-Menu-Itens-IFood.md`](./17-Plano-Implementacao-Menu-Itens-IFood.md) | Plano de implementação para lista de itens mais familiar de delivery: card inteiro abre produto, `+` apenas estético e busca sem ícone de configurações. |
| 18 | [`18-Horarios-Loja-UTC-SaoPaulo.md`](./18-Horarios-Loja-UTC-SaoPaulo.md) | Regra de horário comercial com UTC convertido para São Paulo, mensagem de loja fechada e auto-refresh do cardápio/carrinho na próxima abertura ou fechamento. |

## Resumo executivo (TL;DR)

- **Identidade visual muda:** protótipo adota vinho/coral **`#D54A51`** + fonte **Inter** + layout "app-shell" mobile centralizado. Atual usa laranja **`#f57c52`** + **Nunito Sans**.
- **O protótipo introduz telas específicas por tipo de comida** (hambúrguer com ponto da carne, pizza por tamanho → sabores → bordas/extras, açaí montável com frutas/caldas/crocantes) que **hoje não existem** no front do cliente.
- **O backend já suporta a maior parte da modelagem** necessária (variações, adicionais, opções, grupos de opções com single/multiple e min/max), mas **o front do cliente só renderiza variações, escolhas e adicionais — os `optionGroups` não são exibidos**.
- **Parametrização por `CuisineType`:** o tipo de comida da loja define o **modelo de opções do produto principal** e sugere as **categorias complementares** (Combos, Bebidas, Acompanhamentos, Sobremesas). **1 loja = 1 tipo de comida + complementos.**
- **Combos/Destaques:** definem a **ordem do cardápio** (Destaques → Combos → produto principal → complementos). Já há base no backend (`IsFeatured`, `DisplayOrder`, endpoint `featured`).
- **Busca/filtros:** manter a **busca direta na barra** (nome+descrição) e os **chips de categoria**; **remover o ícone de configuração (sliders)** ao lado da busca no protótipo — não tem finalidade.
- **Grupos de Opções (Personalizações):** reformulados com **4 formatos de exibição** (Botões, Checkbox, Lista, Chips), **reordenação por drag handle**, edição inline (sem ícone de lápis) e validação de min/máx. Detalhes no doc `09`.
- **Faltam no sistema:** meio a meio de pizza real (2 sabores por item) e cupons/descontos.
- **Recomendação central:** transformar a tela de produto em um **renderizador dirigido por dados** (grupos de opções) e usar o **`CuisineType`** para sugerir modelos de opções e a ordem do cardápio.

> Leia primeiro o `01`, `04` e `09`, que são o coração da solicitação.
