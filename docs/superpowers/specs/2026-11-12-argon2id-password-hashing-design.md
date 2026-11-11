# Hash de senhas com Argon2id — design

## Objetivo

Adotar Argon2id para novas senhas do Urbeat enquanto o projeto ainda está em
testes, sem usar pepper e sem misturar o mecanismo de senha com tokens, PINs ou
outros códigos de curta duração.

## Decisões

- Usar **Argon2id** como algoritmo principal para credenciais de usuários.
- Não usar pepper.
- O salt será aleatório e exclusivo por hash, armazenado junto do hash no
  formato versionado.
- Cliente, lojista e administrador usarão o mesmo hasher de credenciais, mas
  continuarão com credenciais/papéis independentes.
- Tokens de confirmação/promoção continuarão usando o mecanismo próprio de
  token aleatório e hash rápido; não serão processados por Argon2id.
- PINs de entrega e OTPs continuarão usando seus mecanismos específicos.

## Compatibilidade

O hasher será composto/compatível durante a transição:

- novos hashes serão Argon2id;
- hashes PBKDF2/ASP.NET Identity existentes continuarão sendo verificados;
- após uma autenticação válida com hash legado, o resultado indicará rehash e o
  serviço atualizará o hash para Argon2id;
- nenhum reset de senha será necessário.

Como o ambiente ainda está em testes, a remoção definitiva do suporte legado
poderá ocorrer antes da primeira carga real de usuários, em uma alteração
separada e explícita.

## Formato e parâmetros

O hash armazenará versão, algoritmo, parâmetros, salt e subchave, permitindo
alterar parâmetros no futuro sem perder a capacidade de identificar hashes
antigos. Os parâmetros iniciais serão configuráveis e calibrados no ambiente:

- Argon2id, versão 19;
- memória inicial de aproximadamente 64 MiB;
- 3 iterações;
- paralelismo 2;
- salt de 16 bytes;
- subchave de 32 bytes.

O formato deve rejeitar entradas malformadas, parâmetros fora dos limites e
hashes truncados sem lançar detalhes sensíveis para o usuário.

## Integração .NET

- Implementar `IPasswordHasher<IdentityUser<Guid>>` compatível com
  `PasswordVerificationResult`.
- Registrar o hasher customizado no Identity depois da configuração do
  `AddIdentityCore`.
- `HashPassword` usa somente senha + salt aleatório; não lê Secrets/Vault para
  pepper.
- `VerifyHashedPassword` retorna `SuccessRehashNeeded` quando o hash legado ou
  os parâmetros Argon2id estão abaixo dos parâmetros atuais.
- Os fluxos existentes que usam `_userManager.PasswordHasher` para credenciais
  devem continuar funcionando; hashes de tokens devem ser isolados em uma
  abstração própria se ainda estiverem reutilizando o password hasher.

## Segurança operacional

- Calibrar custo para não causar lentidão excessiva ou exaustão de memória em
  login/registro.
- Manter rate limiting e lockout existentes.
- Não registrar senha, salt, pepper (inexistente) ou hash completo em logs.
- Não adicionar segredo novo ao OCI Vault, pois não haverá pepper.
- Não colocar parâmetros secretos em `.env` ou em arquivos com credenciais.

## Testes e aceite

- Hashes iguais nunca devem ser produzidos para a mesma senha por causa do salt.
- Senha correta valida; senha incorreta falha.
- Hash Argon2id malformado, truncado ou com parâmetros inválidos falha de forma
  segura.
- Hash PBKDF2 legado valida e retorna `SuccessRehashNeeded`.
- Novo cadastro e alteração/reset de senha persistem Argon2id.
- Login com hash legado migra para Argon2id após sucesso.
- Tokens de promoção/confirmacão e PINs não passam pelo Argon2id.
- Suítes backend, integração e build devem passar sem alterar contratos de API.
