# Exclusão de loja e manutenção de imagens Cloudinary

Data: 2026-11-13

## Contexto

O Urbeat não possuía exclusão completa de loja. As imagens Cloudinary ficavam em
`stores/{slug}/{logo|banner|products|store-media}` e, durante o cadastro, em
`stores/_pending/{userId}/{type}`. A substituição de logo/banner e a exclusão de produto já
removiam o asset antigo, mas uma loja excluída deixaria logo, banner, imagens de produtos e
uploads de `_pending` órfãos no provedor.

Além disso, não havia como auditar com segurança quais assets ainda eram referenciados pelo
banco antes de apagar qualquer coisa. Qualquer rotina de limpeza precisava ser capaz de provar
que um asset não é usado por nenhuma loja/produto antes de destruí-lo.

## Objetivos

1. Permitir que o proprietário autenticado exclua sua loja inteira.
2. Remover do Cloudinary logo, banner e imagens dos produtos da loja, sem acoplar a transação
   do banco à rede.
3. Oferecer rotina administrativa de varredura (dry-run) e execução confirmada que remove
   somente órfãos sob `stores/`, incluindo `_pending`, respeitando idade mínima.
4. Preservar dados globais (`DeliveryNeighborhoods`, categorias de culinária padrão, cidades)
   e o histórico de pedidos.

## Decisões

### Exclusão da loja

- Contrato `IStoreService.DeleteForOwnerAsync(ownerUserId, storeId, ipAddress, ct)`.
- Endpoint `DELETE /api/stores/{storeId:guid}` sob a policy `SellerOnly` existente.
- A loja é carregada e o `OwnerUserId` é comparado antes de qualquer mutação. Loja
  inexistente retorna 404; loja de outro vendedor retorna 403; sucesso retorna 204.
- As URLs de mídia são coletadas **antes** da exclusão: `Stores.LogoUrl`, `Stores.BannerUrl`
  e `Products.ImageUrl` da loja, deduplicadas por `OrdinalIgnoreCase`.
- A mutação do banco roda em transação quando o provider é relacional:
  1. limpa `Stores.CuisineTypeId` quando aponta para uma categoria privada da própria loja;
  2. remove a categoria privada (FK `CuisineTypes.StoreId -> Stores` é `Restrict`);
  3. remove `DeliveryRoutes` antes de `StoreDeliveryDrivers` (FK
     `DeliveryRoutes.StoreDeliveryDriverId` é `Restrict`);
  4. remove `Products` antes de `ProductCategories` (FK `Products.CategoryId` é `Restrict`);
  5. remove templates de grupos de opções, adicionais e grupos de adicionais, endereço, áreas,
     horários, tempos de entrega, verificações de telefone, clientes da loja, configurações de
     gateway, logs de transação, configuração de impressora, `OrderPrintJobs` e
     `SellerSubscriptions` (esta última não tem FK, mas tem índice único em `StoreId`);
  6. remove a loja.
- Dados preservados deliberadamente: pedidos e avaliações (histórico), `DeliveryDrivers`
  globais, `DeliveryNeighborhoods`, cidades e categorias de culinária padrão.
- Estratégia de delete: `ExecuteDeleteAsync` no provider relacional; `ToListAsync` +
  `RemoveRange` no provider InMemory dos testes, seguindo o padrão já usado em
  `UpdateDeliveryConfigAsync`.
- Somente **após o commit**, `IImageUploadService.DeleteAsync` é chamado para cada URL. Cada
  falha é registrada e contabilizada, mas nunca desfaz o banco. O resultado expõe
  `AssetsAttempted`, `AssetsDeleted` e `AssetsFailed`; um `AuditLog` (`StoreDeleted`) é gravado
  em best-effort.
- `CloudinaryImageUploadService.DeleteAsync` passou a lançar exceção quando o provedor rejeita
  a destruição (exceto `not found`, tratado como sucesso idempotente). Todos os chamadores
  existentes já envolvem a chamada em `try/catch`, então a mudança apenas torna a falha
  observável e auditável.

### Manutenção de órfãos

- Abstração separada `ICloudinaryImageCatalog` (`ListImagesAsync` por prefixo com cursor e
  `DestroyAsync` por public id). `IImageUploadService` continua responsável apenas por upload e
  delete por URL.
- `CloudinaryImageCatalog` usa `ResourceType.Image`, `Type = "upload"`, paginação por
  `NextCursor` e no máximo 500 resultados por página (limite do provedor).
- `StoreImageMaintenanceService` orquestra banco + Cloudinary:
  - prefixo fixo `stores/`;
  - página até o limite de assets configurado, reportando `Truncated`;
  - descarta recursos fora do prefixo;
  - extrai public ids referenciados de `Stores.LogoUrl`, `Stores.BannerUrl`,
    `Products.ImageUrl` e de qualquer URL Cloudinary embutida em `LandingPageContents.Value`;
  - compara public ids com `OrdinalIgnoreCase` (protege mais, nunca menos);
  - protege recursos sem timestamp de criação parseável;
  - protege recursos mais novos que a idade mínima configurável — isso cobre `_pending` e também
    um formulário de loja existente que ainda não foi salvo;
  - `_pending` é marcado como `IsPending` apenas para auditoria; a regra de idade é a mesma.
- A varredura é estritamente read-only. A execução exige `confirm=true`; sem confirmação o
  serviço nem lista o provedor.
- Antes de cada destruição o prefixo é revalidado. Falha individual não interrompe o lote; o
  resultado lista `DeletedPublicIds` e `Failures` (public id + erro sanitizado).
- Limites: `MaxAssetsPerScan` (default 500), `MaxDeletionsPerRun` (default 100),
  `MinimumAssetAgeMinutes` (default 1440). Requests só podem reduzir os limites configurados.
- Endpoints administrativos em `api/admin/cloudinary`, sob `AdminOnly`:
  - `GET /orphan-images?maximumAssets=&minimumAgeMinutes=` — dry-run, devolve contagens e
    public ids candidatos;
  - `POST /orphan-images/cleanup` — corpo
    `{ "confirm": true, "maximumAssets": 500, "minimumAgeMinutes": 1440, "maximumDeletions": 100 }`.
- Nenhum job automático é registrado no startup; a rotina é operacional e explícita.
- Auditoria: `AuditLog` com evento `CloudinaryOrphanCleanup` contendo apenas contagens. Segredos,
  URLs completas e credenciais nunca entram na resposta ou no log.

### Normalização de URL

`CloudinaryPublicId.ExtractFromUrl` é a regra única usada pelo upload e pela manutenção:

- aceita apenas URL absoluta de host Cloudinary (`res.cloudinary.com`/`*.cloudinary.com`) com
  segmento `/upload/`;
- encontra o segmento de versão (`v` + dígitos) e inicia depois dele; se não houver versão,
  inicia imediatamente após `upload`, preservando subpastas;
- remove a extensão do último segmento;
- decodifica caracteres escapados e remove barras nas extremidades;
- `IsUnderPrefix` exige prefixo normalizado com `/` e remainder não vazio, rejeitando
  `stores`, `stores-old/...` e `other/stores/...`.

### Frontend (vendedor)

- `StoreService.deleteStore(storeId)` chama `DELETE /api/stores/{storeId}` e devolve
  `Observable<void>`. Nenhum corpo, URL ou senha é enviado: a autorização é o token do vendedor e
  o backend valida o ownership. A limpeza Cloudinary continua sendo responsabilidade exclusiva
  do backend.
- A ação vive em `/app/configuracoes/informacoes` (`SellerStoreInfoPageComponent`), numa zona
  destrutiva renderizada fora da grade de cartões e apenas quando `getMyStore` retornou uma loja.
- Confirmação explícita em duas etapas: o botão "Excluir loja" abre o painel, o vendedor precisa
  digitar `excluir` (comparação case-insensitive, com `trim`) e só então o botão definitivo
  habilita. Nenhum `window.confirm` é usado, o que mantém o fluxo testável e acessível.
- Estados: `deleting` desabilita botões e mostra spinner; `deleteError` exibe a falha inline,
  mantendo o painel aberto para nova tentativa.
- Mapeamento de falhas:

  | Status | Comportamento |
  | --- | --- |
  | 401 | `AuthService.logout()`, toast de sessão expirada, `/login-vendedor` |
  | 403 | toast de permissão negada, painel fechado, sem navegação |
  | 404 | tratado como loja já removida: toast informativo e `/configurar-loja` |
  | outros | erro inline + toast, painel aberto para retry |

- Sucesso (204): toast de confirmação e navegação para `/configurar-loja`. O token do vendedor
  continua válido, então o destino é o wizard de criação de loja — o estado "sem loja" — e não a
  tela de login.
- `StoreConfigPageComponent` (wizard) não exibe a zona destrutiva; apenas a página de
  configurações do dashboard herda o comportamento.

## Contratos

| Método | Rota | Autorização | Efeito |
| --- | --- | --- | --- |
| `DELETE` | `/api/stores/{storeId}` | `SellerOnly` + ownership | Remove a loja e dependências; 204/403/404 |
| `GET` | `/api/admin/cloudinary/orphan-images` | `AdminOnly` | Dry-run de órfãos; nunca apaga |
| `POST` | `/api/admin/cloudinary/orphan-images/cleanup` | `AdminOnly` + `confirm=true` | Apaga somente órfãos sob `stores/` |

Contrato consumido pelo frontend:

| Camada | Chamada | Resultado |
| --- | --- | --- |
| `StoreService.deleteStore` | `DELETE /api/stores/{storeId}` | `204` → `Observable<void>` completo; `401/403/404/5xx` → `HttpErrorResponse` para o componente |


## Riscos e mitigações

| Risco | Mitigação |
| --- | --- |
| Cloudinary indisponível durante exclusão | Banco commita primeiro; falha contabilizada e logada; nunca rollback por rede |
| Asset referenciado ser apagado como órfão | Referências lidas do banco antes da varredura e revalidadas por public id antes de cada delete |
| Upload recém-criado ser apagado | Idade mínima configurável (default 24h), inclusive para `_pending` |
| Cursor infinito do provedor | Limite de assets e de páginas por chamada |
| Exclusão acidental de dados globais | Lista explícita de tabelas store-scoped; `DeliveryNeighborhood`/`CuisineType` padrão nunca entram no delete |
| Execução operacional ampla demais | Prefixo fixo, `confirm=true`, limites por chamada e resposta com contagens |
| Public id divergente entre upload e manutenção | Normalizador único `CloudinaryPublicId` usado pelos dois fluxos |

## Riscos residuais

- Pedidos permanecem com `StoreId` da loja excluída (não há FK para `Stores`). É intencional para
  preservar histórico; consultas por loja ficam inacessíveis após a exclusão.
- Uma URL compartilhada entre duas lojas seria apagada na exclusão de uma delas. O modelo não
  prevê compartilhamento; a varredura de órfãos continua protegendo referências remanescentes.
- A validação SQL de ordenação de FKs depende de PostgreSQL real; os testes de integração usam
  InMemory e os testes relacionais existentes exigem Docker/Testcontainers.
