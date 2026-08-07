# Verificacao SMS OTP no Checkout

Data: 2026-07-28

## Estado Atual

O fluxo de checkout em producao nao usa SMS/OTP neste momento. O cadastro do cliente cria uma sessao segura diretamente via `POST /api/checkout/customer-session`, salva o endereco primario, retorna access token e define o refresh token no cookie `urbeat.refresh_token` (`HttpOnly`, `Secure`, `SameSite=Strict`).

SMS/OTP e Infobip permanecem preparados no codigo e na configuracao para ativacao futura, mas a rota `/checkout/confirmar-sms` nao faz parte do caminho atual do cliente.

Clientes recorrentes sao restaurados pelo cookie seguro: o frontend chama `POST /api/auth/refresh`, carrega `GET /api/customer/me`, exibe um cumprimento discreto na loja (`Ola, Nome`) e, quando existe endereco primario, pula `/checkout/cadastro` ao iniciar o checkout e segue para `/checkout/pagamento`.

## Objetivo Preparado

Adicionar verificacao por SMS no cadastro do cliente durante o checkout quando a validacao por OTP for ativada. O cliente informa seus dados em `/checkout/cadastro`, recebe um codigo de 4 digitos por SMS e confirma o codigo em uma nova tela antes de seguir para pagamento.

## Decisoes Aprovadas

- O canal padrao inicial sera SMS.
- O envio sera chaveavel por configuracao entre `Sms` e `WhatsApp`, sem acoplar a regra de OTP ao canal.
- A primeira implementacao real de SMS usara Infobip com API key em variavel de ambiente/configuracao segura.
- O codigo tera 4 digitos e validade de 1 minuto.
- O cliente so segue para a proxima etapa quando o backend confirmar o codigo.
- Se o codigo expirar ou estiver incorreto, a tela permite reenviar depois do prazo.
- A sessao autenticada e o cookie seguro so serao emitidos depois da confirmacao correta.

## Fluxo Funcional

1. Cliente preenche dados pessoais e endereco em `/checkout/cadastro`.
2. Frontend envia os dados ao backend para iniciar a verificacao.
3. Backend cria ou atualiza o usuario cliente em estado pendente.
4. Backend gera OTP numerico de 4 digitos.
5. Backend salva apenas hash do codigo, data de expiracao, tentativas e status.
6. Backend envia a mensagem pelo `ICustomerVerificationMessageSender` configurado.
7. Frontend navega para `/checkout/confirmar-sms`.
8. Cliente digita os 4 digitos em campos separados.
9. Frontend envia o codigo ao backend assim que os 4 digitos estiverem preenchidos.
10. Backend valida codigo, expiracao, tentativas e vinculo com a tentativa de checkout.
11. Se correto, backend confirma o telefone, salva o endereco, emite JWT e refresh cookie seguro.
12. Frontend salva o access token em memoria/localStorage conforme padrao atual e segue para `/checkout/pagamento`.

## Tela de Confirmacao

Rota: `/:storePath/checkout/confirmar-sms`.

Elementos:

- Titulo claro: `Confirme seu celular`.
- Texto: `Enviamos um SMS com 4 digitos para (XX) XXXXX-XXXX.`
- Quatro inputs individuais, um digito por campo.
- Auto-foco para o proximo campo apos digitar.
- Backspace volta para o campo anterior quando vazio.
- Colar um codigo de 4 digitos distribui os digitos nos campos.
- Cronometro de 60 segundos.
- Botao `Enviar novamente` desabilitado ate o cronometro expirar.
- Mensagens inline para codigo invalido, expirado ou envio indisponivel.
- Estado de carregamento enquanto confirma ou reenvia.

## Backend

### Novas APIs

`POST /api/checkout/customer-session`

Fluxo atual sem SMS. Recebe os mesmos dados de cliente/endereco do inicio de verificacao, cria/atualiza o usuario cliente, salva o nome em claim `FullName`, salva endereco primario, emite access token e define o refresh token no cookie seguro.

`GET /api/customer/me`

Requer cliente autenticado. Retorna perfil minimo para restaurar sessao no frontend:

```json
{
  "fullName": "Maria Oliveira",
  "email": "maria@email.com",
  "phoneNumber": "22999999999",
  "primaryAddressId": "guid"
}
```

Usado para mostrar o nome discreto na loja e reaproveitar o endereco primario no checkout recorrente.

`POST /api/checkout/customer-verification/start`

Entrada:

```json
{
  "storeId": "guid",
  "customer": {
    "fullName": "string",
    "email": "string",
    "phoneNumber": "string"
  },
  "address": {
    "cep": "string",
    "street": "string",
    "number": "string",
    "complement": "string | null",
    "neighborhood": "string",
    "city": "string",
    "state": "string"
  }
}
```

Saida:

```json
{
  "verificationId": "guid",
  "expiresAtUtc": "datetime",
  "resendAvailableAtUtc": "datetime",
  "maskedPhone": "string"
}
```

`POST /api/checkout/customer-verification/confirm`

Entrada:

```json
{
  "verificationId": "guid",
  "code": "1234"
}
```

Saida em sucesso:

```json
{
  "accessToken": "jwt",
  "expiresAtUtc": "datetime",
  "customerAddressId": "guid"
}
```

Tambem define o refresh token em cookie seguro `HttpOnly`.

`POST /api/checkout/customer-verification/resend`

Entrada:

```json
{
  "verificationId": "guid"
}
```

Saida:

```json
{
  "expiresAtUtc": "datetime",
  "resendAvailableAtUtc": "datetime"
}
```

### Servicos

- `ICustomerOtpService`: cria, confirma e reenvia OTP.
- `ICustomerVerificationMessageSender`: abstrai o envio por canal real/simulado.
- `FakeSmsVerificationMessageSender`: implementacao simulada padrao. Em Development/Testing registra destinatario e codigo em log seguro; em Production nao deve expor codigo em log.
- `FakeWhatsAppVerificationMessageSender`: implementacao simulada disponivel para trocar o canal por configuracao.
- `InfobipSmsVerificationMessageSender`: implementacao real de SMS via Infobip.

### Configuracao de Canal

`CustomerVerification`:

```json
{
  "Channel": "Sms",
  "SmsProvider": "Fake",
  "Infobip": {
    "BaseUrl": "https://m9zq59.api.infobip.com",
    "ApiKey": "",
    "Sender": "Urbeat"
  }
}
```

- `Channel`: `Sms` ou `WhatsApp`.
- `SmsProvider`: `Fake` ou `Infobip`.
- `Infobip:BaseUrl`: URL da conta Infobip. Nao e segredo, mas deve ser configurada corretamente.
- `Infobip:ApiKey`: segredo. Nunca versionar; configurar por variavel `CustomerVerification__Infobip__ApiKey`.
- Autenticacao Infobip: header `Authorization: App <API_KEY>`.
- Em producao OCI, a API key deve ficar no OCI Vault como `URBEAT_INFOBIP_API_KEY` e ser projetada no container como `CustomerVerification__Infobip__ApiKey`.
- O deploy tambem aceita `URBEAT_INFOBIP_BASE_URL` e `URBEAT_INFOBIP_SENDER` no OCI Vault para projetar `CustomerVerification__Infobip__BaseUrl` e `CustomerVerification__Infobip__Sender`.
- Para criar esses secrets pelo script, exportar as variaveis no terminal local antes de executar `scripts/criarDeployOracleCloud/01-setup-vault-secrets.ps1`. Nao salvar os valores em arquivo versionado.
- Infobip MCP e util para agentes/IDEs pesquisarem docs ou testarem ferramentas fora do app; o runtime do backend usa chamada HTTP direta para evitar dependencia operacional de MCP.

### Persistencia

Nova entidade sugerida: `CustomerPhoneVerification`.

Campos:

- `Id`
- `UserId`
- `StoreId`
- `PhoneNumber`
- `CodeHash`
- `ExpiresAtUtc`
- `ResendAvailableAtUtc`
- `Attempts`
- `MaxAttempts`
- `ConfirmedAtUtc`
- `ConsumedAtUtc`
- `CreatedAtUtc`
- `UpdatedAtUtc`

O codigo nunca deve ser salvo em texto puro.

### Regras

- OTP deve ter 4 digitos numericos.
- Validade: 1 minuto.
- Reenvio so fica disponivel apos 1 minuto.
- Tentativas maximas por codigo: 5.
- Codigo confirmado nao pode ser reutilizado.
- Confirmacao deve validar `verificationId`, expiracao e hash do codigo.
- O backend deve recusar envio se a loja nao tiver `PhoneNumber` configurado.
- O backend deve recomputar regras de checkout e nao confiar em valores vindos do frontend.

## Cookies e Token

- Access token continua retornando no corpo para compatibilidade com o frontend atual.
- Refresh token deve ser salvo em cookie `urbeat.refresh_token` com:
  - `HttpOnly = true`
  - `Secure = true` em producao
  - `SameSite = Lax` ou configuracao equivalente ja usada no projeto
  - expiracao igual ao refresh token persistido
- Dados sensiveis do cliente nao devem ser salvos em cookie legivel pelo JavaScript.

## Frontend

### Cadastro

Fluxo atual: `CustomerPageComponent` chama `createCustomerSession`, grava token via `AuthService.saveToken`, seta `customerAddressId` e navega para `/checkout/pagamento`.

Fluxo preparado com SMS: `CustomerPageComponent` deixa de registrar, logar e salvar endereco diretamente. Ao clicar em `Continuar` com formulario valido:

1. Chama `startCustomerVerification`.
2. Salva em `CheckoutService` os dados do cliente, endereco, `verificationId`, `expiresAtUtc`, `resendAvailableAtUtc` e telefone mascarado.
3. Navega para `/checkout/confirmar-sms`.

### Confirmacao SMS

Novo componente standalone:

- `SmsVerificationPageComponent`
- HTML/SCSS/spec dedicados.
- Usa `CheckoutService` para ler a tentativa atual.
- Se nao houver `verificationId`, redireciona para `/checkout/cadastro`.
- Confirma automaticamente ao completar 4 digitos.
- Atualiza cronometro a cada segundo.
- Habilita reenvio somente quando `Date.now() >= resendAvailableAtUtc`.
- Ao confirmar com sucesso, grava token via `AuthService.saveToken`, seta `customerAddressId` e navega para `/checkout/pagamento`.

### Pagamento

`PaymentPageComponent` exibe somente duas formas:

- `Pagar ao receber`: confirma o checkout com `PaymentMethod.CashOnDelivery`, grava pedido/itens/historico/auditoria no backend, limpa carrinho e navega direto para `/pedido/:orderId`. O backend notifica o lojista imediatamente porque o pedido ja esta valido para preparo.
- `Pix`: confirma o checkout com `PaymentMethod.PixOnline`, grava o pedido como `PendingPayment`, cria o pagamento online pela strategy de gateway configurada e navega para `/checkout/pagar`. O lojista so recebe sinal quando o webhook do gateway aprovar o pagamento e o backend mover o pedido para `Received`.

`OnlinePaymentPageComponent` nao cria um novo pedido. Ela usa `lastOrderId`, carrega o pagamento existente com `PaymentService.getPayment(orderId)`, mostra o link seguro `gatewayCheckoutUrl`, fica consultando o pedido e navega para `/pedido/:orderId` quando o status sair de `PendingPayment` para `Received` ou etapa posterior.

O gateway de Pix fica isolado em `IOrderPaymentStrategy`/`IOrderPaymentStrategyFactory`. Para trocar ou informar o gateway correto, criar uma nova strategy para `PaymentMethod.PixOnline` ou configurar a existente, sem alterar o fluxo de checkout.

## Erros Esperados

- Loja sem telefone configurado: mostrar mensagem clara e manter cliente no cadastro.
- Codigo invalido: mostrar erro inline e permitir nova tentativa dentro do prazo.
- Codigo expirado: mostrar erro e habilitar reenvio quando permitido.
- Muitas tentativas: bloquear codigo atual e pedir reenvio.
- Falha de envio: mostrar erro e permitir tentar novamente.

## Testes

Backend unitarios:

- Gera OTP com 4 digitos.
- Salva hash e nao texto puro.
- Confirma codigo correto antes de 1 minuto.
- Rejeita codigo incorreto.
- Rejeita codigo expirado.
- Bloqueia apos limite de tentativas.
- Reenvio respeita janela de 1 minuto.
- Nao envia se loja nao tem telefone.
- Confirmacao emite token e cookie apenas apos sucesso.

Frontend unitarios:

- Tela renderiza quatro inputs.
- Auto-avanca ao digitar.
- Backspace volta campo.
- Paste distribui quatro digitos.
- Cronometro habilita reenvio apos expirar.
- Codigo completo chama API de confirmacao.
- Sucesso navega para pagamento.
- Erro mostra mensagem inline.

## Fora de Escopo Inicial

- Painel do lojista para escolher canal de verificacao.
- Integracao real com provedor WhatsApp Business.
- Webhooks de entrega da Infobip.
- Confirmacao por e-mail para esse fluxo de checkout.

## Observacoes de Seguranca

- Nunca logar OTP em producao.
- Nunca armazenar OTP em texto puro.
- Rate limit por telefone, usuario e IP deve ser considerado no primeiro provedor real.
- Mensagem SMS nao deve conter dados sensiveis alem do codigo.
- Token/cookie so apos confirmacao correta.
