# 🧩 Especificação Complementar — Modal de Inclusão de Categoria da Loja (Cuisine Type), Correções de Upload e Padronização de URL (Slug)
## Cadastro de Loja — Seção 1: Informações da Loja e Seção 2: Identidade Visual

---

# 🎯 Objetivo

1. Ajustar o comportamento do botão `...` ao lado do campo **Categoria da Loja** na tela de configuração inicial da loja (`/configurar-loja`), padronizando a experiência do usuário com validação rigorosa de duplicidade e layout otimizado.
2. Corrigir o comportamento de visualização e upload das imagens de **Logo** e **Banner** da loja, garantindo que não sejam cortadas e que o upload seja realizado com sucesso.
3. **Padronização de URL (SEO)**: Remover o campo redundante `storePath` e utilizar exclusivamente o campo `slug` em **kebab-case** (ex: `pizza-hunter`) como identificador canônico para roteamento, seguindo as melhores práticas de SEO (Search Engine Optimization).

---

# 🌐 Padronização de URL (Slug em Kebab-case)

## 1. Comportamento no Frontend
Quando o usuário digita o nome da loja (ex: "Pizza Hunter"), o sistema aplica automaticamente as seguintes transformações para gerar o `slug`:
1. Conversão para minúsculas.
2. Remoção de acentos e caracteres diacríticos (normalização NFD).
3. Substituição de espaços e caracteres não alfanuméricos por hífens (`-`).
4. Remoção de hífens no início ou no final da string.
5. Limitação a 80 caracteres.
*Resultado:* "Pizza Hunter" se torna `pizza-hunter`.

## 2. Comportamento no Backend
- O campo `StorePath` foi completamente removido da entidade `Store`, dos DTOs e dos repositórios.
- O campo `Slug` é o único identificador único e canônico para a rota da loja (ex: `urbeat.com.br/pizza-hunter`).
- O banco de dados possui um índice único (`UNIQUE INDEX`) no campo `Slug` para garantir a integridade e evitar duplicatas.
- O uso de hífens (kebab-case) é a prática recomendada pelo Google, pois os motores de busca interpretam o hífen como um separador de palavras, melhorando a indexação em comparação ao underscore (`_`).

---

# ⚠️ Regra principal de UX (Modal de Categoria)

Sempre que o usuário clicar no botão `...` ao lado de "Categoria da Loja":
1. Deve abrir um **popup/modal** centralizado na tela, com layout em grid vertical consistente.
2. O modal deve conter um campo de entrada claro, com label e placeholder.
3. Ao confirmar a inclusão, o item deve ser validado.
4. Se já existir uma categoria igual (normalizada), **não incluir** e exibir mensagem de erro.
5. Se estiver válido, incluir na lista local, selecionar automaticamente e refletir imediatamente na tela.
6. Cada item listado deve ter um **botão de exclusão (`X` / lixeira)** à direita.
7. Ao clicar na lixeira, deve ser exibida uma **confirmação de exclusão** nativa do navegador (`confirm`).
8. Confirmando, o item deve ser removido da lista local.

---

# 🖼️ Correção de Upload e Visualização de Imagens

## 1. Visualização sem Corte (CSS)
As imagens de pré-visualização (Logo e Banner) devem usar `object-fit: contain` em vez de `cover`. Isso garante que a imagem inteira seja visível dentro do contêiner, sem ser cortada ou distorcida, mantendo sua proporção original. Um fundo sutil e padding interno são aplicados para melhorar a estética quando a proporção da imagem não corresponde exatamente à do contêiner.

## 2. Configuração de Upload e Otimização de Imagens (Backend/Infraestrutura)

### A. Credenciais no Oracle Vault
As credenciais do **Cloudinary** foram adicionadas ao script de provisionamento de secrets (`01-setup-vault-secrets.ps1`) e são injetadas no ambiente de produção via `03-setup-environment.ps1`:
- `CLOUDINARY_CLOUD_NAME`: `dcolnvyhb`
- `CLOUDINARY_API_KEY`: `549543485246375`
- `CLOUDINARY_API_SECRET`: `55CVhToYzFzzP2vA2Lv4FEv5Qg8`

### B. Otimização Automática de Imagens
Para evitar que imagens muito grandes consumam banda e armazenamento desnecessário, o serviço `CloudinaryImageUploadService` foi configurado para aplicar transformações automáticas durante o upload:
- **Redimensionamento Inteligente (`limit`)**: Define um limite máximo de 1920x1920 pixels. Imagens maiores são reduzidas proporcionalmente, enquanto imagens menores mantêm seu tamanho original (sem upscaling).
- **Qualidade Automática (`auto:good`)**: O Cloudinary analisa a imagem e aplica o nível de compressão ideal que reduz o tamanho do arquivo sem perda visível de resolução.
- **Formato Moderno (`auto`)**: Serve a imagem no formato mais eficiente suportado pelo navegador do cliente (ex: WebP ou AVIF), reduzindo ainda mais o peso sem alterar a extensão original da URL.

Essas otimizações ocorrem no lado do servidor (Cloudinary), garantindo que o frontend receba uma URL de imagem já otimizada e pronta para exibição rápida.

---

# 🧱 Comportamento detalhado do Modal de Categoria

## 1. Estrutura Visual
- **Apresentação:** Modal centralizado na tela (`alignment="center"`), compacto e elegante, com largura máxima de 450px, altura mínima de 450px e bordas arredondadas (16px).
- **Cabeçalho:** Título "Nova Categoria" centralizado com tipografia em negrito, e um botão de fechar com ícone "X" (`ion-icon name="close"`) alinhado à direita.
- **Layout em Grid:** O conteúdo do modal é organizado em um grid vertical (`grid-template-rows: auto auto auto 1fr`) para manter o espaçamento consistente e previsível.
- **Campo de entrada:** `ion-input` com `fill="outline"`, placeholder claro e indicador de obrigatoriedade (`*`) em destaque.
- **Dica:** Texto auxiliar abaixo do input informando que categorias duplicadas não serão permitidas.
- **Botão de ação:** "Incluir Categoria" com largura total (`expand="block"`), desabilitado visualmente enquanto o campo estiver vazio ou conter apenas espaços.
- **Pesquisa:** `ion-searchbar` estilizado com borda suave para filtrar categorias já existentes na lista.
- **Lista com Rolagem:** `ion-list` com altura máxima (`max-height: 250px`) e rolagem vertical automática (`overflow-y: auto`) quando houver muitas categorias. A lista possui uma barra de rolagem customizada e discreta.
- **Ordenação Alfabética:** Tanto a lista de categorias no modal quanto o dropdown (listbox) de seleção são ordenados alfabeticamente (pt-BR) de forma reativa.

## 2. Validações de Inclusão

### Obrigatórias
- O nome da categoria não pode estar vazio ou ser apenas espaços em branco.

### Validação de Duplicidade (Normalização)
Antes de incluir, o sistema deve verificar se já existe uma categoria igual na lista local. A comparação deve ser feita após **normalizar** ambos os nomes:
1. Converter para minúsculas (`toLowerCase()`).
2. Remover acentos/diacríticos (`.normalize('NFD').replace(/[\u0300-\u036f]/g, '')`).
3. Remover espaços no início e no fim (`trim()`).

#### Exemplo de duplicidade detectada:
Os itens abaixo devem ser considerados **duplicados** e bloqueados:
- `Hamburgueria`
- ` hamburgueria `
- `HAMBURGUERIA`
- `Hambúrgueria`

### Comportamento em caso de duplicidade
- **Não** incluir no array local.
- **Não** fechar o modal.
- Exibir mensagem de erro via `ToastService`: `"Já existe uma categoria com esse nome."`

## 3. Inclusão bem-sucedida
Ao clicar em **Incluir Categoria** e passar em todas as validações:
1. Adicionar o novo objeto `{ id: string, name: string }` ao array `cuisineTypes`.
2. Atualizar automaticamente o campo `cuisineType` do formulário com o novo valor.
3. Limpar o campo de entrada do modal.
4. Fechar o modal.
5. Exibir mensagem de sucesso via `ToastService`: `"Categoria adicionada com sucesso!"`

## 4. Exclusão de Categoria
Ao clicar no ícone de lixeira ao lado de uma categoria:
1. Exibir confirmação nativa: `"Tem certeza que deseja apagar essa categoria?"`
2. Se o usuário confirmar:
   - Remover o item do array `cuisineTypes`.
   - Se a categoria excluída era a que estava selecionada no formulário, limpar a seleção (`cuisineType.set('')`).
3. Se o usuário cancelar, nada acontece.

---

# 🛠️ Implementação Técnica (Frontend)

## Arquivos impactados
- `frontend/src/app/features/store-config/store-config-page.component.ts`
- `frontend/src/app/features/store-config/store-config-page.component.html`

## Lógica de Normalização (TypeScript)
```typescript
const normalizedName = name
  .toLowerCase()
  .normalize('NFD')
  .replace(/[\u0300-\u036f]/g, '')
  .trim();

const isDuplicate = this.cuisineTypes().some(c => 
  c.name.toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '').trim() === normalizedName
);
```

## Integração com Backend (Observação)
Atualmente, a adição de categorias é tratada localmente no estado do componente (mock/local state) para agilizar o fluxo de onboarding do vendedor. 
Em uma evolução futura, este endpoint pode ser conectado a uma API REST (`POST /api/cuisine-types`) para persistência global no banco de dados, mantendo a mesma lógica de validação de duplicidade no backend.

---

# ✅ Critérios de Aceite

- [ ] O modal abre ao clicar no botão `...` ao lado de "Categoria da Loja".
- [ ] O botão "Incluir Categoria" permanece desabilitado se o campo estiver vazio.
- [ ] Tentar adicionar "Hamburgueria" quando "hamburgueria" já existe exibe o toast de erro e não adiciona o item.
- [ ] Adicionar uma categoria válida a atualiza na lista, seleciona no dropdown e fecha o modal com toast de sucesso.
- [ ] A tecla `Enter` dentro do campo de input dispara a ação de inclusão.
- [ ] A exclusão de uma categoria pede confirmação e remove o item da lista.
- [ ] O design do modal segue o padrão visual limpo e profissional do sistema (bordas, espaçamentos, tipografia).
