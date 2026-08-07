# [MVP] [Loja] RF09 - Cadastro da loja

**Épico:** Cadastro e gestão da loja  
**Fase:** MVP  
**Perfil:** Vendedor  
**Prioridade:** Alta  

## Descrição
Permitir ao vendedor cadastrar sua loja com dados básicos para publicação no app.

## Regras de negócio
- No MVP, 1 vendedor = 1 loja.
- Campos mínimos:
  - nome
  - telefone
  - descrição
  - tipo de culinária
- Loja deve ficar vinculada ao usuário vendedor.

## Critérios de aceite
- Loja é criada com sucesso.
- Loja fica vinculada ao vendedor.
- Sistema impede segunda loja para o mesmo vendedor no MVP.

## Checklist técnico
- [x] Criar entidade `Store`
- [x] Criar endpoint `POST /api/stores`
- [x] Validar ownership (Regra 1 vendedor = 1 loja implementada)
- [x] Criar formulário Angular
- [x] Persistir vínculo `OwnerUserId`

## Dependências
- RF03 - Cadastro de vendedor
- RF04 - Login do vendedor
- RF06 - Controle de perfil e permissões

## Próximo card sugerido
- RF10 - Edição dos dados da loja
- RF11 - Tipo de culinária
- RF14 - Endereço da loja