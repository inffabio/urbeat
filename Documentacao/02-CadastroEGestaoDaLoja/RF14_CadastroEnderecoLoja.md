# [MVP] [Loja] RF14 - Cadastro do endereço da loja

**Épico:** Cadastro e gestão da loja  
**Fase:** MVP  
**Perfil:** Vendedor  
**Prioridade:** Alta  

## Descrição
Permitir cadastrar o endereço físico da loja.

## Regras de negócio
- Campos mínimos:
  - rua
  - número
  - bairro
  - cidade
  - estado
  - CEP
  - Ponto de referência

## Critérios de aceite
- Endereço é salvo corretamente.
- Endereço pode ser editado.
- Loja fica vinculada a um endereço válido.

## Checklist técnico
- [x] Criar uma unica estrutura Address acoplada com AddressObject
- [x] Criar endpoint de manutenção `PUT /api/stores/{storeId}/address`
- [x] Integrar backend com ViaCEP e frontend
- [x] Criar tela administrativa Angular

## Dependências
- RF09 - Cadastro da loja

## Próximo card sugerido
- RF15 - Taxa de entrega e pedido mínimo