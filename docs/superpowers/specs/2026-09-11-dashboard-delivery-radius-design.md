# Dashboard Delivery Radius Configuration

## Objetivo

Adicionar à tela `Configurações -> Bairros` um container lateral para editar o
raio máximo de entrega da loja, usando o mesmo conceito do primeiro passo do
wizard de configuração da loja.

## Comportamento

- O campo inicia com o `maxDeliveryRadiusKm` carregado da loja.
- O valor editado altera imediatamente a consulta e a quantidade de bairros
  disponíveis na tela.
- A regra de distância continua sendo calculada pelo backend.
- Ao reduzir o raio, bairros selecionados que ficarem fora do novo limite são
  removidos automaticamente da lista da loja.
- A alteração do raio e as remoções ficam pendentes até o usuário clicar no
  botão de salvar já existente.
- Aumentar o raio permite que novos bairros elegíveis apareçam para seleção,
  sem salvar automaticamente a configuração.
- O raio deve ser positivo e seguir a validação existente do wizard.

## Arquitetura

O endpoint autenticado de bairros por loja aceitará um raio opcional para
consulta. Esse parâmetro servirá somente para pré-visualizar a configuração em
edição; a persistência ocorrerá no endpoint de configuração de entrega quando
o botão de salvar for acionado.

O payload de configuração de entrega receberá `maxDeliveryRadiusKm`, permitindo
salvar o raio e o `deliveryAreas` na mesma operação. O backend validará o valor
e atualizará a loja de forma transacional junto com as áreas.

A tela continuará reutilizando `StoreDeliveryPageComponent` para estado,
carregamento, filtragem e persistência. `SellerNeighborhoodsPageComponent`
fornecerá apenas o container lateral e o comportamento específico de remoção
de áreas fora do raio.

## Interface

O novo container lateral exibirá:

- título `Alcance de entrega`;
- explicação curta sobre a distância em quilômetros;
- input numérico com `min="1"`, `step="1"` e unidade `km`;
- quantidade de bairros disponíveis após a filtragem;
- indicação de alterações pendentes quando o raio for modificado.

No desktop, o container ocupará a coluna lateral já usada pela página. Em
telas menores, ele será empilhado sem reduzir os alvos de toque abaixo de 44px.

## Estados e erros

- Enquanto a nova lista é carregada, preservar os bairros selecionados e
  indicar atualização sem bloquear a página inteira.
- Para raio vazio, zero, negativo ou inválido, não consultar nem remover áreas;
  exibir o estado de validação e manter o último raio válido.
- Se a consulta falhar, manter a última lista válida, informar o erro e não
  apagar bairros selecionados.
- Se o salvamento falhar, manter o raio e as áreas em edição marcados como não
  salvos.

## Testes e aceite

Os testes devem cobrir:

1. hidratação do raio inicial;
2. consulta com raio editado;
3. aumento exibindo bairros adicionais;
4. redução removendo áreas selecionadas fora do raio;
5. raio inválido sem remoção;
6. payload de salvar contendo raio e áreas;
7. persistência e validação no backend;
8. manutenção do layout estrutural no mobile.

O recurso estará concluído quando o usuário puder alterar o raio, visualizar a
lista recalculada, perder automaticamente apenas os bairros fora do novo
limite e persistir tudo pelo botão de salvar existente.
