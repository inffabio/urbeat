# Especificação Funcional e Visual — Cadastro do Vendedor Urbeat

## Projeto

- **Produto:** Urbeat
- **Objetivo:** Criar a tela/fluxo de cadastro do vendedor para entrada na plataforma
- **Frontend alvo:** complementar o projeto existente em `c:\projetos\urbeat\frontend`
- **Backend alvo:** integrar com o projeto existente em `c:\projetos\urbeat\backend`
- **Stack backend existente:** .NET 9 + PostgreSQL
- **Objetivo funcional:** permitir que um vendedor crie sua conta e inicie o uso da plataforma com teste grátis

---

## Visão Geral da Tela

> Esta tela representa o cadastro inicial do vendedor na plataforma Urbeat.

> Ela tem como principais objetivos:

- 👤 coletar dados básicos do vendedor
- 📱 capturar WhatsApp com máscara
- 📧 capturar e validar e-mail
- 🔒 capturar senha com validação mínima
- 👁️ permitir exibir/ocultar senha
- ✅ criar conta com segurança
- 🛡️ reforçar confiança e proteção de dados
- 📄 exibir concordância com termos de uso e política de privacidade
- 🚀 comunicar proposta de valor da plataforma
- 🎁 destacar benefício de `7 dias grátis`

---

## Estrutura Geral da Tela

> A tela pode ser dividida em 2 grandes blocos:

- Bloco principal de cadastro
- Bloco institucional/comercial com benefícios da plataforma

> Em mobile, os blocos devem empilhar verticalmente.

> Em desktop/tablet, os blocos podem ficar lado a lado.

---

## Estrutura Visual por Seções

> Cabeçalho / Identidade da Marca

- **Função:** reforçar a marca Urbeat e contextualizar o cadastro
- **Elementos esperados:**
  - logo oficial da Urbeat
  - título principal
  - subtítulo de apoio

### Conteúdo identificado

- Título principal:
  - `Crie sua conta e comece agora seu delivery profissional`
- Subtítulo:
  - `É rápido, fácil e você ainda ganha 7 dias grátis para testar.`

### Comportamento esperado

- O logo da Urbeat deve ficar em destaque no topo
- O título deve comunicar claramente o objetivo da tela
- O subtítulo deve reforçar simplicidade e benefício comercial

---

> Área de Separação Social / Alternativa

- **Função:** reservar espaço visual para futura opção de autenticação alternativa
- **Elemento visual:**
  - divisor com texto `ou`

### Regra funcional

- Pode permanecer apenas visual nesta primeira versão
- Deve ficar preparado para futura integração com login social, se necessário

---

> Formulário de Cadastro

- **Função:** coletar os dados mínimos para criação da conta do vendedor

### Campos obrigatórios

- `Nome completo`
- `WhatsApp`
- `E-mail`
- `Senha`

### Ordem recomendada

1. Nome completo
2. WhatsApp
3. E-mail
4. Senha
5. Confirma Senha
6. Botão de criar conta

---

## Campos do Formulário

> Campo Nome completo

- **Tipo:** texto
- **Obrigatoriedade:** obrigatório
- **Objetivo:** identificar o vendedor responsável pela conta

### Comportamento esperado

- aceitar nome completo
- remover espaços excedentes nas extremidades
- exibir mensagem de erro quando vazio

### Mensagem de erro esperada

- `Nome é obrigatório`

---

> Campo WhatsApp

- **Tipo:** telefone
- **Obrigatoriedade:** obrigatório
- **Objetivo:** contato principal do vendedor

### Comportamento esperado

- aplicar máscara brasileira
- formato esperado:
  - `(99) 99999-9999`
- permitir apenas números na base do valor
- exibir erro quando inválido

### Regra de validação

- considerar válido quando possuir 10 ou 11 dígitos numéricos
- remover caracteres não numéricos para validação
- armazenar preferencialmente versão limpa no backend e formatada no frontend

### Mensagem de erro esperada

- `WhatsApp inválido`

---

> Campo E-mail

- **Tipo:** e-mail
- **Obrigatoriedade:** obrigatório
- **Objetivo:** identificação de acesso e comunicação da plataforma
- **Constraint** O email deve ser confirmado

### Comportamento esperado

- validar formato padrão de e-mail
- normalizar para minúsculas no processamento, se aplicável
- exibir erro quando inválido

### Mensagem de erro esperada

- `E-mail inválido`

---

> Campo Senha

- **Tipo:** senha
- **Obrigatoriedade:** obrigatório
- **Objetivo:** criar credencial de acesso segura

### Comportamento esperado

- iniciar mascarado
- permitir alternar entre mostrar/ocultar senha
- validar comprimento mínimo
- exibir orientação abaixo do campo

### Regra de validação

- mínimo de `6 caracteres`

### Texto auxiliar esperado

- `Mínimo de 6 caracteres`

### Mensagem de erro esperada

- `A senha deve ter no mínimo 6 caracteres`

---

> Ação Exibir/Ocultar Senha

- **Função:** melhorar usabilidade durante o preenchimento
- **Comportamento esperado:**

  - alternar o tipo do campo entre `password` e `text`
  - alternar ícone visual de olho aberto/fechado
  - não limpar o valor digitado

---

> Campo ConfirmarSenha

- **Tipo:** senha
- **Obrigatoriedade:** obrigatório
- **Objetivo:** Verificar se a senha confere

### Comportamento esperado

- iniciar mascarado
- permitir alternar entre mostrar/ocultar senha
- validar comprimento mínimo
- exibir orientação abaixo do campo

### Regra de validação

- mínimo de `6 caracteres`

### Texto auxiliar esperado

- `Mínimo de 6 caracteres`

### Mensagem de erro esperada

- `Senha não confirmada`

---

> Botão Principal

- **Texto:** `Criar minha conta`
- **Função:** enviar o formulário para criação da conta do vendedor

### Comportamento esperado

- deve validar todos os campos antes do envio

- deve exibir loading durante a requisição
- deve evitar múltiplos cliques usando spinner.
- em caso de sucesso:
  - criar a conta
  - Antes de habilitar a loja o vendedor deve confirmar o email
  - Gerar um pop up de aviso para confirmacao de email.
  - Após a confirmação
  - iniciar sessão e encaminhar para próximo passo do onboarding
- em caso de erro:
  - exibir feedback claro e amigável
  
### Regra de validação

1. Enviar um email com link de confirmação e mensagem para o Vendedor confirmar o email. Deve ter o corpo do email com uma boa estética com logo da Urbeat
2. Criar a tela de confirmação com a estetica do site
3. Após a confirmação iniciar sessão e redirecionar para o onboarding

---

> Selo de Segurança

- **Função:** reforçar confiança no cadastro
- **Texto esperado:**
  - `Seus dados estão protegidos com segurança`

### Comportamento esperado

- exibir próximo ao CTA ou abaixo do formulário
- pode usar ícone de cadeado/escudo

---

> Termos e Política

- **Função:** informar concordância legal
- **Texto esperado:**
  - `Ao criar sua conta, você concorda com nossos Termos de uso e Política de privacidade.`

### Comportamento esperado

- `Termos de uso` e `Política de privacidade` devem ser clicáveis
- abrir em nova rota, modal ou aba externa conforme arquitetura existente
- o texto deve ficar visível antes do envio final

---

## Bloco Institucional / Benefícios

> Função

- comunicar valor da plataforma
- aumentar conversão do cadastro
- explicar rapidamente o que o vendedor recebe

### Título da seção

- `Tudo que você precisa para vender mais e melhor`

### Itens identificados

- `Cardápio digital profissional`
  - Seu cardápio lindo, organizado e sempre disponível online.
- `Pedidos online`
  - Receba pedidos 24h por dia e organize tudo em um só lugar.
- `Painel de gestão completo`
  - Acompanhe pedidos, vendas, clientes e relatórios em tempo real.
- `Integração com WhatsApp`
  - Seus clientes pedem pelo site e você recebe tudo no WhatsApp.

### Comportamento esperado

- os benefícios devem ser exibidos em lista com ícones
- manter leitura rápida e visual amigável
- em telas grandes, esta área pode conter mockup/ilustração do produto

---

## Branding da Urbeat

> Diretrizes de marca observadas

- logo com tipografia amigável e arredondada
- predominância de:
  - laranja/terracota nas letras iniciais
  - amarelo/dourado nas letras finais
- presença de elementos visuais com sensação de sorriso/felicidade
- linguagem visual calorosa, positiva e acessível
- A logo está na pasta `../images/logo_v2.png`

### Aplicação recomendada

- usar a logo oficial no topo do fluxo
- manter linguagem amigável e acolhedora
- evitar visual frio ou corporativo demais
- reforçar a proposta de simplicidade e crescimento de vendas

---

## Especificação Visual

### Paleta de Cores

- **Laranja principal:** ações, destaques e identidade da Urbeat
- **Amarelo/dourado:** apoio visual da marca
- **Branco:** superfícies e campos
- **Bege/creme claro:** fundo geral, se seguir o padrão já usado nos fluxos
- **Cinza escuro/preto:** textos principais
- **Cinza claro:** bordas, divisores e textos secundários
- **Verde ou neutro positivo:** estados de sucesso, quando necessário

### Diretriz

- a tela deve manter identidade coerente com a marca Urbeat
- o botão principal deve usar a cor de ação da plataforma
- erros devem ser visíveis, mas sem agressividade exagerada

---

### Tipografia

- **Objetivo:** manter leitura simples, amigável e atual
- **Recomendação:** seguir padrão visual já adotado no frontend
- **Hierarquia recomendada:**
  - título principal em destaque
  - subtítulo em peso regular
  - labels dos campos com boa legibilidade
  - mensagens de erro em tamanho menor, porém bem visíveis
  - CTA em destaque

---

### Bordas e Formas

- layout amigável e moderno
- inputs com cantos arredondados
- botão principal com borda arredondada
- cards/benefícios com bordas suaves
- ícones com estilo leve e moderno

---

### Espaçamentos

- espaçamento confortável entre campos
- boa distância entre título, subtítulo e formulário
- respiro adequado entre CTA, selo de segurança e texto legal
- em mobile, evitar densidade excessiva

---

## Padronização de Componentes

> Campo de formulário

- label acima do campo
- ícone opcional no interior do input
- mensagem de erro logo abaixo
- borda neutra no estado padrão
- borda destacada no foco
- borda/estado visual de erro quando inválido

---

> Botão primário

- **Uso:**
  - criar conta
  - confirmar cadastro

### Padrão

- fundo em cor primária da marca
- texto branco
- largura ampla
- bordas arredondadas
- estado loading
- estado disabled quando necessário

---

> Botão/ação de visibilidade da senha

- ícone de olho aberto/fechado
- deve ser discreto, porém fácil de tocar
- não pode interferir na digitação

---

> Cards de benefícios

- ícone à esquerda ou topo
- título curto
- descrição objetiva
- aparência limpa
- podem ter leve sombra ou borda suave

---

## Regras Funcionais

### Validação do formulário

- validar antes do envio
- impedir submissão com campos inválidos
- limpar erro do campo conforme o usuário corrigir o valor
- exibir mensagens específicas por campo

---

### Regras mínimas de validação

- `Nome completo`
  - obrigatório
- `WhatsApp`
  - obrigatório
  - deve conter quantidade mínima válida de dígitos
- `E-mail`
  - obrigatório
  - deve estar em formato válido
- `Senha`
  - obrigatória
  - mínimo de 6 caracteres
- `Confirmar senha`
  - Verifica se está igual a senha
  - se não estiver critique com uma mesagem (`senha não confere`)
  - deixe as bordas do textbox vermelho e a mensagem de crítica abaixo

---

### Máscara do WhatsApp

- aplicar formatação durante a digitação
- limitar entrada ao tamanho brasileiro esperado
- não permitir caracteres inválidos no valor persistido

---

### Envio do cadastro

- ao enviar:
  - validar frontend
  - chamar backend existente
  - tratar resposta de sucesso/erro
  - Antes de habilitar a loja o vendedor deve confirmar o email
    - `Enviar um email com a logo, link de confirmação e mensagem para o Vendedor confirmar o email.`
- em sucesso:
  - criar conta do vendedor
  - acionar próximo passo do onboarding
- em falha:
  - exibir mensagem amigável
  - manter dados preenchidos no formulário

---

### Tratamento de erros esperados do backend

- e-mail já cadastrado
- WhatsApp já cadastrado
- dados inválidos
- erro inesperado de integração
- indisponibilidade temporária

### Mensagens recomendadas

- `Este e-mail já está em uso`
- `Este WhatsApp já está cadastrado`
- `Não foi possível criar sua conta agora. Tente novamente`

---

## Integração com Backend Existente

> Diretriz principal

- **não criar backend novo do zero**
- o frontend deve integrar ao projeto existente em:
  - `c:\projetos\urbeat\backend`

### Stack existente

- .NET 9
- PostgreSQL

### Requisitos de integração

- localizar módulo/endpoint já existente de autenticação ou cadastro
- reaproveitar padrões já definidos no backend:
  - DTOs
  - validações
  - convenções de controller
  - serviços
  - autenticação
  - persistência
- caso falte endpoint específico, criar seguindo o padrão atual da solução

---

### Operação esperada no backend

- receber dados do vendedor
- validar consistência
- verificar duplicidade por e-mail e/ou WhatsApp
- criar registro do vendedor/usuário
- persistir senha com hash seguro
- retornar resposta padronizada para o frontend
- opcionalmente gerar token de autenticação após cadastro

---

### Dados mínimos a persistir

- nome completo
- WhatsApp
- e-mail
- senha hash
- data de criação
- status da conta
- aceite de termos, se aplicável
- origem do cadastro, se aplicável

---

### Banco de dados

- utilizar PostgreSQL já existente no backend
- respeitar migrations, naming conventions e estrutura atual
- evitar duplicação de tabelas se já existir entidade equivalente para usuário/vendedor

---

## Integração com Frontend Existente

> Diretriz principal

- o desenvolvimento deve complementar o projeto em:
  - `c:\projetos\urbeat\frontend`

### Requisitos

- seguir arquitetura já adotada no frontend
- reaproveitar:
  - componentes compartilhados
  - tema global
  - serviços HTTP
  - interceptors
  - tratamento de erro
  - roteamento
  - guards, se existirem
- manter consistência com o design system atual da Urbeat

---

### Estrutura esperada no frontend

- página de cadastro do vendedor
- componente de formulário
- serviço de autenticação/cadastro
- validação reativa
- feedback visual de sucesso/erro
- redirecionamento pós-cadastro

---

## Sugestões de Fluxo Pós-Cadastro

### Opção recomendada 1

- criar conta
- autenticar automaticamente
- redirecionar para onboarding inicial do vendedor

### Opção recomendada 2

- criar conta
- redirecionar para tela de confirmação/verificação
- depois permitir acesso ao painel

### Opção recomendada 3

- criar conta
- exibir mensagem de sucesso
- levar direto para configuração inicial da loja

---

## Sugestões de Ícones e Imagens

### Para os campos

- `Nome completo`
  - ícone de usuário/pessoa
- `WhatsApp`
  - ícone de telefone ou balão WhatsApp
- `E-mail`
  - ícone de envelope
- `Senha`
  - ícone de cadeado
- `Mostrar/ocultar senha`
  - ícone de olho aberto/fechado

---

### Para segurança

- escudo
- cadeado
- selo de proteção

---

### Para os benefícios

- `Cardápio digital profissional`
  - ícone de cardápio/lista
- `Pedidos online`
  - ícone de sacola, pedido ou carrinho
- `Painel de gestão completo`
  - ícone de dashboard, gráfico ou relatório
- `Integração com WhatsApp`
  - ícone oficial do WhatsApp

---

### Para mockup/ilustração lateral

- usar ilustração do painel da plataforma
- usar composição com celular/notebook mostrando:
  - pedidos
  - cardápio
  - painel
- manter estética leve e moderna

---

## Segurança

### Requisitos mínimos

- nunca salvar senha em texto puro
- usar hash seguro no backend
- usar HTTPS nas requisições
- sanitizar entradas
- proteger contra múltiplos envios
- tratar mensagens de erro sem expor detalhes internos do backend
- respeitar LGPD e política de privacidade da plataforma

---

## Acessibilidade

### Requisitos recomendados

- labels claras em todos os campos
- mensagens de erro associadas corretamente
- contraste adequado
- área de toque confortável
- navegação por teclado
- ícones com significado claro
- botão principal acessível
- links de termos e política acessíveis

### Exemplos de rótulos acessíveis

- `Nome completo`
- `WhatsApp`
- `E-mail`
- `Senha`
- `Mostrar senha`
- `Criar minha conta`

---

## Responsividade

### Mobile

- formulário em largura total
- benefícios abaixo do formulário
- CTA sempre bem visível
- teclado não deve encobrir campo ativo nem botão principal

### Desktop

- layout em duas colunas
- formulário em uma área
- benefícios/mockup em outra
- centralização vertical opcional

---

## Critérios de Aceite

### Funcionais ✅

- exibir logo da Urbeat
- exibir título e subtítulo do cadastro
- exibir formulário com nome, WhatsApp, e-mail e senha
- aplicar máscara no WhatsApp
- permitir mostrar/ocultar senha
- validar campos obrigatórios
- impedir envio inválido
- integrar com backend existente
- criar conta com sucesso
- tratar erros de cadastro
- exibir termos de uso e política de privacidade
- permitir continuidade para próximo passo do onboarding

---

### Visuais 🎨

- manter identidade da marca Urbeat
- usar visual amigável e moderno
- destacar benefício de 7 dias grátis
- exibir benefícios da plataforma com clareza
- manter botão principal em destaque
- exibir estados de erro e foco de forma clara

---

### Técnicos ⚙️

- complementar o frontend existente
- integrar ao backend existente em .NET 9
- usar banco PostgreSQL já existente
- reaproveitar arquitetura atual
- código reutilizável e escalável
- preparado para evolução futura com login social e onboarding

---

## Resumo Executivo

> A tela de cadastro do vendedor da Urbeat deve ser simples, confiável e orientada à conversão.

### Resultado esperado

- o vendedor entende rapidamente o valor da plataforma
- consegue criar sua conta sem fricção
- recebe feedback claro em caso de erro
- entra no fluxo de onboarding com rapidez
- o sistema registra os dados com segurança e integração ao backend já existente