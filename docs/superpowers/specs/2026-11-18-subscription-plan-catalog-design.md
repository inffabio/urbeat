# Catálogo de planos de assinatura e contratação — Design

## Objetivo

Centralizar no dashboard do urbeat a configuração dos planos de assinatura e
usar esse catálogo para a landing e para o cadastro de lojistas. O dashboard do
lojista não administrará planos. A primeira cobrança será registrada como stub
de boleto, sem gateway definido ou pagamento real.

## Decisões funcionais

- O administrador pode criar, editar, ordenar, ativar/desativar e configurar
  planos.
- Cada plano possui nome, descrição, features, valor, dias grátis, cobrança
  mensal e/ou anual e desconto percentual anual.
- A configuração inclui uma seção de parâmetros futuros do gateway. Esses
  parâmetros não serão usados para gerar cobranças nesta versão, não serão
  expostos em APIs públicas e não serão gravados em logs.
- A landing exibe somente planos ativos.
- Com mais de um plano, o título da seção é **“Escolha o plano ideal para o
  seu momento”**.
- Com exatamente um plano, o título é **“Crie hoje mesmo sua loja”**.
- O plano único continua visível com valor, features e botão **“Começar”**,
  ligeiramente maior, sem exagero.
- O botão **“Criar meu cadastro”** rola até a seção de planos.
- **“Começar”** leva ao cadastro com plano e periodicidade selecionados.
- O cadastro exige plano e periodicidade para novos lojistas.
- O browser envia somente identificadores; o servidor recalcula valor, trial,
  desconto e datas.

- A seleção fica pendente após o cadastro e é consumida somente quando a criação da loja for concluída; o trial começa na criação da loja.
- O lojista pode trocar de plano somente para um plano superior quando houver
  mais de um plano ativo.
- A superioridade é definida por nível/ordem configurado pelo administrador,
  não apenas pelo preço.
- Downgrade é rejeitado no frontend e no backend.
- O upgrade preserva features já utilizadas, mantém o histórico da assinatura
  anterior e aplica as condições comerciais do novo plano a partir da mudança.
- O trial é calculado em UTC.
- A assinatura guarda snapshot do nome, valor, periodicidade e condições
  contratadas para preservar histórico.
- Após o trial, ou imediatamente sem trial, é criada uma cobrança pendente
  `AwaitingGateway` como stub. Não haverá boleto real, PDF, linha digitável,
  código de barras, baixa ou webhook de pagamento nesta etapa.

## Arquitetura

### Backend

Evoluir `Plan` para suportar periodicidades e desconto anual; adicionar uma
configuração segura de gateway; evoluir `SellerSubscription` com periodicidade
e snapshot; e adicionar uma cobrança pendente de gateway. O catálogo
administrativo será protegido por política Admin. A oferta pública retornará
somente dados seguros e ativos.

O fluxo de cadastro receberá `PlanId` e periodicidade. O serviço de cadastro
validará que o plano está ativo e que a periodicidade está habilitada, criando
ou vinculando a assinatura somente depois da criação bem-sucedida do lojista.
A criação será transacional/idempotente conforme os padrões existentes.

### Frontend

- Completar `admin-plans` como CRUD de catálogo.
- Remover a capacidade de administrar planos do seller; manter leitura da
  assinatura/cobrança e permitir somente upgrade quando elegível.
- Atualizar a landing para título condicional, periodicidade, CTA e scroll.
- Atualizar o cadastro para preservar plano/periodicidade selecionados e
  enviá-los ao servidor.

## Segurança e compatibilidade

- Autorização administrativa no servidor é obrigatória.
- Valores, features, trial, periodicidade e desconto vindos do cliente são
  apenas intenção e nunca são autoridade.
- Parâmetros de gateway sensíveis não entram em DTO público, logs ou cache
  público.
- Datas persistidas em UTC.
- Assinaturas existentes permanecem válidas e mantêm seus snapshots.
- Migrações devem fornecer defaults compatíveis para dados atuais.

- Qualquer segredo futuro usa referência a Vault/secret store; o banco guarda somente parâmetros não secretos.

## Testes e critérios de aceite

- Unitários para validação do catálogo, periodicidade, desconto, cálculo UTC,
  snapshot e idempotência.
- Integração para autorização Admin, oferta pública, cadastro com plano,
  rejeição sem plano/período inválido e criação do stub.
- Frontend para CRUD admin, landing com um/vários planos, periodicidade,
  scroll para planos e cadastro com query params.
- Builds backend/frontend e `git diff --check`.

## Fora do escopo

- Integração com gateway.
- Geração ou pagamento de boleto real.
- Downgrade de plano pelo lojista.
- Prorrata, reembolso, cancelamento de assinatura ou cobrança automática real.

