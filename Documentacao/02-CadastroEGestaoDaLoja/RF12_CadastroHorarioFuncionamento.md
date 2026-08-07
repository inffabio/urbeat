# [MVP] [Loja] RF12 - Cadastro de horário de funcionamento

**Épico:** Cadastro e gestão da loja  
**Fase:** MVP  
**Perfil:** Vendedor  
**Prioridade:** Alta  

## Descrição
Permitir informar dias e horários de funcionamento da loja.

## Regras de negócio
- No MVP, horário é informativo.
- Abertura operacional continua manual.

## Critérios de aceite
- Vendedor cadastra horários por dia.
- Cliente consegue visualizar os horários.

## Checklist técnico
- [x] Criar entidade `StoreBusinessHours`
- [x] Criar endpoints relacoes via `GET` e `PUT` `/api/stores/{storeId}/business-hours`
- [x] Criar tela administrativa Angular
- [x] Exibir horários na loja pública

## Dependências
- RF09 - Cadastro da loja

## Próximo card sugerido
- RF13 - Abrir e fechar loja manualmente