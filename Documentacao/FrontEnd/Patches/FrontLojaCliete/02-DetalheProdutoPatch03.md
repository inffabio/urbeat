# 🍔 Especificação Complementar — Tela de Detalhe do Produto com Personalizações
## Parte 3 — Da seção 6. Adicionais até a Regra Principal

---

# 6. Seção de Adicionais

## 🎯 Objetivo
Permitir que o cliente acrescente itens extras opcionais ao produto no momento da compra, conforme o que foi cadastrado pela loja no painel administrativo.

Essa seção deve seguir o padrão visual da tela mobile de detalhe do produto, mantendo clareza, leveza e atualização imediata do preço.

---

## ✅ Quando exibir
A seção de adicionais deve ser renderizada **somente quando o produto possuir adicionais cadastrados e ativos**.

### Regras
- se o produto tiver lista `additionals` com itens ativos, a seção aparece;
- se não houver adicionais cadastrados, a seção não deve aparecer;
- se todos os adicionais estiverem inativos, a seção também não deve aparecer;
- não renderizar título, subtítulo ou container vazio.

### Regra de exibição
A tela pública deve sempre refletir apenas os adicionais válidos para compra naquele momento.

---

## 🧱 Estrutura visual da seção

### Elementos esperados
A seção deve conter:

- título da seção;
- subtítulo informando que é opcional;
- lista de adicionais;
- valor individual de cada adicional;
- estado visual de marcado/desmarcado;
- atualização do preço ao selecionar.

### Título sugerido
- `Adicionais`

### Subtítulo sugerido
- `Opcional`
- `Escolha quantos quiser`
- `Adicione extras ao seu pedido`

### Exemplo visual
- `[ ] Bacon  + R$ 3,00`
- `[ ] Queijo extra  + R$ 2,50`
- `[ ] Ovo  + R$ 2,00`
- `[ ] Cebola caramelizada  + R$ 2,50`
- `[ ] Picles  + R$ 1,50`

---

## 🔘 Comportamento funcional

### Regra principal da interação
Adicionais devem funcionar como **seleção múltipla**.

Isso significa:
- o cliente pode marcar nenhum adicional;
- o cliente pode marcar um adicional;
- o cliente pode marcar vários adicionais ao mesmo tempo;
- cada adicional é independente dos demais.

### Comportamento ao clicar
- se o adicional estiver desmarcado, passa a ficar marcado;
- se já estiver marcado, passa a ficar desmarcado;
- cada mudança recalcula o valor imediatamente;
- o estado visual deve acompanhar a mudança sem reload.

---

## 💰 Regras de preço
Cada adicional selecionado deve somar ao valor unitário do produto.

### Exemplo 1
- produto base: `R$ 24,90`
- bacon: `+ R$ 3,00`
- queijo extra: `+ R$ 2,50`

### Resultado
- valor unitário final: `R$ 30,40`

### Exemplo 2
- produto base: `R$ 24,90`
- bacon: `+ R$ 3,00`
- ovo: `+ R$ 2,00`
- quantidade: `2`

### Resultado
- valor unitário: `R$ 29,90`
- valor total: `R$ 59,80`

---

## 📌 Regras funcionais
Na versão atual:

- adicionais são opcionais;
- não bloqueiam a compra;
- não exigem escolha mínima;
- não exigem escolha máxima;
- o cliente pode enviar o item sem nenhum adicional.

### Regra de evolução futura
A modelagem deve permitir expansão posterior para:
- limite mínimo;
- limite máximo;
- grupos de adicionais;
- adicionais obrigatórios;
- adicionais por grupo.

Mas essas regras **não são obrigatórias nesta etapa**.

---

## 🧠 Estrutura sugerida
```ts
type ProductAdditional = {
  id: string;
  name: string;
  price: number;
  isActive?: boolean;
};
