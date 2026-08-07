# [MVP] [Assinatura] RF57 - Bloqueio de loja inadimplente

**Épico:** Assinatura do vendedor  
**Fase:** MVP  
**Perfil:** Admin / Sistema  
**Prioridade:** Alta  

## Descrição
Impedir que lojas inadimplentes recebam novos pedidos.

## Regras de negócio
- Loja inadimplente não recebe novos pedidos.
- Loja pode continuar acessando o painel para regularização.
- Cliente não deve conseguir concluir checkout.
- Loja inadimplente não deve aparecer na vitrine pública.

## Critérios de aceite
- Loja inadimplente é bloqueada operacionalmente.
- Cliente não consegue fechar pedido.
- Vendedor vê aviso de pendência.
- Loja inadimplente deixa de aparecer na vitrine pública.

## Checklist técnico
- [ ] No `PublicStoresController`, o endpoint `ListPublicAsync` já deve ocultar lojas onde a assinatura encontra-se com bloqueio.
- [ ] No `CheckoutController`, no `PreviewAsync` já existe a validação de bloqueio da loja resultando em booleano `.StoreBlocked`.
- [ ] O `SellerSubscriptionNotificationJob` em background pode avaliar inscrições vencidas e repassar flag atualizada para a entidade de Loja (ex: `blocked: true`).
- [ ] UI de checkout barra processo se `StoreBlocked == true`.

## Dependências
- RF56 - Webhook da assinatura
- RF13 - Abrir/fechar loja
- RF39 - Pedido

## Próximo card sugerido
- RF58 - Tela da assinatura
- RF64 - Gestão de inadimplência