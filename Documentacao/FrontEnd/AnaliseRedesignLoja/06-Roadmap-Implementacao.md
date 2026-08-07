# 06 — Roadmap de Implementação

Roadmap faseado que combina o **redesign visual** (protótipo NovaVersaoFront) com a **parametrização por `CuisineType`** (tipo de comida). Escopo: **delivery de comida** (não multi-segmento). Ordenado por **valor/risco**: primeiro o que destrava muito sem mudar modelo; depois mudanças estruturais.

Legenda de esforço: **P** (pequeno, ≤2d) · **M** (médio, 3–5d) · **G** (grande, 1–2 semanas).

---

## Fase 0 — Fundação visual (tokens) · risco baixo
| Item | Esforço | Notas |
|---|---|---|
| Atualizar `variables.scss` (primária `#D54A51`, fonte, bordas, fundo) | P | Efeito global imediato. Manter estrutura semântica atual. |
| Definir escala tipográfica real (não forçar 15px único) | P | Acessibilidade. |
| Componente `footer-nav` global (Cardápio/Pedidos/Carrinho/Conta) | M | Novo na navegação. |
| Componente `store-metrics` (status/tempo/mínimo) | P | Extrair da store-page. |

**Entregável:** app com a nova cara (cor/fonte) e navegação inferior, sem novas regras de negócio.

---

## Fase 1 — Renderizar `optionGroups` no cliente · **maior alavanca** · risco baixo
| Item | Esforço | Notas |
|---|---|---|
| Componente `option-group` dirigido por dados (radio/checkbox/chips, min/max, obrigatório) | M | Backend já entrega `optionGroups` no `ProductResponseDto`. |
| Badges **Obrigatório/Opcional** + numeração de grupos | P | Padrão do protótipo (açaí). |
| Validação de seleção (min/max, obrigatórios) no add-to-cart | P | — |
| Passar seleções de grupo para o carrinho/checkout | M | Concatenar em `AdditionalNames`/`Notes` no curto prazo (sem mudar modelo). |
| Layout `chips` com `emoji`/ícone | P | Açaí. |

**Entregável:** hambúrguer (ponto da carne), açaí (montagem), pizza (bordas/extras) e qualquer montagem funcionam **usando o que o lojista já cadastra**.

---

## Fase 2 — Cardápio: ordem, Combos/Destaques e busca · risco baixo
| Item | Esforço | Notas |
|---|---|---|
| Ordem canônica do cardápio: Destaques → Combos → produto principal → complementos | M | Usa `IsFeatured`/`DisplayOrder` (já existem). Ver doc 04 §4. |
| Seções especiais ligáveis: Destaques, Mais vendidos, Novidades | M | Mapear `IsFeatured`/`IsBestSeller`/`IsNew`. |
| Categorias de complemento pré-criadas no onboarding (Combos, Bebidas, Acompanhamentos, Sobremesas) | P | — |
| Chips de categoria (visual do protótipo) + busca 100% largura | P | — |
| **Remover botão de filtros (sliders)** ao lado da busca | P | Sem função (doc 04 §5). |
| (Opcional) Tema por loja: `PrimaryColorHex` na Store | P | CSS var por loja. |

**Entregável:** cardápio prioriza conversão (Combos/Destaques no topo) e busca limpa.

---

## Fase 3 — CuisineType sugere modelos de opções · risco médio
| Item | Esforço | Notas |
|---|---|---|
| `CuisineType` sugere **modelos de grupo de opções** do produto principal | M | Hamburgueria→ponto da carne; Pizzaria→tamanho/sabores/borda; Açaiteria→tamanho/frutas/caldas. |
| **Biblioteca de modelos de grupo** (1 clique: "Ponto da carne", "Tamanho açaí"…) | M | Acelera cadastro. |
| Terminologia por tipo de comida (rótulos "Sabores", "Montar"…) | P | — |
| Prévia do produto renderizando grupos | M | Reusar componente da Fase 1. |

**Entregável:** onboarding do lojista guiado pelo tipo de comida; cadastro rápido; front coerente.

---

## Fase 4 — Recursos por tipo de comida · risco médio
| Item | Esforço | Notas |
|---|---|---|
| `Product`: `soldByWeight`+`unit`+`step` (marmita/comida a quilo/açaí por peso) | M | Stepper de peso no carrinho. |
| `Product`: `requiresScheduling`+`leadTimeHours` (doceria/encomenda) | M | Seletor de data no checkout. |
| `Product`: `minimumAge` (bebidas alcoólicas) | P | Confirmação de idade. |
| Disponibilidade por horário do produto | M | Café/almoço/happy hour. |

---

## Fase 5 — Meio a meio (mudança estrutural) · risco alto
| Item | Esforço | Notas |
|---|---|---|
| Modelo: `OrderItem`/`CheckoutItem` guardar **N sabores** (tabela filha ou JSON) | G | Hoje só 1 `ChoiceOptionName`. |
| Grupo `flavorSplit` + `maxFlavors` + `priceRule` (highest/average/sum) | M | — |
| UI de seleção de sabores por fração (1/2, 1/3…) | M | — |
| Cálculo de preço no checkout (server-side) | M | Regra de preço confiável no backend. |

---

## Fase 6 — Cupom / desconto · risco médio
| Item | Esforço | Notas |
|---|---|---|
| Entidade `Coupon` (percentual/fixo, mínimo, validade, uso único, por loja) | M | Não existe no backend. |
| Aplicação no checkout + linha "Descontos" no resumo | M | Protótipo já prevê visual. |
| Validação server-side | P | Anti-fraude. |

---

## Fase 7 — Extras / diferenciais · futuro
| Item | Esforço | Notas |
|---|---|---|
| Estoque com **baixa efetiva** no checkout | M | Campos já existem. |
| Combos/kits (bundle) | G | Relacionamento entre produtos. |
| Avaliações expostas no front do cliente | P | Backend já tem `OrderReview`. |
| Fidelidade / cashback | G | Diferencial futuro (opcional). |

---

## Sequenciamento recomendado

```
Fase 0 (tokens + nav)  ─►  Fase 1 (optionGroups no cliente)  ─►  Fase 2 (cardápio: combos/destaques + busca)
        └────────────────────────────────────────────────────────────────────┘
                                   │
                                   ▼
        Fase 3 (CuisineType sugere modelos)  ─►  Fase 4 (recursos por tipo de comida)
                                   │
                                   ▼
        Fase 5 (meio a meio)  +  Fase 6 (cupom)  ─►  Fase 7 (diferenciais)
```

## Dependências backend (checklist)

- [ ] Ordenação do cardápio (usa `IsFeatured`/`DisplayOrder` existentes) + endpoint `featured` (Fase 2).
- [ ] Campos aditivos em `ProductOptionGroup`/`ProductOptionItem` (`displayStyle`, `emoji`, `freeQuantity`, `maxPerItem`) (Fase 3/4).
- [ ] `CuisineType` com modelos de opções sugeridos + terminologia (Fase 3).
- [ ] Recursos por tipo de comida em `Product` (`soldByWeight`, `requiresScheduling`, `minimumAge`) (Fase 4).
- [ ] N sabores em `OrderItem`/`CheckoutItem` (Fase 5).
- [ ] `Coupon` + aplicação no checkout (Fase 6).
- [ ] Baixa de estoque no `CheckoutService` (Fase 7).

## Métrica de sucesso

- Tempo de onboarding do lojista (do cadastro à loja publicada).
- % de produtos usando grupos de opções (indica adoção da parametrização).
- Cobertura de tipos de comida (nº de `CuisineType` com lojas reais e modelos de opções).
- Conversão no funil produto → carrinho → checkout → pedido.

---

## Observação sobre testes (AGENTS.md)

Toda alteração de código deve vir com testes (backend xUnit + frontend Jest). Em especial:
- Componente `option-group`: testes de validação min/max e obrigatoriedade.
- Ordenação do cardápio (Destaques/Combos primeiro): teste da regra de ordem.
- Meio a meio e cupom: testes de cálculo de preço **no backend** (fonte da verdade).
