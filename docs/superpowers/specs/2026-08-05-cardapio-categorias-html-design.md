# Cardapio Categorias: Reproducao do HTML de Referencia

## Objetivo

Reproduzir no Angular a estrutura visual de `Documentacao/DashBoard/html/cardapio-categorias.html`, usando `Documentacao/DashBoard/html/assets/styles.css` como fonte de estilos. O componente deve manter os dados e comportamentos reais da aplicacao.

## Estrutura

- O `SellerAppShellComponent` continua fornecendo `app-shell`, sidebar e `main`.
- `SellerCategoriesPageComponent` reproduz, dentro de `main`, `mobile-top`, `topbar`, `notice`, `menu-tabs` e o grid de conteudo do HTML.
- Classes do HTML de referencia serao mantidas sempre que possivel, evitando uma camada paralela de nomes visuais.
- Links estaticos serao substituidos por navegacao Angular somente onde necessario.

## Dados e comportamento

- Categorias, contagem de itens, status e ordem continuam vindo do backend.
- O formulario continua suportando criacao e edicao.
- Acoes de editar, ativar/inativar, excluir e reordenar continuam funcionando.
- Estados de carregamento, erro e lista vazia ocupam o mesmo content-card sem alterar a estrutura principal.

## Estilos

- O CSS de referencia sera copiado para dentro de `frontend` para funcionar no build e no deploy.
- Nao serao introduzidas novas cores, tipografia, espacamentos ou raios fora da referencia.
- Ajustes especificos do Angular ficarao limitados a estados dinamicos e responsividade necessaria para os dados reais.

## Validacao

- Testes existentes de categorias devem continuar passando.
- O build de producao Angular deve passar.
- A estrutura do template sera comparada visualmente com o HTML de referencia antes de aplicar o mesmo tratamento a Produtos e Adicionais.
