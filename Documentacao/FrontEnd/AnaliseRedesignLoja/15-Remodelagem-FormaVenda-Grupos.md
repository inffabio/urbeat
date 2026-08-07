# 15. Remodelagem: Forma de Venda, Variações e Grupos de Opções

**Status:** Implementado (2026-07)

## Resumo

Remodelagem completa do cadastro de produtos e do cardápio digital, substituindo o modelo anterior de variações/choices/additionals não configuráveis pelo vendedor por um sistema de **forma de venda** com 4 modos, variações editáveis com drag-reorder, e grupos de opções simplificados.

## Motivação

O cadastro anterior não permitia ao lojista criar ou editar variações de tamanho/peso, e os grupos de opções dependiam de conceitos visuais (`displayStyle`) e de precificação (`priceMode`) que complicavam a UI sem benefício real. O cardápio digital também não refletia produtos com múltiplos tamanhos/pesos (não havia "A partir de R$").

## O que mudou

### 1. Backend

- **`Product.SaleMode`** (novo campo): `single` | `size` | `fixed_weight` | `variable_weight`.
- **`ProductVariation`** ampliada: `Description`, `WeightGrams`, `IsDefault` (sem SKU). Usada tanto para tamanhos quanto para pesos fixos.
- **`ProductWeightConfig`** (nova entidade): `PricePerKg`, `MinGrams`, `MaxGrams`, `IncrementGrams`, `IsEstimated`. 1:1 com Product, só para `variable_weight`.
- **`ProductOptionGroup`** simplificado: removidos `DisplayStyle` e `PriceMode`; `ChoiceType` (`single`/`multiple`) vem diretamente do request.
- **`OrderItem.WeightGrams`** (novo campo): snapshot do peso escolhido para pedidos de peso variável.
- **Migration**: `AddSaleModeWeightVariationsAndSimplifyOptionGroups` no projeto `Urbeat.Infrastructure`.
- **`PricingService`** refatorado: `size`/`fixed_weight` → preço da variação substitui o base; `variable_weight` → `PricePerKg × grams / 1000` com validação de min/max/incremento; grupos sempre somam itens.
- **`ProductService`** normaliza `SaleMode`, preço base ("A partir de" = menor variação ativa), garantia de exatamente 1 default.
- **Validators** atualizados com regras por modo de venda.

### 2. Frontend — Cadastro de Produto

- **Forma de venda:** seletor segmentado (radio cards) com 4 opções.
- **Variações de tamanho:** tabela com drag-reorder (`ion-reorder-group`), campos Nome/Descrição/Preço/Padrão/Ativo/Remover. Sem SKU. Apenas 1 padrão.
- **Variações de peso fixo:** Peso/Unidade(g/kg)/Preço/Equivalente(R$/kg, calculado automaticamente)/Padrão/Ativo/Remover.
- **Peso variável:** grid Preço por kg / Peso mín / Peso máx / Incremento + checkbox "preço estimado" + preview de exemplo.
- **Grupos de opções simplificado:** Nome / Tipo de seleção (Escolha única/Múltipla) / Condição (Obrigatório/Opcional) / Mín-Máx / Itens com Nome + Preço adicional (aceita R$ 0,00) + Remover. Accordion + drag-reorder preservados.
- **Catálogo lateral:** coluna direita (desktop) com resumo de cada produto (foto, nome, preço/"A partir de"/"R$/kg", descrição) e accordion com detalhes (variações, grupos, tags).
- **Cópia de configurações:** cada produto cadastrado tem botão pequeno **Copiar** no catálogo lateral. A ação abre/preenche um rascunho com forma de venda, variações de tamanho/peso, peso variável e grupos de opções do produto original, mantendo nome, descrição, foto, categoria e preço para preenchimento manual. Se já houver rascunho em criação, apenas substitui as configurações desse rascunho.
- Layout responsivo: 2 colunas → 1 coluna em ≤1050px.
- Estilo: 100% SCSS com tokens do DESIGN.md (bordeaux, cream, Inter, shadows-sm).

### 3. Frontend — Cardápio Digital

- **Store page:** preço mostra "A partir de R$ X" (size/fixed_weight) ou "R$ X/kg" (variable_weight + "estimado" se marcado). Produtos com variações nunca são "simples" (sempre abrem detalhe).
- **Product detail page:**
  - `size`/`fixed_weight`: cartões de rádio com nome, descrição, preço. Variação padrão pré-selecionada.
  - `variable_weight`: stepper −/+ com limites min/max/incremento, recálculo imediato.
  - Grupos renderizados como radio (single) ou checkbox (multiple), posicionados **antes** de Observações.
  - Botão "Adicionar por R$ X".
  - Compatibilidade legada: produtos `single` com variações antigas continuam renderizando (preço soma).
- **Carrinho:** exibe peso selecionado. `CartItem.weightGrams` enviado ao checkout.
- **Checkout:** `CheckoutItemRequestDto.WeightGrams` validado e precificado server-side.

### 4. Testes

- **Backend**: 16 novos casos em `PricingServiceTests` (size, fixed_weight, variable_weight, min/max/incremento, soma de grupos, item R$ 0,00). Validators atualizados.
- **Frontend**: casos cobrindo sale mode, variações, equivalente, price label, product detail e cópia de configurações no cadastro de produto. `product-detail-page.component.spec.ts` criado.

## Arquivos afetados

| Arquivo | Mudança |
|---------|---------|
| `Product.cs`, `ProductVariation.cs`, `ProductOptionGroup.cs`, `OrderItem.cs` | Entidades atualizadas |
| `ProductWeightConfig.cs` | Nova entidade |
| `ApplicationDbContext.cs` | EF config + DbSet |
| `CreateProductRequestDto.cs`, `UpdateProductRequestDto.cs`, `ProductResponseDto.cs`, `ProductOptionGroupDto.cs`, `CheckoutItemRequestDto.cs`, `ItemPricingResultDto.cs` | DTOs |
| `CreateProductRequestDtoValidator.cs`, `UpdateProductRequestDtoValidator.cs`, `ProductVariationDtoValidator.cs` | Validators |
| `PricingService.cs` | Refatorado (saleMode switch) |
| `ProductService.cs` | BuildOptionGroup simplificado, BuildVariation, NormalizeBasePrice, WeightConfig |
| `CheckoutService.cs` | WeightGrams + WeightConfig include |
| `ProductReadRepository.cs` | WeightConfig include |
| `store-products-page.component.{ts,html,scss}` | Remodelagem completa |
| `store-page.component.{ts,html}` | productPriceLabel, "A partir de" |
| `product-detail-page.component.{ts,html,scss}` | saleMode rendering, weight stepper, grupos radio/checkbox |
| `cart-page.component.html` | Exibição do peso |
| `cart.service.ts` | weightGrams no merge + checkout |
| `product.model.ts`, `cart-item.model.ts`, `checkout.model.ts` | Modelos |
