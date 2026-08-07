# [MVP] [Autenticação] RF04 - Login do vendedor

**Épico:** Autenticação e perfis  
**Fase:** MVP  
**Perfil:** Vendedor  
**Prioridade:** Alta  

## Descrição
Permitir que o vendedor faça login no painel da loja.

## Regras de negócio
- Apenas usuário com role `Seller` pode acessar a área do vendedor.
- Se o vendedor ainda não tiver loja, deve ser redirecionado para criação da loja.
- Conta bloqueada pode autenticar ou não conforme política, mas loja inadimplente não pode operar.

## Critérios de aceite
- Vendedor autentica com sucesso.
- Usuário sem role `Seller` não acessa o painel.
- Sistema direciona para criação da loja quando necessário.

## Checklist técnico
- [x] Reutilizar endpoint de login (Adaptado para `POST /api/auth/login/seller`)
- [x] Validar de status de `EmailConfirmed` com reposta `403` contendo flag `isEmailNotConfirmed`
- [x] Criar tela de Login Vendedor e redirecionador em caso de Email não confirmado
- [x] Criar guard de rota do vendedor
- [x] Criar layout privado do vendedor
- [x] Validar role no frontend e backend

## Dependências
- RF03 - Cadastro de vendedor

## Próximo card sugerido
- RF06 - Controle de perfil e permissões
- RF09 - Cadastro da loja