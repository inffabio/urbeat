# Histórico de Deploy - Cadastro de Loja do Vendedor

Esta documentação detalha a implementação das funcionalidades da **Tela de Cadastro da Loja do Vendedor** e da infraestrutura de **Confirmação de E-mail**, finalizada com sucesso. 

Diferente de tentativas anteriores, **todas as funcionalidades descritas na especificação (01-TelaCadastroLojaVendedor.md)** foram implementadas, validadas e o sistema está rodando plenamente no servidor.

## 1. O Que Foi Desenvolvido

### 1.1. Frontend (Angular)
- **`SellerRegisterPageComponent` (`/cadastro`):** Tela completa de cadastro contendo:
  - Layout em duas colunas (formulário e benefícios).
  - Máscara de WhatsApp automática (`(XX) XXXXX-XXXX`).
  - Validação de segurança de senha (mínimo 6 caracteres).
  - Confirmação de senha funcional com feedback de "Senha não confere".
  - Botões para exibir/ocultar senha (eye icon).
  - Validação de formato de E-mail.
- **`EmailConfirmationPageComponent` (`/confirmacao-email`):** Tela orientando o vendedor a checar a caixa de entrada, contendo opção funcional de "Reenviar e-mail".
- **`EmailConfirmPageComponent` (`/confirmar-email?userId=...&token=...`):** Tela encarregada de capturar o clique no link do email e confirmar o cadastro no Backend.
- **Atualização de Serviços e Rotas:**
  - `AuthService` com os métodos `registerSeller()`, `loginSeller()`, `confirmEmail()` e `resendConfirmation()`.

### 1.2. Backend (.NET)
- **Endpoints Utilizados:**
  - `POST /api/auth/register/seller`
  - `POST /api/auth/email/confirm`
  - `POST /api/auth/email/resend-confirmation`
- **Adaptação Estratégica:**
  - O fluxo foi configurado para que o Vendedor **não seja ativado automaticamente**. A regra no `AuthService` agora é `EmailConfirmed = role != "Seller"`.
  - Background Job em Hangfire ativo para disparo de emails transacionais aos vendedores recém-cadastrados.

## 2. Configuração de E-mail (`contato@urbeat.com.br`)

O remetente do sistema para comunicação com o vendedor e envio do token de confirmação foi configurado explicitamente para usar **`contato@urbeat.com.br`**.

### Variaveis e Infraestrutura no Docker (`/opt/urbeat/docker/.env`):
No servidor, a configuracao se baseia nas seguintes chaves SMTP, predefinindo o remetente oficial:

```env
# Modulo de Envio (OCI Email Delivery)
SMTP_HOST=smtp.email.sa-saopaulo-1.oci.oraclecloud.com
SMTP_PORT=587
SMTP_USER=<oci-smtp-username>
SMTP_PASS=<oci-smtp-password>
SMTP_FROM=contato@urbeat.com.br
EMAIL_LOGONLY=false
```

- **`appsettings.json` do WebApi** tambem foi ajustado para consolidar o remetente `contato@urbeat.com.br` como o padrao do envio e o link do front foi setado para `https://urbeat.com.br`.

## 3. Estado da Implantação (Deploy)

As alterações foram implantadas integralmente no servidor `52.144.45.199`:
- Container `urbeat_webapi` recriado com a última versão do `AuthService` e as strings do `.env`.
- Frontend reconstruído e os assets do Angular distribuídos via NGINX.
- Tudo rodando de forma saudável. 

**Rotas ativas:**
- Produção Frontend: [https://urbeat.com.br/cadastro](https://urbeat.com.br/cadastro)
- Produção API Auth: [https://urbeat.com.br/api/auth/register/seller](https://urbeat.com.br/api/auth/register/seller)

## 4. Uso seguro dos scripts de deploy (hardening)

Os scripts de deploy validam a origem antes de empacotar ou enviar qualquer arquivo.

- `scripts/criarDeployOracleCloud/04-deploy-application.ps1` e `scripts/deploy-internal.ps1`:
  - Resolvem `git HEAD`, branch e status do working tree antes de gerar/enviar pacotes.
  - **Bloqueiam por padrão** qualquer working tree sujo (tracked ou untracked).
  - `-AllowDirty` libera explicitamente o envio de um snapshot local sujo.
  - `-ExpectedCommit <sha>` exige que o `HEAD` corresponda (SHA completo ou prefixo com 7+ caracteres).
- Cada deploy gera um `deployment-manifest.json` com `commit`, `branch`, `dirty`, `generatedAtUtc`, `projectRoot` e os SHA256 dos arquivos enviados. O manifesto **não** contém conteúdo de arquivos nem segredos, e é enviado junto ao deploy (`/opt/urbeat/deployment-manifest.json` no OCI; raiz do deploy interno).
- `scripts/deploy-internal.ps1` **não** faz `git commit` nem `git push`; o Git não é alterado pelo deploy.

Exemplos (produção, exigindo um commit específico):

```powershell
scripts/criarDeployOracleCloud/deploy-all.ps1 -Step application -ExpectedCommit <sha>
scripts/deploy-internal.ps1 -ExpectedCommit <sha>
```

Para um snapshot local sujo (uso consciente):

```powershell
scripts/criarDeployOracleCloud/deploy-all.ps1 -Step application -AllowDirty
scripts/deploy-internal.ps1 -AllowDirty
```

`deploy-all.ps1` expõe `-AllowDirty`/`-ExpectedCommit` e os repassa **apenas** ao passo `application`; os demais passos continuam recebendo somente os parâmetros de SSH. `04-deploy-application.ps1` também pode ser executado diretamente (defaults de SSH `dexter@136.248.115.135:2208`).

Valide as invariantes antes de qualquer deploy:

```powershell
scripts/criarDeployOracleCloud/validate-pipeline.ps1
Invoke-Pester scripts/criarDeployOracleCloud/tests
```

