# Promoção de papel com verificação por e-mail — design

## Contexto confirmado

O Urbeat usa uma identidade única por e-mail em `AspNetUsers`, podendo associar
os papéis `Customer` e `Seller` à mesma identidade. As credenciais de cada papel
são mantidas em `UserRoleCredential`, portanto cliente e lojista não precisam
compartilhar a mesma senha.

Hoje, ao adicionar o segundo papel a um e-mail existente, o backend exige
`CurrentPassword` como prova de posse quando a requisição não está autenticada.
O cadastro de lojista exibe esse campo como “Já tem conta urbeat?”.

## Objetivo

Remover a exigência e o campo de senha atual da experiência de cadastro, mantendo
a proteção contra apropriação indevida da conta por meio de confirmação de posse
do e-mail.

## Fluxos

### Usuário já autenticado

- Se a requisição de cadastro do segundo papel vier autenticada pelo próprio
  `userId`, o backend reconhece a sessão como prova de posse.
- O novo papel é associado à identidade existente.
- A nova credencial do papel continua sendo criada com a senha informada para
  esse papel, sem alterar a credencial do papel anterior.
- O usuário segue para o fluxo normal de confirmação/login conforme o estado do
  e-mail.

### Usuário não autenticado

- Se o e-mail já existir, o backend não adiciona imediatamente o novo papel.
- O backend cria um desafio de promoção de papel, assinado, de curta duração,
  vinculado à identidade, ao e-mail e ao papel solicitado.
- Um link/código é enviado para o e-mail existente.
- O e-mail deve explicar claramente que alguém solicitou adicionar o acesso de
  lojista/cliente à conta, informar que nenhuma senha atual será solicitada pelo
  link, apresentar o nome do papel que será adicionado, o prazo de validade e a
  ação para confirmar ou ignorar a solicitação.
- A confirmação consome o desafio uma única vez e conclui a promoção de papel de
  forma transacional/idempotente.
- Somente após a confirmação a nova credencial é criada e o acesso/token do novo
  papel pode ser liberado.

## Senhas e tokens

- Não haverá senha compartilhada automaticamente entre cliente e lojista.
- A senha do novo papel continua sendo criada explicitamente no cadastro, ou o
  fluxo poderá direcionar o usuário para definir essa senha após confirmar o
  e-mail; nunca será armazenada vazia.
- Token JWT não substitui prova de posse: ele só será emitido após sessão válida
  ou desafio de e-mail validado.
- Não gerar token baseado apenas em e-mail informado, campo de senha vazio ou
  existência de uma identidade.
- Recuperação de senha continuará independente por papel.

## Backend

- Remover `CurrentPassword` do contrato público de cadastro quando o fluxo final
  estiver migrado, ou mantê-lo apenas como compatibilidade temporária sem ser
  solicitado pela UI; a validação autoritativa deve aceitar sessão autenticada ou
  desafio de promoção válido.
- Reutilizar a assinatura/expiração segura existente de desafios somente se o
  payload incluir explicitamente finalidade de promoção, papel, identidade e uso
  único; não reutilizar um desafio de troca de e-mail sem diferenciar a finalidade.
- Criar endpoint de confirmação de promoção ou estender o endpoint de confirmação
  com um tipo de fluxo explícito, sem aceitar tokens ambíguos.
- Aplicar rate limiting, expiração, consumo único, respostas genéricas para não
  revelar se um e-mail possui outra função e logs de auditoria sem senha/token.
- Preservar transação: identidade, papel e `UserRoleCredential` devem ser
  consistentes mesmo em falhas intermediárias.
- Atualizar DTOs, validators, interfaces, serviço, controller, testes unitários e
  integração; nenhuma senha em claro deve ser registrada ou persistida.

## Frontend

- Remover do cadastro de lojista o campo `currentPassword`, seu toggle, hint e
  validações associadas.
- Manter os campos de senha nova e confirmação enquanto a nova credencial for
  definida nessa tela; atualizar os textos para explicar que ela é exclusiva do
  acesso lojista.
- Tratar a resposta de “confirmação de promoção pendente” com uma tela/mensagem
  clara: link enviado para o e-mail informado, sem mencionar detalhes sobre
  papéis existentes além do necessário.
- O texto exibido no e-mail deve ser específico para promoção de papel, sem
  reutilizar uma mensagem genérica de confirmação de cadastro ou troca de e-mail.
- Reutilizar a tela de confirmação somente com um parâmetro explícito de tipo de
  operação e dados mínimos; não colocar token sensível em logs ou mensagens.
- Ler o token de promoção uma única vez na tela de confirmação e removê-lo
  imediatamente da URL (`history.replaceState`/navegação com `replaceUrl`),
  mantendo-o apenas em memória e nunca em `localStorage`/`sessionStorage`. O
  token viaja no **fragmento** da URL (`#promotionToken=...`), nunca na query
  string: o fragmento não é enviado ao servidor, não aparece em logs de proxy e
  não vaza pelo header `Referer`. O `index.html` captura o fragmento em memória e
  o remove antes do Angular/analytics; a tela também lê o fragmento como fallback
  e nunca aceita `?promotionToken=`. A confirmação também aplica
  `Referrer-Policy: no-referrer` global.
- Após confirmação, concluir o fluxo de criação/login do papel correto.

## Copy aprovado do e-mail

### Acesso de lojista

**Assunto:** `Confirme seu novo acesso de lojista no urbeat`

```text
Olá!

Foi solicitado o acréscimo de um acesso de lojista à sua conta urbeat usando este endereço de e-mail.

Para confirmar e ativar esse acesso, clique no botão abaixo:

[Confirmar acesso de lojista]

Essa confirmação:

- não altera seu acesso de cliente;
- não altera sua senha atual;
- não solicita sua senha por e-mail;
- permite que você use o urbeat também como lojista.

Este link é válido por 30 minutos e pode ser usado apenas uma vez.

Se você não solicitou esse acesso, ignore esta mensagem. Nenhuma alteração será realizada.

Por segurança, o urbeat nunca solicita sua senha por e-mail.

Atenciosamente,
Equipe urbeat
```

### Acesso de cliente

O fluxo inverso usa a mesma estrutura, substituindo “acesso de lojista” por
“acesso de cliente” e “use o urbeat também como lojista” por “use o urbeat também
como cliente”.

## Durabilidade, concorrência e retomada

- O token de confirmação é derivado com HMAC-SHA256 do identificador da solicitação
  e de uma chave **dedicada** (`RolePromotionChallenge:SigningKey`). Não há fallback
  para `Jwt:Secret` nem para a chave de troca de e-mail, e nenhum valor padrão é
  distribuído no repositório. Nada do token é persistido além do seu SHA-256, e o
  job de e-mail reconstrói o link a partir da solicitação persistida. Assim, perda
  de cache, reinício de worker, job atrasado dentro dos 30 minutos ou queda entre o
  commit e o enfileiramento não deixam a solicitação sem forma de envio.
- A entrega usa um lease persistido (`EmailSendingAtUtc` + `EmailSendAttemptId`).
  O job só envia depois de reivindicar o lease; a recuperação só reenfileira
  solicitações cujo lease está ausente ou expirado. Assim, enfileiramento duplicado,
  job atrasado e varredura de recuperação não geram envio concorrente, e um worker
  que caiu deixa de bloquear a entrega após o lease expirar.
- `EmailSentAtUtc` só é gravado depois que o provedor aceitou a mensagem. O
  `Message-Id`/chave de idempotência é determinístico por `requestId`
  (`role-promotion-<requestId>`, sem `:` para continuar sendo um dot-atom válido do
  `Message-Id`), nunca derivado do token ou da senha, para permitir
  deduplicação pelo MTA em retentativas. SMTP puro continua sendo pelo menos uma vez
  se o processo cair depois do aceite e antes da gravação; o lease elimina as
  duplicidades evitáveis.
- Existe no máximo uma solicitação pendente por identidade/papel: a criação é
  serializada por um advisory lock transacional e protegida por um índice único
  parcial (`ConsumedAtUtc IS NULL AND SupersededAtUtc IS NULL`). Uma nova
  solicitação para o mesmo papel **reutiliza** a pendente enquanto ela estiver
  válida: o link já entregue nunca é invalidado, a senha pendente não pode ser
  trocada antes de expirar e a resposta continua genérica. Somente uma
  solicitação expirada é substituída — nesse caso não há link válido a perder, e
  uma falha na gravação da nova não deixa a conta sem nada que já funcionasse.
- Um cooldown persistente por identidade (`RolePromotionChallenge:CreationCooldownSeconds`,
  padrão 120s) impede que uma solicitação para o outro papel dispare um segundo
  e-mail dentro da janela. A chave usada nos logs é um HMAC do e-mail
  normalizado sob a chave dedicada (`IdentityKey`), nunca o endereço em claro, e
  nenhuma resposta distingue existência de conta, papel pendente ou supressão.
- A confirmação reserva o desafio por um lease curto (`ClaimedAtUtc` +
  `ClaimedBy`, o id da tentativa) e só grava `ConsumedAtUtc` depois de aplicar
  papel e credencial. `MarkAppliedAsync` exige `requestId` + `ClaimedBy` e faz
  update condicional no lease ainda válido: se o lease expirou ou foi assumido por
  outra tentativa, o request **não** é marcado como aplicado e a confirmação
  responde erro (recarregando/abortando), nunca sucesso. Uma falha no meio não
  consome o link, e uma repetição depois de aplicação parcial responde de forma
  idempotente sem duplicar papel ou credencial. No PostgreSQL tudo ocorre na
  mesma transação; quando o provedor não suporta transação (InMemory usado nos
  testes), a aplicação libera o lease e compensa credencial, papel, claim
  `FullName` e `EmailConfirmed` antes de responder a falha, verificando cada
  `IdentityResult` e o estado final. A compensação só remove o que a tentativa
  comprovadamente criou: a credencial pelo **id** gerado e pelo
  `RolePromotionRequestId` da requisição, o papel apenas se a própria tentativa o
  atribuiu, e claim/`EmailConfirmed` apenas se a tentativa os alterou. Uma
  credencial ou papel criados por cadastro/confirmação concorrente nunca são
  removidos nem sobrescritos. A compensação não é tratada como rollback
  garantido: se um passo falhar, a falha é auditada
  (`RolePromotionCompensationFailed`), a resposta continua sendo de erro e o
  estado residual é escolhido para ser retomável — a credencial é removida antes
  do papel e uma nova tentativa conclui a aplicação reaproveitando um papel
  órfão, sem duplicar papel ou credencial.
- A credencial criada pela promoção guarda `UserRoleCredential.RolePromotionRequestId`
  (coluna opcional, índice único parcial e FK para `RolePromotionRequests` com
  `ON DELETE SET NULL`; credenciais normais permanecem com a coluna nula). Se a
  gravação da credencial vencer e a do papel falhar, a repetição do mesmo link
  reaproveita essa credencial e conclui papel/claim/`EmailConfirmed` e o `Applied`
  sem duplicar nada. Uma credencial sem vínculo (ou ligada a outra solicitação)
  **nunca** é adotada nem sobrescrita: a confirmação falha de forma genérica, o
  caso é auditado e a remediação é explícita (reenvio do link da solicitação que a
  originou ou intervenção operacional).
- A auditoria posterior a uma promoção já commitada é **não fatal**: se a escrita
  do `AuditLog` falhar depois do sucesso (ou do commit), o erro é logado para
  retry/outbox e o usuário continua recebendo a confirmação de sucesso. Nenhum
  token, senha ou hash é incluído na descrição da auditoria.
- Rate limiting continua particionado por `RemoteIpAddress`, que só reflete
  `X-Forwarded-For` de proxies confiáveis. `ForwardedHeaders` lê
  `ForwardedHeaders:KnownProxies`/`KnownNetworks`; configuração vazia falha
  fechada. Em produção apenas a rede privada do Docker (`172.16.0.0/12`, onde o
  nginx roda) é confiável, e o nginx envia `X-Real-IP`/`X-Forwarded-For`; um
  cliente externo não consegue forjar o IP usado pelo rate limiting.

## Configuração da chave de promoção

- `RolePromotionChallenge:SigningKey` é obrigatória e validada no startup
  (`ValidateOnStart`): vazia, placeholder conhecido, menos de 32 caracteres ou com
  entropia insuficiente impedem a aplicação de iniciar. A mensagem de erro nunca
  imprime o valor.
- Em produção, forneça o valor por variável de ambiente/secret store
  (`RolePromotionChallenge__SigningKey`), por exemplo com 32+ bytes aleatórios
  gerados por CSPRNG. Nenhum segredo real deve ser commitado.
- O `appsettings.json` mantém a chave vazia apenas como documentação do nome da
  configuração; testes e factories fornecem uma chave explícita de teste.

## Segurança e compatibilidade

- O servidor permanece autoritativo para posse de conta, papel, senha, expiração
  e emissão de token.
- Uma pessoa não autenticada nunca ganha o papel apenas por digitar um e-mail.
- A sessão existente de cliente não deve ser apagada ao promover para lojista.
- Cadastros de e-mails novos continuam funcionando sem desafio de promoção.
- Repetição do mesmo desafio ou confirmação concorrente não deve criar duas
  credenciais nem duplicar o papel.

## Critérios de aceite

- O formulário não exibe mais o textbox de senha atual para e-mail já cadastrado.
- Conta nova de cliente/lojista continua cadastrando normalmente.
- Usuário autenticado consegue adicionar o segundo papel sem digitar a senha do
  primeiro papel.
- Usuário não autenticado recebe confirmação por e-mail e só ganha o segundo
  papel após validar o desafio.
- Cliente e lojista conseguem autenticar com credenciais independentes.
- Não existe emissão de token com senha vazia ou somente com base no e-mail.
- Expiração, uso único, rate limit, auditoria e respostas genéricas são testados.
