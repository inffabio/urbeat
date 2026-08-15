# Fluxo de Bairros por UF

O fluxo vale para todas as 27 unidades federativas do Brasil, incluindo o
Distrito Federal. A UF e validada contra a tabela oficial de codigos IBGE;
nenhuma lista de municipios e mantida no codigo.

## Regra operacional

1. O importador consulta os municipios oficiais na API IBGE.
2. Para cada codigo IBGE de municipio, consulta os bairros na API Brasil Aberto.
3. O importador tenta geocodificar os bairros ativos, preservando coordenadas
   existentes, antes de gerar `snapshots/bairros_<uf>.csv`.
4. Quando encontrada, a coordenada representa aproximadamente a primeira
   rua/CEP encontrada no bairro: o e-DNE/CEP e consultado primeiro e
   OSM/Nominatim e usado somente como fallback real. A origem da coordenada e
   registrada em `Source`; nunca e usado centroide do municipio.
5. Restauracoes usam somente CSV. Restaurar nunca consulta API, e a operacao
   e idempotente.

Um snapshot so e criado depois que os bairros foram importados no banco e a tentativa de geolocalizacao terminou. Ele aceita bairros pendentes sem geolocalizacao: nesses casos, os campos
`Latitude` e `Longitude` permanecem vazios no CSV. Pares parciais (uma coordenada
preenchida e a outra vazia) e coordenadas invalidas sao rejeitados. Bairros
pendentes nao bloqueiam a publicacao do CSV; as mensagens distinguem bairros
totais, geolocalizados e pendentes. Nao execute importacao real sem
banco/configuracao; testes nao fazem chamadas reais nem devem expor segredos.

## Comandos

Executar a partir de `backend/scripts/import/`:

```bash
# Qualquer UF: MG, ES, RJ, SP, DF etc.
python brasil_aberto_import.py --uf MG

# Compatibilidade com os entrypoints antigos
python brasil_aberto_import.py          # RJ
python brasil_aberto_import_sp.py      # SP

# Geocodificacao por UF
python geocode_via_cep.py --uf ES
python geocode_via_cep_sp.py            # SP

# Restauracao sem API
python restore_neighborhoods.py restore --file snapshots/bairros_mg.csv
```

Variaveis necessarias para importacao/geocodificacao:

- `BRASIL_ABERTO_API_KEY`
- `URBEAT_DB_PASSWORD`
- `URBEAT_DB_HOST`, `URBEAT_DB_NAME` e `URBEAT_DB_USER` (opcionais)
- `URBEAT_DNE_DB` (opcional; padrao `/home/dexter/dne.db`)

Nao registre chaves reais na documentacao ou no repositorio.

## Snapshots

Cada arquivo possui as colunas `Uf`, `CityIbgeCode`, `City`, `Neighborhood`,
`NormalizedName`, `Latitude`, `Longitude`, `Source` e `IsActive`. O municipio
na restauracao e resolvido exclusivamente por `Uf + CityIbgeCode`.

O `UPSERT` por `CityId + NormalizedName` garante idempotencia. Latitude e
longitude existentes no banco sao preservadas. A restauracao aceita e preserva
campos vazios, rejeita pares parciais ou fora da faixa e valida todas as linhas
antes de alterar o banco. Restaurar via CSV nunca inventa coordenadas.

## Fontes

- Municipios e codigos: [API IBGE](https://servicodados.ibge.gov.br/api/v1/localidades/estados/33/municipios)
- Bairros: API Brasil Aberto `districts-by-ibge-code`
- CEPs: [Correios e-DNE](https://github.com/cauethenorio/edne-correios-loader)
- Coordenadas: API Brasil Aberto `v2/zipcode/{cep}`
- Fallback de coordenadas: [OpenStreetMap Nominatim](https://nominatim.openstreetmap.org/)
