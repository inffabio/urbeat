# Populacao de Bairros do RJ

## Visao geral

Base de bairros do estado do Rio de Janeiro populada com **2.745 bairros** cobrindo **92 municipios** (100%), com nome, cidade e coordenadas geograficas (latitude/longitude).

## Fontes utilizadas

| Recurso | Proposito | Fonte |
|---------|-----------|-------|
| Municipios | Lista oficial dos 92 municipios + codigos IBGE | [API IBGE](https://servicodados.ibge.gov.br/api/v1/localidades/estados/33/municipios) |
| Bairros | Nomes dos bairros por municipio | [Brasil Aberto](https://brasilaberto.com) API `districts-by-ibge-code` |
| CEPs | CEP de cada bairro para geolocalizacao | [Correios e-DNE](https://github.com/cauethenorio/edne-correios-loader) |
| Coordenadas | Latitude/longitude via CEP | [Brasil Aberto](https://brasilaberto.com) API CEP v2 |

## API Keys

- **Brasil Aberto**: `Y0rIwm5rEtWymrRul3IrUcSPjcVla9CDD1z8CuC1f1ubZueuciTPxzBk8UQNAXRe`
  - Header: `Authorization: Bearer {key}`
  - Endpoints: `GET /v1/districts-by-ibge-code/{ibgeCode}`, `GET /v2/zipcode/{cep}`

## Processo completo

### 1. Municipios (SQL)

Os 92 municipios do RJ foram inseridos na tabela `Cities` com seus codigos IBGE oficiais. Duplicatas (variantes com/sem acento) foram removidas via `unaccent()`.

Tabela: `urbeat."Cities"` — colunas relevantes: `Id`, `Name`, `Uf`, `IbgeCode`.

```sql
-- Exemplo: inserir municipios faltantes
INSERT INTO urbeat."Cities" ("Id", "Name", "Uf", "CreatedAtUtc")
SELECT gen_random_uuid(), v.name, 'RJ', now()
FROM (VALUES ('Paracambi'), ('Rio das Ostras'), ...) AS v(name)
WHERE NOT EXISTS (
    SELECT 1 FROM urbeat."Cities" c
    WHERE c."Uf" = 'RJ' AND public.unaccent(upper(c."Name")) = public.unaccent(upper(v.name))
);

-- Atualizar codigos IBGE
UPDATE "Cities" SET "IbgeCode" = '3304524'
WHERE "Uf" = 'RJ' AND unaccent(upper("Name")) = unaccent(upper('Rio das Ostras'));
```

### 2. Bairros (Python + Brasil Aberto)

Script `brasil_aberto_import.py` executado no servidor (`/opt/urbeat/`):

1. Le todas as cidades do RJ com `IbgeCode` da tabela `Cities`
2. Para cada cidade, chama `GET /v1/districts-by-ibge-code/{ibgeCode}`
3. Insere bairros na tabela `DeliveryNeighborhoods` com `ON CONFLICT DO NOTHING`

```python
# Trecho principal
url = f"https://api.brasilaberto.com/v1/districts-by-ibge-code/{ibge_code}"
req = urllib.request.Request(url)
req.add_header("Authorization", f"Bearer {API_KEY}")
req.add_header("User-Agent", "Urbeat/1.0")
```

Tabela: `urbeat."DeliveryNeighborhoods"` — campo `"Source" = 'brasil_aberto'`.

### 3. Coordenadas (Correios e-DNE + Brasil Aberto CEP v2)

#### 3.1 Base dos Correios

O pacote [`edne-correios-loader`](https://github.com/cauethenorio/edne-correios-loader) baixa e importa o e-DNE Basico dos Correios (~1.6M CEPs) para SQLite:

```bash
uvx edne-correios-loader load --database-url sqlite:////tmp/dne.db
```

Tabela resultante: `cep_unificado` com colunas `cep`, `logradouro`, `bairro`, `municipio`, `uf`.

#### 3.2 Cruzamento e geolocalizacao

Script `geocode_via_cep.py`:

1. Para cada bairro sem coordenadas, consulta o SQLite por `bairro + municipio` → CEP
2. Chama `GET /v2/zipcode/{cep}` no Brasil Aberto → extrai `coordinates.latitude` e `coordinates.longitude`
3. Atualiza `DeliveryNeighborhoods` com `UPDATE SET "Latitude" = ..., "Longitude" = ...`

```python
def get_cep(bairro, cidade):
    cur.execute("SELECT cep FROM cep_unificado WHERE bairro = ? AND municipio = ? LIMIT 1",
                (bairro, cidade))

def get_coordinates(cep):
    url = f"https://api.brasilaberto.com/v2/zipcode/{cep}"
    # Response: {"result": {"coordinates": {"latitude": -22.5, "longitude": -41.9}}}
```

#### 3.3 Rate limit

Delay de 150ms entre chamadas a API do Brasil Aberto para respeitar limites.

## Resultado final

```
 urbeat=> SELECT COUNT(*) FROM "Cities" WHERE "Uf" = 'RJ';
  92

 urbeat=> SELECT "Source", COUNT(*) FROM "DeliveryNeighborhoods" GROUP BY "Source";
     Source     | count
 ---------------+-------
  brasil_aberto |  2745

 urbeat=> SELECT COUNT(*) FROM "DeliveryNeighborhoods" WHERE "Latitude" IS NOT NULL;
   2745
```

- **92/92** municipios do RJ com codigo IBGE
- **2.745** bairros (fonte: `brasil_aberto`)
- **2.745/2.745** bairros com latitude e longitude (100%)
- **0** dados OSM — removidos por serem redundantes e incompletos

## Schemas relevantes

### Cities
| Coluna | Tipo | Descricao |
|--------|------|-----------|
| Id | uuid | PK |
| Name | varchar(255) | Nome do municipio |
| Uf | varchar(2) | Sigla do estado |
| IbgeCode | varchar(20) | Codigo IBGE do municipio |

### DeliveryNeighborhoods
| Coluna | Tipo | Descricao |
|--------|------|-----------|
| Id | uuid | PK |
| CityId | uuid | FK → Cities |
| City | varchar(80) | Nome do municipio (denormalizado) |
| Neighborhood | varchar(80) | Nome do bairro |
| NormalizedName | varchar(255) | Nome normalizado (lowercase) |
| Latitude | double | Obtido via CEP |
| Longitude | double | Obtido via CEP |
| Source | varchar(50) | `'brasil_aberto'` |
| IsActive | boolean | `true` |

### Indices
- `(Neighborhood, City)` — unique
- `(CityId, NormalizedName)` — unique (filtered: `"CityId" IS NOT NULL`)

## Notas

- Google Places API (New) foi testada com grid search 2x2 ate 4x4, mas retornou 0 resultados para todas as cidades — a API key nao tem o endpoint `places:searchText` habilitado. O codigo do `GooglePlacesTextSearchImporter` permanece no projeto para uso futuro.
- OpenStreetMap (Overpass API) foi removido como fonte — falhava com HTTP 504 para varias cidades e retornava cobertura parcial.
- IBGE Setores Censitarios 2010 foi removido como fonte — shapefile desatualizado (2010), nao cobre municipios criados apos o censo.
