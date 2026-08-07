# [MVP] [Autenticação] RF03 - Cadastro de vendedor

**Épico:** Autenticação e perfis  
**Fase:** MVP  
**Perfil:** Vendedor  
**Prioridade:** Alta  

## Descrição
Permitir que um vendedor crie sua conta para acessar o painel administrativo.

## Regras de negócio
- E-mail deve ser único.
- Conta recebe role `Seller`.
- O cadastro da conta não cria automaticamente a loja.
- Após cadastro, o vendedor deve seguir para o fluxo de criação da loja.

## Critérios de aceite
- Vendedor consegue criar conta.
- Conta recebe role `Seller`.
- E-mail duplicado é bloqueado.
- Fluxo direciona o vendedor para criação da loja.

## Checklist técnico
- [x] Criar endpoint `POST /api/auth/register/seller`
- [x] Criar DTO e validações
- [x] Criar usuário no Identity (Pendente de EmailConfirmation)
- [x] Disparar via Hangfire e-mail de confirmação (`ShortCode` de 25 caracteres cacheado no Redis via `IEmailTokenCache`)
- [x] Vincular role `Seller`
- [x] Criar tela Angular de cadastro do vendedor
- [x] Criar tela Angular para processamento de código curto de e-mail (`/c/{shortCode}`) e reenvio.

## Dependências
- Nenhuma

## Próximo card sugerido
- RF04 - Login do vendedor
- RF09 - Cadastro da loja