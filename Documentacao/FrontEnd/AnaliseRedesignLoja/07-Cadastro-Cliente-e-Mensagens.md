# 07 — Cadastro do Cliente (Front da Loja) e Princípio de Mensagens

Especificação do **novo cadastro do cliente no checkout** (front da loja) e do **princípio único de mensagens/toasts** para todo o sistema. Esta é a referência para recriar/modificar a tela.

Arquivos envolvidos:
- `frontend/src/app/features/checkout/customer-page.component.ts` / `.html` / `.scss`
- `frontend/src/app/core/services/toast.service.ts` (serviço compartilhado — vale para o sistema inteiro)
- `frontend/src/theme/global.scss` (estilos do toast)

---

## 1. Cadastro do cliente no checkout

### 1.1 Objetivo

Coletar, em uma única tela, os **dados de contato** e o **endereço de entrega** do cliente que está comprando, com o mínimo de atrito. O cadastro/login do cliente acontece de forma transparente (senha derivada do telefone) ao concluir.

### 1.2 Organização em duas seções

A tela é dividida em blocos claros, com títulos:

1. **Seus dados** — nome, telefone, e-mail.
2. **Endereço de entrega** — **CEP primeiro**, depois os campos preenchidos automaticamente.

### 1.3 Crítica dos campos e obrigatoriedade

| Campo | Seção | Obrigatório | Regra / crítica |
|---|---|---|---|
| **Nome completo** | Seus dados | ✅ | mínimo 3 caracteres. Necessário para identificar o pedido. |
| **Telefone (com DDD)** | Seus dados | ✅ | mínimo 10 dígitos. Contato da entrega + base da senha. Máscara `(DD) XXXXX-XXXX`. |
| **E-mail** | Seus dados | ✅ | formato válido (`regex` de e-mail, não só conter `@`). Usado para criar a conta. |
| **CEP** | Endereço | ✅ | **8 dígitos**. É o **primeiro** campo do endereço e dispara a busca automática. Máscara `XXXXX-XXX`. |
| **Cidade** | Endereço | ✅ | preenchido pelo CEP; editável. |
| **Estado (UF)** | Endereço | ✅ | **2 letras**, maiúsculas. Antes era invisível/derivado só do CEP — agora é campo próprio e validado (o backend exige UF). |
| **Bairro** | Endereço | ✅ | preenchido pelo CEP; editável. |
| **Rua** | Endereço | ✅ | preenchido pelo CEP; editável. |
| **Número** | Endereço | ✅ | sempre digitado pelo cliente (CEP não traz número). `inputmode=numeric`. |
| **Complemento** | Endereço | ⬜ (opcional) | placeholder "Complemento (opcional)". |

> **Correção principal da crítica:** o **Estado (UF)** passa a ser um campo obrigatório e visível. Antes ele só era setado quando o CEP era encontrado; em preenchimento manual (CEP não localizado) ficava vazio e o pedido podia ir sem UF.

### 1.4 CEP primeiro (busca de endereço pela API do backend)

- O **CEP é o primeiro campo** do bloco de endereço, com a instrução: *"Digite o CEP para buscarmos seu endereço automaticamente."*
- Ao completar **8 dígitos**, dispara `AddressService.lookupCep(cep)` (API correta do backend → `GET /api/address-lookup/cep/{cep}`, que consulta o ViaCEP).
- Em sucesso: preenche **Cidade, UF, Bairro, Rua** e marca `cepValidated`.
- Em falha: exibe aviso inline *"CEP não encontrado. Preencha o endereço manualmente."* e permite edição livre.
- Um spinner aparece no campo durante a busca (`cepLoading`).

### 1.5 Validação na conclusão (agrupada)

Ao tocar em **Continuar**, o botão **não fica desabilitado** — o clique dispara a validação e, se houver problemas, **todos são exibidos juntos** em uma única mensagem (ver §2). Mensagens propostas:

| Situação | Sinal | Texto |
|---|---|---|
| Nome < 3 | ❌ error | Informe seu nome completo. |
| Telefone < 10 dígitos | ❌ error | Informe um telefone com DDD. |
| E-mail inválido | ❌ error | Informe um e-mail válido. |
| CEP ≠ 8 dígitos | ❌ error | Informe um CEP válido (8 dígitos). |
| CEP não localizado (mas 8 dígitos) | ⚠️ warning | CEP não localizado: confira cidade, bairro e rua. |
| Cidade vazia | ❌ error | Informe a cidade. |
| UF vazia | ❌ error | Informe o estado (UF). |
| Bairro vazio | ❌ error | Informe o bairro. |
| Rua vazia | ❌ error | Informe a rua. |
| Número vazio | ❌ error | Informe o número. |

Erros de servidor (cadastro/endereço) usam toast simples:
- Falha ao criar conta → *"Não foi possível concluir seu cadastro. Verifique os dados e tente novamente."*
- Falha ao salvar endereço → *"Não foi possível salvar o endereço. Tente novamente."*

### 1.6 Fluxo ao concluir

1. Validação agrupada (acima). Se ok, segue.
2. Salva `customerInfo` e `customerAddress` no `CheckoutService`.
3. `register` do cliente (senha `Urbeat@<telefone>`); se já existir, cai no `login` (fallback).
4. Cria o endereço (`AddressService.create`).
5. Navega para `/{loja}/checkout/pagamento`.

---

## 2. Princípio de mensagens (vale para TODO o sistema)

### 2.1 Problema observado

Quando várias mensagens são disparadas em sequência, os toasts aparecem **na mesma posição (topo)** e a **última cobre a anterior** — o cliente não lê os erros anteriores.

### 2.2 Solução adotada (padrão do sistema)

Centralizada no **`ToastService`** (compartilhado), portanto o mesmo comportamento vale para qualquer tela:

1. **Uma caixa única para vários erros** — em vez de N toasts, agrupar tudo em **uma** mensagem (`showGrouped`).
2. **Sinal por linha** — cada linha começa com um ícone conforme o tipo:
   - ❌ **error**
   - ⚠️ **warning**
   - ✅ **success/ok**
   - ℹ️ **info**
3. **Cor da caixa pela severidade máxima** — se há qualquer `error`, a caixa é vermelha; senão amarela (warning); etc.
4. **Timer maior** — mensagens agrupadas duram **20 segundos** por padrão (mais texto para ler). Mensagens simples seguem em 4s.
5. **Sem sobreposição** — antes de exibir uma nova mensagem, a **anterior é fechada** (o serviço guarda o toast atual e faz `dismiss`), garantindo sempre uma única caixa na tela.
6. **Botão fechar (X)** disponível para dispensar antes do tempo.

### 2.3 API do ToastService

```ts
// Mensagem simples (4s, fecha a anterior)
toast.showError('...'); toast.showSuccess('...'); toast.showWarning('...'); toast.showInfo('...');

// Mensagem agrupada — várias linhas em uma caixa (20s por padrão)
toast.showGrouped([
  { type: 'error',   text: 'Informe seu nome completo.' },
  { type: 'error',   text: 'Informe um CEP válido (8 dígitos).' },
  { type: 'warning', text: 'CEP não localizado: confira cidade, bairro e rua.' },
]);
// duração customizável: toast.showGrouped(linhas, 15000)
```

Exemplo do que o cliente vê (uma caixa só, cor de erro):
```
❌ Informe seu nome completo.
❌ Informe um CEP válido (8 dígitos).
⚠️ CEP não localizado: confira cidade, bairro e rua.
```

### 2.4 Estilo (global.scss)

Classe `urbeat-toast-grouped` aplica **`white-space: pre-line`** (respeita as quebras de linha `\n`), alinhamento à esquerda e remove o limite de altura para caber várias linhas. As cores por tipo (`urbeat-toast-error/warning/success/info`) já existiam.

---

## 3. Estado da implementação

**Já implementado nesta rodada:**
- `toast.service.ts`: fecha a mensagem anterior antes de abrir a nova; novo método `showGrouped(lines, duration=20000)` com sinal por linha e cor pela severidade; tipos `ToastType`/`ToastLine` exportados.
- `global.scss`: estilos `.urbeat-toast-grouped` (multi-linha).
- `customer-page.component.ts`: validação agrupada (`validate()` → `showGrouped`), e-mail com regex, UF na validação, toasts de erro no fluxo de servidor.
- `customer-page.component.html`: seções "Seus dados" e "Endereço de entrega" (CEP primeiro), campo **UF** adicionado, botão Continuar sem `disabled` (para permitir a validação agrupada).

**Pendências de implementação (para quem for finalizar):**
- Adicionar ao `customer-page.component.scss` os estilos das novas classes usadas no HTML: `.form-section`, `.form-hint`, `.field-row`, `.field-grow`, `.field-uf`.
- Atualizar `toast.service.spec.ts`: o mock do `ToastController.create` deve retornar também `dismiss` e `onDidDismiss` (o serviço agora os utiliza) e incluir teste de `showGrouped` (mensagem única com as linhas + duração 20000).
- Rodar `npx jest --no-coverage` e `npx ng build` para validar.

---

## 4. Aplicação do princípio em outras telas

O `showGrouped` deve substituir sequências de `showError` em qualquer formulário com múltiplas validações, por exemplo:
- **Cadastro de produto** (nome, categoria, preço, imagem, min/max de grupos) — hoje dispara vários toasts.
- **Configuração da loja** (dados, endereço, tempos, entrega) — validações do wizard.
- **Login/registro do vendedor**.

Regra geral: **se o clique pode gerar mais de um erro, use `showGrouped`**; se é um único aviso pontual, use `showError/Success/Warning/Info`.
