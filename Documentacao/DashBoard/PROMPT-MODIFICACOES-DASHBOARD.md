# Prompt de modificação — Dashboard Brasa Burguer

> Envie este documento à IA responsável por modificar o projeto existente.
>
> Este documento é uma **revisão incremental** da especificação anterior do Dashboard Brasa Burguer em Angular 20 + Ionic. Em qualquer conflito, as instruções deste arquivo têm prioridade.

---

## 1. Papel e objetivo

Atue como desenvolvedor sênior de Angular e Ionic, com atenção a UI/UX, acessibilidade e manutenção de código.

Modifique o projeto existente do **Dashboard Brasa Burguer**. Não recrie a aplicação do zero e não substitua a arquitetura atual sem necessidade.

O objetivo é:

1. tornar editáveis as informações do estabelecimento;
2. remover a seção de redes sociais;
3. remover a exportação de clientes;
4. implementar corretamente as ações Visualizar e Editar cliente;
5. remover a tela/opção independente de Adicionais;
6. remover atalhos de entregas do Dashboard;
7. simplificar o menu lateral;
8. remover as telas dedicadas de Entregas e Instalar;
9. preservar os conceitos de entrega dentro de Pedidos e de opções dentro de Produtos.

---

## 2. Restrições técnicas

Preserve a base tecnológica existente:

- Angular 20.3.x;
- Ionic Angular 8.8.x;
- componentes standalone;
- TypeScript strict;
- SCSS;
- Angular Router;
- Angular Signals;
- Reactive Forms tipados;
- persistência local já usada no projeto;
- componentes Ionic importados de `@ionic/angular/standalone`;
- `ChangeDetectionStrategy.OnPush`.

Não adicionar:

- Bootstrap;
- jQuery;
- NgRx;
- biblioteca de dashboard;
- nova dependência apenas para realizar alterações simples.

Antes de editar:

1. inspecione a árvore atual;
2. identifique as rotas, componentes, stores e testes realmente existentes;
3. adapte os nomes abaixo à estrutura real do projeto;
4. preserve alterações do usuário não relacionadas a esta solicitação.

---

## 3. Regra de escopo mais importante

Três telas/opções independentes serão removidas:

- Entregas;
- Instalar;
- Adicionais.

Isso **não** autoriza remover:

- status de entrega dos pedidos;
- endereço, taxa ou previsão de entrega de um pedido;
- transições “Saiu para entrega”, “Em entrega” e “Concluído”;
- grupos de opções configurados dentro de um produto;
- tamanhos, bebidas e complementos associados diretamente ao produto;
- infraestrutura PWA já configurada, quando usada pelo projeto.

Em resumo:

| Remover | Preservar |
|---|---|
| Tela dedicada de Entregas | Fluxo de entrega dentro de Pedidos |
| Tela/página Instalar | Manifest e service worker já existentes |
| CRUD independente de Adicionais | Grupos de opções no editor de Produto |

---

## 4. Resumo das alterações

| Área | Alteração obrigatória |
|---|---|
| Configurações > Informações | Tornar todos os dados cadastrais editáveis |
| Configurações > Informações | Remover completamente o card Redes sociais |
| Clientes | Remover botão Exportar |
| Clientes | Olho abre detalhes somente leitura |
| Clientes | Lápis abre formulário de edição |
| Cardápio | Remover aba, tela e rota de Adicionais |
| Dashboard | Remover atalhos Acompanhar entregas e Gerenciar entregas |
| Menu lateral | Remover seta do status da loja |
| Menu lateral | Remover Entregas |
| Menu lateral | Remover Instalar |
| Router | Redirecionar URLs antigas para rotas válidas |

---

## 5. Configurações — Informações do estabelecimento

Rota esperada:

```text
/app/configuracoes/informacoes
```

### 5.1 Tornar o formulário editável

Localize a página e substitua valores estáticos ou inputs bloqueados por um `FormGroup` tipado.

Campos esperados:

- nome do estabelecimento;
- link de acesso/slug;
- WhatsApp;
- CNPJ;
- CEP;
- rua;
- número;
- complemento;
- bairro;
- cidade;
- estado;
- demais campos cadastrais já existentes na seção.

Exemplo de tipo:

```ts
export interface StoreInformationFormValue {
  name: string;
  slug: string;
  whatsapp: string;
  cnpj: string;
  zipCode: string;
  street: string;
  number: string;
  complement: string;
  neighborhood: string;
  city: string;
  state: string;
}
```

Exemplo de formulário:

```ts
readonly form = new FormGroup({
  name: new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(100)],
  }),
  slug: new FormControl('', {
    nonNullable: true,
    validators: [
      Validators.required,
      Validators.pattern(/^[a-z0-9]+(?:-[a-z0-9]+)*$/),
    ],
  }),
  whatsapp: new FormControl('', {
    nonNullable: true,
    validators: [Validators.required],
  }),
  cnpj: new FormControl('', {
    nonNullable: true,
    validators: [Validators.required],
  }),
  zipCode: new FormControl('', {
    nonNullable: true,
    validators: [Validators.required],
  }),
  street: new FormControl('', {
    nonNullable: true,
    validators: [Validators.required],
  }),
  number: new FormControl('', {
    nonNullable: true,
    validators: [Validators.required],
  }),
  complement: new FormControl('', { nonNullable: true }),
  neighborhood: new FormControl('', {
    nonNullable: true,
    validators: [Validators.required],
  }),
  city: new FormControl('', {
    nonNullable: true,
    validators: [Validators.required],
  }),
  state: new FormControl('', {
    nonNullable: true,
    validators: [
      Validators.required,
      Validators.pattern(/^[A-Z]{2}$/),
    ],
  }),
});
```

Adapte as validações aos utilitários já existentes. Não duplique máscaras ou validadores que o projeto já possua.

### 5.2 Carregamento, salvamento e cancelamento

Ao iniciar:

- carregar os dados atuais do store/serviço;
- preencher o formulário com `reset()` ou `patchValue()`;
- guardar uma cópia do último estado salvo;
- manter o formulário como `pristine` após a carga.

Ao salvar:

1. marcar os campos tocados se o formulário estiver inválido;
2. focar o primeiro campo inválido;
3. normalizar os valores sem destruir a formatação de exibição;
4. persistir pelo store/serviço existente;
5. atualizar a cópia do último estado salvo;
6. marcar o formulário como `pristine`;
7. mostrar `ion-toast`:

```text
Informações do estabelecimento atualizadas com sucesso.
```

Ao cancelar:

- restaurar o último estado salvo;
- limpar erros transitórios;
- marcar o formulário como `pristine`;
- não persistir alterações.

Durante o salvamento:

- desabilitar ações conflitantes;
- mostrar spinner no botão;
- impedir envio duplicado.

Se houver guard para alterações não salvas, manter o formulário integrado ao `CanDeactivate`.

### 5.3 Remover Redes sociais

Remover do template:

- card com título “Redes sociais”;
- Instagram;
- Facebook;
- TikTok;
- Site;
- respectivos ícones e separadores.

Remover do código da página:

- form controls exclusivos da seção;
- computed signals exclusivos da seção;
- handlers;
- imports;
- validações;
- estilos;
- testes exclusivos.

Não é obrigatório apagar propriedades de domínio compartilhadas se elas forem usadas por outra tela. Porém, nenhuma referência de redes sociais deve continuar visível ou editável nesta página.

### 5.4 Reorganizar o layout

Depois de remover o card lateral:

- expandir “Informações do estabelecimento” para toda a largura útil;
- não manter uma coluna vazia;
- usar duas colunas de campos no desktop quando houver espaço;
- usar uma coluna no mobile;
- preservar logo, banner, status ou outras seções não citadas;
- manter os mesmos tokens de card, espaçamento e tipografia.

Exemplo de SCSS:

```scss
.store-information-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 20px;
}

.field--full {
  grid-column: 1 / -1;
}

@media (max-width: 767px) {
  .store-information-grid {
    grid-template-columns: 1fr;
  }

  .field--full {
    grid-column: auto;
  }
}
```

---

## 6. Clientes

Rota esperada:

```text
/app/clientes
```

### 6.1 Remover Exportar

Remover da barra de filtros:

- botão “Exportar”;
- ícone de download;
- callback;
- gerador de CSV;
- serviço/import usado somente por exportação;
- teste exclusivo de exportação.

Não remover:

- busca;
- filtro por status;
- ordenação;
- paginação.

Reorganizar os controles restantes para ocupar a largura. Não deixar uma coluna ou `gap` reservado ao botão antigo.

### 6.2 Ação do ícone de olho — Visualizar cliente

O botão de olho deve executar:

```ts
viewCustomer(customer: Customer): Promise<void>
```

Comportamento:

- abrir `ion-modal` ou painel equivalente;
- apresentar dados em modo somente leitura;
- não reutilizar o formulário de edição em estado aparentemente desabilitado;
- mostrar uma hierarquia clara de informações;
- permitir fechar por botão, `Esc` e gesto suportado pelo modal;
- devolver o foco ao botão acionador.

Conteúdo mínimo:

- nome;
- telefone;
- e-mail;
- status;
- quantidade de pedidos;
- total gasto;
- data do último pedido;
- histórico resumido, se disponível.

Textos acessíveis:

```html
<ion-button
  fill="clear"
  aria-label="Visualizar cliente Elizabeth Souza"
  title="Visualizar cliente"
>
  <ion-icon name="eye-outline" aria-hidden="true" />
</ion-button>
```

No modal:

- título “Detalhes do cliente”;
- ação “Fechar”;
- nenhuma ação Salvar;
- nenhum campo editável.

### 6.3 Ação do ícone de lápis — Editar cliente

O botão de lápis deve executar:

```ts
editCustomer(customer: Customer): Promise<void>
```

Comportamento:

- abrir modal com Reactive Form tipado;
- preencher com os valores do cliente selecionado;
- permitir alterar nome, telefone, e-mail e status;
- validar antes de salvar;
- cancelar sem mutar o objeto original;
- salvar por uma operação do store;
- atualizar a linha e métricas derivadas;
- persistir no armazenamento existente;
- mostrar toast de sucesso.

Mensagem:

```text
Cliente atualizado com sucesso.
```

Formulário mínimo:

```ts
readonly customerForm = new FormGroup({
  name: new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(100)],
  }),
  phone: new FormControl('', {
    nonNullable: true,
    validators: [Validators.required],
  }),
  email: new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.email],
  }),
  active: new FormControl(true, { nonNullable: true }),
});
```

Não edite o objeto recebido diretamente. Gere um novo objeto ao salvar.

Exemplo:

```ts
const updatedCustomer: Customer = {
  ...selectedCustomer,
  ...this.customerForm.getRawValue(),
};

this.customersStore.update(updatedCustomer);
```

Textos acessíveis:

```html
<ion-button
  fill="clear"
  aria-label="Editar cliente Elizabeth Souza"
  title="Editar cliente"
>
  <ion-icon name="create-outline" aria-hidden="true" />
</ion-button>
```

### 6.4 Regras visuais dos botões

- área clicável mínima de 44 × 44 px;
- foco visível;
- tooltip ou `title`;
- ordem: Visualizar, depois Editar;
- mesma ordem no desktop e mobile;
- ícones não devem receber foco separado do botão;
- evitar botão sem nome acessível.

---

## 7. Cardápio — remover Adicionais

Rotas que devem permanecer:

```text
/app/cardapio/categorias
/app/cardapio/produtos
```

Rota a remover/desativar:

```text
/app/cardapio/adicionais
```

### 7.1 Interface

Remover da navegação interna:

- aba “Adicionais”;
- ícone da aba;
- badge ou indicador relacionado.

O seletor do Cardápio deve mostrar apenas:

- Categorias;
- Produtos.

Reorganizar as duas opções sem deixar uma terceira coluna vazia.

### 7.2 Código e navegação

Remover:

- `AddOnsPage` ou equivalente;
- rota lazy de Adicionais;
- item de menu desktop/mobile;
- links para a tela;
- formulário “Novo adicional”;
- lista “Adicionais cadastrados”;
- store/repositório dedicado, se ele não tiver consumidores restantes;
- testes e estilos que ficaram órfãos.

Adicionar redirecionamento:

```ts
{
  path: 'cardapio/adicionais',
  pathMatch: 'full',
  redirectTo: 'cardapio/produtos',
}
```

Ajuste o caminho conforme o nível real de `children` no router.

### 7.3 Preservar opções de produto

Não remova do editor de produto:

- `ProductOptionGroup`;
- opções de seleção única/múltipla;
- tamanhos;
- adicionais/complementos configurados dentro do produto;
- limites mínimo e máximo;
- preços adicionais.

Se o store independente de Adicionais alimentar grupos de produto, migre os dados necessários para o modelo do próprio produto antes de remover o store. Não deixe referências quebradas.

---

## 8. Dashboard — atalhos rápidos

Rota:

```text
/app/dashboard
```

Na seção “Atalhos rápidos”, remover:

- “Acompanhar entregas”;
- “Gerenciar entregas”.

Manter:

- “Pedidos” → `/app/pedidos`;
- “Gerenciar cardápio” → `/app/cardapio/produtos`.

Atualizar o array/configuração que gera os atalhos, em vez de apenas esconder itens por CSS.

Exemplo:

```ts
readonly quickActions: QuickAction[] = [
  {
    label: 'Pedidos',
    icon: 'receipt-outline',
    route: '/app/pedidos',
  },
  {
    label: 'Gerenciar cardápio',
    icon: 'book-outline',
    route: '/app/cardapio/produtos',
  },
];
```

Layout:

```scss
.quick-actions-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
}

@media (max-width: 479px) {
  .quick-actions-grid {
    grid-template-columns: 1fr;
  }
}
```

Preservar as demais áreas do Dashboard, inclusive “Resumo por serviço”. O resumo pode continuar mostrando Delivery e Retirada, pois são modalidades de pedido e não um link para a tela removida.

---

## 9. Menu lateral

O menu final deve conter:

### Grupo Menu

- Dashboard;
- Pedidos;
- Cardápio;
- Clientes.

### Grupo Sistema

- Mensalidade;
- Configurações.

### Rodapé

- Suporte;
- Sair.

Aplicar a mesma composição:

- no `ion-split-pane` desktop;
- no `ion-menu` mobile;
- em qualquer array único que alimente ambas as versões.

### 9.1 Status da loja não é botão

No card:

```text
Loja aberta
Recebendo pedidos
```

Remover:

- seta/chevron à direita;
- `routerLink`;
- evento de clique;
- `role="button"`;
- `tabindex="0"` adicionado apenas por interatividade;
- hover de botão;
- cursor `pointer`;
- ripple.

Preservar:

- ponto verde;
- título e subtítulo;
- aparência de card informativo.

Semântica sugerida:

```html
<section
  class="store-status"
  role="status"
  aria-live="polite"
  aria-label="Status da loja: aberta e recebendo pedidos"
>
  <span class="store-status__dot" aria-hidden="true"></span>
  <div>
    <strong>Loja aberta</strong>
    <span>Recebendo pedidos</span>
  </div>
</section>
```

Se o status puder mudar em tempo real, mantenha o texto calculado por signal.

### 9.2 Remover Entregas

Remover:

- item de menu Entregas;
- rota/página dedicada;
- links de navegação;
- imports;
- preload;
- breadcrumbs;
- teste de navegação exclusivo;
- atalhos do Dashboard citados na seção anterior.

Redirecionar a URL antiga:

```ts
{
  path: 'entregas',
  pathMatch: 'full',
  redirectTo: 'pedidos',
}
```

Não remover os estados de entrega do domínio `Order`.

### 9.3 Remover Instalar

Remover:

- item de menu Instalar;
- página “Instalar aplicativo”;
- rota;
- links internos;
- teste da página removida.

Redirecionar a URL antiga:

```ts
{
  path: 'instalar',
  pathMatch: 'full',
  redirectTo: 'dashboard',
}
```

Se manifest e service worker já estiverem configurados, preserve-os. Não remova a capacidade técnica PWA somente porque a página deixou de existir.

---

## 10. Router esperado

A estrutura final deve equivaler a:

```ts
export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () =>
      import('./features/login/pages/login.page')
        .then((m) => m.LoginPage),
  },
  {
    path: 'app',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./layout/app-shell/app-shell.component')
        .then((m) => m.AppShellComponent),
    children: [
      {
        path: 'dashboard',
        loadComponent: () =>
          import('./features/dashboard/pages/dashboard.page')
            .then((m) => m.DashboardPage),
      },
      {
        path: 'pedidos',
        loadComponent: () =>
          import('./features/orders/pages/orders.page')
            .then((m) => m.OrdersPage),
      },
      {
        path: 'cardapio/produtos',
        loadComponent: () =>
          import('./features/menu/products/pages/products.page')
            .then((m) => m.ProductsPage),
      },
      {
        path: 'cardapio/categorias',
        loadComponent: () =>
          import('./features/menu/categories/pages/categories.page')
            .then((m) => m.CategoriesPage),
      },
      {
        path: 'clientes',
        loadComponent: () =>
          import('./features/customers/pages/customers.page')
            .then((m) => m.CustomersPage),
      },
      {
        path: 'mensalidade',
        loadComponent: () =>
          import('./features/subscription/pages/subscription.page')
            .then((m) => m.SubscriptionPage),
      },

      // Manter aqui as cinco rotas de Configurações já existentes.

      {
        path: 'entregas',
        pathMatch: 'full',
        redirectTo: 'pedidos',
      },
      {
        path: 'instalar',
        pathMatch: 'full',
        redirectTo: 'dashboard',
      },
      {
        path: 'cardapio/adicionais',
        pathMatch: 'full',
        redirectTo: 'cardapio/produtos',
      },
      {
        path: '',
        pathMatch: 'full',
        redirectTo: 'dashboard',
      },
    ],
  },
  { path: '**', redirectTo: 'app/dashboard' },
];
```

Não copie esse trecho sem adaptar os imports e níveis de rota ao projeto real.

Rotas funcionais finais:

| Rota | Tela |
|---|---|
| `/login` | Login |
| `/app/dashboard` | Dashboard |
| `/app/pedidos` | Pedidos |
| `/app/cardapio/produtos` | Produtos |
| `/app/cardapio/categorias` | Categorias |
| `/app/clientes` | Clientes |
| `/app/mensalidade` | Mensalidade |
| `/app/configuracoes/horarios` | Horários |
| `/app/configuracoes/informacoes` | Informações |
| `/app/configuracoes/impressao` | Impressão |
| `/app/configuracoes/bio` | Bio |
| `/app/configuracoes/bairros` | Bairros |

---

## 11. Limpeza técnica

Depois de alterar a interface:

1. execute busca global por:
   - `entregas`;
   - `instalar`;
   - `adicionais`;
   - `export`;
   - `social`;
2. analise cada ocorrência antes de apagar;
3. remova apenas código que ficou realmente sem consumidor;
4. preserve tipos compartilhados usados por Pedidos e Produtos;
5. remova imports não usados;
6. remova estilos órfãos;
7. atualize breadcrumbs e títulos;
8. atualize seeds caso apontem para páginas removidas;
9. atualize testes e snapshots;
10. não esconda funcionalidades removidas apenas com `display: none`.

Não deixar:

- links quebrados;
- rota carregando componente inexistente;
- item oculto ainda acessível por teclado;
- coluna vazia;
- botão sem função;
- store órfão inicializado no app;
- código comentado como substituto de remoção;
- erros no console.

---

## 12. Testes obrigatórios

### Menu e rotas

- menu desktop não contém Entregas nem Instalar;
- menu mobile não contém Entregas nem Instalar;
- status da loja não é link nem botão;
- status não possui chevron;
- `/app/entregas` redireciona para `/app/pedidos`;
- `/app/instalar` redireciona para `/app/dashboard`;
- `/app/cardapio/adicionais` redireciona para Produtos.

### Configurações

- formulário carrega dados atuais;
- todos os campos previstos são editáveis;
- valores inválidos impedem salvar;
- salvar persiste;
- cancelar restaura;
- redes sociais não aparecem;
- layout não deixa coluna vazia.

### Clientes

- botão Exportar não existe;
- olho abre detalhes somente leitura;
- modal de detalhes fecha corretamente;
- lápis abre valores atuais;
- cancelar edição não altera o cliente;
- salvar edição atualiza a lista;
- alteração permanece após recarregar;
- botões têm nomes acessíveis.

### Cardápio

- somente Categorias e Produtos aparecem nas abas;
- não existe link para a página independente de Adicionais;
- editor de produto ainda permite grupos de opções;
- produtos já configurados mantêm suas opções.

### Dashboard

- somente Pedidos e Gerenciar cardápio aparecem em Atalhos rápidos;
- ambos navegam corretamente;
- layout funciona em 390 px e 1366 px;
- Resumo por serviço permanece.

---

## 13. Validação manual

Testar pelo menos:

1. entrar no painel;
2. abrir Configurações > Informações;
3. editar nome e WhatsApp;
4. cancelar e confirmar restauração;
5. editar novamente e salvar;
6. recarregar e confirmar persistência;
7. abrir Clientes;
8. visualizar um cliente pelo olho;
9. editar o mesmo cliente pelo lápis;
10. confirmar que Exportar não aparece;
11. abrir Cardápio e confirmar somente duas abas;
12. editar produto com grupo de opções;
13. abrir Dashboard e testar os dois atalhos restantes;
14. verificar o menu desktop;
15. verificar o menu mobile;
16. acessar as três URLs antigas e confirmar os redirecionamentos.

Larguras mínimas:

- 390 × 844;
- 768 × 1024;
- 1366 × 768.

---

## 14. Critérios de aceite

- [ ] Informações do estabelecimento são editáveis.
- [ ] Salvar persiste os dados.
- [ ] Cancelar restaura os dados.
- [ ] Redes sociais foi removido da tela e do layout.
- [ ] Exportar foi removido de Clientes.
- [ ] O olho visualiza dados sem edição.
- [ ] O lápis edita e persiste dados.
- [ ] Os dois botões de cliente são acessíveis.
- [ ] Adicionais foi removido da navegação e das rotas funcionais.
- [ ] Grupos de opções de Produto continuam funcionando.
- [ ] Atalhos de entregas foram removidos do Dashboard.
- [ ] Pedidos e Gerenciar cardápio continuam no Dashboard.
- [ ] A seta do status da loja foi removida.
- [ ] O status da loja não é interativo.
- [ ] Entregas foi removido de todos os menus.
- [ ] Instalar foi removido de todos os menus.
- [ ] Rotas antigas redirecionam corretamente.
- [ ] Não há espaços vazios causados pelas remoções.
- [ ] Desktop e mobile apresentam a mesma navegação.
- [ ] Build finaliza sem erros.
- [ ] Testes passam.
- [ ] Console não apresenta erros.

---

## 15. Comandos de verificação

Use os scripts já definidos no `package.json`. No mínimo:

```bash
npm install
npm run build
npm test
```

Para revisão local:

```bash
npx ionic serve
```

Se houver lint configurado:

```bash
npm run lint
```

Não declare a tarefa concluída se build ou testes estiverem falhando por causa das alterações.

---

## 16. Relatório final esperado da IA

Ao terminar, informe:

1. arquivos alterados;
2. componentes/rotas removidos;
3. redirecionamentos adicionados;
4. funcionamento das ações Visualizar e Editar cliente;
5. mecanismo de persistência usado;
6. testes executados e resultados;
7. qualquer decisão tomada por diferença entre esta especificação e a estrutura real.

Não entregue apenas screenshots. Entregue o código funcional e validado.
