# [MVP] [Autenticação] RF05 - Login do administrador

**Épico:** Autenticação e perfis  
**Fase:** MVP  
**Perfil:** Admin  
**Prioridade:** Alta  

## Descrição
Permitir que o administrador da plataforma acesse a área administrativa global.

## Regras de negócio
- Apenas role `Admin` pode acessar esse painel.
- O admin principal pode ser criado via seed inicial.
- O admin principal (seed inicial) não precisa de confirmação de e-mail para autenticar.

## Critérios de aceite
- Admin consegue autenticar.
- Usuários comuns não acessam rotas do admin.
- Token contém a role `Admin`.
- Admin principal (seed inicial) consegue autenticar mesmo sem fluxo de confirmação de e-mail.

## Checklist técnico
- [ ] Criar seed inicial de admin
- [ ] Criar política `RequireAdmin`
- [ ] Criar área Angular do admin
- [ ] Proteger endpoints administrativos

## Dependências
- RF06 - Controle de perfil e permissões

## Próximo card sugerido
- RF61 - Dashboard básico da plataforma