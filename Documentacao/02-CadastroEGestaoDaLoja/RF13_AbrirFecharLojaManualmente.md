# [MVP] [Loja] RF13 - Abrir e fechar loja manualmente

**Épico:** Cadastro e gestão da loja  
**Fase:** MVP  
**Perfil:** Vendedor  
**Prioridade:** Alta  

## Descrição
Permitir ao vendedor controlar manualmente se a loja está aceitando pedidos.

## Regras de negócio
- Loja fechada não aceita pedido.
- Loja pode estar fechada mesmo dentro do horário cadastrado.

## Critérios de aceite
- Vendedor altera status da loja.
- Cliente visualiza se está aberta ou fechada.
- Checkout é bloqueado quando a loja está fechada.

## Checklist técnico
- [x] Criar campo `IsOpen` na Loja (`Store`)
- [x] Criar endpoint de atualização de status (`PUT /api/stores/{storeId}/status`) chamando `UpdateStatusAsync`
- [x] Atualizar home pública via Angular mostrando Lojas Fechadas
- [x] Validar bloqueio no backend ao criar pedido de Lojas Fechadas (RF39 já inclui bloqueio via `CheckoutService`)

## Dependências
- RF09 - Cadastro da loja
- RF12 - Cadastro de horário

## Próximo card sugerido
- RF28 - Home pública
- RF39 - Criação do pedido