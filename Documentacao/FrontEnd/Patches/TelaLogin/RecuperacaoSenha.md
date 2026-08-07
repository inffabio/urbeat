# 📋 Especificação Técnica: Feature de Recuperação de Senha

---

## 📌 Visão Geral

Implementação completa do fluxo de recuperação de senha para usuários que esqueceram suas credenciais de acesso. O fluxo será iniciado por um link **"Recuperar senha"** localizado abaixo do campo de senha na tela de login.

---

## 🗂️ Índice

- [Fluxo Completo](#fluxo-completo)
- [Telas e Componentes](#telas-e-componentes)
- [Rotas](#rotas)
- [Lógica de Negócio](#lógica-de-negócio)
- [Integração com API](#integração-com-api)
- [Validações](#validações)
- [Tratamento de Erros](#tratamento-de-erros)
- [Segurança](#segurança)
- [Testes](#testes)

---

## 🔄 Fluxo Completo

```
[Tela de Login]
      │
      ▼
Usuário clica em "Recuperar senha"
      │
      ▼
[Tela 1: Inserir E-mail]
      │
      ▼
Sistema envia e-mail com token/link
      │
      ▼
[Tela 2: Confirmação de envio de e-mail]
      │
      ▼
Usuário acessa link no e-mail
      │
      ▼
[Tela 3: Criar nova senha]
      │
      ▼
Senha atualizada com sucesso
      │
      ▼
[Tela 4: Sucesso → Redireciona para Login]
```

---

## 🖥️ Telas e Componentes

---

### 🔐 Tela de Login (Modificação)

**Arquivo:** `pages/Login.tsx` *(ou equivalente no seu framework)*

**Modificação necessária:**
- Adicionar o link **"Recuperar senha"** logo abaixo do campo `input` de senha.

```tsx
// Exemplo de estrutura JSX
<div className="input-group">
  <label htmlFor="password">Senha</label>
  <input
    type="password"
    id="password"
    placeholder="Digite sua senha"
  />
  <a href="/recuperar-senha" className="forgot-password-link">
    Recuperar senha
  </a>
</div>
```

**Estilo do link:**
- Alinhamento: `text-align: right` ou `flex-end`
- Cor: cor primária do sistema (ex: `#3B82F6`)
- Tamanho da fonte: `0.85rem`
- Sem sublinhado por padrão; sublinhado ao hover
- Cursor: `pointer`

```css
.forgot-password-link {
  display: block;
  text-align: right;
  font-size: 0.85rem;
  color: var(--color-primary);
  margin-top: 4px;
  text-decoration: none;
  cursor: pointer;
}

.forgot-password-link:hover {
  text-decoration: underline;
}
```

---

### 📧 Tela 1: Inserir E-mail

**Arquivo:** `pages/RecuperarSenha/InserirEmail.tsx`

**Descrição:** Tela onde o usuário informa o e-mail cadastrado para receber o link de recuperação.

**Elementos da tela:**
- ✅ Título: `"Recuperar senha"`
- ✅ Subtítulo/descrição: `"Informe o e-mail cadastrado na sua conta. Enviaremos um link para você criar uma nova senha."`
- ✅ Campo `input` de e-mail com label `"E-mail"`
- ✅ Botão primário: `"Enviar link de recuperação"`
- ✅ Link secundário: `"← Voltar para o login"` → redireciona para `/login`

**Comportamento:**
- O botão fica **desabilitado** enquanto o campo estiver vazio ou com e-mail inválido
- Ao submeter, exibir um **loading spinner** no botão enquanto aguarda resposta da API
- Após resposta da API (sucesso **ou** e-mail não encontrado), redirecionar para a **Tela 2**

> ⚠️ **Importante (Segurança):** Mesmo que o e-mail **não exista** no banco de dados, o sistema deve exibir a mesma mensagem de sucesso para não revelar quais e-mails estão cadastrados.

---

### ✉️ Tela 2: Confirmação de Envio

**Arquivo:** `pages/RecuperarSenha/EmailEnviado.tsx`

**Descrição:** Tela de feedback informando que o e-mail foi enviado.

**Elementos da tela:**
- ✅ Ícone de envelope (SVG ou biblioteca de ícones)
- ✅ Título: `"Verifique seu e-mail"`
- ✅ Mensagem: `"Enviamos um link de recuperação para o e-mail informado. Verifique também sua caixa de spam."`
- ✅ Botão secundário: `"Reenviar e-mail"` (com cooldown de **60 segundos**)
- ✅ Link: `"← Voltar para o login"`

**Comportamento do botão "Reenviar e-mail":**
- Após o clique, iniciar contagem regressiva: `"Reenviar em 60s"`, `"Reenviar em 59s"`, ...
- Ao zerar, o botão volta ao estado normal e pode ser clicado novamente
- Exibir feedback de sucesso ao reenviar (ex: toast/snackbar `"E-mail reenviado com sucesso!"`)

---

### 🔑 Tela 3: Criar Nova Senha

**Arquivo:** `pages/RecuperarSenha/NovaSenha.tsx`

**Descrição:** Tela acessada pelo link enviado no e-mail. Contém os campos para definir a nova senha.

**Parâmetros de URL esperados:**
```
/recuperar-senha/nova-senha?token=<TOKEN_AQUI>
```

**Elementos da tela:**
- ✅ Título: `"Criar nova senha"`
- ✅ Campo `"Nova senha"` (input type password)
- ✅ Campo `"Confirmar nova senha"` (input type password)
- ✅ Indicador visual de força da senha *(ver seção de validações)*
- ✅ Botão primário: `"Salvar nova senha"`
- ✅ Ícone de olho 👁️ para mostrar/ocultar senha em ambos os campos

**Comportamento:**
- Validar o `token` da URL ao carregar a página
  - Se token **inválido ou expirado** → redirecionar para página de erro (Tela de Token Inválido)
  - Se token **válido** → renderizar o formulário normalmente
- O botão fica desabilitado até que todas as validações sejam atendidas
- Ao submeter com sucesso → redirecionar para **Tela 4**

---

### ✅ Tela 4: Senha Alterada com Sucesso

**Arquivo:** `pages/RecuperarSenha/Sucesso.tsx`

**Elementos da tela:**
- ✅ Ícone de check/sucesso (verde)
- ✅ Título: `"Senha alterada com sucesso!"`
- ✅ Mensagem: `"Sua senha foi atualizada. Faça login com sua nova senha."`
- ✅ Botão primário: `"Ir para o login"` → redireciona para `/login`

**Comportamento:**
- Redirecionar automaticamente para `/login` após **5 segundos**
- Exibir contador regressivo: `"Redirecionando em 5s..."`

---

### ❌ Tela de Token Inválido/Expirado

**Arquivo:** `pages/RecuperarSenha/TokenInvalido.tsx`

**Elementos da tela:**
- ✅ Ícone de erro/alerta (vermelho ou amarelo)
- ✅ Título: `"Link inválido ou expirado"`
- ✅ Mensagem: `"Este link de recuperação não é mais válido. Solicite um novo link."`
- ✅ Botão primário: `"Solicitar novo link"` → redireciona para `/recuperar-senha`
- ✅ Link: `"← Voltar para o login"`

---

## 🗺️ Rotas

| Rota | Componente | Descrição |
|------|-----------|-----------|
| `/login` | `Login.tsx` | Tela de login (modificada) |
| `/recuperar-senha` | `InserirEmail.tsx` | Inserção do e-mail |
| `/recuperar-senha/email-enviado` | `EmailEnviado.tsx` | Confirmação de envio |
| `/recuperar-senha/nova-senha?token=TOKEN` | `NovaSenha.tsx` | Formulário de nova senha |
| `/recuperar-senha/sucesso` | `Sucesso.tsx` | Senha alterada com sucesso |
| `/recuperar-senha/token-invalido` | `TokenInvalido.tsx` | Token inválido ou expirado |

---

## ⚙️ Lógica de Negócio

### 📌 Geração do Token
- O token deve ser gerado no **backend**
- Tipo: **UUID v4** ou **string criptograficamente segura** (mínimo 32 caracteres)
- Expiração: **1 hora** após a geração
- Armazenar no banco de dados vinculado ao usuário com:
  - `token` (hash)
  - `user_id`
  - `expires_at`
  - `used` (boolean) → marcar como usado após utilização

### 📌 Invalidação do Token
- O token deve ser invalidado (marcado como `used = true`) **imediatamente** após o uso
- Tokens expirados devem ser rejeitados mesmo que `used = false`
- Um usuário pode ter apenas **um token ativo** por vez; ao solicitar novo, invalidar o anterior

### 📌 E-mail
- Utilizar o serviço de e-mail já configurado no projeto (ex: SendGrid, SES, Nodemailer, Resend)
- **Template do e-mail:**

```
Assunto: "Recuperação de senha - [Nome do Sistema]"

Corpo:
  Olá, [Nome do Usuário]!

  Recebemos uma solicitação para redefinir a senha da sua conta.
  Clique no botão abaixo para criar uma nova senha:

  [BOTÃO: Redefinir minha senha] → link com token

  Este link expira em 1 hora.

  Se você não solicitou a recuperação de senha, ignore este e-mail.
  Sua senha permanecerá a mesma.

  Atenciosamente,
  Equipe [Nome do Sistema]
```

---

## 🔌 Integração com API

### `POST /api/auth/recuperar-senha`

**Descrição:** Solicita o envio do e-mail de recuperação.

**Request Body:**
```json
{
  "email": "usuario@exemplo.com"
}
```

**Response (sempre 200, independente de o e-mail existir):**
```json
{
  "message": "Se este e-mail estiver cadastrado, você receberá as instruções em breve."
}
```

---

### `GET /api/auth/recuperar-senha/validar-token?token=TOKEN`

**Descrição:** Valida se o token é válido e não expirou (chamado ao carregar a Tela 3).

**Response 200 (válido):**
```json
{
  "valid": true
}
```

**Response 400 (inválido/expirado):**
```json
{
  "valid": false,
  "message": "Token inválido ou expirado."
}
```

---

### `POST /api/auth/recuperar-senha/nova-senha`

**Descrição:** Atualiza a senha do usuário.

**Request Body:**
```json
{
  "token": "TOKEN_AQUI",
  "novaSenha": "NovaSenha@123",
  "confirmarSenha": "NovaSenha@123"
}
```

**Response 200 (sucesso):**
```json
{
  "message": "Senha alterada com sucesso."
}
```

**Response 400 (erro):**
```json
{
  "message": "Token inválido ou expirado."
}
```

**Response 422 (validação):**
```json
{
  "message": "As senhas não coincidem."
}
```

---

## ✅ Validações

### Campo de E-mail (Tela 1)
- [ ] Campo não pode estar vazio
- [ ] Formato de e-mail válido (regex: `/^[^\s@]+@[^\s@]+\.[^\s@]+$/`)
- [ ] Exibir mensagem de erro inline abaixo do campo

### Campo de Nova Senha (Tela 3)
- [ ] Mínimo de **8 caracteres**
- [ ] Pelo menos **1 letra maiúscula**
- [ ] Pelo menos **1 letra minúscula**
- [ ] Pelo menos **1 número**
- [ ] Pelo menos **1 caractere especial** (`!@#$%^&*`)
- [ ] Campo "Confirmar senha" deve ser **idêntico** ao campo "Nova senha"

### Indicador de Força da Senha
| Nível | Critério | Cor |
|-------|---------|-----|
| 🔴 Fraca | Apenas 1-2 critérios atendidos | Vermelho |
| 🟡 Média | 3 critérios atendidos | Amarelo |
| 🟢 Forte | Todos os critérios atendidos | Verde |

---

## 🚨 Tratamento de Erros

| Situação | Comportamento |
|----------|--------------|
| E-mail inválido (formato) | Mensagem inline: `"Informe um e-mail válido."` |
| Erro de rede ao enviar e-mail | Toast de erro: `"Erro ao enviar o e-mail. Tente novamente."` |
| Token expirado ao acessar Tela 3 | Redirecionar para `/recuperar-senha/token-invalido` |
| Senhas não coincidem | Mensagem inline: `"As senhas não coincidem."` |
| Senha não atende requisitos | Mensagem inline listando os requisitos faltantes |
| Erro interno do servidor (500) | Toast de erro: `"Ocorreu um erro inesperado. Tente novamente mais tarde."` |

---

## 🔒 Segurança

- 🔐 **Nunca** retornar se um e-mail existe ou não no banco (evitar user enumeration)
- 🔐 O token deve ser armazenado como **hash** (bcrypt ou SHA-256) no banco de dados
- 🔐 Implementar **rate limiting** no endpoint `POST /api/auth/recuperar-senha`:
  - Máximo de **3 requisições por IP** a cada 15 minutos
- 🔐 O link de recuperação deve funcionar **apenas uma vez**
- 🔐 Tokens expirados devem ser **deletados ou marcados** no banco periodicamente (job/cron)
- 🔐 A nova senha deve ser **hasheada** (bcrypt, argon2) antes de salvar no banco
- 🔐 Usar **HTTPS** em todos os endpoints
- 🔐 O campo de token na URL deve ser transmitido via **HTTPS** para evitar interceptação

---

## 🧪 Testes

### Testes de Unidade
- [ ] Validação de formato de e-mail
- [ ] Validação de força de senha
- [ ] Validação de senhas iguais
- [ ] Geração e expiração de token

### Testes de Integração
- [ ] Fluxo completo: solicitar → receber e-mail → redefinir senha → login com nova senha
- [ ] Tentativa de uso de token já utilizado
- [ ] Tentativa de uso de token expirado
- [ ] Reenvio de e-mail com cooldown

### Testes de UI (E2E)
- [ ] Clicar em "Recuperar senha" na tela de login navega para a rota correta
- [ ] Botão de envio desabilitado com e-mail inválido
- [ ] Contador regressivo do botão "Reenviar e-mail"
- [ ] Redirecionamento automático após sucesso
- [ ] Exibição correta da tela de token inválido

---

## 📁 Estrutura de Arquivos Sugerida

```
src/
├── pages/
│   ├── Login.tsx                          ← modificar
│   └── RecuperarSenha/
│       ├── index.tsx                      ← InserirEmail
│       ├── EmailEnviado.tsx
│       ├── NovaSenha.tsx
│       ├── Sucesso.tsx
│       └── TokenInvalido.tsx
├── components/
│   └── PasswordStrengthIndicator.tsx      ← componente de força de senha
├── services/
│   └── recuperarSenhaService.ts           ← chamadas à API
├── hooks/
│   └── useCountdown.ts                    ← hook para o contador regressivo
└── utils/
    └── validators.ts                      ← funções de validação (email, senha)
```

---

## 📝 Observações Finais

> - 🎨 Manter o padrão visual (design system/tokens) já existente no projeto
> - ♿ Garantir **acessibilidade**: labels associados aos inputs, mensagens de erro com `aria-live`, foco gerenciado entre telas
> - 📱 Todas as telas devem ser **responsivas** (mobile-first)
> - 🌐 Caso o projeto tenha **internacionalização (i18n)**, adicionar todas as strings nos arquivos de tradução
> - 🔄 Caso o projeto use **gerenciamento de estado global** (Redux, Zustand, Context), avaliar se algum estado da recuperação de senha precisa ser compartilhado