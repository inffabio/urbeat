# Autenticação Social no Urbeat - Guia Backend

## 📋 O que o Backend Precisa Tratar

## Fluxo de Autenticação OAuth 2.0  no google e facebook

[ ] Receber o token de autenticação do frontend
[ ] Validar o token com a API da rede social (Google/Facebook)
[ ] Verificar a autenticidade e validade do token
[ ] Extrair informações do usuário
    - Apenas Email, Nome e Telefone(se existir)

---

## Gerenciamento de Usuários

> Verificar se o usuário já existe no banco de dados (por email ou ID social)
> ➕ Criar novo registro se for primeiro login
> 🔄 Atualizar dados se usuário já existe
> 🔗 Vincular conta social à conta existente (se aplicável)
> Colocar como email verificado true
---

## Segurança

> 🔐 Gerar JWT ou sessão própria da aplicação
> 🛡️ Proteger rotas de checkout/pagamento
> ⏰ Gerenciar expiração de tokens
> 🚫 Implementar rate limiting para prevenir abuso

## Tratamento de Erros

> Token inválido ou expirado
> Permissões negadas pelo usuário
> Falha na comunicação com API social
> Email já cadastrado com método diferente
---

## Considerações Importantes

>🔒 Nunca armazene tokens de acesso das redes sociais permanentemente (verificar se já existe esta funcionalidade)
>🔄 Implemente refresh tokens para sessões longas (verificar se já existe esta funcionalidade)
> 📧 Valide unicidade de email entre diferentes métodos de login (verificar se já existe esta funcionalidade)
> 📱 Facilite vinculação de contas (mesmo email, providers diferentes)
---

## Checklist Técnico de Implementação (Pendente)
- [ ] No `AuthController` não existe endpoint de external login (ex: `POST /api/auth/external/login`).
- [ ] O `IdentityDbContext` precisa gerenciar logins externos se o EntityFramework Microsoft Identity for utilizado, via `UserLogins` table, ou armazenando na entidade customizada se não houver.
- [ ] O token Google precisa ser recebido pelo backend via IdToken.

## Chaves do google

* A Chave secreta google oAuth encontra-se no arquivo [../../InstrucoesFrontEnd.md]

## Se existir novos requisitos que a IA contemple adicionar abaixo
