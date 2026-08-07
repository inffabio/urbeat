# [MVP] [Loja] RF15 - Configuração de taxa de entrega e pedido mínimo

**Épico:** Cadastro e gestão da loja  
**Fase:** MVP  
**Perfil:** Vendedor  
**Prioridade:** Alta  

## Descrição
Permitir definir taxa de entrega fixa e valor mínimo do pedido.

## Regras de negócio
- Taxa de entrega é fixa no MVP.
- Pedido deve respeitar valor mínimo.

## Critérios de aceite
- Vendedor define taxa e mínimo.
- Checkout bloqueia pedido abaixo do mínimo.
- Taxa entra no total final do pedido.

## Checklist técnico
- [x] Adicionar campos na loja (`DeliveryFee`, `MinimumOrderValue` e `FreeShippingThreshold`) no Endpoint de atualização (`PUT /api/stores/{storeId}/delivery-config`)
- [x] Atualizar painel do vendedor em Angular
- [x] Validar no backend com FluentValidation
- [x] Agrupar áreas de entrega (`DeliveryAreas`) à config de delivery
- [x] Mostrar resumo correto no checkout

## Dependências
- RF09 - Cadastro da loja

## Próximo card sugerido
- RF33 - Checkout do pedido
- RF39 - Criação do pedido