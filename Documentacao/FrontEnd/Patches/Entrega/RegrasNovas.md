
---

# Plano de implementacao executado — Entrega

## Resumo do que foi feito

### Backend - Entidades

| Entidade | Arquivo | Descricao |
|----------|---------|-----------|
| `City` | `Domain/Entities/City.cs` | Nome, UF, IBGE, OsmId |
| `DeliveryNeighborhood` | `Domain/Entities/DeliveryNeighborhood.cs` | NormalizedName, Latitude, Longitude, Source, CityId FK, IsActive |

### Backend - Servicos

| Metodo | Descricao |
|--------|-----------|
| `GooglePlacesTextSearchImporter.ImportAsync` | Importa bairros via Google Places API (New) com grid search |
| `OsmService.GetNeighborhoodsMapAsync` | Dados para mapa Leaflet — apenas bairros com taxa configurada |
| `OsmService.SearchNeighborhoodsAsync` | Busca incremental com filtro por nome |

### Backend - Endpoints

| Metodo | Rota | Descricao |
|--------|------|-----------|
| POST | `/api/admin/import-neighborhoods-google?uf=RJ` | Dispara importacao Google Places (Hangfire) |
| GET | `/api/neighborhoods/cities/{id}/search?search=X&storeId=Y` | Busca bairros |
| GET | `/api/neighborhoods/cities/{id}/map?storeId=Y` | Dados do mapa |
| GET | `/api/neighborhoods/cities` | Lista cidades |

### Backend - Migrations

- `20260622181914_AddCityAndNeighborhoodOsmFields.cs` — Tabela `Cities` + colunas em `DeliveryNeighborhoods`

### Frontend - Modelos

Novas interfaces TypeScript em `store.model.ts`:
- `ImportNeighborhoodsResult`, `NeighborhoodSearchResult`, `NeighborhoodMapResponse`, `NeighborhoodMapItem`, `CityDto`

### Frontend - StoreService

Novos metodos em `store.service.ts`:
- `importNeighborhoods`, `searchNeighborhoods`, `getNeighborhoodsMap`, `getCities`

### Frontend - Pagina de entrega (`store-delivery-page.component`)

**Fluxo de carregamento de bairros:**

```
Entra em /configurar-loja/entrega
       ↓
loadStoreCity() → getStoreAddress()
       ↓
loadExistingNeighborhoods(city)
       ↓
  lista vazia? ──sim──→ mostra modal "Importar bairros"
       ↑                   ↓
       │           botao "Importar bairros"
       │           polling a cada 3s ate ter dados
       │
       └──nao──→ carrega bairros existentes
```

**Modal de importacao:**
- Botao "Importar bairros" visivel quando nao ha bairros
- Polling automatico com fallback para atualizar lista

### Mapa (Leaflet) - Regra de exibicao

O mapa (`GET /api/neighborhoods/cities/{cityId}/map?storeId=X`) **so exibe bairros que ja tem taxa de entrega configurada** (`StoreDeliveryArea`).

Cor dos pinos:
- Verde = taxa <= R$ 5
- Amarelo = taxa R$ 5-12
- Vermelho = taxa > R$ 12
- Cinza = bairro inativo

### Populacao da base de bairros

A base de bairros do RJ foi populada conforme documentado em [`PopulacaoBairrosRJ.md`](../../Backend/PopulacaoBairrosRJ.md):
- **Fonte principal**: Brasil Aberto API (`districts-by-ibge-code`)
- **Coordenadas**: Correios e-DNE + Brasil Aberto CEP v2
- **Resultado**: 2.745 bairros em 92 municipios, 100% com coordenadas

OSM e Google Places foram testados mas nao produziram resultados satisfatorios.

### Nao implementado (futuro)

- Tabela `neighborhood_aliases` para apelidos de bairros
- Busca de bairro pelo CEP do cliente no checkout
