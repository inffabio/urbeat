# Organização de produtos e vitrines do cardápio digital

## Objetivo

Permitir que cada produto tenha, além da categoria principal obrigatória, no máximo uma classificação especial: **Ofertas**, **Mais vendidos** ou **Novidades**. As classificações serão exibidas no storefront como pílulas e vitrines próprias, junto das categorias criadas pelo lojista.

Um produto classificado aparecerá deliberadamente duas vezes no feed padrão: uma vez na vitrine especial e outra vez em sua categoria original. As duas aparições usarão o mesmo registro e o mesmo `Product.Id`; não haverá cópia no banco.

## Decisões aprovadas

- O campo legado `IsFeatured` será migrado para **Ofertas**.
- `IsBestSeller` e `IsNew` serão migrados para as respectivas classificações.
- Em dados legados com mais de um booleano ativo, a precedência será: **Ofertas > Mais vendido > Novidade**.
- A ordenação dos produtos dentro de cada seção continuará sendo `DisplayOrder`, depois nome.
- A ordem fixa das vitrines especiais será: **Ofertas**, **Mais vendidos**, **Novidades**.
- As categorias do lojista manterão sua ordenação cadastrada.
- Pílulas sem produtos disponíveis serão ocultadas.
- A pílula `Todos` exibirá as vitrines especiais disponíveis antes das categorias.
- Ao selecionar uma pílula especial, somente aquela vitrine será exibida.
- Ao selecionar uma categoria, somente os produtos daquela categoria serão exibidos.
- A classificação especial não substitui a categoria principal.
- Não será aplicada deduplicação global entre seções.

## Modelo de dados

Adicionar ao produto uma classificação única e anulável, por exemplo `ProductSpecialClassification`/`SpecialClassification`, com valores persistidos estáveis equivalentes a:

- `Offers`
- `BestSeller`
- `New`

A categoria principal continua sendo `CategoryId` obrigatório.

A migração deverá:

1. preencher a nova coluna para produtos existentes usando a precedência aprovada;
2. preservar produtos, categorias, preços, imagens, adicionais, variações, estoque e disponibilidade;
3. impedir classificações inválidas no servidor;
4. impedir que novos fluxos criem mais de uma classificação;
5. manter campos legados somente se necessário para compatibilidade de leitura/migração, sem usá-los como fonte autoritativa após a migração.

A remoção dos campos legados, caso seja segura para todos os consumidores, deve ser uma decisão explícita durante a implementação. A primeira migração não deve causar perda de dados nem exigir reset de produtos.

## Contratos e backend

Requests de criação e atualização de produto deverão aceitar uma única classificação opcional. O backend será autoritativo e deverá rejeitar valores desconhecidos, inconsistentes ou tentativas de enviar múltiplas classificações por contratos legados.

Responses administrativas e públicas deverão expor a classificação normalizada com o produto, para que painel e storefront usem a mesma fonte de verdade.

O catálogo público deverá continuar garantindo produto disponível, categoria ativa pertencente à mesma loja, regras atuais de estoque/disponibilidade e dados completos de preço, preço promocional, imagem, adicionais, variações e opções.

O endpoint legado de produtos destacados deverá ser mantido por compatibilidade quando houver consumidores existentes, mas sua semântica deverá corresponder a **Ofertas**. O novo storefront deverá obter os produtos com sua classificação para montar as seções sem remover o produto da categoria original.

O backend não deverá criar cópias de produtos nem retornar uma lista global deduplicada. Cada contexto de exibição — vitrine especial ou categoria — poderá selecionar o mesmo produto independentemente.

## Painel administrativo

Nos formulários de cadastro e edição:

- substituir o texto `Destaque` por `Ofertas`;
- apresentar a classificação como seleção única, permitindo nenhuma opção;
- manter a categoria principal obrigatória;
- carregar a classificação existente ao editar;
- salvar alteração e remoção corretamente;
- impedir combinações conflitantes na interface e no servidor.

Nas listagens administrativas:

- exibir etiqueta `Ofertas`, `Mais vendido` ou `Novidade` quando aplicável;
- disponibilizar filtro por classificação;
- preservar filtros e ordenações já existentes;
- atualizar a listagem após salvar sem alterar dados não relacionados.

Wizard e dashboard que compartilham o editor deverão usar a mesma regra e labels. Textos, filtros e componentes relacionados não devem continuar apresentando `Destaque` como classificação de produto.

## Storefront e pílulas

As pílulas serão montadas nesta ordem, omitindo as vazias:

```text
Todos | Ofertas | Mais vendidos | Novidades | <categorias do lojista>
```

No modo `Todos`, a composição será:

1. seção `Ofertas`, se houver produtos;
2. seção `Mais vendidos`, se houver produtos;
3. seção `Novidades`, se houver produtos;
4. categorias convencionais na ordenação existente.

Ao selecionar uma pílula especial, o storefront exibirá somente os produtos daquela classificação. Ao selecionar uma categoria, exibirá somente os produtos da categoria. O produto classificado continuará presente na categoria quando o usuário retornar a ela.

As aparições devem compartilhar os dados atuais do mesmo produto. Se o produto for atualizado, desativado, removido da disponibilidade ou afetado pelas regras de estoque, a mesma decisão deverá valer nas duas posições. Remover a classificação elimina apenas a aparição especial.

O layout atual, os componentes de card, carrinho, adicionais, variações, preços e comportamento responsivo devem ser preservados. A mudança deve apenas adicionar vitrines/pílulas e labels necessários, sem redesenho geral.

## Segurança e autoridade

- A classificação recebida do cliente é apenas entrada; o servidor valida e persiste a regra final.
- Todas as consultas devem permanecer limitadas à loja correta.
- Não expor produtos de categorias inativas ou de outra loja.
- Não alterar cálculo autoritativo de preços, checkout, estoque ou pedidos.
- Não alterar autenticação, promoção de papéis ou envio de e-mails.

## Testes obrigatórios

### Backend

- migração preserva produtos existentes;
- `IsFeatured` migra para `Offers`;
- conflitos são resolvidos por `Offers > BestSeller > New`;
- criação e atualização aceitam uma classificação ou nenhuma;
- valores inválidos são rejeitados;
- remoção persiste `null`;
- listagem pública retorna produtos disponíveis com classificação;
- produtos de outras lojas/categorias inativas continuam excluídos;
- endpoint legado de destacados representa Ofertas;
- alterações de classificação não alteram categoria, preço, imagem, adicionais, variações ou disponibilidade.

### Frontend administrativo

- cadastro e edição exibem `Ofertas`, não `Destaque`;
- somente uma classificação pode ser selecionada;
- edição carrega a classificação existente;
- remoção é enviada corretamente;
- etiquetas e filtro administrativo funcionam;
- wizard e dashboard mantêm comportamento consistente.

### Frontend storefront

- produto em Ofertas, Mais vendidos ou Novidades aparece na vitrine e na categoria original;
- produto sem classificação aparece somente na categoria;
- pílulas especiais aparecem junto das categorias;
- ordem das pílulas e seções é a aprovada;
- pílulas vazias são ocultadas;
- seleção especial filtra somente sua seção;
- seleção de categoria filtra somente sua categoria;
- não há duplicação dentro da mesma seção;
- a mesma identificação do produto é mantida nas duas aparições;
- remoção/alteração da classificação atualiza corretamente o feed;
- indisponibilidade e estoque se refletem nas duas aparições;
- comportamento responsivo existente permanece íntegro.

## Critérios de aceite

1. Um produto classificado aparece uma vez na vitrine correspondente e uma vez na categoria original, usando o mesmo ID.
2. `Todos` mostra as vitrines especiais disponíveis antes das categorias.
3. As pílulas permitem navegar entre vitrines especiais e categorias.
4. Nenhuma seção vazia aparece.
5. Um produto pode ter no máximo uma classificação especial.
6. `Destaque` deixa de ser exibido como label de classificação de produto.
7. Produtos e configurações existentes são preservados por migração segura.
8. Backend, frontend, testes e build passam sem alterações fora do escopo.
