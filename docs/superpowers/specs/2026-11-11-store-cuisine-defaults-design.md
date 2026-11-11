# Categorias padrão de loja — design

## Objetivo

Substituir as categorias globais padrão de loja por uma lista inicial de 20
opções, apresentada em ordem alfabética tanto no cadastro inicial quanto no
dashboard do vendedor.

## Categorias padrão

- Açaíteria
- Batata Recheada
- Cachorro-quente (Hot Dog)
- Cafeteria
- Caldos e Sopas
- Comida Japonesa
- Comida Saudável (Fitness)
- Confeitaria
- Creperia
- Doceria
- Espetinhos e Churrasco
- Hamburgueria
- Marmitaria
- Massas e Lasanhas
- Padaria
- Pastelaria
- Pizzaria
- Salgaderia
- Sorveteria
- Tapiocaria

## Arquitetura e persistência

- Atualizar `CuisineTypeDefaults` como fonte única dos nomes padrão e do
  reconhecimento de categorias protegidas.
- Manter o `CuisineTypeSeeder` idempotente: cria categorias novas, reativa e
  corrige nomes das categorias aprovadas e marca somente as 20 como padrão.
- Categorias antigas globais que não fazem parte da nova lista:
  - se estiverem vinculadas a uma loja, serão copiadas como categorias privadas
    daquela loja e a referência da loja será preservada;
  - se não tiverem vínculo, serão removidas.
- Categorias personalizadas já existentes permanecem intactas.
- Atualizar o modelo/seed do EF Core e adicionar migration somente se a
  alteração de dados/modelo exigir. O startup continuará aplicando migrations e
  executando o seeder em banco relacional.

## Frontend

O cadastro inicial usa `StoreConfigPageComponent`. O dashboard de informações
da loja (`SellerStoreInfoPageComponent`) herda esse componente, portanto a
alteração será compartilhada sem duplicar lógica ou criar um segundo seletor.

O componente continuará permitindo categorias personalizadas, mas exibirá a
união entre padrões e categorias da loja ordenada por `localeCompare` em
`pt-BR`, sem numeração visível.

## Compatibilidade e validação

- Não alterar o valor de `CuisineType` já salvo em lojas.
- Não remover categorias personalizadas ou categorias antigas ainda usadas.
- Manter unicidade normalizada sem acentos e sem distinção de maiúsculas.
- Garantir que as 20 categorias retornem pela API global e apareçam no cadastro
  de uma nova loja.
- Garantir que uma loja existente com categoria antiga continue vendo e usando
  sua categoria privada.

## Testes e aceite

- Testes do `CuisineTypeDefaults` para os 20 nomes, normalização e ordem.
- Testes do `CuisineTypeSeeder` para criação, atualização, remoção de globais
  órfãos e conversão de globais antigos vinculados em categorias privadas.
- Testes do cadastro/dashboard para a lista ordenada, ausência de numeração,
  preservação de categoria personalizada e seleção da categoria padrão.
- `dotnet build backend/Urbeat.sln`.
- Testes unitários backend direcionados e `dotnet test backend/tests/Urbeat.UnitTests`.
- `npx jest --no-coverage` e `npx ng build --configuration production`.

Critério de aceite: novas lojas veem exatamente as 20 categorias acima, em
ordem alfabética e sem numeração; lojas existentes mantêm categorias antigas
não pertencentes à lista como se fossem categorias criadas pelo cliente.
