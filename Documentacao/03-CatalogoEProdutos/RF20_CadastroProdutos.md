# [MVP] [Catálogo] RF20 - Cadastro de produtos

**Épico:** Catálogo e produtos  
**Fase:** MVP  
**Perfil:** Vendedor  
**Prioridade:** Alta  

## Descrição
Permitir cadastrar produtos com nome, descrição, preço, categoria e imagem.

## Regras de negócio
- Produto pertence a uma loja e uma categoria.
- Preço deve ser positivo.
- Nome é obrigatório.

## Critérios de aceite
- Produto é criado com sucesso.
- Produto aparece no cardápio.
- Produto só pode ser vinculado a categoria da própria loja.

## Checklist técnico
- [x] Criar entidade `Product`
- [x] Criar endpoint de cadastro (`POST /api/stores/{storeId}/products`) na controladora `StoreProductsController`
- [x] Validar persistencia associando o produto corretamente no `ProductCategoryId`
- [x] Criar tela Angular e integração de listagem

## Dependências
- RF19 - Cadastro de categorias
- RF23 - Upload de imagens

## Próximo card sugerido
- RF21 - Edição de produtos
- RF22 - Ativar/inativar produto
- RF30 - Página da loja e cardápio