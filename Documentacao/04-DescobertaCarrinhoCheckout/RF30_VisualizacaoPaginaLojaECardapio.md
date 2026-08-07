# [MVP] [Compra] RF30 - Visualização da página da loja e cardápio

**Épico:** Descoberta, carrinho e checkout  
**Fase:** MVP  
**Perfil:** Cliente  
**Prioridade:** Alta  

## Descrição
Exibir os dados da loja e o cardápio organizado por categorias.

## Regras de negócio
- Exibir:
  - nome
  - logo
  - descrição
  - horário
  - taxa de entrega
  - pedido mínimo
  - status da loja
- Exibir apenas produtos ativos.
- Loja inadimplente não deve ser exibida na rota pública da loja.

## Critérios de aceite
- Cliente acessa a loja e vê o cardápio.
- Produtos e categorias aparecem corretamente.
- Itens indisponíveis não podem ser comprados.
- Loja inadimplente não é exibida ao cliente na vitrine pública nem na página da loja.

## Checklist técnico
- [ ] Criar endpoint de detalhe da loja
- [ ] Carregar categorias e produtos
- [ ] Criar tela pública Angular
- [ ] Exibir status operacional
- [ ] Bloquear retorno público para lojas inadimplentes

## Dependências
- RF19 - Categorias
- RF20 - Produtos
- RF22 - Disponibilidade
- RF24 - Ordenação

## Próximo card sugerido
- RF31 - Carrinho