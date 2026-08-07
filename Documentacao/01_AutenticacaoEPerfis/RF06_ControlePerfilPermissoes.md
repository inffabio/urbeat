# [MVP] [Autenticação] RF06 - Controle de perfil e permissões

**Épico:** Autenticação e perfis  
**Fase:** MVP  
**Perfil:** Admin / Vendedor / Cliente  
**Prioridade:** Alta  

## Descrição
Controlar o acesso às áreas do sistema e aos endpoints de acordo com o perfil do usuário autenticado.

## Regras de negócio
- Cliente não acessa painel do vendedor.
- Vendedor não acessa painel global do admin.
- Admin acessa apenas recursos globais.
- Vendedor só manipula recursos da própria loja.
- Cliente só acessa seus próprios pedidos e endereços.

## Critérios de aceite
- Rotas respeitam perfil.
- Endpoints retornam 403 quando apropriado.
- Vendedor não vê pedidos de outra loja.
- Cliente não vê pedidos de outros clientes.

## Checklist técnico
- [ ] Definir roles no Identity
- [ ] Definir policies
- [ ] Adicionar claims ao JWT
- [ ] Criar guards Angular
- [ ] Tratar 401/403 no frontend
- [ ] Validar ownership no backend

## Dependências
- RF02 - Login do consumidor
- RF04 - Login do vendedor
- RF05 - Login do administrador

## Próximo card sugerido
- RF09 - Cadastro da loja
- RF32 - Cadastro de endereço
- RF61 - Dashboard do admin