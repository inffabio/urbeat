# 03 — Inventário de Funcionalidades e Matriz de Gaps

Panorama de **tudo que já existe** no sistema (frontend + backend) e o que o protótipo/visão de produto **ainda exige**. Escopo: **delivery de comida** (não multi-segmento).

## 1. Funcionalidades já implementadas

### 1.1 Backend (.NET 9, clean architecture)

**Domínio / entidades relevantes:**

- **Store** — nome, slug, telefone, descrição, `CuisineTypeId`, banner, logo, TawkTo, `IsOpen`, `IsSubscriptionBlocked`, `SupportsDelivery`, `SupportsPickup`, `DeliveryFee`, `MinimumOrderValue`, `FreeShippingThreshold`, `InitialMinute`/`FinalMinute` (tempo de entrega), `AverageRating`, `TotalReviews`, `MaxDeliveryRadiusKm`, `DeliveryAreas`.
- **StoreAddress**, **StoreBusinessHour** (por dia da semana, TimeOnly), **StoreDeliveryArea** (taxa por bairro), **StorePaymentGatewayConfig** (MercadoPago).
- **CuisineType** — o **tipo de comida da loja** (hoje só rótulo). Ex.: Lanches, Pizza, Japonesa.
- **Product** — categoria, nome, descrição, preço, `PromotionalPrice`, imagem, `IsAvailable`, `IsFeatured`, `StockEnabled`, `StockQuantity`, `IsBestSeller`, `IsNew`, `TagPriority`.
- **ProductCategory** — nome, ordem, `IsActive`, `IsFeatured`.
- **ProductAdditional** (adicionais), **ProductChoiceOption** (escolha de sabor/opção), **ProductVariation** (variações/tamanhos, com preço e preço promocional), **ProductOptionGroup** (grupo com `ChoiceType` single/multiple, `MinChoices`, `MaxChoices`) + **ProductOptionItem** (itens do grupo, com preço).
- **Order / OrderItem / OrderStatusHistory / OrderReview** — pedido com `FulfillmentType` (Delivery/PickUp), snapshot de endereço, `PaymentMethod`, `Status`, `Subtotal`, `DeliveryFee`, `Total`. Item guarda `VariationName`, `ChoiceOptionName`, `AdditionalNames` (string).
- **Payment / PaymentStatusHistory / Webhooks** — MercadoPago (Pix online, cartão online) + pagamento na entrega (dinheiro/cartão).
- **Notification** (novo pedido, mudança de status, assinatura), **SellerSubscription/Plan** (assinatura via Asaas), **City/DeliveryNeighborhood/DeliveryTime** (OSM/IBGE), **LandingPageContent**, **SystemParameter**, **AuditLog**.

**Enums:** `FulfillmentType` (Delivery/PickUp), `OrderStatus` (Created→…→Delivered/Cancelled), `PaymentMethod` (PixOnline/CardOnline/CashOnDelivery/CardOnDelivery), `PaymentStatus`, `PaymentGateway` (MercadoPago), `NotificationType`.

**API (destaques):** auth (customer/seller/admin), stores (CRUD, endereço, horários, status, delivery-config, upload imagem), products (CRUD, availability, imagens, batch), categories (CRUD), publish (summary + publicar), catálogo público (categorias, produtos, featured), checkout (preview/confirm), orders (customer/seller, status), payments (online), reviews, endereços do cliente, notificações, assinaturas, admin (planos, parâmetros, landing), neighborhoods (import OSM), address-lookup (ViaCEP).

### 1.2 Frontend atual (Angular 20 / Ionic)

**Vendedor — wizard de configuração da loja** (`store-config`):
1. Loja (nome, cozinha, WhatsApp, descrição, endereço/CEP, logo/banner, delivery/retirada, tempo, raio, pedido mínimo).
2. Horários.
3. Entrega (bairros/taxas, raio, mapa Leaflet).
4. **Produtos** (categorias, produto com preço/imagem/estoque, **grupos de opções** single/multiple + min/max, tags destaque/mais vendido/novidade).
5. Publicar.

**Cliente — jornada de compra:**
- `store-page` — banner, logo, info, busca, categorias (tabs), lista de produtos, FAB WhatsApp, "Ver sacola".
- `product-detail` — hero, título/preço, **variações (radio)**, **opções de escolha (radio)**, **adicionais (checkbox)**, observações, quantidade, adicionar. **Não renderiza `optionGroups`.**
- `cart` — itens com resumo de customização, cupom (placeholder), Entrega/Retirada, resumo financeiro, pedido mínimo.
- `checkout/cadastro` — dados + endereço (ViaCEP), auto-registro/login do cliente.
- `checkout/pagamento` — pagar pelo app vs pagar na entrega, endereço, resumo (desconto = 0 fixo).
- `order-tracking` — sucesso, resumo, **timeline em tempo real (SignalR)**, detalhes, ajuda.

## 2. Matriz de gaps (protótipo/visão × sistema)

| Recurso | Backend | Front atual | Protótipo pede | Gap |
|---|---|---|---|---|
| Tela de produto por **tipo de comida** (hambúrguer/pizza/açaí) | Modelo genérico OK | Genérico (3 seções fixas) | Sim | **Renderizar por dados** (grupos) |
| **Grupos de opções** (single/multiple, min/max, obrigatório) | **Sim** (`ProductOptionGroup`) | **Não renderiza** | Sim (padrão açaí) | **Front não exibe optionGroups** |
| **Ponto da carne** (single obrigatório) | Sim (via grupo) | Não | Sim | Cadastrar como grupo + renderizar |
| **Pizza: tamanho → sabores** | Parcial (variações/produtos) | Não | Sim | Modelar fluxo; ideal via grupos/variações |
| **Pizza meio a meio** (2 sabores/item) | **Não** (item guarda 1 `ChoiceOptionName`) | Não | (implícito) | **Novo**: suportar N sabores + regra de preço |
| **Chips com emoji** (frutas/caldas) | Sim (itens de grupo) | Não | Sim | Layout "chip" no option-group |
| **Tags Obrigatório/Opcional** por grupo | Sim (`IsRequired`) | Não | Sim | Exibir badge |
| **footer-nav global** (4 abas) | n/a | **Não** | Sim | Criar navegação |
| **Combos / Destaques no topo do cardápio** | Sim (`IsFeatured`, `DisplayOrder`, endpoint `featured`) | Parcial (pseudo-categoria "Destaques") | Sim | Definir ordem canônica + seções especiais |
| **Busca por texto (nome+descrição)** | Catálogo público | Sim | Sim | OK |
| **Botão de filtros (sliders) ao lado da busca** | n/a | Não | Presente (sem função) | **Remover** (não tem finalidade) |
| **Cupom / desconto** | **Não** | Placeholder | Sim (linha Descontos) | **Novo módulo cupom** |
| **Agendamento / pré-pedido (encomenda)** | **Não** | Não | (doceria/bolo) | **Novo** |
| **Controle de estoque efetivo** | Campos existem, sem baixa | Exibe | — | Baixa no checkout |
| **CuisineType sugere modelo de opções e ordem do cardápio** | Só `CuisineType` (rótulo) | Não | (crítico) | **Presets de opções por tipo de comida** (doc 04) |
| **Produto simples** (bebida/complemento) | Sim | Sim | Sim | OK |
| **Rastreio em tempo real** | Sim | Sim (SignalR) | Sim | OK |
| **Retirada/Entrega** | Sim | Sim | Sim | OK |
| **Pagamento app/entrega** | Sim | Sim | Sim | OK |
| **Avaliações** | Sim | Parcial | (não no protótipo) | Expor no front |

## 3. Conclusões

1. **A maior alavanca de valor é ligar o front do cliente aos `ProductOptionGroup`** já existentes no backend. Isso, sozinho, viabiliza hambúrguer (ponto da carne), açaí (montagem), pizza (bordas/extras) e qualquer tipo de comida — **sem telas por arquivo**.
2. **Meio a meio** é o único recurso que exige mudança de modelo (item precisa guardar múltiplos sabores + regra de preço: maior valor / média).
3. **Cupom/desconto** aparece no protótipo (linha "Descontos") e não existe no backend — é um módulo novo.
4. **`CuisineType`** (tipo de comida da loja) hoje é só um rótulo. Deve passar a **sugerir o modelo de opções do produto principal** e ajudar a definir a **ordem do cardápio** (Destaques/Combos primeiro) — detalhado no doc 04. **Não** há multi-segmento: 1 loja = 1 tipo de comida + complementos (Bebidas/Acompanhamentos/Sobremesas).
5. **Busca/filtros:** manter busca por texto + chips de categoria; **remover o botão de filtros (sliders)** do protótipo, sem função.
