# [MVP] [Compra] RF31 - Carrinho de compras

**Épico:** Descoberta, carrinho e checkout  
**Fase:** MVP  
**Perfil:** Cliente  
**Prioridade:** Alta  

## Descrição
Permitir montar um pedido adicionando itens ao carrinho.

## Regras de negócio
- O carrinho deve conter itens de apenas uma loja.
- Produto indisponível não pode ser adicionado.
- Total parcial deve ser recalculado sempre.

## Critérios de aceite
- Cliente adiciona item.
- Cliente remove item.
- Cliente altera quantidade.
- Sistema impede misturar produtos de lojas diferentes.

## Checklist técnico
- [ ] Criar estado do carrinho no frontend (Angular) ou Backend For Frontend
- [ ] Persistir carrinho localmente para anônimos, ou usar local storage
- [ ] Validar uma loja por carrinho (limpar itens se mudar de loja)
- [ ] O backend atualmente não possui rotas para manter o estado do carrinho; o estado é gerido inteiramente no cliente até o envio no `/api/checkout/preview` ou `/api/orders` (como `CheckoutRequestDto.Items`)

## Dependências
- RF30 - Página da loja
- RF22 - Disponibilidade do produto

## Próximo card sugerido
- RF32 - Endereço de entrega
- RF33 - Checkout