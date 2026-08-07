# Verificação de Email - Registro Tradicional Urbeat

## Registro do Usuário

 > Após registro do usário deve ser enviado um email para confirmação
 > Após confirmação o usuario terá acesso ao fechamento das compras
 > Fazer todas as tratativas de tokens e link no email para confirmação
 > Criar um template do email a ser enviado tanto para o cliente quanto para o Vendedor (Loja)
 > Se não existir no Cliente e no vendedor o campo de email confirmado criar
---

## Implementação Atual (Auditoria de Código)
 - No `AuthController`, a submissão de cadastro retorna flag `emailConfirmationPending = true`.
 - Logins subsequentes são interceptados em `_authService.LoginAsync` que pode resultar em `403` com header/código customizado `EMAIL_NOT_CONFIRMED`.
 - A persistência do código de short link no Redis (`RedisEmailTokenCache`) é utilizada para verificação através do endpoint `POST /api/auth/email/confirm/{code}`.
 - Endpoints expostos: `POST /api/auth/email/resend-confirmation`, `POST /api/auth/email/confirm` (padrão com UUID completo) e `POST /api/auth/email/confirm/{code}` (short string de 25 caracteres para compatibilidade cross-device de mobile browsers que interceptam intent links).
 - A classe Job `SendEmailConfirmationJob` envia pelo Hangfire os templates.
