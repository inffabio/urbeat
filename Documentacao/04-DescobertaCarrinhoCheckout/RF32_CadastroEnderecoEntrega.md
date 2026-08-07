# [MVP] [Compra] RF32 - Cadastro de endereço de entrega

**Épico:** Descoberta, carrinho e checkout  
**Fase:** MVP  
**Perfil:** Cliente  
**Prioridade:** Alta  

## Descrição
Permitir ao cliente cadastrar e gerenciar endereços de entrega.

## Regras de negócio
- Cliente pode ter múltiplos endereços.
- Um endereço pode ser principal.
- Pedido deve armazenar cópia do endereço usado.

## Critérios de aceite
- Cliente cadastra, edita e remove endereços.
- Endereço pode ser selecionado no checkout.
- Pedido grava snapshot do endereço.

## Checklist técnico
- [ ] Criar endpoint `GET /api/customer/addresses` em `CustomerAddressesController`
- [ ] Criar endpoint `POST /api/customer/addresses` usando `UpsertCustomerAddressRequestDto` em `CustomerAddressesController`
- [ ] Criar endpoint `PUT /api/customer/addresses/{addressId}`
- [ ] Criar endpoint `DELETE /api/customer/addresses/{addressId}`
- [ ] Criar entidade `CustomerAddress` com campos pertinentes (ViaCEP opcional mas recomendado)
- [ ] Vincular `CustomerAddress` ao usuário com role Customer (não Seller)


## Dependências
- RF01 - Cadastro de consumidor
- RF02 - Login do consumidor
- RF06 - Permissões

## Próximo card sugerido
- RF33 - Checkout
- RF39 - Criação do pedido