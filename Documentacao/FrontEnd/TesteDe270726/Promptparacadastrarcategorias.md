**Prompt para cadastrar categorias**

\
Permitir que o lojista cadastre categorias antes dos produtos e determine a ordem em que elas aparecerão no cardápio do cliente.

Estrutura desejada da tela:

1. Categorias do cardápio
1. Cadastrar produto
1. Grupos de opções
1. Organização

Como a etapa agora reúne categorias e produtos, altere o nome da etapa superior de “Produtos” para “Cardápio”.

1. Nova seção “Categorias do cardápio”

Adicione essa seção antes de “Cadastrar produto”.

Título:

“1. Categorias do cardápio”

Descrição:

“Cadastre e organize as categorias na ordem em que elas aparecerão para seus clientes.”

A seção deve conter:

- Campo para informar o nome da categoria.
- Botão “Adicionar categoria”.
- Contador de categorias.
- Lista das categorias cadastradas.
- Estado de carregamento.
- Estado vazio.
- Tratamento visual de erro.
- Atualização automática após criar, editar, excluir ou reordenar.

Mantenha o padrão visual atual da aplicação, reutilizando os componentes de formulário, botões, cartões, modais, alertas e notificações já existentes.

2. Estrutura visual de cada categoria

Exiba cada categoria em uma linha compacta contendo:

- Alça de arrastar.
- Indicador da posição: 01, 02, 03 etc.
- Nome da categoria.
- Quantidade de produtos associados, se essa informação estiver disponível.
- Status ativa ou oculta.
- Controle para ativar ou desativar.
- Botão para editar.
- Botão para excluir.
- Botão para mover para cima.
- Botão para mover para baixo.

Os botões de subir e descer devem funcionar como alternativa ao drag-and-drop, principalmente em dispositivos móveis e para acessibilidade.

3. Ordenação das categorias

Implemente ordenação por drag-and-drop.

Quando uma categoria mudar de posição:

- Atualize imediatamente a lista na interface.
- Recalcule as posições sequencialmente.
- Mostre os indicadores 01, 02, 03 conforme a nova ordem.
- Persista a ordenação usando o backend já existente.
- Use o campo de ordenação já definido no modelo ou contrato atual, como order, position, sortOrder ou equivalente.
- Não invente um novo nome de campo se já existir um no projeto.

Se houver uma rota específica de reordenação, utilize-a.

Se a API trabalhar com atualizações individuais, utilize o fluxo já adotado pelo projeto.

Envie apenas as informações necessárias para a ordenação, preferencialmente em uma estrutura equivalente a:

[{ id, order }]

Adapte esse formato ao contrato real do projeto.

A interface pode usar atualização otimista, mas deve:

- Restaurar a ordem anterior se a requisição falhar.
- Mostrar uma mensagem de erro.
- Evitar múltiplas requisições simultâneas durante o mesmo movimento.
- Invalidar ou atualizar corretamente o cache após o sucesso.

Após salvar, mostre uma confirmação discreta:

“Ordem do cardápio atualizada.”

4. Cadastro de categoria

Ao adicionar uma categoria:

- O nome deve ser obrigatório.
- Remova espaços desnecessários no início e no final.
- Não permita nomes duplicados.
- Considere duplicados mesmo com diferenças entre maiúsculas e minúsculas.
- Exiba os erros de validação retornados pelo backend.
- Adicione a nova categoria no final da lista.
- Atualize o indicador de posição.
- Limpe o campo após o sucesso.
- Atualize o seletor de categoria do produto.
- Mostre uma mensagem de sucesso.

Enquanto a requisição estiver em andamento:

- Desative o botão de cadastro.
- Exiba um indicador de carregamento.
- Evite envios duplicados.
5. Edição de categoria

O botão de edição deve abrir o componente de modal já utilizado pelo projeto.

Permita alterar o nome da categoria.

Ao salvar:

- Valide o nome.
- Use o serviço existente de atualização.
- Preserve a posição atual.
- Preserve os produtos associados.
- Atualize a lista e o seletor de categorias.
- Mostre uma mensagem de sucesso.
- Trate erros retornados pelo backend.
6. Ativação e desativação

Permita ativar ou desativar uma categoria.

Ao desativar:

- Não exclua a categoria.
- Não remova seus produtos.
- Informe que ela ficará oculta no cardápio.
- Atualize o backend.
- Atualize a interface após a confirmação.

Categorias inativas não devem aparecer como opção para novos produtos, salvo se o comportamento atual do sistema determinar algo diferente.

Ao editar um produto que já utiliza uma categoria inativa, preserve o vínculo existente e exiba a categoria atual de forma apropriada.

7. Exclusão de categoria

Ao clicar em excluir:

- Solicite confirmação.
- Verifique se existem produtos associados.
- Utilize a informação já fornecida pelo backend ou a consulta existente no projeto.

Se não houver produtos:

- Permita a exclusão após confirmação.

Se houver produtos associados:

- Não exclua imediatamente.
- Mostre quantos produtos utilizam a categoria.
- Solicite uma categoria de destino.
- Transfira os produtos antes de concluir a exclusão.
- Utilize as operações e rotas já disponíveis no backend.
- Se o backend já bloquear a exclusão, apresente sua mensagem de forma amigável.
- Nunca deixe produtos apontando para uma categoria inexistente.

Desative o botão de confirmação enquanto a operação estiver sendo processada.

8. Formulário “Cadastrar produto”

Altere a numeração para:

“2. Cadastrar produto”

O campo de categoria deve:

- Ser obrigatório.
- Consumir as categorias reais do backend.
- Respeitar a ordem definida na nova seção.
- Exibir somente categorias disponíveis para novos produtos.
- Atualizar após criação, edição, ativação, desativação, exclusão ou reordenação.
- Exibir “Selecione uma categoria” como opção inicial.
- Exibir uma orientação para cadastrar uma categoria quando a lista estiver vazia.

Não permita cadastrar um produto sem uma categoria válida.

Preserve todos os demais campos e comportamentos atuais do formulário.

9. Painel “Seus produtos”

Mantenha o painel lateral atual.

Adicione em cada produto uma identificação discreta da sua categoria.

Inclua um filtro de produtos por categoria, caso ele ainda não exista.

O filtro deve:

- Consumir as categorias reais.
- Respeitar a ordem definida.
- Atualizar automaticamente após mudanças nas categorias.
- Manter a opção “Todas as categorias”.

Não altere o comportamento atual da busca por produto.

10. Demais seções

Atualize a numeração das seções seguintes:

- “3. Grupos de opções”
- “4. Organização”

Preserve integralmente as funcionalidades existentes dessas seções.

11. Estados da interface

Implemente corretamente:

- Carregamento inicial das categorias.
- Carregamento durante criação e edição.
- Carregamento durante exclusão.
- Carregamento durante reordenação.
- Estado vazio.
- Erro ao carregar.
- Erro ao salvar.
- Repetição da tentativa quando apropriado.
- Feedback de sucesso.
- Bloqueio de ações duplicadas.

Utilize o sistema de toast, snackbar ou alertas já existente no projeto.

12. Responsividade

No desktop:

- Exiba posição, nome, quantidade, status e ações na mesma linha.
- Preserve o painel lateral de produtos.
- Mantenha a densidade visual atual.

No celular:

- Evite rolagem horizontal.
- Reorganize as informações da categoria quando necessário.
- Mantenha o indicador de posição.
- Disponibilize os botões de subir e descer.
- Garanta áreas de toque adequadas.
- Não dependa exclusivamente do drag-and-drop.
13. Acessibilidade
- Use botões reais para as ações.
- Adicione nomes acessíveis para editar, excluir, ativar, subir e descer.
- Mantenha foco visível.
- Permita reordenar sem depender do mouse.
- Use os componentes acessíveis de modal já adotados pelo projeto.
- Informe mudanças de ordem e resultados das operações para tecnologias assistivas.
- Preserve a navegação por teclado.
14. Requisitos de implementação
- Trabalhe sobre a arquitetura atual.
- Reutilize componentes existentes antes de criar novos.
- Reutilize a biblioteca de drag-and-drop já instalada, se houver.
- Não adicione uma nova dependência sem necessidade.
- Respeite os padrões de TypeScript, lint e formatação do projeto.
- Mantenha a tipagem correta.
- Evite uso desnecessário de any.
- Não duplique regras de negócio já existentes no backend.
- Centralize chamadas nos serviços ou hooks de API já utilizados.
- Atualize corretamente caches, queries ou stores.
- Não faça recarregamento completo da página.
- Não remova funcionalidades existentes.
- Não altere o restante do fluxo de onboarding.
- Não use localStorage para categorias, produtos ou ordenação.
- Não deixe mocks ou comentários de implementação pendente.
15. Critérios de aceite

A implementação estará concluída quando:

- A seção de categorias aparecer antes do formulário de produtos.
- As categorias forem carregadas do backend.
- For possível cadastrar uma categoria.
- Categorias duplicadas forem bloqueadas.
- For possível editar uma categoria.
- For possível ativar e desativar uma categoria.
- For possível reordenar arrastando.
- For possível reordenar usando subir e descer.
- Os indicadores 01, 02, 03 forem atualizados automaticamente.
- A ordem for persistida no backend.
- A ordem permanecer correta após atualizar a página.
- Falhas de reordenação restaurarem a ordem anterior.
- O seletor do produto respeitar a nova ordem.
- Um produto não puder ser cadastrado sem categoria.
- A exclusão tratar corretamente categorias com produtos.
- O painel lateral exibir a categoria dos produtos.
- A tela funcionar em desktop e celular.
- Nenhuma funcionalidade atual for perdida.
16. Verificação final

Depois de implementar:

- Execute o lint.
- Execute a verificação de tipos.
- Execute os testes existentes relacionados.
- Adicione ou atualize testes para o cadastro e ordenação de categorias.
- Teste manualmente a reordenação, inclusive após recarregar a página.
- Verifique os estados de carregamento e erro.
- Verifique a tela em resolução móvel.
- Corrija todos os erros causados pela alteração.

Ao finalizar, informe:

- Arquivos modificados.
- Componentes ou hooks criados.
- Serviços e endpoints reutilizados.
- Como a ordenação foi persistida.
- Testes executados e seus resultados.
- Qualquer limitação real encontrada nos contratos atuais do backend.

