# [MVP] [Compra] RF28 - Home pública com listagem de lojas

**Épico:** Descoberta, carrinho e checkout  
**Fase:** MVP  
**Perfil:** Cliente  
**Prioridade:** Alta  

## Descrição
Exibir a vitrine pública da plataforma com as lojas ativas.

## Regras de negócio
- Mostrar apenas lojas ativas.
- Não mostrar lojas inadimplentes na vitrine pública.
- Exibir se a loja está aberta ou fechada.
- Exibir nome, logo, tipo de culinária e taxa de entrega.

## Critérios de aceite
- Cliente vê lista de lojas.
- Loja fechada aparece sinalizada.
- Lojas inativas não aparecem.
- Lojas inadimplentes não aparecem.

## Checklist técnico
- [ ] Criar endpoint público de listagem
- [ ] Criar home Angular
- [ ] Exibir cards de loja
- [ ] Filtrar lojas ativas
- [ ] Excluir lojas inadimplentes no endpoint público de vitrine

## Dependências
- RF09 - Cadastro da loja
- RF11 - Tipo de culinária
- RF13 - Abrir/fechar loja

## Próximo card sugerido
- RF29 - Busca por tipo de comida
- RF30 - Página da loja