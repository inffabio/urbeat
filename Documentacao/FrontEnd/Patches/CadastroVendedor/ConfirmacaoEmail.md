# Confirmação de email

## Regras e Fluxo Atual (Implementado)

### 1. Após cadastro inicial do vendedor:

- O vendedor preenche os dados e confirma no botão de cadastro.
- O Frontend aciona a API (`POST /api/auth/register/seller`), que cadastra o usuário e faz o disparo do envio do e-mail de confirmação em segundo plano (via *Hangfire*). O e-mail conta com um layout próprio (HTML) e carrega as boas vindas da startup (Urbeat).
- Imediatamente após o retorno da API, o Frontend direciona o vendedor à tela de **aguardando confirmação** (`/confirmacao-email?email=xxx`).
- Na tela de aguardando, o usuário sabe para onde o e-mail foi enviado e tem um botão parar **reenviar o e-mail**.
- Todo reenvio de e-mail inutiliza os tokens anteriores gerados e cria um token novo, disparando a nova mensagem pela API.
- Ao clicar no link encaminhado para a caixa de e-mail `/c/{shortCode}` (link encurtado de 25 caracteres), o vendedor abre a plataforma pela rota de checagem.
- O endpoint da API agora é `POST /api/auth/email/confirm/{shortCode}`, trocando o token JWT na query pelo shortcode direto na rota e alocando essa referência no Redis por intermédio da interface `IEmailTokenCache`.
- A tela de checagem processa a rota em posse do ShortCode e exibe sucesso. Após alguns segundos (ou ao clicar explicitamente), o vendedor é redirecionado diretamente para o painel de montagem da loja (`/configurar-loja`).

### 2. Fluxo pelo Login (caso o usuário ainda não possua o e-mail confirmado):

- O vendedor acessa a tela de Login (`/login-vendedor`) sem ter verificado a caixa de correio.
- Ele fornece o e-mail e a senha corretos e clica no botão de entrar.
- O Backend verifica as credenciais. Como a validação da senha passa (o problema é apenas no status do e-mail), a API recusa a emissão do token e retorna o Status HTTP `403 Forbidden` informando o contexto internamente: `code: "EMAIL_NOT_CONFIRMED"`.
- O Frontend intercepta esse erro de `403` e entende que a senha está correta, mas o e-mail está pendente. Dessa forma:
  1. Ele não exibe uma mensagem de falha bloqueadora ("Erro de login") para o usuário.
  2. Ele **dispara automaticamente** a requisição (`resendConfirmation`) para enviar um novo e-mail para a caixa do usuário.
  3. Redireciona o usuário perfeitamente de volta à tela de alerta visual (`/confirmacao-email`), notificando visualmente e dando seguimento à liberação da loja de forma orgânica.
- Se o usuário preencher a senha errada em qualquer cenário, a API retorna `401 Unauthorized`, indicando ao front que ele deve emitir um alerta visual ("Dados incorretos / E-mail ou senha incorretos") impedindo qualquer passo seguinte.
