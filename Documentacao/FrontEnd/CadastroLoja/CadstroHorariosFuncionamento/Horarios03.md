# Responsabilidades dessa funçã

1. Essa função deve ser interpretada como a base da aplicação rápida de horários nos grupos de dias. Ela precisa ser responsável por:

- localizar o grupo de dias alvo;
- marcar o grupo como ativo/aberto;
- atualizar o indicador visual de seleção/check;
- preencher o horário inicial;
- preencher o horário final;
- alterar o status visual para Aberto;
- reconstruir a estrutura de horários caso o grupo esteja - fechado e ainda não tenha os elementos visuais necessários;
- permitir aplicação seletiva para um ou mais grupos de dias;
- centralizar a regra de aplicação de horários rápidos e cópias.

> Comportamento esperado Ao executar essa função:

- os grupos de dias alvo devem ficar abertos;
- o check visual deve ser exibido;
- o texto/status da linha deve virar Aberto;
- a linha deve mostrar o intervalo no formato:
- HH:mm → HH:mm;
- caso a linha estivesse fechada, a UI deve ser reconstruída - sem inconsistências.
- ✅ Aplicação padrão identificada
- Quando acionada por atalhos rápidos, a função aplica o - horário para:
- 
- Segunda a Quinta
- Sexta e Sábado
- Por padrão:
- 
- Domingo não é afetado;
- exceção: quando o preset é 24 horas, domingo também é aberto.
- ✅ Recomendação técnica
- Na implementação nova:
- 
- essa lógica não deve manipular o DOM diretamente;
- deve operar sobre o estado centralizado;
- a interface deve ser renderizada declarativamente;
- a função deve ser desacoplada da camada visual;
- a mesma lógica deve ser reaproveitável por:
- presets;
- botão de copiar;
- sincronização do toggle;
- inicialização de horários;
- futuras automações de onboarding.
- 🧩 Componentes recomendados
- A tela deve ser implementada com componentes pequenos, - reutilizáveis e orientados a estado.
- 
- 1. StoreSetupStepper

- 🎯 Responsabilidade
- Exibir o progresso do fluxo de onboarding/cadastro da loja.
- Deve mostrar
- logo/marca;
- as etapas do cadastro;
- etapa atual destacada;
- indicador percentual de progresso;
- preparação para futura navegação entre etapas.

- Requisitos visuais
- Horários deve estar destacada como etapa atual;
- o progresso deve mostrar 40% concluído;
- as etapas seguintes devem parecer futuras;
- etapas anteriores podem ser tratadas como concluídas ou - acessíveis conforme regra do produto.

---

2. QuickShortcuts

>🎯 Responsabilidade

**Renderizar os atalhos rápidos de configuração de horário.**

**Atalhos identificados**

- Comercial → 09:00 às 18:00
- Almoço → 11:00 às 16:00
- Jantar → 18:00 às 23:00
- 24 horas → 00:00 às 23:59
- Limpar horários

>Regras:

- apenas um preset pode ficar ativo por vez;
- ao selecionar um preset:
- ele deve receber destaque visual;
- os demais devem perder o estado ativo;
- ao clicar em Limpar horários:
- todos os presets devem perder destaque;
- todos os grupos devem ser fechados

3. DayScheduleRow

>🎯 Responsabilidade
**Representar cada linha da grade de horários por grupo de dias**

- Grupos identificados
- Segunda a Quinta
- Sexta e Sábado
- Domingo
- Responsabilidades da linha
- abrir/fechar grupo;
- exibir status;
- exibir horários;
- editar horários;
- adicionar novos intervalos;
- reagir à regra de sincronização;
- refletir claramente se o grupo está aberto ou fechado.

**Observação importante**
`Embora o comportamento atual exiba visualmente apenas um intervalo por grupo, a ação + Adicionar intervalo aparece na interface.` 

> Portanto:
`a implementação deve nascer preparada para múltiplos intervalos;`

- no MVP pode ser entregue inicialmente com 1 intervalo visível
- a estrutura de dados já deve suportar expansão.`

4. InsightCard

>🎯 Responsabilidade
**Exibir insights de negócio ou operação com base nos horários da loja**

>Insights identificados

- Lojas abertas até 00h recebem em média 27% mais pedidos
- Sua loja ficará aberta 72h por semana
- Domingo está fechado
- Considere abrir e vender mais!

## Requisitos

- exibir em formato de card;
- permitir realce visual do dado principal;

- permitir conteúdo dinâmico em evolução futura;
- responder ao estado real da tela quando necessário.

5. AutoSaveStatus

>🎯 Responsabilidade
**Exibir o estado de persistência automática**


6. CopyHoursButton
🎯 Responsabilidade
Copiar os horários da origem principal para outros grupos.

Requisitos
usar Segunda a Quinta como origem;
copiar para grupos-alvo configurados;
exibir feedback visual temporário de sucesso;
voltar ao estado normal após ~1.5s.
Texto padrão
Copiar para outros dias
Texto de sucesso
Copiado!
🗂️ Modelo de dados sugerido

A solução deve ser modelada para suportar estado previsível, testes e evolução funcional.


Requisitos de modelagem
o estado deve ser a fonte única da verdade;
a UI não deve inferir dados a partir do DOM;
horários devem ser sempre persistidos como dados estruturados;
a representação de grupos deve ser explícita;
o cálculo de horas semanais deve vir do estado;
múltiplos intervalos devem ser suportados mesmo que parcialmente ocultos no MVP.
⚙️ Regras de negócio
Regra 1 — Dia fechado
Se open = false:

não exibir inputs editáveis de horário;
exibir Loja fechada;
exibir badge/status Fechado;
ocultar ou substituir a renderização dos intervalos;
preservar a capacidade de reabrir o grupo;
no comportamento atual observado, ao fechar:
intervals = [].
Regra 2 — Abrir um dia fechado
Ao abrir um grupo fechado:

marcar visualmente o check;
mudar o status para Aberto;
se não existirem intervalos:
criar automaticamente:
11:00 → 23:00.
Regra 3 — Fechar um dia aberto
Ao desativar um grupo aberto:

remover o check visual;
substituir a área de horários por:
Loja fechada
Fechado;
no comportamento atual:
open = false
intervals = [].
Recomendação futura
Opcionalmente, em evolução posterior, pode-se preservar os horários anteriores para facilitar reativação sem perda.

Regra 4 — Aplicação de presets
Ao clicar em um preset:

remover o estado ativo dos demais presets;
destacar o preset atual;
aplicar o horário para:
Segunda a Quinta
Sexta e Sábado;
abrir os grupos afetados;
sobrescrever horários existentes nesses grupos;
atualizar status para Aberto.
Regra 5 — Preset 24 horas
Ao aplicar o preset 24 horas:

Segunda a Quinta deve virar 00:00 → 23:59;
Sexta e Sábado deve virar 00:00 → 23:59;
Domingo deve ser aberto automaticamente;
Domingo deve ficar:
00:00 → 23:59;
o status de domingo deve virar Aberto.
Regra 6 — Limpar horários
Ao clicar em Limpar horários:

remover o estado ativo de todos os atalhos;
fechar todos os grupos;
remover checks visuais;
cada linha deve passar a mostrar:
Loja fechada
Fechado.
Regra 7 — Copiar horários
Ao clicar em Copiar para outros dias:

usar Segunda a Quinta como origem;
copiar os horários para:
Sexta e Sábado;
exibir feedback visual temporário;
restaurar o texto padrão do botão após cerca de 1.5s.
Observação do comportamento atual
domingo não é incluído nessa cópia.
Regra 8 — Aplicar o mesmo horário para todos os dias
Quando o toggle Aplicar o mesmo horário para todos os dias estiver ativo:

Segunda a Quinta deve ser a referência principal;
ao ativar o toggle, deve ocorrer uma sincronização inicial;
no comportamento atual identificado:
essa sincronização replica apenas para Sexta e Sábado;
se o usuário alterar a origem depois:
os grupos alvo devem ser sincronizados conforme a regra adotada.
Recomendação ideal
Na implementação nova:

a sincronização deve ocorrer pelo estado;
deve ser possível configurar destinos;
a lógica deve aceitar:
copiar apenas para abertos;
ou abrir e copiar automaticamente.
Regra 9 — Intervalos múltiplos
A interface expõe + Adicionar intervalo, logo a estrutura precisa suportar:

múltiplos intervalos por grupo;
adição de novo intervalo;
remoção de intervalo;
validação contra sobreposição;
soma total do tempo por grupo;
compatibilidade futura com UI mais rica.
Requisito mínimo
Mesmo que o MVP não permita fluxo completo de múltiplos intervalos, a modelagem e a arquitetura já devem prever isso.

Regra 10 — Autosave
Toda alteração deve:

atualizar imediatamente o estado local;
disparar persistência automática;
atualizar status visual;
usar debounce para evitar excesso de chamadas;
tratar erro de salvamento;
permitir nova tentativa em alterações posteriores.

🔒 Requisitos técnicos adicionais
Estado
usar estado centralizado;
evitar leitura/escrita direta no DOM;
separar estado de apresentação e estado de domínio;
garantir imutabilidade nas atualizações.
Persistência
usar autosave com debounce;
idealmente enviar payload normalizado;
refletir estados de salvamento na UI;
suportar falha de rede sem quebrar a interação.
Sincronização
sincronização do toggle deve ser baseada em dados;
cópia entre dias deve operar sobre arrays de intervalos;
mudanças na origem devem refletir corretamente nos destinos definidos.
Testabilidade
lógica de domínio deve ser isolável;
funções de cálculo e sincronização devem ser puras sempre que possível;
handlers de UI devem ser simples adaptadores.
Escalabilidade
a solução deve aceitar novos grupos de dias futuramente;
o sistema deve aceitar múltiplos intervalos sem reescrita estrutural;
deve ser possível substituir insights fixos por dados do backend.
🖱️ Eventos esperados
Eventos de interface
clique em preset;
clique em limpar horários;
clique para abrir/fechar grupo;
alteração de horário inicial;
alteração de horário final;
clique em copiar horários;
toggle Aplicar o mesmo horário para todos os dias;
clique em Adicionar intervalo;
clique em Remover intervalo;
clique em Voltar.
Eventos de domínio sugeridos
ts
 
onPresetApply(presetId)
onClearSchedules()
onToggleDay(dayId)
onChangeDayStart(dayId, intervalIndex, value)
onChangeDayEnd(dayId, intervalIndex, value)
onCopyFromPrimaryDay()
onToggleApplySameHours(value)
onAddInterval(dayId)
onRemoveInterval(dayId, intervalIndex)
onSaveSchedule(payload)
onBack()

💬 Textos visíveis na interface
Todos os textos abaixo devem ser tratados como catálogo de conteúdo inicial.

Títulos e descrições
Defina os horários e área de atendimento
Informe quando sua loja funciona e onde entrega seus pedidos.
Autosave
Salvo automaticamente
Suas alterações são salvas em tempo real.
Atalhos rápidos
Atalhos rápidos
Escolha um horário pré-definido ou personalize abaixo
Presets
Comercial
9h às 18h
Almoço
11h às 16h
Jantar
18h às 23h
24 horas
Todos os dias
Limpar horários
Remover todos
Ações auxiliares
Aplicar o mesmo horário para todos os dias
Copiar para outros dias
Status e ações
+ Adicionar intervalo
Loja fechada
Aberto
Fechado
Navegação e confiança
Voltar
Seus dados estão seguros conosco
📈 Bloco de insights
A tela possui cards com mensagens de apoio comercial e operacional.

Conteúdo identificado
Insight 1
Lojas abertas até 00h recebem em média 27% mais pedidos
Insight 2
Sua loja ficará aberta 72h por semana
Insight 3
Domingo está fechado
Considere abrir e vender mais!
Requisitos de implementação
renderizar em cards visuais;
destacar o conteúdo principal;
permitir conteúdo estático no MVP;
preparar estrutura para conteúdo dinâmico;
recalcular pelo menos o insight de horas semanais;
reagir ao estado real de domingo fechado/aberto.
🧮 Regra de cálculo sugerida para horas semanais
A implementação deve calcular dinamicamente a carga horária semanal total da loja.

Peso de cada grupo
Segunda a Quinta = 4 dias
Sexta e Sábado = 2 dias
Domingo = 1 dia
Exemplo do estado inicial observado
Segunda a Quinta
11:00 → 23:00
duração: 12h
total do grupo: 48h
Sexta e Sábado
11:00 → 00:00
duração: 13h
total do grupo: 26h
Domingo
fechado
total: 0h
Soma real
48 + 26 + 0 = 74h
Inconsistência observada
A interface exibe 72h por semana, mas os horários mostrados resultam em 74h.

Recomendação obrigatória
A nova implementação deve:

calcular dinamicamente;
não depender de texto fixo;
considerar corretamente horários que cruzam meia-noite;
suportar soma de múltiplos intervalos no futuro.

>⚠️ Inconsistências e limitações observadas no comportamento atual

1. Insight semanal inconsistente
a UI mostra 72h por semana;
o cálculo real com os horários exibidos dá 74h.
2. Toggle “Aplicar o mesmo horário para todos os dias” não cumpre literalmente o nome
na prática ele replica parcialmente;
a cópia fica restrita a Sexta e Sábado.
3. Domingo só é aberto automaticamente no preset 24 horas
nos demais presets ele permanece fechado.
4. Adicionar intervalo existe apenas visualmente
a ação aparece;
a implementação funcional correspondente não foi encontrada.
5. Replicação depende da estrutura visual existir
isso fragiliza a lógica;
dias fechados podem não ter a estrutura necessária.
6. Mistura de estado visual com estado de dados
a lógica atual manipula elementos da interface diretamente;
isso dificulta manutenção, testes e previsibilidade.
Recomendação de correção
A nova implementação deve usar:

## estado centralizado

renderização declarativa;
regras de domínio puras;
sincronização baseada em dados.

✅ Requisitos de implementação recomendados
Requisito funcional mínimo
Implementar fielmente o comportamento identificado:

- presets rápidos;
- limpar horários;
- abrir/fechar grupo;
- copiar Segunda a Quinta para Sexta e Sábado;
- toggle de mesmo horário com comportamento equivalente;
- autosave;
- insights visuais.
- Requisito funcional ideal
- Evoluir a base para:

- múltiplos intervalos por grupo;
- cálculo dinâmico de horas;
- cópia configurável entre grupos;
- sincronização robusta;
- separação clara entre dados e UI;
- persistência confiável;
- preparação para internacionalização;
- possibilidade de backend dinâmico de insights.

>🧪 Critérios de aceite
**Presets**

- Ao clicar em Comercial, Segunda a Quinta e Sexta e Sábado ficam 09:00 → 18:00
- Ao clicar em Almoço, Segunda a Quinta e Sexta e Sábado - ficam 11:00 → 16:00
- Ao clicar em Jantar, Segunda a Quinta e Sexta e Sábado - ficam 18:00 → 23:00
- Ao clicar em 24 horas, Segunda a Quinta, Sexta e Sábado e Domingo ficam abertos
- Em 24 horas, domingo fica 00:00 → 23:59

## Limpeza

- Ao clicar em Limpar horários, todos os grupos ficam fechados
- Todos os checks visuais são removidos
- Todos os presets deixam de ficar ativos
- Cada linha exibe Loja fechada e Fechado

## Toggle de grupos
 
- Ao abrir um grupo fechado, ele recebe 11:00 → 23:00
- Ao fechar um grupo aberto, ele passa a mostrar Loja fechada
- O status alterna corretamente entre Aberto e Fechado
Cópia
- Ao clicar em Copiar para outros dias, os horários de - Segunda a Quinta são replicados para Sexta e Sábado
- O botão mostra sucesso temporário
- Após ~1.5s, o botão volta ao estado normal

> Mesmo horário para todos

- Ao ativar o toggle, a sincronização inicial é disparada
- Os destinos recebem o mesmo horário da origem segundo a - regra adotada
- Alterações na origem podem refletir nos destinos quando - aplicável


>Construir a tela com:

- componentes reutilizáveis;
- estado centralizado;
- renderização declarativa;
- autosave com debounce;
- domínio desacoplado da UI;
- modelagem pronta para múltiplos intervalos.

## etapa atual do fluxo

- preset ativo;
- status Aberto;
- status Fechado;
- feedback de cópia;
- estado de autosave;
- insights importantes.
- Recomendações de UI
- cards com bordas arredondadas;
- espaçamento confortável;
- badges para status;
- ícones consistentes;
- boa separação entre blocos;
- ótima legibilidade em mobile;
- evitar sobrecarga visual.

>Procurar nessa pasta:

- punbic/images
- logo da marca;
- ícones de check;
- ícones de relógio;
- ícones de cópia;
- ícones de segurança;
- ilustrações de insights;
- elementos gráficos do onboarding.
- Caso algum asset não exista
- usar SVG inline;
- manter coerência visual com o restante da aplicação.

>📱 Responsividade

## Mobile

>A tela deve considerar:

- cards empilhados;
- stepper simplificado;
- horários organizados verticalmente;
- botões com boa área de toque;
- insights em coluna;
- toggles e CTA sem conflito visual.
- Tablet
- manter leitura confortável;
- preservar espaçamentos;
- permitir boa hierarquia dos blocos.
- Desktop
- stepper horizontal completo;
- linhas de horários com boa distribuição;

>insights em grid;
**ações rápidas visíveis.**

- 🔐 Rodapé / confiança
- Na parte inferior da tela, exibir:

- botão Voltar;

> mensagem:

- Seus dados estão seguros conosco.

> Objetivo:

- reforçar confiança;
- manter coerência com o onboarding;
- permitir retorno à etapa anterior.

---

# ✅ Observações finais

## O que este arquivo já entrega

- 🧠 responsabilidades da lógica principal
- 🧩 componentes recomendados
- 🗂️ modelo de dados
- ⚙️ regras funcionais
- 🔒 requisitos técnicos
- 🖱️ eventos esperados
- 💬 textos de interface
- 📈 insights
- 🧮 cálculo de horas
- ⚠️ inconsistências atuais
- 🧪 critérios de aceite
- 🧑‍💻 estratégia técnica
- 🔁 pseudocódigo
- 🎨 diretrizes visuais
- 📱 responsividade
- 🚀 escopo de entrega
