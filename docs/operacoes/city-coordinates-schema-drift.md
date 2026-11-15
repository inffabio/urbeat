# Correção de schema drift em `Cities`

Em 14/11/2026, a produção possuía a migration
`20261114025008_AddCnefeGeographicBase` aplicada, mas a tabela `Cities` não
continha `Latitude` e `Longitude`, embora essas propriedades já fizessem parte
da entidade `City`. Consultas que materializavam cidades, incluindo a
resolução usada pelo endpoint de bairros por raio, falhavam com PostgreSQL
`42703`.

A migration incremental `20261115021032_AddCityCoordinates` adiciona as duas
colunas como `double precision` nullable. Ela não preenche coordenadas
existentes nem altera a semântica do raio; o `Down` remove somente essas duas
colunas. Aplicações devem executar as migrations normalmente antes de usar o
fluxo de bairros por raio.
