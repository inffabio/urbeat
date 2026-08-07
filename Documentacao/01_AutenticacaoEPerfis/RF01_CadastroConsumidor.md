# [MVP] [Autenticação] RF01 - Cadastro de consumidor

**Épico:** Autenticação e perfis  
**Fase:** MVP  
**Perfil:** Cliente  
**Prioridade:** Alta  

## Descrição
Permitir que um consumidor crie conta na plataforma informando nome, e-mail, telefone e senha.

## Regras de negócio
- E-mail deve ser único.
- Senha deve seguir política mínima:
  - 8 caracteres
  - 1 letra maiúscula
  - 1 letra minúscula
  - 1 número
  - 1 caractere especial
- Conta deve ser criada com role `Customer`.
- Conta pode nascer ativa no MVP.

## Critérios de aceite
- Cliente consegue se cadastrar com dados válidos.
- Sistema impede e-mail duplicado.
- Sistema impede senha fora da política.
- Usuário é salvo com perfil `Customer`.

## Checklist técnico
- [x] Criar endpoint `POST /api/auth/register/customer`
- [x] Criar DTO de entrada
- [x] Validar com FluentValidation
- [x] Criar usuário no ASP.NET Core Identity (bypass EmailConfirm para Customer)
- [x] Vincular role `Customer`
- [x] Criar tela Angular de cadastro
- [x] Exibir mensagens amigáveis

## Dependências
- Nenhuma

## Próximo card sugerido
- RF02 - Login do consumidor

## Observações técnicas
- Usar `UserManager<ApplicationUser>` do Identity.