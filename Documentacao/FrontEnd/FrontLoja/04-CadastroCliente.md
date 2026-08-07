# Especificação Funcional e Visual — Tela 4: Cadastro do Cliente / Endereço da Urbeat

## Projeto

- **Stack alvo:** Angular 20 + Ionic
- **Visão do software:** Existe uma empresa chamada Urbeat que controla todos os clientes vendedores que farão cadastro e cada um terá sua página de venda.
- **Objetivo:** Criar a tela de cadastro do cliente, responsável por coletar os dados pessoais e endereço de entrega após a revisão do carrinho.

---

## Visão Geral da Tela

> Esta tela representa a etapa de identificação do cliente e preenchimento do endereço no fluxo de delivery mobile do cliente da Urbeat.

> Ela tem como principais objetivos:

- 👤 Coletar os dados básicos do cliente
- 📱 Coletar número de celular para contato
- 📧 Coletar e-mail
- 📍 Coletar endereço de entrega
- 🏙️ Permitir seleção de cidade
- 🧭 Permitir seleção de bairro
- 🏠 Coletar rua, número e complemento
- ✅ Permitir seguir para a próxima etapa do checkout

---

## Estrutura Geral da Tela

> A tela pode ser dividida em 4 blocos principais:

- Identificação visual da loja
- Formulário de cadastro e endereço
- Botão principal de continuidade
- Navegação para próxima etapa do fluxo

---

## Estrutura Visual por Seções

> Identificação da Loja

- **Função:** Reforçar a identidade visual da loja durante o cadastro
- **Elementos identificados:**
  - Logo redondo no topo da tela
  - Nome da loja logo abaixo

> Comportamento esperado

- O logo exibido no topo deve ser a logo da loja do cliente
- A logo deve vir do backend de forma dinâmica
- O nome da loja também deve ser dinâmico
- O bloco de identificação deve ficar centralizado e com destaque visual

---

> Formulário de Cadastro

- **Função:** Coletar os dados do cliente e o endereço de entrega

### Campos identificados

- `Nome completo`
- `Número de celular`
- `Email`
- `Cidade`
- `Bairro`
- `Rua`
- `Número`
- `Complemento`

### Comportamento esperado

- Todos os campos devem possuir label clara
- Os campos devem seguir fluxo simples e objetivo
- O preenchimento deve ser confortável em mobile
- Os dados devem ser persistidos enquanto o usuário navega no checkout
- Recomenda-se preenchimento automático futuro para clientes já cadastrados

---

> Campo Nome completo

- **Função:** Identificar o cliente
- **Tipo:** texto
- **Comportamento esperado:**
  - aceitar nome completo
  - permitir digitação simples
  - validar campo obrigatório

---

> Campo Número de celular

- **Função:** Contato com o cliente
- **Tipo:** telefone com máscara
- **Exemplo visual:** `(22) 99999-9999`

### Comportamento esperado

- Aplicar máscara de telefone brasileiro
- Validar formato antes de continuar
- Campo obrigatório

---

> Campo Email

- **Função:** Identificação complementar e comunicação
- **Tipo:** e-mail

### Comportamento esperado

- Validar formato de e-mail
- Pode ser obrigatório ou opcional conforme regra do negócio
- Padrão recomendado: obrigatório para identificação do cliente

---

### Comportamento esperado

- Aceitar número da residência/comércio
- Pode aceitar valores como `S/N`, se necessário
- Campo obrigatório conforme regra definida

---

> Campo Cep

- **Função:** Informar o cep endereço
- **Tipo:** numero

### Comportamento esperado

- aceitar numero **formatado**
- campo obrigatório
- **Consulta pelo backend o viacep**
- Consultar API na documentação (../backend/API.md)
- Enquanto consulta use um Spinner global (centralizado) e Overlay de tela.
- Encontrando preencher os campos encontrados, Rua, Bairro etc.
- Se não encontrar liberar os campos para preencher manualmente

---

> Campo Rua

- **Função:** Informar a rua do endereço
- **Tipo:** texto
- **Comportamento esperado:**
  - aceitar nome da rua
  - campo obrigatório

---

> Campo Número

- **Função:** Informar o número do endereço
- **Tipo:** texto curto ou numérico

---

> Campo Complemento

- **Função:** Informações adicionais do endereço
- **Tipo:** texto
- **Exemplos:**
  - apartamento
  - bloco
  - referência
  - casa fundos

### Comportamento esperado

- Campo opcional
- Não deve bloquear continuidade se estiver vazio

---

> Campo Cidade

- **Função:** Selecionar a cidade de entrega
- **Tipo:** seletor / dropdown

### Comportamento esperado

- Deve carregar opções vindas do backend
- Ao selecionar cidade, pode impactar a lista de bairros disponíveis
- Campo obrigatório

---

> Campo Bairro

- **Função:** Selecionar o bairro de entrega
- **Tipo:** seletor / dropdown

### Comportamento esperado

- Deve depender da cidade selecionada, quando aplicável
- Deve carregar bairros válidos para entrega
- Campo obrigatório

---

> Botão Principal

- **Elemento identificado:** Botão largo com texto `CONTINUAR`
- **Função:** Avançar para a próxima etapa do checkout

### Comportamento esperado

- Deve validar os campos obrigatórios antes de continuar
- Deve ser a ação de maior destaque da tela
- Pode permanecer desabilitado até que os dados mínimos sejam preenchidos corretamente
- Ao clicar, deve salvar os dados do cliente e seguir para a etapa seguinte

---

## Especificação Visual

### Paleta de Cores

- **Laranja/Coral:** usado no botão principal e em elementos de destaque
- **Bege/Creme claro:** fundo geral da tela
- **Branco:** campos e superfícies
- **Cinza escuro / preto:** textos principais
- **Cinza médio/claro:** bordas, placeholders e elementos secundários

<  --app-primary: #f57c52;
   --app-primary-dark: #e5673f;
   --app-bg: #f7f1ea;
   --app-surface: #ffffff;
   --app-text-primary: #222222;
   --app-text-secondary: #6b6b6b;
   --app-border-light: #ececec; />

---

### Tipografia

- **Hierarquia recomendada**
  - Nome da loja: destaque, semibold/bold
  - Labels dos campos: semibold ou regular com boa legibilidade
  - Texto digitado: regular
  - Botão principal: bold

- **Nome fonte**
  - Google Fonts Nunito Sans

---

### Bordas e Formas

- Layout moderno, amigável e arredondado
- Campos com cantos suaves
- Botão principal com bordas arredondadas
- Logo circular da loja com destaque visual

< --radius-sm: 8px;
  --radius-md: 12px;
  --radius-lg: 16px;
  --radius-full: 999px; />

---

### Espaçamentos

<  
--space-1: 4px;
--space-2: 8px;
--space-3: 12px;
--space-4: 16px;
--space-5: 20px;
--space-6: 24px; >

- Espaçamento confortável entre campos
- Boa separação entre logo, nome da loja e formulário
- Respiro visual entre o formulário e o botão principal
- Área inferior suficiente para não conflitar com safe area

---

## Padronização de Campos e Botões

> Campos de formulário

- Fundo branco
- Borda leve
- Cantos arredondados
- Altura confortável para mobile
- Ícone opcional à esquerda quando fizer sentido
- Placeholder discreto
- Estado de foco com destaque na cor primária
- Estado de erro com mensagem clara abaixo do campo

---

> Campo seletor

- Mesmo padrão visual dos inputs
- Ícone de seta à direita
- Exibir placeholder enquanto não houver seleção
- Abrir lista/modal de seleção de forma amigável em mobile

---

> Botão primário

- **Uso:**
  - Continuar
  - Confirmar avanço no checkout

- **Padrão:**
  - Fundo laranja/coral
  - Texto branco
  - Bordas arredondadas
  - Largura ampla
  - Deve ser o principal destaque da tela

---

## Componentização Recomendada

> Para Angular 20 + Ionic, recomenda-se quebrar a tela em componentes reutilizáveis.

---

## Componentes sugeridos

- **customer-checkout-header.component**
  > Responsável por:
  - logo da loja
  - nome da loja

---

- **customer-form.component**
  > Responsável por:
  - nome completo
  - celular
  - email
  - cidade
  - bairro
  - rua
  - número
  - complemento

---

- **address-select-field.component**
  > Responsável por:
  - seleção de cidade
  - seleção de bairro

---

- **checkout-continue-action.component**
  > Responsável por:
  - botão continuar

---

## Regras Funcionais

### Cadastro do cliente

> Deve coletar os dados básicos para continuidade do pedido
> Deve manter os dados preenchidos durante a navegação entre etapas

---

### Validação de campos

- `Nome completo` obrigatório
- `Número de celular` obrigatório e validado com máscara
- `Email` validado conforme formato
- `Cep` irá buscar pelo via cep e preencher automaticamente os campos encontrados (Verificar a api no ./backend/API.md)
- `Cidade` obrigatória
- `Bairro` obrigatório
- `Rua` obrigatória
- `Número` obrigatório conforme regra definida
- `Complemento` opcional

---

### Cidade e Bairro

- A lista de cidades deve vir do backend
- A lista de bairros pode depender da cidade selecionada
- A seleção deve ser feita de forma simples e clara no mobile

---

### Continuidade do fluxo

- Clique no botão `CONTINUAR` deve validar o formulário
- Se os dados estiverem válidos, deve salvar as informações
- Após salvar, deve navegar para a próxima etapa do checkout

---

## Acessibilidade

> Requisitos recomendados:

- Campos com label clara
- Botões com `aria-label`
- Contraste adequado entre texto e fundo
- Área de toque mínima de 44x44px
- Mensagens de erro visíveis e objetivas
- Ordem de navegação consistente entre os campos

> Exemplos

<aria-label="Nome completo"
aria-label="Número de celular"
aria-label="Email"
aria-label="Selecionar cidade"
aria-label="Selecionar bairro"
aria-label="Rua"
aria-label="Número do endereço"
aria-label="Complemento"
aria-label="Continuar para próxima etapa" />

---

## Responsividade e Comportamento Mobile

**Como a tela é claramente mobile-first:**

### Requisitos

- Layout otimizado para smartphones
- Scroll vertical fluido
- Campos grandes e confortáveis para toque
- Botão principal com bom destaque visual
- Compatível com Android/iOS
- Respeitar `ion-safe-area`
- Ajustar espaçamento inferior para teclado virtual e safe area

---

## Critérios de Aceite

### Funcionais ✅

- Exibir logo redondo da loja no topo da tela
- Exibir nome da loja abaixo da logo
- Exibir formulário com nome, celular, email, cidade, bairro, rua, número e complemento
- Permitir preenchimento correto dos campos
- Validar campos obrigatórios antes de continuar
- Permitir seleção de cidade e bairro
- Salvar os dados do cliente durante o fluxo
- Permitir continuar para a próxima etapa do checkout

---

### Visuais 🎨

- Manter identidade visual das telas anteriores
- Usar laranja/coral como cor principal
- Manter fundo claro/bege
- Padronizar campos e botão principal
- Destacar visualmente a logo da loja no topo

---

### Técnicos ⚙️

- Desenvolvido em Angular 20 + Ionic
- Componentização clara
- Dados preparados para integração via API/backend
- Código reutilizável e escalável
- Compatível com Android e iOS

---

## Implementação Técnica

### Angular 20

- Preferir componentes standalone
- Usar signals ou RxJS para gerenciamento simples de estado
- Estrutura preparada para persistir os dados do cliente durante o checkout

### Ionic

- ion-content
- ion-input
- ion-select ou modal customizado
- ion-button
- ion-item, se necessário
- tratamento de teclado e safe area

### Estilo

- SCSS modular
- Tokens de cor no tema global
- Componentes isolados por responsabilidade

---

## Resumo Executivo

> A Tela 4 deve funcionar como a etapa de cadastro do cliente e endereço, garantindo simplicidade, clareza e continuidade no fluxo de compra.

### Resultado esperado

- O usuário consegue preencher seus dados com facilidade
- O endereço de entrega é informado de forma clara
- A identidade visual da loja permanece evidente
- O fluxo para a próxima etapa acontece sem fricção

## APIs do Backend

### 1. Registro do Cliente
Caso o cliente não tenha conta:

```http
POST /api/auth/register/customer
```

**Request:**
```json
{
  "fullName": "João Silva",
  "email": "joao@email.com",
  "password": "Senha@123",
  "phoneNumber": "(11) 99999-8888"
}
```

**Response 201:**
```json
{
  "succeeded": true,
  "userId": "a1b2c3d4-...",
  "emailConfirmationPending": true
}
```

### 2. Login do Cliente
Caso o cliente já tenha conta:

```http
POST /api/auth/login/customer
```

**Response 200:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",
  "expiresAtUtc": "2026-05-28T12:00:00Z",
  "refreshToken": "dGhpcyBpcyBhIHJlZnJl...",
  "refreshTokenExpiresAtUtc": "2026-06-27T12:00:00Z"
}
```

> O `accessToken` deve ser armazenado e enviado no header `Authorization: Bearer <token>` nas chamadas autenticadas.

### 3. Busca de CEP (ViaCEP)

```http
GET /api/address-lookup/cep/{cep}
```

**Exemplo:** `GET /api/address-lookup/cep/01304001`

**Response 200:**
```json
{
  "cep": "01304-001",
  "street": "Rua Augusta",
  "neighborhood": "Consolação",
  "city": "São Paulo",
  "state": "SP"
}
```

> **Fluxo:** Ao preencher o CEP → chamar API → exibir spinner/overlay → preencher Rua, Bairro, Cidade, Estado automaticamente.\
> Se o CEP não for encontrado (erro), liberar os campos para preenchimento manual.

### 4. Criar Endereço do Cliente

```http
POST /api/customer/addresses
```

**Request:**
```json
{
  "cep": "01304001",
  "number": "1500",
  "street": "Rua Augusta",
  "neighborhood": "Consolação",
  "city": "São Paulo",
  "state": "SP",
  "complement": "Apto 42",
  "reference": null,
  "isPrimary": true
}
```

**Response 201:**
```json
{
  "id": "guid...",
  "cep": "01304001",
  "street": "Rua Augusta",
  "number": "1500",
  "neighborhood": "Consolação",
  "city": "São Paulo",
  "state": "SP",
  "complement": "Apto 42",
  "isPrimary": true
}
```

### 5. Listar Endereços do Cliente

```http
GET /api/customer/addresses
```

**Response 200:** `CustomerAddressResponseDto[]` (max 3 endereços)

### Fluxo de Dados na Tela
1. Exibir logo + nome da loja (vindo do estado do checkout — já carregado na tela 1)
2. Usuário preenche formulário:
   - **Nome, Celular, Email** → usados para `POST /api/auth/register/customer`
   - **CEP** → `GET /api/address-lookup/cep/{cep}`
   - **Rua, Número, Complemento** → preenchido automaticamente ou manual
   - **Cidade, Bairro** → preenchido automaticamente via CEP (não há endpoint de dropdowns)
3. Ao clicar em CONTINUAR:
   - Se novo: `POST /api/auth/register/customer` → login automático → `POST /api/customer/addresses`
   - Se existente: `POST /api/auth/login/customer` → `POST /api/customer/addresses`

### Observações sobre Cidade/Bairro
O backend **não possui** endpoints de listagem de cidades ou bairros. O preenchimento é feito integralmente via consulta de CEP (`/api/address-lookup/cep/{cep}`). Para cidades não encontradas pelo ViaCEP, o usuário deve preencher manualmente.

