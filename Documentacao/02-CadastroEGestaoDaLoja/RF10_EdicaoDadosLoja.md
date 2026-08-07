# [MVP] [Loja] RF10 - Edição dos dados da loja

**Épico:** Cadastro e gestão da loja  
**Fase:** MVP  
**Perfil:** Vendedor  
**Prioridade:** Alta  

## Descrição
Permitir ao vendedor editar os dados básicos da sua loja.

## Regras de negócio
- Apenas o dono da loja pode editar.
- Alterações devem refletir na vitrine pública.

## Critérios de aceite
- Vendedor edita a própria loja.
- Mudanças aparecem corretamente no front público.
- Outro vendedor não consegue editar.

## Checklist técnico
- [x] Criar endpoint `PUT /api/stores/{storeId}`
- [x] Validar ownership
- [x] Atualizar tela do vendedor
- [x] Atualizar vitrine pública

## Dependências
- RF09 - Cadastro da loja

## Próximo card sugerido
- RF12 - Horário de funcionamento
- RF13 - Abrir/fechar loja
- RF15 - Taxa de entrega e pedido mínimo