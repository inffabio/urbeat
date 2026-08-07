# 🍽️ Cadastro da Loja — Etapa 2: Horários de Funcionamento  
## Especificação Complementar para Implementação por IA

---

# 🎯 Objetivo deste documento

Este documento complementa a especificação principal da tela de **Cadastro de Horários de Funcionamento** da loja de delivery.

O foco aqui é detalhar:

- responsabilidades internas da lógica;
- comportamento esperado dos componentes;
- estrutura de dados;
- regras de negócio;
- eventos;
- textos da interface;
- insights;
- cálculos;
- inconsistências encontradas;
- critérios de aceite;
- estratégia de implementação;
- pseudocódigo;
- diretrizes visuais;
- responsividade;
- entrega esperada.

Este material deve servir como base para outra IA implementar a tela com fidelidade ao comportamento encontrado, mas já preparada para evolução futura.

---

# 🧠 Responsabilidades dessa lógica

Foi identificada uma lógica central equivalente a:

```js
setTimes(start, end, applyTo = ['seg-qui', 'sex-sab'])
