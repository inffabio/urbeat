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
- Após confirmação, concluir o fluxo de criação/login do papel correto.

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
