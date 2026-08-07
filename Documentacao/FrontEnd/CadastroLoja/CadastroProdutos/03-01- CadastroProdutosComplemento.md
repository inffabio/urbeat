# 🍔 Especificação Complementar — Inclusão de Cardápio / Produtos da Loja
## Parte 2 — Regras do JSON local, Persistência, Backend, Frontend, UX e Critérios de Aceite

---

# 8. Estado local e JSON do produto

## 8.2 Regras do JSON local

Toda ação de inclusão, edição e exclusão deve primeiro refletir no **estado local da tela**, garantindo resposta imediata para o usuário e experiência fluida.

### ✅ Inclusão
Fluxo esperado:

- abrir modal correspondente;
- preencher os campos;
- clicar em **Incluir**;
- adicionar o item ao array correspondente no objeto do produto;
- renderizar imediatamente o novo item na tela;
- disparar o autosave para persistência no backend.

### ❌ Exclusão
Fluxo esperado:

- clicar no botão **X** do item;
- remover o item do array local;
- remover o item da interface imediatamente;
- disparar autosave para persistir a remoção.

### 💾 Itens já persistidos
Se o item já existir no banco:

- pode ser removido do array local e o frontend enviar o produto atualizado por `PUT`;
- ou o frontend pode chamar um endpoint específico de exclusão.

### 🔧 Recomendação
Para melhor UX:

- remover da tela imediatamente;
- persistir em background;
- em caso de erro, restaurar o item e exibir mensagem amigável.

---

# 9. Salvamento automático

## 9.1 Regra geral

A tela deve utilizar **autosave** em todas as alterações relevantes.

### Status visuais esperados

- `Salvando...`
- `Salvo automaticamente`
- `Erro ao salvar`

## 9.2 Estratégia recomendada

- atualizar o estado local imediatamente;
- aplicar debounce entre `800ms` e `1200ms`;
- enviar alterações ao backend em background;
- evitar bloqueio da interface durante o salvamento.

## 9.3 Casos especiais

### Upload de imagem
- salvar imediatamente após o upload concluído com sucesso;
- atualizar a URL da imagem no estado local;
- refletir na UI sem necessidade de refresh.

### Inclusão e exclusão de personalizações
- refletir na tela na hora;
- persistir em seguida;
- usar remoção/inclusão otimista.

---

# 10. Backend — Modelagem sugerida (.NET 9 + PostgreSQL)

## 10.1 Entidades principais

### `store_categories`
Tabela responsável pelas categorias da loja.

#### Campos sugeridos
- `id` UUID PK
- `store_id` UUID FK
- `name` VARCHAR(120)
- `description` VARCHAR(255) NULL
- `is_active` BOOLEAN
- `display_order` INT
- `created_at` TIMESTAMP
- `updated_at` TIMESTAMP

---

### `store_products`
Tabela principal dos produtos.

#### Campos sugeridos
- `id` UUID PK
- `store_id` UUID FK
- `category_id` UUID FK
- `name` VARCHAR(100)
- `description` VARCHAR(300)
- `price` NUMERIC(10,2)
- `promotional_price` NUMERIC(10,2) NULL
- `image_url` TEXT
- `is_available` BOOLEAN
- `is_featured` BOOLEAN
- `display_order` INT
- `created_at` TIMESTAMP
- `updated_at` TIMESTAMP

---

### `store_product_additionals`
Tabela de adicionais do produto.

#### Campos sugeridos
- `id` UUID PK
- `product_id` UUID FK
- `name` VARCHAR(120)
- `price` NUMERIC(10,2)
- `is_active` BOOLEAN
- `display_order` INT
- `created_at` TIMESTAMP
- `updated_at` TIMESTAMP

---

### `store_product_choice_options`
Tabela de opções de escolha do produto.

#### Campos sugeridos
- `id` UUID PK
- `product_id` UUID FK
- `name` VARCHAR(120)
- `price` NUMERIC(10,2) DEFAULT 0
- `is_active` BOOLEAN
- `display_order` INT
- `created_at` TIMESTAMP
- `updated_at` TIMESTAMP

---

### `store_product_variations`
Tabela de variações do produto.

#### Campos sugeridos
- `id` UUID PK
- `product_id` UUID FK
- `name` VARCHAR(120)
- `price` NUMERIC(10,2)
- `promotional_price` NUMERIC(10,2) NULL
- `is_active` BOOLEAN
- `display_order` INT
- `created_at` TIMESTAMP
- `updated_at` TIMESTAMP

---

### `store_coupons` *(caso incluído nesta fase)*
Tabela opcional para cupons da loja.

#### Campos sugeridos
- `id` UUID PK
- `store_id` UUID FK
- `code` VARCHAR(50)
- `discount_type` VARCHAR(20)
- `discount_value` NUMERIC(10,2)
- `usage_limit` INT NULL
- `expires_at` TIMESTAMP NULL
- `is_active` BOOLEAN
- `created_at` TIMESTAMP
- `updated_at` TIMESTAMP

---

# 11. Regras de persistência

## 11.1 Produto

- todo produto deve pertencer a uma loja;
- toda categoria vinculada ao produto deve pertencer à mesma loja;
- não permitir produto sem:
  - nome,
  - categoria,
  - descrição,
  - preço,
  - imagem.

## 11.2 Preço

- `promotional_price` deve ser menor que `price`;
- não permitir valores negativos;
- persistir valores monetários com precisão decimal.

## 11.3 Personalizações

- adicionais, opções e variações são entidades filhas do produto;
- toda alteração feita no frontend deve refletir no banco;
- ao excluir um item da interface, a persistência deve remover ou inativar corretamente.

## 11.4 Exclusão

### Recomendação principal
Usar **soft delete** para produtos e categorias quando fizer sentido operacional.

#### Exemplo
- `deleted_at TIMESTAMP NULL`

### Para personalizações simples
Pode-se optar por:

- exclusão física;
- ou `is_active = false`.

---

# 12. Contratos de API sugeridos

## 12.1 Categorias

### Buscar categorias da loja
`GET /api/stores/{storeId}/categories`

### Criar categoria
`POST /api/stores/{storeId}/categories`

### Atualizar categoria
`PUT /api/stores/{storeId}/categories/{categoryId}`

### Excluir categoria
`DELETE /api/stores/{storeId}/categories/{categoryId}`

---

## 12.2 Produtos

### Listar produtos da loja
`GET /api/stores/{storeId}/products`

### Buscar produto por id
`GET /api/stores/{storeId}/products/{productId}`

### Criar produto
`POST /api/stores/{storeId}/products`

### Atualizar produto
`PUT /api/stores/{storeId}/products/{productId}`

### Excluir produto
`DELETE /api/stores/{storeId}/products/{productId}`

---

## 12.3 Upload de imagem do produto

### Upload
`POST /api/stores/{storeId}/products/upload-image`

### Retorno esperado
```json
{
  "url": "https://cdn.urbeat.com.br/products/x-burger-bacon.png",
  "fileName": "x-burger-bacon.png",
  "contentType": "image/png",
  "size": 182344
}