# 🚀 Tarefa de Implementação — Angular 20 + Ionic
## Tela de Cadastro de Áreas e Taxas de Entrega da Loja

Você é uma IA especialista em frontend com **Angular 20**, **Ionic** e **Ionicons**.  
Implemente uma tela completa, funcional, componentizada e pronta para evolução, referente à etapa de onboarding de uma loja de delivery chamada:

## `Áreas e taxas`

---

# 🎯 Objetivo da tela

Criar a etapa do onboarding onde o lojista poderá:

- cadastrar os **bairros atendidos**;
- informar a **taxa de entrega** de cada bairro;
- adicionar bairros rapidamente por uma **linha inline**;
- adicionar bairros por um botão principal;
- remover bairros;
- configurar **frete grátis a partir de** um valor mínimo do pedido;
- visualizar status de **salvamento automático**;
- navegar entre etapas com **Voltar** e **Continuar**.

---

# 🧱 Stack obrigatória

Implemente obrigatoriamente com:

- **Angular 20**
- **Ionic**
- **Ionicons**
- **Reactive Forms**
- componentes preferencialmente **standalone**

---

# 🖼️ Assets / imagens

Todos os assets visuais devem ser procurados em:

`public/images`

## Regras
- Se existir asset em `public/images`, use-o.
- Se não existir, use:
  - `ion-icon`
  - SVG inline
  - elementos nativos do Ionic
- Priorize **Ionicons** para ícones de ação, feedback e navegação.

---

# 🧭 Contexto do fluxo

A tela faz parte do onboarding da loja e deve exibir um stepper com estas etapas:

1. `Loja`
2. `Áreas e taxas`
3. `Entrega`
4. `Publicar`

## Estado do stepper
- etapa atual: **Áreas e taxas**
- progresso: **50% concluído**
- texto complementar: **Continue assim!**

---

# 🖥️ Estrutura visual da página

A página deve conter os seguintes blocos:

---

## 1. Header / Stepper
Exibir:

- logo/marca
- etapas do onboarding
- etapa atual destacada
- progresso visual
- texto:
  - `50% concluído`
  - `Continue assim!`

---

## 2. Título e descrição
Exibir:

### Título
`Cadastre os bairros e defina as taxas de entrega`

### Descrição
`Informe as áreas que você atende e quanto cobra pela entrega.`

---

## 3. Status de autosave
Exibir bloco de status com:

- `Salvo automaticamente`
- `Suas alterações são salvas em tempo real.`

A implementação deve suportar os estados:

- `saving`
- `saved`
- `error`

Textos sugeridos:
- `Salvando...`
- `Salvo automaticamente`
- `Erro ao salvar`

---

## 4. Card principal — Lista de bairros e taxas
Exibir um card com o título:

`1. Adicione os bairros e defina a taxa de entrega`

Dentro dele, exibir uma estrutura tipo tabela/lista com colunas:

- `Bairro`
  - subtítulo: `Área de entrega`
- `Taxa de entrega`
  - subtítulo: `Valor cobrado do cliente`
- `Ações`

---

## 5. Linhas de bairros
Cada linha da lista deve conter:

- alça visual de reordenação
- input de bairro
- input monetário da taxa
- botão de remover

### Campo de bairro
- texto livre
- placeholder:
  - `Nome do bairro`

### Campo de taxa
- input monetário com prefixo visual `R$`
- placeholder:
  - `0,00`

### Ações
- botão remover com ícone de lixeira

---

## 6. Linha inline para adição rápida
No final da lista deve existir uma linha especial de adição com:

- input de bairro
  - placeholder: `Ex.: Aparecida`
- input de taxa
- botão `Adicionar bairro`

### Regras dessa linha inline
Ao adicionar:
- o **bairro é obrigatório**;
- se o bairro estiver vazio:
  - não adicionar;
  - focar o campo de bairro;
- se a taxa estiver vazia:
  - usar valor padrão **`5,00`**;
- a nova linha deve ser inserida **antes** da linha inline;
- a linha inline deve continuar no final;
- limpar os campos da linha inline após inclusão.

### Teclado
- pressionar **Enter** nessa linha inline deve adicionar o bairro.

---

## 7. Botão principal “Adicionar bairro”
Além da linha inline, deve existir um botão de ação:

`Adicionar bairro`

### Comportamento
Ao clicar:
- criar uma nova linha vazia editável;
- inserir essa linha antes da linha inline;
- focar o input de bairro da nova linha.

---

## 8. Card de dica
Exibir um card/bloco informativo com ícone e texto:

### Título
`Dica`

### Texto
`Seus clientes verão a taxa de entrega antes de finalizar o pedido.`

Use Ionicon, por exemplo:
- `information-circle-outline`

---

## 9. Card de frete grátis
Exibir um segundo card com:

### Título
`2. Frete grátis a partir de`

### Descrição
`Defina o valor mínimo do pedido para o frete ser grátis.`

### Campo
- input monetário com prefixo `R$`

### Texto auxiliar
`Pedidos iguais ou acima desse valor terão frete grátis.`

---

## 10. Rodapé de navegação
Exibir:

- botão `Voltar`
- mensagem `Seus dados estão seguros conosco.`
- botão `Continuar`

---

# ⚙️ Comportamentos funcionais obrigatórios

---

## 1. Adição de bairros
Deve haver duas formas:

### A. Via linha inline
- exige nome do bairro
- taxa vazia usa fallback `5,00`
- inclui antes da linha inline
- limpa os campos após adicionar

### B. Via botão principal
- cria linha vazia
- foca no input de bairro

---

## 2. Remoção de bairros
Ao clicar na lixeira:
- remover a linha imediatamente
- atualizar o formulário
- disparar autosave

Não precisa confirmação no comportamento base.

---

## 3. Formatação monetária
Todos os campos monetários devem:
- aceitar apenas valor monetário válido;
- usar vírgula como decimal;
- limitar a 2 casas decimais;
- impedir valores negativos;
- formatar no evento de blur.

### Exemplos esperados
- `5` → `5,00`
- `5,5` → `5,50`
- `10` → `10,00`

Campos que usam isso:
- taxa por bairro
- frete grátis a partir de

---

## 4. Autosave
Toda alteração deve disparar salvamento automático com debounce.

### Alterações que devem salvar
- adicionar bairro
- editar nome
- editar taxa
- remover bairro
- reordenar bairros
- editar frete grátis

### Fluxo esperado
- usuário altera algo
- status vira `saving`
- persistência executa
- em sucesso: `saved`
- em falha: `error`

---

## 5. Reordenação
Cada linha deve exibir uma alça de reordenação.

### Requisito mínimo
- mostrar a alça visual
- deixar a estrutura pronta para reorder

### Requisito ideal
- implementar com `ion-reorder-group` e `ion-reorder`

Se implementar reorder:
- atualizar `position`
- persistir nova ordem

---

# 🧩 Componentização esperada

Implemente com componentes reaproveitáveis.

## Estrutura sugerida
- `DeliveryAreasStepPage`
- `OnboardingStepperComponent`
- `DeliveryAreaListComponent`
- `DeliveryAreaRowComponent`
- `MoneyInputComponent`
- `AutoSaveStatusComponent`
- `InfoTipCardComponent`
- `FreeShippingCardComponent`

---

# 🗂️ Modelo de dados

Use uma modelagem próxima disso:

```ts
export interface DeliveryArea {
  id: string;
  neighborhood: string;
  fee: number | null;
  position: number;
}

export type SaveStatus = 'idle' | 'saving' | 'saved' | 'error';

export interface DeliveryFeesFormValue {
  areas: DeliveryArea[];
  freeShippingMinimum: number | null;
  saveStatus: SaveStatus;
}
