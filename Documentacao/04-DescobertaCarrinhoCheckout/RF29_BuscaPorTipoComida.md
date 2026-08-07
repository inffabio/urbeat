# [MVP] [Compra] RF29 - Busca por tipo de comida

**Épico:** Descoberta, carrinho e checkout  
**Fase:** MVP  
**Perfil:** Cliente  
**Prioridade:** Alta  

## Descrição
Permitir filtrar lojas por categoria culinária.

## Regras de negócio
- O filtro usa o tipo principal da loja.
- O resultado da busca deve considerar apenas lojas adimplentes.

## Critérios de aceite
- Cliente seleciona o tipo.
- Lista é filtrada corretamente.
- Remover o filtro volta ao estado inicial.
- Lojas inadimplentes não aparecem no resultado.

## Checklist técnico
- [ ] Criar filtro no endpoint
- [ ] Criar componente de filtro Angular
- [ ] Exibir feedback visual
- [ ] Aplicar filtro de adimplência no endpoint de busca

## Dependências
- RF11 - Tipo de culinária
- RF28 - Home pública

## Próximo card sugerido
- RF30 - Página da loja e cardápio