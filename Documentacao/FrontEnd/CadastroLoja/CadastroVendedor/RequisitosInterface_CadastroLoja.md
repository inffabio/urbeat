# Requisitos de Interface: Fluxo de Cadastro de Loja (Vendedor)

## 1. Componente "Stepper" (Progresso do Assistente)
- **Nome do Componente**: *Stepper* (ou *Wizard Progress Indicator*).
- **Comportamento Visual**: 
  - Deve estar presente no topo de todas as telas do ciclo de configuração.
  - **Status Concluído (Verde)**: Representa etapas (*steps*) já preenchidas e salvas.
  - **Status Atual (Laranja)**: Etapa na qual o usuário se encontra ativamente navegando/preenchendo.
  - **Status Futuro (Padrão/Cinza)**: Etapas que ainda não foram iniciadas.
- Deve fornecer ao usuário previsibilidade de preenchimento ("20% concluído").

## 2. Componentes de Categoria (Loja vs Produtos)
- **Diferenciação no Backend**: O banco de dados e APIs (`API.md`) devem tratar **`CategoriaLoja`** e **`CategoriaProduto`** como entidades e domínios independentes para evitar choques nas tabelas. O catálogo da loja tem categorias, e as lojas em si também possuem.
- **Inclusão Dinâmica de Categorias**: O *ListBox* (ComboBox) nas telas de configuração não deve ser *hardcoded*.
- **Ação de Popup `...`**: 
  - O Botão `...` abrirá um *Modal popup*.
  - **Pesquisa**: A modal contém uma caixa de consulta de texto com ícone de `lupa` interno.
  - **Ações de Inclusão**: O Vendedor pode digitar um novo nome na caixa e clicar no botão "Incluir". Uma validação (*crítica*) deve acusar `alert/error` se o registro já existir. Ao inserir, a categoria é selecionada automaticamente no *dropdown* principal e a popup fecha.
  - **Ações de Exclusão (Trash)**: Na listagem alfábetica (grid) dentro da popup, os itens mostram um ícone indicativo de lixeira. 
  - **Validação de Exclusão**: Ao excluir, perguntar previamente "Deseja realmente apagar?". Se a categoria estiver em uso por *alguma loja ativa no banco de dados*, bloquear a deleção ou não renderizar o ícone cor lixeira para o usuário final.

## 3. Dinâmica de Tempos de Entrega
- Segue exatamente os mesmos princípios e janela Modal de "Categorias", retirando os dados fixados e tornando o CRUD dinâmico via Backend.
- O campo de inserção na Popup é formatado exclusivamente via campos numéricos: `<Entre> [input num: N] e [input num: M] <min>` com o botão de "Incluir".

## 4. Ordem e Validação de Endereço via CEP
- Em todos os locais onde endereço for exigido, o **campo `CEP`** obrigatoriamente deve ser listado primeiro.
- **Autopreenchimento**: Ao concluir a digitação do CEP (no momento do desfoque/blur do campo), o Frontend deve disparar uma consulta na API (ex: `ViaCEP`) para resgatar *Logradouro, Bairro, Cidade e Uf*, preenchendo automaticamente e reposicionando o foco (*focus*) para o campo de `Número`.

## 5. Responsividade 
- Todos os ajustes nas telas (principalmente painéis e modais) se adequam por `@media` queries a dispositivos:
  - **Mobile** (até 768px): Colapsa blocos de 3+ colunas para 1, retira/condensa stepper extensos e converte grades.
  - **Tablet** (até 1200px): Reacomoda layout do grid dividido flexível, mantendo clareza.
  - **Desktop**: Telas largas dividindo áreas de edição e simulação de *Preview ao Vivo* simultaneamente.