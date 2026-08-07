# 🍔 Especificação Complementar — Tela de Detalhe do Produto com Personalizações
## Parte 2 — Da seção 5 até a Regra principal

---

# 5. Seção de Opções de escolha

## 🎯 Objetivo
Permitir ao cliente selecionar uma opção única relacionada ao produto, conforme configurado pela loja no cadastro administrativo.

Essa seção representa escolhas como, por exemplo:

- tipo de pão;
- ponto da carne;
- massa;
- sabor principal;
- tipo de acompanhamento;
- bebida do combo;
- molho da casa.

---

## ✅ Quando exibir
A seção deve ser renderizada **somente quando o produto possuir opções de escolha cadastradas e ativas**.

### Regras
- se `choiceOptions` existir e possuir itens ativos, a seção deve aparecer;
- se não houver opções cadastradas, a seção não deve ser exibida;
- não mostrar container vazio;
- não mostrar título sem conteúdo.

---

## 🧱 Estrutura visual da seção

### Elementos visuais esperados
- título da seção;
- subtítulo ou indicação de obrigatoriedade;
- lista de opções clicáveis;
- destaque visual para item selecionado;
- preço adicional, quando existir;
- indicação clara de seleção única.

### Exemplo visual
- `Ponto da carne`
- subtítulo: `Obrigatório`
- opções:
  - `Ao ponto para mal`
  - `Ao ponto`
  - `Bem passada`

ou

- `Tipo de pão`
- subtítulo: `Escolha 1 opção`
- opções:
  - `Pão brioche`
  - `Pão australiano`
  - `Pão integral`

---

## 🔘 Comportamento funcional

### Seleção única
Na versão atual, as opções de escolha devem funcionar como **single select**.

Ou seja:
- apenas **uma opção** pode estar selecionada por vez;
- ao selecionar uma nova opção, a anterior deve ser desmarcada automaticamente.

### Regras de interação
- tocar/clicar em uma opção marca essa opção;
- tocar em outra opção substitui a seleção anterior;
- se a seção for obrigatória, deve sempre haver uma opção marcada antes do envio;
- se a seção não for obrigatória, pode permitir nenhuma opção selecionada.

---

## 💰 Regras de preço
Caso uma opção de escolha possua preço adicional:

- esse valor deve ser somado ao total do item;
- o recálculo deve ser feito imediatamente no frontend;
- o backend deve recalcular novamente ao adicionar ao carrinho.

### Exemplo
- Produto base: `R$ 24,90`
- Opção escolhida com acréscimo: `+ R$ 2,00`
- Total unitário: `R$ 26,90`

---

## 📌 Regras de obrigatoriedade
A obrigatoriedade deve ser controlada por regra do produto.

### Cenários
#### Obrigatória
- o usuário não pode adicionar ao carrinho sem escolher uma opção;
- o botão principal deve ficar desabilitado ou acusar erro ao tentar seguir.

#### Opcional
- o usuário pode prosseguir sem marcar nenhuma opção.

### Mensagens sugeridas
- `Escolha uma opção.`
- `Selecione uma opção obrigatória.`

---

## 🧠 Estrutura sugerida
```ts
type ProductChoiceOption = {
  id: string;
  name: string;
  price?: number | null;
  isDefault?: boolean;
  isActive?: boolean;
};
