# [MVP] [Admin] RF61 - Dashboard básico da plataforma

**Épico:** Administração da plataforma  
**Fase:** MVP  
**Perfil:** Admin  
**Prioridade:** Alta  

## Descrição
Exibir visão geral da plataforma.

## Regras de negócio
- Indicadores mínimos:
  - total de lojas
  - lojas ativas
  - assinaturas ativas
  - pedidos totais

## Critérios de aceite
- Admin vê indicadores principais.
- Dados estão consistentes com a base.

## Checklist técnico
- [ ] Endpoint de teste de autorização `GET /api/admin/dashboard` implementado. Evoluir para popular e retornar objeto estruturado do dashboard com indicadores reais.
- [ ] Construir lógica no Entity Framework somando totais (assinaturas, stores, status).
- [ ] Criar cards/gráficos simples no Angular.

## Dependências
- RF05 - Login do admin

## Próximo card sugerido
- RF62 - Gestão de vendedores e lojas
- RF63 - Gestão de planos
- RF64 - Gestão de assinaturas