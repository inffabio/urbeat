**Prompt Turnos de horários**


Permitir configurações como:

- Segunda-feira: 11:00–14:00 e 18:00–23:00
- Terça-feira: 09:00–12:00 e 13:00–18:00
- Sexta-feira: 11:00–14:00 e 18:00–00:00
- Sábado: 11:00–00:00
- Domingo: fechado

1. Estrutura da tela

Preserve a identidade visual atual da Urbeat, incluindo:

- Cabeçalho.
- Etapas do cadastro.
- Tipografia.
- Cores.
- Cartões.
- Botões.
- Espaçamentos.
- Responsividade.

Exiba os sete dias separadamente:

- Segunda-feira
- Terça-feira
- Quarta-feira
- Quinta-feira
- Sexta-feira
- Sábado
- Domingo

Não agrupe dias como “Segunda a Quinta” ou “Sexta e Sábado”, pois cada dia precisa aceitar horários próprios.

2. Configuração de cada dia

Cada dia deve possuir:

- Controle para marcar como aberto ou fechado.
- Nome completo do dia.
- Quantidade de turnos cadastrados.
- Lista dos turnos.
- Campo de horário inicial.
- Campo de horário final.
- Botão “Adicionar turno”.
- Botão para remover cada turno.
- Botão “Copiar”.
- Status “Aberto” ou “Fechado”.
- Mensagens de validação próximas ao respectivo dia.

Quando o dia estiver fechado:

- Exiba “Loja fechada neste dia”.
- Não mostre campos de horários ativos.
- Preserve ou descarte os turnos anteriores conforme a regra já adotada pelo sistema.
- Se não existir regra definida, preserve os turnos no estado do formulário enquanto o usuário estiver na tela, mas envie o dia como fechado ao backend.

Quando o dia for reaberto:

- Restaure os turnos anteriores da sessão, quando disponíveis.
- Caso não existam turnos anteriores, adicione um horário inicial padrão, como 09:00–18:00.

3. Cadastro de múltiplos turnos

O botão “Adicionar turno” deve adicionar um novo intervalo somente ao dia selecionado.

Cada turno deve possuir:

- Identificador, quando já estiver persistido.
- Horário inicial.
- Horário final.
- Informação sobre encerramento no dia seguinte, se o modelo do backend exigir.

Ao editar um turno existente:

- Preserve seu identificador.
- Não exclua e recrie registros desnecessariamente se o backend já trabalhar com atualização individual.
- Utilize o fluxo de atualização já definido pelo projeto.

Ao remover:

- Remova o turno da interface.
- Marque-o para exclusão ou envie o estado final da semana, conforme o contrato real da API.
- Não deixe registros órfãos no backend.

4. Modelo de dados no backend

Adapte os nomes ao padrão já utilizado pelo projeto.

A estrutura precisa representar um dia com vários turnos, equivalente a:

- Dia da semana.
- Aberto ou fechado.
- Lista de intervalos.

Exemplo conceitual:

dayOfWeek\
enabled\
intervals[]

Cada intervalo deve conter algo equivalente a:

- id
- startTime
- endTime
- crossesMidnight, caso necessário

Não crie campos duplicados se o modelo atual já representar essas informações.

Se o banco atualmente possuir apenas opening\_time e closing\_time diretamente no dia:

- Crie a estrutura necessária para múltiplos intervalos.
- Faça uma migration segura.
- Converta cada horário existente em um primeiro turno.
- Preserve dias fechados.
- Não apague configurações já cadastradas.
- Crie índices e relacionamentos seguindo o padrão do projeto.
- Garanta exclusão em cascata ou limpeza transacional conforme as convenções existentes.

5. Representação dos dias da semana

Utilize o enum, número ou identificador de dia já adotado pelo backend.

Não crie um segundo padrão de dias da semana.

Garanta que frontend e backend usem o mesmo mapeamento.

Verifique especialmente se a semana começa no domingo ou na segunda-feira e não faça conversões implícitas incorretas.

6. Horários que passam da meia-noite

Permita turnos como:

- 18:00 – 01:00
- 20:00 – 00:00
- 22:00 – 02:30

Mostre na interface uma indicação:

“Encerra no dia seguinte”

Utilize a convenção de horários já existente no backend.

Se ainda não existir uma convenção, adote uma regra explícita e única:

- Horário final menor que o inicial significa encerramento no dia seguinte.
- Horário final igual ao inicial deve representar 24 horas somente se essa for uma regra aceita pelo negócio; caso contrário, trate como inválido.
- Não converta horários locais de funcionamento em instantes UTC.
- Armazene os valores como horário local da loja, como HH:mm.
- Utilize o fuso horário da loja apenas quando for necessário transformar o horário em uma data real.

Documente essa regra no código e nos testes.

7. Validações

Valide no frontend e novamente no backend.

Regras obrigatórias:

- Um dia aberto deve possuir pelo menos um turno.
- Todos os turnos devem ter início e fim.
- Os horários devem usar formato válido.
- Não permita turnos duplicados.
- Não permita turnos sobrepostos.
- Permita horários consecutivos, como 09:00–12:00 e 12:00–18:00.
- Considere corretamente turnos que passam da meia-noite.
- Valide conflitos entre um turno que avança para o dia seguinte e os turnos cadastrados nesse dia.
- Considere também o conflito entre domingo e segunda-feira.
- Não confie somente nas validações do navegador.
- Retorne erros de domínio claros pelo backend.
- Relacione os erros do backend com o respectivo dia ou turno na interface.

Exemplo de conflito:

- Segunda-feira: 18:00–02:00
- Terça-feira: 01:00–04:00

Esses horários se sobrepõem e devem seguir uma regra de negócio explícita. Na ausência de regra anterior, bloqueie a sobreposição.

8. Persistência

Prefira uma operação atômica para salvar a configuração semanal completa.

Utilize o endpoint existente, caso ele já aceite a semana inteira.

Se for necessário criar ou adaptar o contrato, use uma operação equivalente a:

- Buscar a configuração semanal.
- Substituir ou atualizar todos os dias e turnos em uma única transação.

O payload conceitual pode ser equivalente a:

days: [{ dayOfWeek, enabled, intervals: [{ id?, startTime, endTime }] }]

Adapte ao padrão real do projeto.

A operação de salvamento deve:

- Validar a semana completa.
- Garantir que todos os registros pertençam à loja autenticada.
- Executar dentro de uma transação.
- Criar novos turnos.
- Atualizar turnos existentes.
- Excluir turnos removidos.
- Preservar dados em caso de falha.
- Retornar a configuração persistida e normalizada.
- Impedir que o usuário altere horários de outra loja por manipulação de IDs.

9. Salvamento automático

A tela atual informa que os dados são salvos automaticamente. Portanto, o texto só deve permanecer se o salvamento automático realmente funcionar.

Implemente o autosave seguindo os padrões do projeto:

- Aguarde um pequeno intervalo após a última alteração.
- Não envie uma requisição a cada tecla digitada.
- Não salve enquanto houver erros de validação.
- Evite requisições concorrentes para versões diferentes do formulário.
- Cancele ou serialize requisições anteriores quando necessário.
- Não permita que uma resposta antiga sobrescreva uma alteração mais recente.
- Atualize o cache ou store após o sucesso.
- Mostre “Salvando...” durante a operação.
- Mostre “Alterações salvas” após o sucesso.
- Mostre uma mensagem clara e uma opção de tentar novamente em caso de erro.
- Não perca as alterações do formulário se a requisição falhar.

Se o projeto utiliza salvamento explícito em outras etapas, siga o padrão existente e ajuste o texto da interface para não prometer autosave.

10. Atalhos rápidos

Implemente os seguintes modelos:

Comercial:

- 09:00 – 12:00
- 13:00 – 18:00

Almoço:

- 11:00 – 16:00

Restaurante:

- 11:00 – 14:00
- 18:00 – 23:00

24 horas:

- Utilize a representação de 24 horas aceita pelo backend.

Limpar horários:

- Feche todos os dias e remova seus turnos no estado do formulário.

Comportamento:

- Aplique o modelo aos dias abertos.
- O modelo de 24 horas pode abrir todos os dias.
- Mostre feedback após aplicar.
- Não envie várias requisições separadas; salve a configuração consolidada.
- Permita desfazer antes da persistência, se o projeto já possuir esse padrão.

11. Usar os mesmos turnos em todos os dias

Adicione a opção:

“Usar os mesmos turnos em todos os dias”

Ao ativar:

- Utilize o primeiro dia aberto como modelo.
- Caso nenhum dia esteja aberto, utilize um horário padrão.
- Copie o status e os turnos para todos os dias.
- Alterações posteriores devem ser replicadas enquanto a opção estiver ativa.
- Desative os botões individuais de cópia.
- Mostre uma explicação de que todos os dias estão sincronizados.

Antes de substituir configurações diferentes, solicite confirmação se o projeto já tiver um padrão de confirmação para alterações em massa.

Persistir somente o resultado final no backend. Não é obrigatório salvar a opção de sincronização, a menos que ela represente uma regra permanente do domínio.

12. Copiar horários entre dias

Cada dia aberto deve possuir um botão “Copiar”.

Ao clicar:

- Abra um modal.
- Exiba os outros dias da semana.
- Permita selecionar um ou vários destinos.
- Ao confirmar, substitua os turnos dos dias selecionados pelos turnos do dia de origem.
- Gere novos identificadores apenas para turnos ainda não persistidos.
- Não reutilize o mesmo ID de turno em dias diferentes.
- Atualize o estado do formulário.
- Persista a configuração consolidada.
- Mostre quantos dias foram atualizados.

13. Carga horária semanal

Exiba um resumo com a carga horária semanal.

O cálculo deve:

- Somar todos os turnos dos dias abertos.
- Considerar horários que passam da meia-noite.
- Não contar intervalos sobrepostos em duplicidade.
- Atualizar sempre que um horário for alterado.
- Utilizar os dados atuais do formulário.

O backend também deve ser capaz de validar ou calcular corretamente a duração quando essa informação for utilizada por outras regras do sistema.

14. Carregamento da tela

Ao abrir a página:

- Busque os horários reais da loja.
- Converta a resposta da API para o estado do formulário.
- Ordene os dias corretamente.
- Preserve IDs dos turnos.
- Mostre estado de carregamento.
- Mostre erro com opção de tentar novamente.
- Diferencie uma configuração vazia de uma falha de carregamento.
- Não exiba horários fictícios enquanto os dados reais estão sendo buscados.

Se a loja ainda não possuir configuração:

- Inicialize a tela com uma configuração padrão apenas no frontend.
- Só persista essa configuração após uma ação válida do usuário ou conforme o fluxo já adotado pelo sistema.

15. Botão “Continuar”

Ao clicar em “Continuar”:

- Valide todos os dias e turnos.
- Destaque o primeiro dia com problema.
- Leve o foco ou role a tela até o erro.
- Não permita continuar se nenhum dia estiver aberto.
- Aguarde o salvamento pendente.
- Não avance se o backend rejeitar a configuração.
- Avance para a próxima etapa somente após confirmação de sucesso.

Evite criar registros duplicados se o usuário clicar várias vezes.

16. Estados da interface

Implemente:

- Carregamento inicial.
- Configuração vazia.
- Salvando.
- Salvo.
- Erro ao carregar.
- Erro ao salvar.
- Tentativa de reconexão.
- Campos temporariamente desabilitados quando necessário.
- Feedback de ações em massa.
- Confirmação antes de descartar configurações diferentes.

Utilize o sistema de toast, alertas, skeletons e componentes já existente no projeto.

17. Responsividade

No desktop:

- Exiba o dia, os turnos e as ações de forma compacta.
- Mantenha o padrão visual atual.

No celular:

- Empilhe os turnos quando necessário.
- Evite rolagem horizontal.
- Preserve os botões de adicionar, remover e copiar.
- Garanta áreas adequadas para toque.
- Mantenha as mensagens de erro próximas aos campos correspondentes.

18. Acessibilidade

- Use input type="time" ou o componente de horário já adotado pelo projeto.
- Associe labels aos campos.
- Adicione nomes acessíveis aos botões de adicionar, remover e copiar.
- Preserve foco visível.
- Mova o foco para novos turnos quando apropriado.
- Informe erros de validação às tecnologias assistivas.
- Não dependa apenas de cores para indicar aberto, fechado ou erro.
- Garanta navegação por teclado nos modais.

19. Segurança e consistência no backend

- Valide a loja a partir da sessão ou contexto autenticado.
- Não aceite um storeId arbitrário sem confirmar a autorização.
- Confirme que IDs de turnos pertencem à loja atual.
- Execute alterações em massa dentro de uma transação.
- Não permita IDs duplicados no payload.
- Defina limites razoáveis para a quantidade de turnos por dia.
- Normalize o formato dos horários.
- Não confie nas durações calculadas pelo frontend.
- Mantenha compatibilidade com dados já existentes.
- Evite condições de corrida e sobrescrita de alterações mais recentes.
- Utilize controle de versão, updatedAt ou mecanismo equivalente se o projeto já possuir concorrência otimista.

20. Requisitos técnicos

- Trabalhe sobre a arquitetura existente.
- Reutilize componentes, hooks, schemas e serviços existentes.
- Respeite os padrões de TypeScript, lint, testes e formatação.
- Não duplique regras de domínio.
- Centralize validações compartilhadas quando a arquitetura permitir.
- Não adicione dependências sem necessidade.
- Não use localStorage.
- Não use mocks no código de produção.
- Não remova funcionalidades atuais da tela.
- Não deixe comentários TODO no lugar de implementações.
- Não altere contratos públicos sem atualizar todos os consumidores.
- Documente migrations e mudanças de contrato relevantes.


21. Testes obrigatórios

Frontend:

- Renderização dos sete dias.
- Abertura e fechamento de um dia.
- Adição de vários turnos.
- Remoção de turnos.
- Validação de dia aberto sem turno.
- Validação de sobreposição.
- Horário que passa da meia-noite.
- Aplicação de atalhos.
- Sincronização semanal.
- Cópia entre dias.
- Estados de carregamento e erro.
- Salvamento e atualização do cache.
- Bloqueio do botão Continuar quando inválido.

Backend:

- Migration dos horários existentes.
- Criação de vários turnos no mesmo dia.
- Atualização e exclusão de turnos.
- Salvamento atômico da semana.
- Rejeição de horários inválidos.
- Rejeição de sobreposição no mesmo dia.
- Rejeição de conflito entre dias adjacentes.
- Turnos que passam da meia-noite.
- Autorização por loja.
- Rollback da transação em caso de falha.
- Idempotência ou prevenção de duplicidade.
- Compatibilidade com configurações antigas.

22. Critérios de aceite

A implementação estará concluída quando:

- Os sete dias forem configuráveis separadamente.
- Cada dia aceitar vários turnos.
- Os dados forem carregados do backend.
- Os turnos forem persistidos no backend.
- Os horários antigos forem preservados após a migration.
- For possível adicionar, editar e remover turnos.
- Horários após a meia-noite funcionarem corretamente.
- Turnos sobrepostos forem bloqueados.
- A cópia entre dias funcionar.
- A sincronização semanal funcionar.
- Os atalhos rápidos funcionarem.
- A carga horária semanal estiver correta.
- Erros do backend forem exibidos na interface.
- O autosave não perder alterações.
- O botão Continuar aguardar a persistência.
- O layout funcionar em desktop e celular.
- Nenhuma funcionalidade atual for perdida.

23. Verificação final

Depois da implementação:

- Execute testes frontend e backend.
- Teste a migration usando dados antigos.
- Teste manualmente horários que passam da meia-noite.
- Teste conflitos entre domingo e segunda-feira.
- Teste falha de rede durante o autosave.
- Teste cliques repetidos no botão Continuar.
- Teste a tela em resolução móvel.
- Corrija todos os erros causados pela alteração.

Ao finalizar, informe:

- Arquivos modificados.
- Migrations criadas.
- Mudanças no modelo de dados.
- Endpoints criados ou alterados.
- Componentes e hooks atualizados.
- Estratégia usada para salvar a semana.
- Como horários antigos foram migrados.
- Testes executados e seus resultados.
- Qualquer limitação real encontrada no sistema atual.