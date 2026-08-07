# [MVP] [Assinatura] RF53 - Cadastro de planos

**Épico:** Assinatura do vendedor  
**Fase:** MVP  
**Perfil:** Admin  
**Prioridade:** Alta  

## Descrição
Permitir ao administrador cadastrar planos de assinatura da plataforma.

## Regras de negócio
- Plano deve ter:
  - nome
  - valor
  - descrição
  - status
- No MVP, pode existir um plano único, mas modelar para vários.

## Critérios de aceite
- Admin cria plano.
- Admin edita plano.
- Plano pode ser ativado/inativado.

## Checklist técnico
- [ ] O `AdminController` possui endpoints para criar `POST /api/admin/plans`, atualizar `PUT /api/admin/plans/{planId}`, inativar e listar pianos (`GET /api/admin/plans`).
- [ ] A entidade `Plan` baseia os preços cobrados.
- [ ] Os planos disponíveis podem ser vistos pelos Sellers no endpoint `GET /api/subscriptions/plans`.

## Dependências
- RF05 - Login do admin
- RF61 - Dashboard do admin

## Próximo card sugerido
- RF54 - Contratação da assinatura