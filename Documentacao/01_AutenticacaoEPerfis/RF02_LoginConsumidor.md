# [MVP] [Autenticação] RF02 - Login do consumidor

**Épico:** Autenticação e perfis  
**Fase:** MVP  
**Perfil:** Cliente  
**Prioridade:** Alta  

## Descrição
Permitir que o consumidor faça login com e-mail e senha para acessar funcionalidades privadas da plataforma.

## Regras de negócio
- Apenas usuários ativos podem autenticar.
- Login inválido deve retornar erro genérico.
- Login bem-sucedido deve gerar:
  - access token JWT
  - refresh token rotativo
- O token deve conter a role do usuário.

## Critérios de aceite
- Login com credenciais válidas funciona.
- Login inválido falha.
- Usuário inativo não consegue entrar.
- Access token é retornado corretamente.
- Refresh token é criado corretamente.

## Checklist técnico
- [ ] Criar endpoint `POST /api/auth/login`
- [ ] Validar credenciais com `SignInManager`
- [ ] Gerar JWT
- [ ] Gerar refresh token seguro
- [ ] Persistir refresh token
- [ ] Gravar refresh token em cookie HttpOnly
- [ ] Criar tela Angular de login
- [ ] Criar `AuthService`
- [ ] Criar `HttpInterceptor`

## Dependências
- RF01 - Cadastro de consumidor

## Próximo card sugerido
- RF06 - Controle de perfil e permissões

## Observações técnicas
- Access token: 15 min
- Refresh token: 7 dias