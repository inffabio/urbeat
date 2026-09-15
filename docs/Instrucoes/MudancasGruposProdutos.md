# Implementar reutilização de grupos de opções no cadastro de produtos

Execute as modificações descritas abaixo no aplicativo de pedidos de comida delivery. Faça as alterações no código e no banco de dados necessárias para entregar a funcionalidade completa, seguindo a arquitetura existente.

## Objetivo

Permitir que os grupos de adicionais criados na seção do wisard **“3. Grupos de opções”** sejam salvos e reutilizados em outros produtos, sem precisar cadastrar novamente suas informações.

## 1. Salvar grupos para reutilização

Ao salvar um produto com sucesso, persista os novos grupos de opções criados, incluindo:

- Nome do grupo.
- Tipo de seleção.
- Condição: obrigatório ou opcional.
- Quantidade mínima e máxima de escolhas.
- Nomes dos itens.
- Preços dos itens.
- Ordem dos grupos e dos itens, caso já exista esse recurso.

Os grupos devem pertencer à loja atual e permanecer disponíveis após atualizar a página ou entrar novamente no aplicativo.

Não persista grupos de cadastros cancelados. Se o salvamento falhar, evite registros incompletos.

## 2. Carregar os grupos em novos cadastros

Ao abrir a tela de cadastro de um novo produto:

- Busque os grupos salvos da loja atual.
- Mostre-os na seção **“3. Grupos de opções”**.
- Identifique cada grupo pelo nome cadastrado, como **“Escolha um molho”**.
- Não utilize títulos genéricos como “Grupo de opções 1” para identificar grupos salvos.
- Preserve a possibilidade de criar novos grupos.

## 3. Selecionar os grupos do produto

Coloque um **checkbox à esquerda do nome de cada grupo salvo**.

Regras:

- Em um novo produto, os grupos existentes começam desmarcados.
- O usuário pode selecionar nenhum, um ou vários grupos.
- Marcar um grupo inclui no produto todos os seus itens, preços e configurações.
- Desmarcar um grupo remove sua associação somente com o produto atual.
- Desmarcar ou remover um grupo do produto não deve apagar o grupo salvo para reutilização.
- Novos grupos criados no formulário devem começar selecionados.
- Ao salvar o produto, associe somente os grupos selecionados.
- Ao editar um produto existente, mostre como selecionados os grupos já associados a ele.

O checkbox define se o grupo pertence ao produto. Ele é independente da configuração **“Obrigatório/Opcional”**, que define a regra de escolha dos adicionais pelo cliente.

Reutilizar um grupo não deve duplicar seu registro na lista de grupos disponíveis. Utilize identificadores persistentes para controlar a reutilização, sem depender apenas do nome.

Alterações em um grupo dentro do cadastro de um produto não devem modificar silenciosamente outros produtos. Preserve as configurações dos demais produtos ao implementar a reutilização.

## 4. Adicionar o botão inferior “+ Adicionar grupo”

Adicione um botão com o texto **“+ Adicionar grupo”** ao final da seção, abaixo do botão **“+ Adicionar item”** do último grupo.

Esse botão deve seguir o mesmo padrão visual de **“+ Adicionar item”**:

- Mesma largura.
- Mesma altura.
- Mesmo estilo de borda.
- Mesmo arredondamento.
- Mesma tipografia.
- Mesma cor do texto.
- Mesmo alinhamento centralizado.
- Espaçamento consistente com o formulário.

Reutilize o componente ou as classes de estilo existentes quando possível.

Mantenha apenas um botão inferior de adicionar grupo, posicionado após a lista de grupos. Ele deve continuar disponível quando a lista estiver vazia.

Preserve o botão superior **“+ Adicionar grupo”**. Ambos devem executar a mesma ação: adicionar um novo formulário de grupo com os campos atuais.

O botão **“+ Adicionar item”** deve continuar adicionando itens somente ao grupo correspondente.

## 5. Preservar o funcionamento atual

- Mantenha os campos e as validações existentes.
- Preserve os produtos já cadastrados e seus adicionais.
- Mantenha o cálculo dos preços e as regras de seleção no pedido.
- Restrinja a consulta e a reutilização de grupos à loja atual.
- Preserve o padrão visual e a responsividade.
- Não altere outras seções do cadastro sem necessidade para esta implementação.
- Utilize persistência no banco de dados; não dependa apenas do estado da tela ou do armazenamento local do navegador.

## 6. Critérios de aceite

Valide o seguinte fluxo:

1. Criar um produto com o grupo **“Escolha um molho”**.
2. Configurar o grupo como múltipla escolha, opcional, com mínimo 0 e máximo 2.
3. Adicionar **“Molho 1” — R$ 5,00** e **“Molho 2” — R$ 5,50**.
4. Salvar o produto.
5. Abrir o cadastro de outro produto.
6. Confirmar que **“Escolha um molho”** aparece com um checkbox à esquerda, inicialmente desmarcado.
7. Marcar o grupo e confirmar o reaproveitamento dos itens, preços e regras.
8. Salvar o novo produto e confirmar sua associação ao grupo.
9. Confirmar que a reutilização não duplicou o grupo na lista.
10. Cadastrar outro produto deixando o grupo desmarcado e confirmar que ele não recebe esses adicionais.
11. Atualizar a página e confirmar que o grupo continua disponível.
12. Editar um produto e confirmar que seus grupos aparecem selecionados.
13. Confirmar que desmarcar um grupo não o exclui dos demais produtos nem da lista reutilizável.
14. Confirmar que os botões superior e inferior **“+ Adicionar grupo”** funcionam.
15. Confirmar que **“+ Adicionar item”** continua funcionando no grupo correto.

## Entrega

Implemente a solução, incluindo eventuais alterações de esquema e migrações necessárias. Não se limite a apresentar um plano ou uma sugestão.

Ao concluir, informe resumidamente:

- O que foi implementado.
- Como os grupos são armazenados e reutilizados.
- Quais verificações foram realizadas.
- Se existe alguma etapa necessária para aplicar a alteração no ambiente.
