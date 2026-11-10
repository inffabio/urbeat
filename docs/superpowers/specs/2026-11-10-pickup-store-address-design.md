# Especificação: snapshot do endereço da loja para retirada

**Data:** 2026-11-10  
**Status:** aprovado para implementação  
**Escopo:** backend .NET 9/EF Core e detalhe de pedido do cliente no Angular 20/Ionic 8

## 1. Objetivo

Para um pedido com `FulfillmentType=PickUp`, exibir, no detalhe do pedido aberto pelo histórico (e também quando o detalhe já estiver aberto), um container separado chamado **“Retirada na loja”**, abaixo da linha do tempo, com o endereço da loja.

O endereço deve ser uma cópia imutável persistida no pedido no momento da confirmação do checkout. Alterações posteriores no cadastro da loja não podem alterar o endereço apresentado naquele pedido.

### Fora de escopo

- Alterar o endereço da loja, o cadastro de endereços ou o fluxo de geocodificação.
- Exibir esse bloco em pedidos `Delivery`, no resumo/listagem de pedidos ou em telas do vendedor/motoboy.
- Corrigir retroativamente o endereço de pedidos antigos cujo snapshot não exista.
- Criar mapa, cálculo de rota, botão de navegação ou novo endpoint público.
- Alterar regras de preço, disponibilidade, status, pagamento ou conclusão do pedido.

## 2. Contexto e fluxo

### Fluxo atual

1. O cliente escolhe `Delivery` ou `PickUp` e confirma o checkout.
2. O backend cria `Order` e, atualmente, copia os campos de endereço do cliente apenas quando o fulfillment é `Delivery`.
3. O histórico do cliente abre `GET /api/orders/{orderId}` e o tracking compõe a linha do tempo, itens, pagamento e detalhes da entrega.
4. O template do tracking possui um card de “Detalhes da entrega”; pedidos de retirada não têm endereço persistido para renderizar.

### Fluxo proposto

1. Na confirmação autoritativa do checkout, o backend carrega o endereço atual da loja dentro da mesma transação que cria o pedido.
2. Para `PickUp`, copia os campos textuais do `StoreAddress` para colunas de snapshot do `Order`. A cópia ocorre antes do `SaveChanges` e não mantém dependência de leitura posterior do cadastro da loja.
3. A leitura customer-facing de detalhes retorna um objeto opcional `pickupStoreAddress` somente quando o pedido é `PickUp` e o snapshot está disponível.
4. O componente de tracking mantém a linha do tempo sem alteração e renderiza, imediatamente abaixo dela, o card independente “Retirada na loja”.
5. Ao reabrir um pedido concluído pelo histórico, o endereço vem do `Order`, portanto permanece igual ao endereço vigente na confirmação.

## 3. Persistência e snapshot

### Modelo recomendado

Adicionar ao `Order` um conjunto de colunas nullable, flat, exclusivo do snapshot de retirada:

- `PickupStoreAddressZipCode` (`varchar(8)`)
- `PickupStoreAddressStreet` (`varchar(120)`)
- `PickupStoreAddressNumber` (`varchar(20)`)
- `PickupStoreAddressNeighborhood` (`varchar(80)`)
- `PickupStoreAddressCity` (`varchar(80)`)
- `PickupStoreAddressState` (`varchar(2)`)
- `PickupStoreAddressComplement` (`varchar(120)`, nullable)
- `PickupStoreAddressReference` (`varchar(200)`, nullable)

Os nomes devem permanecer distintos dos campos `Address*` já usados pelo destino de delivery. Não adicionar FK para `StoreAddress`: o snapshot deve sobreviver à edição, troca ou remoção do cadastro da loja. Latitude/longitude não fazem parte do requisito visual e não devem ser copiados sem necessidade funcional.

No checkout, para `PickUp`, consultar o `StoreAddress` pertencente ao `store.Id`. A confirmação deve falhar de forma autoritativa e transacional se a loja não tiver endereço mínimo válido; não criar um pedido de retirada sem a origem necessária. O conjunto copiado deve refletir exatamente uma única leitura do endereço, sem buscar novamente a loja durante a renderização.

### Migration EF Core

Criar uma migration incremental no projeto `Urbeat.Infrastructure`, após a migration vigente, adicionando as oito colunas nullable em `Orders`, com os limites acima. O `Down` deve remover somente essas colunas. Atualizar `ApplicationDbContext` e o model snapshot gerado.

Não fazer backfill inferido a partir do endereço atual da loja: isso falsificaria a informação histórica. A aplicação da migration deve ser compatível com o startup relacional (`Database.MigrateAsync()`) e com o harness InMemory.

### Compatibilidade com pedidos antigos

- Pedidos existentes terão todas as colunas novas nulas.
- Um pedido antigo `PickUp` sem snapshot continua acessível e não deve falhar a leitura.
- A API retornará `pickupStoreAddress: null`; a UI não exibirá o container nem uma mensagem de endereço ausente para evitar sugerir que o endereço atual é histórico.
- Pedidos `Delivery` não devem preencher nem consumir essas colunas, inclusive os antigos.

## 4. Contrato HTTP, DTOs e autorização

### DTO

Adicionar um DTO aninhado, por exemplo `PickupStoreAddressResponseDto`, com os campos de endereço em formato de resposta (`zipCode`, `street`, `number`, `neighborhood`, `city`, `state`, `complement`, `reference`). Adicionar `PickupStoreAddressResponseDto? PickupStoreAddress` ao contrato de detalhes do pedido, serializado como `pickupStoreAddress` conforme a política existente de naming.

O mapper deve aplicar as regras:

- `FulfillmentType != PickUp` => sempre `null`, mesmo que dados inconsistentes existam.
- `PickUp` com snapshot completo => objeto preenchido.
- `PickUp` sem snapshot => `null`.
- Nunca montar o objeto lendo `StoreAddress` atual durante uma consulta de histórico.

O modelo TypeScript `OrderDetails` deve representar o mesmo objeto opcional, sem tornar o campo obrigatório para preservar respostas antigas e mocks.

### Endpoints

Não criar endpoint novo. O contrato é acrescentado ao retorno de:

- `GET /api/orders/{orderId}` — leitura do cliente autenticado, já protegida por `CustomerOnly` e filtrada por `CustomerUserId`.
- Respostas customer-facing já existentes que devolvem `OrderDetailsResponseDto` após confirmação/atualização podem carregar o mesmo campo, sem alterar a semântica dos comandos.

O endpoint de histórico resumido (`GET /api/orders/my`) não recebe o endereço. Endpoints seller, driver, rotas, impressão e SignalR não devem expor o novo snapshot por acidente. Para isso, preferir um mapper/audience explícito: o campo é populado apenas na projeção customer-facing; projeções seller/driver deixam-no nulo ou usam DTO específico sem esse campo.

A autorização de `GET /api/orders/{orderId}` continua exigindo identidade do cliente e correspondência exata do pedido. Respostas para usuário não autenticado, outro cliente ou acesso indevido permanecem `401/404` conforme o contrato atual, sem revelar existência ou endereço. Logs, eventos de outbox e payloads de terceiros não devem incluir o endereço.

## 5. Frontend afetado

### Arquivos e responsabilidades

- `shared/models/order.model.ts`: adicionar o tipo opcional do snapshot e `pickupStoreAddress?: ...` em `OrderDetails`.
- `core/services/order.service.ts` e `customer-order-tracking.service.ts`: nenhuma nova chamada; preservar o carregamento/reconciliação do detalhe e aceitar o campo opcional.
- `features/order-tracking/tracking-page.component.ts/html/scss`: derivar a condição de retirada e renderizar o container abaixo da linha do tempo, antes dos cards de itens/detalhes gerais.
- Testes do tracking e do modelo/mock de `OrderDetails`: cobrir presença, ausência e não exibição.

O card não deve reutilizar o card de endereço de entrega. O título visível deve ser exatamente **“Retirada na loja”**; dentro dele, usar um rótulo acessível “Endereço da loja” e formatar rua/número, bairro, cidade/UF e CEP quando presentes. Complemento e referência são opcionais e só aparecem se tiverem valor.

### Estados, acessibilidade e responsividade

- **Loading:** o card não aparece enquanto o pedido ainda não foi carregado; o overlay/estado de loading atual permanece.
- **Erro/indisponível:** manter a tela de pedido indisponível existente; não mostrar endereço parcial ou dado cacheado de outro pedido.
- **Snapshot presente:** mostrar card completo e estático, sem ação obrigatória.
- **Snapshot ausente em pedido antigo:** não renderizar card, texto substituto ou endereço atual da loja.
- **Delivery:** não renderizar card, mesmo que a resposta contenha campos de endereço de destino.

Usar `@if`/signals conforme o padrão atual, sem acesso inseguro a propriedades. O card deve ter hierarquia semântica (`section`/heading), texto legível, contraste dos tokens existentes, foco natural e ícone decorativo com `aria-hidden="true"`. Não depender apenas de cor. Em telas estreitas, o conteúdo deve quebrar linhas sem overflow; manter padding seguro para iOS/PWA e alvos interativos existentes de pelo menos 44px. Não inserir botão ou interação nova.

## 6. Regras de exibição

| Tipo | Snapshot | Exibir “Retirada na loja”? | Conteúdo |
|---|---|---:|---|
| `PickUp` | completo | Sim | Snapshot persistido |
| `PickUp` | ausente, pedido antigo | Não | Nenhum endereço |
| `PickUp` | parcial/inconsistente | Não | Não exibir endereço parcial; registrar métrica/log técnico sem dados pessoais |
| `Delivery` | qualquer | Não | O fluxo de endereço de entrega permanece inalterado |

“Após concluir” descreve o caso de uso prioritário de reabertura pelo histórico; a regra de renderização não deve depender exclusivamente de `Delivered`, pois um cliente pode consultar um pedido `PickUp` ainda em andamento. A posição permanece abaixo da linha do tempo em qualquer detalhe customer-facing elegível.

## 7. Testes

### Backend

- Unitário do checkout: `PickUp` copia todos os campos do `StoreAddress`; edição posterior da loja não altera o `Order`.
- Unitário do checkout: `Delivery` não popula campos de snapshot de retirada.
- Unitário/integração: loja sem endereço válido rejeita confirmação de `PickUp` sem persistir pedido parcial.
- Integração customer: cliente dono recebe `pickupStoreAddress` no detalhe, inclusive após status `Delivered`.
- Integração de autorização: outro cliente recebe `404`/resposta já contratada e nunca o snapshot; seller/driver não recebem o novo campo em suas leituras.
- Compatibilidade: pedido antigo com colunas nulas retorna `null` e não quebra desserialização; `Delivery` não expõe o campo.
- Migration: aplicar `dotnet ef`/startup em banco relacional limpo e validar model snapshot; InMemory deve continuar funcionando.

### Frontend

- Renderiza o card com título e endereço para `PickUp` com snapshot.
- Não renderiza para `Delivery`, para `PickUp` sem snapshot e para snapshot incompleto.
- Mantém o card abaixo da linha do tempo e não altera os passos de retirada.
- Reabrir detalhe já concluído mantém o snapshot retornado pela API.
- Loading, pedido indisponível/erro e troca rápida de pedido não exibem endereço stale.
- Verificação de acessibilidade: heading/landmark, texto acessível do endereço, ícones decorativos, contraste e layout em viewport estreito.

Comandos de validação esperados na implementação: `dotnet test backend/tests/Urbeat.UnitTests`, testes de integração aplicáveis (com PostgreSQL quando disponível), `npx jest --no-coverage` direcionado ao tracking e `npx ng build --configuration production`.

## 8. Critérios de aceite

1. Ao confirmar um `PickUp`, o backend persiste um snapshot independente do endereço da loja na mesma transação do pedido.
2. Após editar o endereço da loja, o detalhe histórico do pedido continua exibindo o endereço original.
3. Um cliente autorizado vê, abaixo da linha do tempo, um container separado intitulado “Retirada na loja” com o snapshot completo.
4. Pedidos `Delivery` nunca exibem esse container.
5. Pedidos antigos sem snapshot continuam abrindo normalmente e não exibem endereço inventado ou atual.
6. A leitura de outro cliente, seller, driver ou integração não vaza o novo snapshot fora do contrato autorizado.
7. A migration é reversível, aplicada pelo fluxo normal de migrations e não exige backfill destrutivo.
8. Os estados de loading/erro/indisponível, acessibilidade e responsividade não sofrem regressão.

## 9. Riscos e rollback

### Riscos

- A leitura do endereço da loja pode não estar disponível em dados legados ou em lojas mal configuradas; tratar como erro autoritativo no novo checkout e como ausência compatível na leitura antiga.
- Reutilizar `Address*` pode misturar destino de delivery com origem de retirada; por isso os campos têm prefixo próprio e DTO próprio.
- `OrderDetailsResponseDto` é compartilhado por várias audiências; um mapper customer-only é necessário para evitar exposição involuntária.
- Uma migration com colunas `NOT NULL` quebraria pedidos existentes; todas as novas colunas devem ser nullable.
- Cache/SignalR pode entregar uma versão antiga de `OrderDetails`; o frontend deve manter o campo opcional e nunca reaproveitar o endereço de outro pedido.

### Rollback

O rollout deve ser em duas etapas: primeiro migration e backend compatível (aceitando colunas nulas), depois frontend. Para desativar a feature, remover a projeção/renderização customer-facing e manter as colunas sem uso; isso preserva pedidos já criados. Só executar `Down` da migration após confirmar que nenhuma versão do backend/frontend ainda depende dos campos, pois isso elimina snapshots. Não fazer rollback por atualização do endereço da loja nem por backfill.
