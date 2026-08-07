# 🍔 Especificação Funcional e Técnica — Inclusão de Cardápio / Produtos da Loja

## 🎯 Objetivo

Implementar a página de **Inclusão de Cardápio / Cadastro de Produtos** como próxima etapa do fluxo:

1. **CadastroLojasVendedor**
2. **ConfiguraLoja**
3. **Inclusão de Cardápio**

A funcionalidade será integrada ao projeto existente em:

- **Backend:** `c:\projetos\urbeat\backend`
- **Frontend:** `c:\projetos\urbeat\frontend`
- **Stack backend:** `.NET 9`
- **Banco de dados:** `PostgreSQL`

O objetivo desta etapa é permitir que o vendedor:

- gerencie categorias do cardápio,
- cadastre produtos,
- configure foto, preço e disponibilidade,
- adicione personalizações,
- configure variações,
- organize destaque e ordem de exibição,
- visualize a lista de produtos já cadastrados,
- mantenha tudo salvo automaticamente.

---

# 🧭 Visão geral da tela

A página representa uma etapa avançada do onboarding/configuração da loja, com progresso em torno de **75% concluído**.

## Estrutura visual esperada

A tela deve ser dividida em **dois blocos principais**:

### 🧱 Bloco esquerdo — Configuração e cadastro
Contém os formulários e seções de gerenciamento:

1. **Gerenciar categorias**
2. **Cadastrar produtos**
   - Informações do produto
   - Personalização
   - Organização
   - Sabores e Variações
   - Cupons (opcional / conforme escopo)

### 🛍️ Bloco direito — Lista de produtos cadastrados
Exibe os produtos já incluídos na loja, com:

- foto do produto,
- nome,
- preço,
- categoria,
- status,
- ações.

---

# ✅ Objetivo funcional principal

Permitir que o lojista cadastre e organize os itens do cardápio da loja de forma prática, com experiência semelhante a um painel moderno de delivery.

O sistema deve suportar, no mínimo:

- categorias por loja,
- múltiplos produtos por categoria,
- imagem do produto,
- preço normal e promocional,
- adicionais,
- opções de escolha,
- variações por tamanho/sabor,
- destaque,
- ordenação,
- disponibilidade.

---

# 🧩 Escopo funcional detalhado

---

# 1. Gerenciar Categorias

## 1.1 Objetivo
Permitir que o lojista crie e organize as categorias do cardápio.

Exemplos de categorias:

- Hambúrgueres
- Batatas
- Bebidas
- Combos
- Sobremesas

## 1.2 Exibição em tela
A seção deve exibir uma tabela/lista com colunas:

- **Categoria**
- **Produtos**
- **Status**
- **Ações**

## 1.3 Ações da categoria
Cada categoria deverá permitir:

- visualizar quantidade de produtos vinculados,
- editar nome,
- ativar/inativar,
- excluir, se não houver restrição de negócio,
- reordenar posição de exibição.

## 1.4 Botão `+ Nova categoria`
Ao clicar, abrir **modal/popup** para cadastro de categoria.

### Campos do modal
- **Nome da categoria** *(obrigatório)*
- **Descrição curta** *(opcional)*
- **Ativa** *(boolean)*
- **Ordem de exibição** *(opcional)*

### Ações do modal
- `Cancelar`
- `Incluir`

## 1.5 Regras
- o nome da categoria deve ser único por loja,
- não permitir categoria vazia,
- uma categoria inativa não deve aparecer para o cliente final,
- produtos continuam vinculados mesmo se a categoria for inativada, mas deixam de aparecer na loja pública.

---

# 2. Cadastrar Produtos

## 2.1 Objetivo
Cadastrar os produtos que ficarão disponíveis para pedidos online.

---

## 2.2 Seção 1 — Informações do Produto

### Campos obrigatórios
- **Nome do produto** `*`
- **Categoria** `*`
- **Descrição** `*`
- **Preço** `*`
- **Foto do produto** `*`

### Campos opcionais
- **Preço promocional**
- **Produto disponível** *(toggle/checkbox)*

### Especificação dos campos

#### 🏷️ Nome do produto
- tipo: texto
- obrigatório
- limite sugerido: `100 caracteres`
- deve exibir contador visual, ex.: `13/100`

#### 📂 Categoria
- tipo: select
- obrigatório
- listar apenas categorias da loja
- não permitir cadastro de produto sem categoria

#### 📝 Descrição
- tipo: textarea
- obrigatório
- limite sugerido: `300 caracteres`
- exibir contador visual, ex.: `51/300`

#### 💰 Preço
- tipo: monetário
- obrigatório
- prefixo visual: `R$`
- persistir como `numeric(10,2)` no banco

#### 🏷️ Preço promocional
- tipo: monetário
- opcional
- se informado, deve ser menor que o preço normal

#### 🖼️ Foto do produto
- tipo: upload
- obrigatório
- formatos aceitos:
  - `JPG`
  - `PNG`
  - `WEBP`
- tamanho máximo: `2MB`

#### ✅ Produto disponível
- boolean
- se desativado, o produto permanece cadastrado, porém não aparece no cardápio público

---

## 2.3 Comportamento do upload de imagem
Ao enviar a imagem do produto:

- deve mostrar preview da foto no formulário,
- deve salvar a URL retornada pelo backend,
- deve atualizar a lista de produtos após salvar,
- deve permitir troca da imagem com botão `Trocar imagem`.

### Sugestão de uso inicial para mocks/testes
Podem ser utilizados assets de exemplo já alinhados com o cardápio visual esperado, como:

- **X-Burger Bacon**
- **Batata Frita**
- **Coca-Cola 350ml**

---

# 3. Personalização do Produto

## 3.1 Objetivo
Permitir que o lojista configure itens extras e opções de escolha vinculados ao produto.

A seção deve conter dois blocos:

1. **Adicionais**
2. **Opções de escolha**

---

## 3.2 Comportamento geral da seção
Os itens já incluídos devem aparecer em lista no formulário.

### Regra obrigatória solicitada
Quando o usuário clicar em:

- `+ Adicionar adicional`
- `+ Adicionar opção`

deve abrir um **popup/modal** com os campos de inserção.

Ao clicar em **Incluir**:

- o item deve ser adicionado ao **JSON do produto em memória/estado local**,
- o item deve aparecer imediatamente na tela,
- o autosave deve persistir a alteração no backend.

Cada item listado deve ter, **do lado direito**, um botão `X` para exclusão.

Ao clicar no `X`:

- remover do **JSON local**,
- remover da tela imediatamente,
- persistir a remoção no backend no autosave ou por chamada imediata.

---

## 3.3 Bloco `Adicionais`

### O que são
Itens extras opcionais que o cliente pode acrescentar ao produto.

Exemplos:
- Bacon — R$ 4,50
- Queijo extra — R$ 3,00
- Ovo — R$ 2,50

### Exibição em tela
Cada adicional deve aparecer como uma linha/tag com:

- nome,
- preço,
- botão `X` à direita.

Exemplo visual:
- `Bacon — R$ 4,50   [X]`
- `Queijo extra — R$ 3,00   [X]`

### Modal `Adicionar adicional`
Campos sugeridos:

- **Nome do adicional** `*`
- **Preço adicional** `*`
- **Ativo** *(default true)*
- **Ordem de exibição** *(opcional)*

### Botões
- `Cancelar`
- `Incluir`

### Regras
- nome obrigatório,
- preço obrigatório,
- valor maior ou igual a zero,
- ao incluir, inserir no array `additionals`,
- ao excluir, remover do array `additionals`.

---

## 3.4 Bloco `Opções de escolha`

### O que são
Itens onde o cliente escolhe uma opção do produto.

Exemplos:
- Pão brioche
- Pão australiano
- Pão integral

### Exibição em tela
Cada opção deve aparecer em linha/tag com:

- nome da opção,
- preço adicional, se existir,
- botão `X` à direita.

Exemplo:
- `Pão brioche   [X]`
- `Pão australiano   [X]`

### Modal `Adicionar opção`
Campos sugeridos:

- **Nome da opção** `*`
- **Preço adicional** *(opcional, default 0,00)*
- **Ativa** *(default true)*
- **Ordem de exibição** *(opcional)*

### Botões
- `Cancelar`
- `Incluir`

### Regras
- nome obrigatório,
- preço opcional,
- ao incluir, inserir no array `choiceOptions`,
- ao excluir, remover do array `choiceOptions`.

---

## 3.5 Observação de modelagem importante
Para simplificar a primeira versão, `Adicionais` e `Opções de escolha` podem ser modelados como listas simples no produto.

### V1 sugerida
- `additionals[]`
- `choiceOptions[]`

### Evolução futura possível
No futuro, o sistema pode evoluir para grupos mais complexos, por exemplo:

- grupo “Escolha o pão”
- grupo “Adicione extras”
- seleção mínima/máxima
- obrigatoriedade de escolha

Mas **na versão atual**, a implementação pode ser objetiva e prática, conforme o layout atual da página.

---

# 4. Organização do Produto

## 4.1 Objetivo
Definir como o produto será exibido no cardápio.

### Campos
- **Destaque**
- **Ordem de exibição**

### Regras

#### 🌟 Destaque
- boolean
- se ativo, o produto poderá aparecer em uma seção especial de destaques na loja pública

#### 🔢 Ordem de exibição
- inteiro positivo
- quanto menor o número, mais acima o produto aparece

---

# 5. Sabores e Variações

## 5.1 Objetivo
Permitir produtos com múltiplas variações de preço/tamanho.

Exemplos:
- 300g — R$ 24,90
- 500g — R$ 32,90

## 5.2 Exibição
A seção deve listar as variações já cadastradas com:

- nome da variação,
- preço,
- ações.

## 5.3 Ação `+ Adicionar variação`
Ao clicar, abrir **modal/popup**.

### Campos do modal
- **Nome da variação** `*`
- **Preço** `*`
- **Preço promocional** *(opcional)*
- **Ativa** *(default true)*
- **Ordem** *(opcional)*

### Regras
- ao incluir, adicionar no array `variations`,
- ao excluir, remover do array `variations`,
- exibir botão `X` ou menu de ação ao lado de cada variação,
- o produto pode existir com ou sem variações.

## 5.4 Regra importante de preço
Definir comportamento de negócio:

### Opção recomendada
- se o produto **não tem variações**, usar `price` e `promotionalPrice` do produto;
- se o produto **tem variações**, a compra deve considerar o preço da variação selecionada.

---

# 6. Cupons (opcional / escopo controlado)

## 6.1 Observação
A tela apresenta uma área de cupons com listagem e botão `+ Novo cupom`.

Como o foco principal desta etapa é **cardápio e produtos**, recomenda-se:

### Estratégia
- manter a seção visualmente preparada,
- mas implementar cupons como módulo separado ou feature flag.

## 6.2 Se incluído nesta fase
Permitir:
- código do cupom,
- tipo (`percentual` ou `fixo`),
- valor,
- limite de uso,
- validade,
- status.

---

# 7. Lista de Produtos Cadastrados

## 7.1 Objetivo
Exibir no lado direito os produtos já criados para a loja.

## 7.2 Informações exibidas por card/item
Cada item da lista deve mostrar:

- imagem do produto,
- nome,
- preço,
- categoria,
- status (`Ativo` / `Inativo`),
- botão de ações (`...`)

## 7.3 Funcionalidades da lista
- filtrar produtos,
- buscar por nome,
- editar produto,
- ativar/inativar,
- excluir,
- atualizar automaticamente após salvamento.

## 7.4 Exemplos de itens exibidos
- X-Burger Bacon — R$ 24,90
- Batata Frita — R$ 12,90
- Coca-Cola 350ml — R$ 6,90
- Combo Clássico — R$ 34,90
- Brownie com Sorvete — R$ 16,90

---

# 8. Estado local e JSON do produto

## 8.1 Estrutura sugerida do objeto no frontend

```json
{
  "id": "uuid",
  "storeId": "uuid",
  "categoryId": "uuid",
  "name": "X-Burger Bacon",
  "description": "Pão, burger, queijo, bacon e molho especial.",
  "price": 24.90,
  "promotionalPrice": null,
  "imageUrl": "https://cdn/.../x-burger.png",
  "isAvailable": true,
  "isFeatured": false,
  "displayOrder": 1,
  "additionals": [
    {
      "id": "uuid-ou-tempid",
      "name": "Bacon",
      "price": 4.50,
      "isActive": true,
      "displayOrder": 1
    },
    {
      "id": "uuid-ou-tempid",
      "name": "Queijo extra",
      "price": 3.00,
      "isActive": true,
      "displayOrder": 2
    }
  ],
  "choiceOptions": [
    {
      "id": "uuid-ou-tempid",
      "name": "Pão brioche",
      "price": 0.00,
      "isActive": true,
      "displayOrder": 1
    },
    {
      "id": "uuid-ou-tempid",
      "name": "Pão australiano",
      "price": 0.00,
      "isActive": true,
      "displayOrder": 2
    }
  ],
  "variations": [
    {
      "id": "uuid-ou-tempid",
      "name": "300g",
      "price": 24.90,
      "promotionalPrice": null,
      "isActive": true,
      "displayOrder": 1
    }
  ]
}
