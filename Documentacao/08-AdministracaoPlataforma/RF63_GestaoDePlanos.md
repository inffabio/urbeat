# [MVP] [Admin] RF63 - Gestão de planos

**Épico:** Administração da plataforma  
**Fase:** MVP  
**Perfil:** Admin  
**Prioridade:** Alta  

## Descrição
Permitir manutenção administrativa dos planos ofertados aos lojistas.

## Regras de negócio
- Admin pode criar, editar e inativar planos.
- Planos inativos não aparecem para novas contratações.

## Critérios de aceite
- Admin gerencia planos com sucesso.
- Assinaturas antigas permanecem consistentes.

## Checklist técnico
- [ ] Endpoints implementados em `AdminController`: `GET /api/admin/plans`, `POST /api/admin/plans`, `PUT /api/admin/plans/{planId}`, `PATCH /api/admin/plans/{planId}/status`.
- [ ] O `AdminController` faz injeção de dependência do `IPlanService`. 
- [ ] Proteção via atributo `[Authorize(Policy = AuthorizationPolicies.AdminOnly)]`.
- [ ] O app frontend deve exibir e gerir o CRUD destes planos (o backend gerencia que eles fiquem em `isActive`).

## Dependências
- RF53 - Cadastro de planos
- RF61 - Dashboard do admin

## Próximo card sugerido
- RF54 - Contratação da assinatura