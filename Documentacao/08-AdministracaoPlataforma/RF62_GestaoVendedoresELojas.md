# [MVP] [Admin] RF62 - Gestão de vendedores e lojas

**Épico:** Administração da plataforma  
**Fase:** MVP  
**Perfil:** Admin  
**Prioridade:** Alta  

## Descrição
Permitir que o administrador liste e consulte vendedores e lojas cadastradas.

## Regras de negócio
- Admin pode listar lojas.
- Admin pode ativar/inativar loja.
- Operações devem ser auditáveis.

## Critérios de aceite
- Admin visualiza lojas.
- Admin consegue ativar/inativar.
- Admin consegue consultar dados do vendedor responsável.

## Checklist técnico
- [ ] O `StoresController` hoje serve aos Sellers. O `AdminController` precisará de endpoints `GET /api/admin/stores` com acesso e privilégio sobre todos os registros independente do `SellerUserId`.
- [ ] O `AdminController` precisará de endpoints `PATCH /api/admin/stores/{storeId}/status` caso o admin deseje desativar manualmente, além de `DELETE /api/admin/stores/{storeId}`.
- [ ] O Admin precisará de endpoint para verificar e listar os usuários através do `IdentityDbContext` ou endpoints construídos.

## Dependências
- RF61 - Dashboard do admin
- RF09 - Cadastro da loja

## Próximo card sugerido
- RF64 - Gestão de assinaturas e inadimplência