# [MVP] [Admin] RF64 - Gestão de assinaturas e inadimplência

**Épico:** Administração da plataforma  
**Fase:** MVP  
**Perfil:** Admin  
**Prioridade:** Alta  

## Descrição
Permitir ao administrador acompanhar o estado das assinaturas e identificar lojistas inadimplentes.

## Regras de negócio
- Deve listar:
  - ativas
  - pendentes
  - vencidas
  - inadimplentes
  - canceladas

## Critérios de aceite
- Admin identifica lojas bloqueadas.
- Admin consulta vencimento e status da assinatura.
- Dados batem com Asaas e com a base interna.

## Checklist técnico
- [ ] Atualmente implementados `POST /api/admin/subscriptions/status` (via `UpsertSellerSubscriptionStatusRequestDto`) e `POST /api/admin/subscriptions/notifications/process` para forçar disparo de e-mails em processamento de backlog.
- [ ] O `AdminController` precisará do endpoint `GET /api/admin/subscriptions` (com paginação e filtros) para permitir navegação da lista de assinaturas na IU e do controle de assinaturas inadimplentes.
- [ ] Construir layout para listagem por `status`.

## Dependências
- RF56 - Webhook da assinatura
- RF57 - Bloqueio por inadimplência
- RF61 - Dashboard do admin

## Próximo card sugerido
- RF69 - Notificação de vencimento para vendedor