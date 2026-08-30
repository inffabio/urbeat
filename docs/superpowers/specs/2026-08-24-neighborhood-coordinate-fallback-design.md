# Fallback de Coordenadas por Primeira Rua

## Objetivo

Preencher latitude e longitude de bairros que chegam sem coordenadas, com foco inicial nos bairros do RJ, usando uma referencia geografica real e deterministica: a primeira rua nomeada em ordem alfabetica.

O sistema nao deve inventar coordenadas de municipio, copiar coordenadas de outro bairro ou substituir coordenadas ja existentes.

## Fluxo de Resolucao

Para cada bairro importado:

1. Preservar latitude e longitude existentes quando ambas estiverem preenchidas e validas.
2. Quando o bairro nao tiver um par valido, consultar o e-DNE local pela primeira rua em ordem alfabetica dentro do bairro e municipio.
3. Usar o CEP dessa rua no Brasil Aberto para obter latitude e longitude.
4. Registrar a origem como `brasil_aberto_first_street`.
5. Se a rua/CEP nao existir ou nao retornar coordenadas validas, consultar o Nominatim pelo bairro, cidade e UF.
6. Usar o resultado do bairro somente se latitude e longitude forem validas, registrando `osm_nominatim`.
7. Se nenhuma fonte retornar um par valido, manter ambos os campos vazios e registrar o motivo no log.

Latitude e longitude sempre devem ser tratadas como um par. Um valor parcial deve ser descartado, nunca combinado com outro resultado.

## Limite Geografico

O e-DNE deve filtrar simultaneamente bairro e municipio, ordenar o logradouro alfabeticamente e ignorar registros sem logradouro ou CEP. Isso evita atribuir uma rua de outro municipio. O Nominatim deve receber bairro, cidade e UF como contexto.

O municipio deve ser identificado por nome e UF. Coordenadas do centro do municipio nao sao fallback valido para um bairro.

## Dados e Origem

O modelo existente `DeliveryNeighborhood` continuara armazenando:

- `Latitude`
- `Longitude`
- `Source`

As coordenadas existentes nao serao sobrescritas. A atualizacao de um bairro sem coordenadas deve preencher tambem os metadados OSM disponiveis quando a rua for encontrada.

## Snapshot CSV

Depois de concluir a insercao ou atualizacao dos bairros no banco, o processo deve exportar novamente o snapshot da UF processada para:

`backend/scripts/import/snapshots/bairros_<uf>.csv`

O arquivo deve ser gerado pela rotina existente `neighborhood_snapshot.py`, lendo o estado final da tabela `DeliveryNeighborhoods`. Assim, bairros reparados com a primeira rua passam a aparecer com suas coordenadas no CSV, enquanto bairros ainda pendentes permanecem com latitude e longitude vazias.

O export deve ocorrer tanto na importacao inicial quanto em uma rotina de reparo de coordenadas. A exportacao deve acontecer depois do commit das alteracoes e deve validar pares parciais e limites antes de substituir o arquivo.

## Resiliencia

- Respeitar o `User-Agent` e os timeouts existentes dos clientes externos.
- Falha da consulta e-DNE/Brasil Aberto nao deve interromper toda a importacao; o fluxo deve tentar o fallback Nominatim.
- Respostas sem coordenadas, coordenadas fora dos limites validos ou pares parciais devem ser ignorados.
- O cache existente por cidade continua valido; uma rotina de reparo/importacao deve ser necessaria para bairros ja armazenados sem coordenadas.
- O snapshot CSV da UF deve ser atualizado ao final de cada importacao ou reparo concluido.

## Testes

Adicionar testes unitarios para:

- escolher a primeira rua alfabeticamente no e-DNE;
- ignorar ruas sem nome ou sem coordenadas;
- preservar coordenadas existentes;
- rejeitar pares parciais ou fora dos limites;
- usar Nominatim quando nenhuma rua/CEP valido existir;
- manter coordenadas vazias quando ambas as fontes falharem.

Adicionar teste de integracao ou contrato para confirmar que a importacao grava `Source` e o par de coordenadas esperado.

## Fora de Escopo

- Integracao com Google Maps, Overpass amplo ou API paga.
- Geocodificacao de todos os bairros automaticamente durante o deploy.
- Uso de centroide municipal como aproximacao.
- Alteracoes na forma como o frontend calcula distancia.
