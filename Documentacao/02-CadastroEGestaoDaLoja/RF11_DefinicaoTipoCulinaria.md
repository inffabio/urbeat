# [MVP] [Loja] RF11 - Definição do tipo de culinária

**Épico:** Cadastro e gestão da loja  
**Fase:** MVP  
**Perfil:** Vendedor  
**Prioridade:** Alta  

## Descrição
Permitir classificar a loja por tipo de culinária, como pizzaria, hambúrgueria, pastelaria, açaí etc.

## Regras de negócio
- Loja deve ter ao menos um tipo principal.
- Tipo será usado na busca pública.

## Critérios de aceite
- Vendedor seleciona o tipo da loja.
- Tipo aparece no front público.
- Busca por tipo funciona corretamente.

## Checklist técnico
- [x] Criar tabela `CuisineTypes`
- [x] Popular tipos iniciais e endpoint publico (`GET /api/stores/cuisine-types`)
- [x] Relacionar loja ao tipo (`CuisineTypeId`) no endpoint Store
- [x] Exibir filtro público no FrontEnd
- [x] Criar seed com muitos tipo de culinárias comuns (`DataSeeder`)

## Dependências
- RF09 - Cadastro da loja

## Próximo card sugerido
- RF29 - Busca por tipo de comida