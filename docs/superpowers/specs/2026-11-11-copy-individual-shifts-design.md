# Cópia individual de turnos — design

## Objetivo

Permitir que o lojista copie qualquer turno individual para outros dias ativos,
sem copiar automaticamente os demais turnos do dia e sem criar sobreposição de
horários. A regra deve ser idêntica no wizard de cadastro e no dashboard.

## Comportamento da interface

- O botão **Copiar** ficará ao lado de cada turno, inclusive do primeiro turno.
- O botão atualmente associado ao nível do dia será removido.
- Ao clicar em um turno, o modal indicará o dia e o turno selecionado, por
  exemplo: `Copiar turno 2 de Segunda-feira para:`.
- O modal listará apenas dias ativos diferentes do dia de origem.
- O botão de confirmação copiará somente o turno selecionado para os dias
  marcados.
- O componente de cadastro e o dashboard utilizarão a mesma lógica da classe
  compartilhada `StoreHoursState`, com seus templates próprios atualizados.

## Regras de cópia e conflitos

- O turno copiado será clonado com nova chave de UI e seus horários serão
  preservados.
- Cada destino será verificado contra todos os turnos já existentes naquele dia,
  usando a mesma regra de intervalos que trata turnos que atravessam meia-noite.
- Destinos sem conflito recebem o turno; destinos com conflito não recebem.
- Ao final, o usuário verá aviso com os dias que tiveram sobreposição, incluindo
  o nome de cada dia. Os destinos válidos continuam sendo aplicados.
- O limite máximo de turnos por dia continua valendo; um destino que já atingiu
  o limite será tratado como não aplicável e informado no aviso.
- A operação marca o rascunho como alterado apenas quando ao menos um destino
  recebe o turno. O modal é fechado após a operação e os alvos são limpos.
- O modo de sincronização de todos os dias continua bloqueando cópia individual
  quando aplicável, sem alterar a lógica existente de sincronização.

## Arquitetura

- Evoluir `StoreHoursState` para armazenar origem como `copySourceShift` com
  `dayId`, `shiftIndex` e uma cópia do turno, em vez de apenas `copySourceDay`.
- Reutilizar a infraestrutura atual de modal, seleção de destinos e clonagem,
  mas fazer `confirmCopy` operar sobre um único turno.
- Extrair ou reutilizar uma verificação de conflito que não dependa de detalhes
  do template; a validação já existente de sobreposição deve permanecer
  autoritativa no frontend antes da cópia e no envio.
- Atualizar os dois templates: `store-hours-page.component.html` e
  `seller-hours-page.component.html`. Não criar lógica duplicada no dashboard.

## Compatibilidade

- Nenhum contrato backend ou modelo persistido será alterado.
- Turnos existentes não serão modificados ao copiar; apenas novos turnos válidos
  serão adicionados aos destinos.
- A edição, remoção, adição, presets e sincronização existentes permanecem.
- Manter acessibilidade dos botões, aria-labels com dia e número do turno, foco e
  comportamento mobile.

## Testes e aceite

- Testar que cada turno renderiza seu botão Copiar e que não existe botão
  duplicado no nível do dia.
- Testar que copiar o primeiro turno copia somente ele quando existem dois ou
  mais turnos no dia de origem.
- Testar que copiar segundo, terceiro e turnos seguintes copia apenas o índice
  selecionado.
- Testar que somente dias ativos aparecem como destinos.
- Testar cópia parcial com aviso por dia conflitante, incluindo sobreposição
  normal e turno que atravessa meia-noite.
- Testar limite máximo de turnos, nenhum destino selecionado, cancelamento e
  comportamento no wizard e dashboard.
- Executar specs de horas, `npx jest --no-coverage` e build de produção.

Critério de aceite: todos os turnos possuem cópia individual no wizard e no
dashboard; a cópia nunca sobrepõe horários, informa os dias conflitantes e
continua copiando para os demais dias válidos.
