# [MVP] [Catálogo] RF24 - Ordenação do cardápio

**Épico:** Catálogo e produtos  
**Fase:** MVP  
**Perfil:** Vendedor  
**Prioridade:** Média  

## Descrição
Permitir definir a ordem das categorias e produtos no cardápio público.

## Regras de negócio
- Deve existir campo `DisplayOrder`.
- Cardápio público deve respeitar a ordenação.

## Critérios de aceite
- Vendedor organiza categorias e produtos.
- Cliente vê o cardápio na ordem configurada.

## Checklist técnico
- [x] Adicionar campo de ordenação (`DisplayOrder`) na entidade de `Product` e `ProductCategory`
- [x] Validar campo nas requisições DTO e no Controller
- [x] Criar UI de reordenação com Drag and Drop no front administrativo
- [x] Ordenar consulta pública do Cliente respeitando a ordem do vendedor (ascendente)

## Dependências
- RF19 - Categorias
- RF20 - Produtos

## Próximo card sugerido
- RF30 - Página da loja e cardápio