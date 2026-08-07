# [MVP] [Catálogo] RF19 - Cadastro de categorias de produto

**Épico:** Catálogo e produtos  
**Fase:** MVP  
**Perfil:** Vendedor  
**Prioridade:** Alta  

## Descrição
Permitir criar categorias como pizzas, bebidas, sobremesas e hambúrgueres.

## Regras de negócio
- Categoria pertence a uma loja.
- Pode ter ordem de exibição.
- Categoria inativa não aparece no front.

## Critérios de aceite
- Vendedor cria, edita e remove categorias.
- Categorias aparecem organizadas no cardápio.
- Outra loja não manipula categorias indevidas.

## Checklist técnico
- [x] Criar entidade `ProductCategory`
- [x] Criar CRUD de categorias atrelado a Store (`StoreCategoriesController` - Ex: `POST /api/stores/{storeId}/categories`)
- [x] Criar ordenação e validação de pertencimento
- [x] Exibir no front público e administrativo Angular

## Dependências
- RF09 - Cadastro da loja

## Próximo card sugerido
- RF20 - Cadastro de produtos