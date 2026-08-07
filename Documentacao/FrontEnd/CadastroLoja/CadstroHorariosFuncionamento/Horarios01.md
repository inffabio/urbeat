# 🍽️ Cadastro da Loja — Etapa 2: Horários de Funcionamento

## 🎯 Objetivo
Implementar a tela de **cadastro de horários de funcionamento e área de atendimento** da loja de delivery, permitindo que o lojista configure:

- dias e faixas horárias de funcionamento;
- aplicação rápida de horários pré-definidos;
- replicação de horários entre dias;
- abertura/fechamento de dias específicos;
- visualização de insights de operação;
- progresso do cadastro da loja.

---

## 🧭 Contexto no fluxo
Esta tela faz parte da sequência de cadastro da loja e representa:

- **Etapa 2: Horários**
- progresso exibido: **40% concluído**

Fluxo visível no topo:
1. Loja
2. Horários
3. Entrega
4. Produtos
5. Publicar

---

## 🖼️ Assets / Imagens
Todas as imagens, ícones e ilustrações devem ser procuradas em:

`./Documentacao/frontend/images`

### Regras para uso de assets
- Buscar nessa pasta por:
  - logo da marca;
  - ícones de check;
  - ícones de relógio/tempo;
  - ícones de cópia;
  - ícones de segurança;
  - ilustrações de insights;
  - possíveis ícones de progresso e navegação.
- Caso algum asset não exista:
  - usar **SVG inline**;
  - manter estilo visual limpo e moderno;
  - priorizar consistência com a identidade do produto.

---

# 🧱 Estrutura da tela

## 1. Header / Progresso
### Elementos
- logo/marca
- stepper de navegação do cadastro
- indicação visual da etapa atual
- barra ou indicador de progresso
- texto: **40% concluído**

### Comportamento esperado
- a etapa **Horários** deve aparecer destacada;
- etapas seguintes devem aparecer como futuras;
- etapas anteriores como concluídas ou acessíveis conforme estratégia do sistema.

---

## 2. Bloco introdutório
### Conteúdo
**Título:**  
`Defina os horários e área de atendimento`

**Descrição:**  
`Informe quando sua loja funciona e onde entrega seus pedidos.`

---

## 3. Estado de salvamento
### Conteúdo
- indicador: **Salvo automaticamente**
- texto auxiliar:  
  `Suas alterações são salvas em tempo real.`

### Regra funcional
- qualquer alteração na grade de horários deve ser persistida automaticamente;
- mostrar feedback visual de autosave;
- idealmente usar estados como:
  - salvando...
  - salvo automaticamente
  - erro ao salvar

---

## 4. Atalhos rápidos
### Finalidade
Permitir definir horários pré-configurados com um clique.

### Opções existentes
- **Comercial** → `09:00 às 18:00`
- **Almoço** → `11:00 às 16:00`
- **Jantar** → `18:00 às 23:00`
- **24 horas** → `00:00 às 23:59`
- **Limpar horários** / **Remover todos**

### Comportamentos
#### Ao clicar em um atalho padrão
- remover estado ativo dos demais atalhos;
- marcar o atalho clicado como ativo;
- aplicar o horário configurado para:
  - `Segunda a Quinta`
  - `Sexta e Sábado`

#### Caso o atalho seja “24 horas”
- além de `Segunda a Quinta` e `Sexta e Sábado`,
- abrir também o `Domingo`;
- definir domingo como:
  - início: `00:00`
  - fim: `23:59`
  - status: `Aberto`

#### Ao clicar em “Limpar horários”
- remover o estado ativo de todos os atalhos;
- fechar todos os dias;
- limpar visualmente os checks;
- substituir horários por:
  - texto: `Loja fechada`
  - status: `Fechado`

---

## 5. Ações auxiliares
### Ações visíveis
- **Aplicar o mesmo horário para todos os dias**
- **Copiar para outros dias**

### Componentes
- 1 toggle/checkbox: `Aplicar o mesmo horário para todos os dias`
- 1 botão: `Copiar para outros dias`

---

# 📅 Grade de dias e horários

## Grupos de dias exibidos
1. **Segunda a Quinta**
2. **Sexta e Sábado**
3. **Domingo**

### Estado inicial identificado
- **Segunda a Quinta**
  - início: `11:00`
  - fim: `23:00`
  - status: `Aberto`
- **Sexta e Sábado**
  - início: `11:00`
  - fim: `00:00`
  - status: `Aberto`
- **Domingo**
  - status: `Fechado`
  - texto: `Loja fechada`

### Ação adicional em cada linha
- botão/link: `+ Adicionar intervalo`

> Observação: o HTML exibe a ação de adicionar intervalo, mas a lógica de múltiplos intervalos ainda não está implementada no JS fornecido.  
> Mesmo assim, a IA responsável pela implementação deve estruturar isso de forma extensível.

---

# 🧠 Regras funcionais identificadas

## 1. Abrir/fechar um dia
Cada linha de dia possui um controle de seleção/check.

### Ao marcar um dia fechado
Se o dia estiver fechado e o usuário ativá-lo:
- marcar visualmente o check;
- inserir horários padrão:
  - início: `11:00`
  - fim: `23:00`
- mudar status para `Aberto`

### Ao desmarcar um dia aberto
Se o dia estiver aberto e o usuário desativá-lo:
- remover o check;
- substituir a área de horários por:
  - `Loja fechada`
  - status `Fechado`

---

## 2. Aplicar horário rápido
### Função observada
Existe uma função equivalente a:

```js
setTimes(start, end, applyTo = ['seg-qui', 'sex-sab'])
