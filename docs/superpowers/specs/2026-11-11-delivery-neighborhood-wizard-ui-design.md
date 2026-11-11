# Ajustes do wizard de bairros e raio de entrega — design

## Objetivo

Ajustar a etapa **Entrega → Cadastro de bairros** do wizard para tornar a
seleção de bairros mais clara e concentrar nela toda a configuração do raio de
alcance.

## Alterações de interface

- Renomear o botão **“Inserir Bairros”** para **“Selecionar Bairros”**.
- Aplicar ao botão o fundo verde claro do sistema, usando os tokens existentes,
  especialmente `var(--app-success-soft)` e a cor de sucesso correspondente.
- Remover da barra superior o botão **“Aplicar valor a todos”**. O controle de
  taxa em lote dentro do modal de seleção permanece, pois é usado no momento em
  que bairros são selecionados.
- Remover o ícone `navigate-outline` exibido no centro do estado vazio.
- Alterar a mensagem para exatamente:
  `Nenhum bairro cadastrado. Use o botão 'selecionar Bairros' para selecionar`.
- Remover o campo **“Raio máximo (km)”** da primeira página **Loja**.
- Adicionar o campo **“Raio máximo (km)”** na etapa **Entrega → Cadastro de
  bairros**, ao lado do botão **“Visualizar mapa”**.

## Arquitetura e comportamento

- A lógica existente de raio em `StoreDeliveryPageComponent` será preservada:
  validação de valor positivo, preview assíncrono, filtragem de bairros,
  atualização do mapa, remoção de áreas fora do raio quando aplicável,
  bloqueio de salvamento durante atualização e persistência no backend.
- A primeira página (`StoreConfigPageComponent`) deixará de renderizar,
  carregar, validar e enviar o raio, evitando duas fontes de configuração. A
  página de entrega continuará carregando e salvando `maxDeliveryRadiusKm`.
- O fluxo de dashboard que reutiliza a página de entrega continuará com o mesmo
  controle, sem criar estado duplicado.
- O novo controle deverá manter acessibilidade, label associado, mensagens de
  erro e comportamento responsivo/mobile.

## Escopo de arquivos provável

- `frontend/src/app/features/store-config/store-config-page.component.ts`
- `frontend/src/app/features/store-config/store-config-page.component.html`
- `frontend/src/app/features/store-config/delivery/store-delivery-page.component.html`
- `frontend/src/app/features/store-config/delivery/store-delivery-page.component.scss`
- testes dos dois componentes.

Não haverá alteração de contrato backend, banco, API, mapa ou regras de cálculo.

## Testes e aceite

- Testar que a primeira página não exibe nem envia o campo de raio.
- Testar que a página de bairros exibe o campo ao lado de “Visualizar mapa” e
  mantém seu valor, validação e salvamento.
- Testar texto, cor/classe do botão, remoção do botão de aplicação em lote, ícone
  removido e mensagem vazia exata.
- Testar cadastro inicial e dashboard responsivos sem duplicação de controles.
- Executar specs direcionados, `npx jest --no-coverage` e
  `npx ng build --configuration production`.

Critério de aceite: o raio aparece exclusivamente em **Entrega → Cadastro de
bairros**, ao lado de **Visualizar mapa**, e todas as cinco alterações visuais e
textuais solicitadas são aplicadas sem regressão do comportamento de entrega.
