# [MVP] [Catálogo] RF21 - Edição de produtos

**Épico:** Catálogo e produtos  
**Fase:** MVP  
**Perfil:** Vendedor  
**Prioridade:** Alta  

## Descrição
Permitir alterar nome, descrição, preço, imagem e categoria do produto.

## Regras de negócio
- Produto continua pertencendo à mesma loja.
- Alteração futura não modifica pedidos antigos.

## Critérios de aceite
- Produto pode ser editado.
- Cardápio reflete a mudança.
- Pedidos antigos mantêm snapshot original.

## Checklist técnico
- [x] Criar endpoint de edição (`PUT /api/stores/{storeId}/products/{productId}`)
- [x] Validar ownership (Produto atrelado a Store e Store atrelada a UserId)
- [x] Atualizar tela administrativa
- [x] Garantir snapshot em pedidos (Lógica de Order e OrderItem desacoplada guardando copias)

## Dependências
- RF20 - Cadastro de produtos

## Próximo card sugerido
- RF39 - Criação do pedido