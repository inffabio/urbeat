# [MVP] [Catálogo] RF22 - Ativar e inativar produto

**Épico:** Catálogo e produtos  
**Fase:** MVP  
**Perfil:** Vendedor  
**Prioridade:** Alta  

## Descrição
Permitir ao vendedor controlar a disponibilidade do produto.

## Regras de negócio
- Produto indisponível não pode ser adicionado ao carrinho.
- Pode permanecer visível como indisponível.

## Critérios de aceite
- Vendedor altera disponibilidade.
- Front público reflete status.
- Backend bloqueia compra de item indisponível.

## Checklist técnico
- [x] Criar campo `IsAvailable` ou similar na entidade `Product`
- [x] Criar endpoint de disponibilidade (`PUT /api/stores/{storeId}/products/{productId}/availability`)
- [x] Atualizar cardápio público integrando ao UI
- [x] Validar no pedido (Bloqueio ao Checkout)

## Dependências
- RF20 - Cadastro de produtos

## Próximo card sugerido
- RF31 - Carrinho
- RF39 - Pedido