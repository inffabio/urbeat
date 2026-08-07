# 🚀 Especificação Funcional e Técnica — Tela de Configuração da Loja

## 🎯 Objetivo

Implementar a tela de **Configuração da Loja** como complemento do projeto existente em:

- **Backend:** `c:\projetos\urbeat\backend`
- **Frontend:** `c:\projetos\urbeat\frontend`

A funcionalidade permitirá ao lojista configurar sua loja de delivery com:

- dados básicos da loja,
- identidade visual,
- configuração rápida de operação,
- URL pública da loja,
- atualização em tempo real da pré-visualização da tela de vendas.

---

## 🧭 Visão geral da experiência

A tela será dividida em **duas colunas principais**:

### 🧱 Coluna esquerda — Formulário de configuração
Nesta área estarão todos os campos editáveis da loja.

Seções:

1. **Informações da loja**
2. **Identidade visual**
3. **Configuração rápida**
4. **URL da loja**

### 📱 Coluna direita — Preview em tempo real
Nesta área será exibido o **esqueleto da tela de vendas**, sendo atualizado conforme os campos forem preenchidos.

Importante:
- No início, o lado direito deve mostrar apenas o **layout base/skeleton**.
- À medida que os dados forem sendo informados, os elementos reais devem substituir os placeholders.
- O preview deve simular uma tela mobile de cardápio/vendas.

---

## ✅ Comportamento principal esperado

### 🔄 Atualização em tempo real
Toda informação preenchida no formulário da esquerda deve refletir imediatamente no preview da direita.

Exemplos:
- digitou o nome da loja → aparece no topo da loja no preview;
- selecionou categoria → aparece como subtítulo;
- enviou banner → aparece na área de banner do preview;
- enviou logo → aparece na logo redonda do preview;
- marcou tipo de atendimento → aparecem os indicadores correspondentes;
- informou tempo médio de entrega → aparece ao lado do status;
- definiu pedido mínimo → aparece na linha de informações;
- configurou horários → status “Aberto agora” ou “Fechado” deve refletir no preview.

---

## 🧩 Escopo funcional detalhado

# 1. Informações da Loja

## 1.1 Campos
Campos obrigatórios e comportamento esperado:

- **Nome da loja**
  - tipo: texto
  - obrigatório
  - limite sugerido: 120 caracteres
  - refletir no preview como título principal da loja

- **Categoria**
  - tipo: select
  - exemplos iniciais:
    - Hamburgueria
    - Pizzaria
    - Doceria
  - refletir no preview abaixo do nome da loja

- **WhatsApp principal**
  - tipo: texto com máscara
  - formato: telefone com DDD
  - usado para contato operacional
  - inicialmente não precisa aparecer no preview principal, mas deve ser salvo

- **Rua**
  - tipo: texto
  - salvar no endereço da loja

- **Número**
  - tipo: texto
  - aceitar número e complemento curto

- **Bairro**
  - tipo: texto

- **Cidade**
  - tipo: select
  - opções iniciais:
    - Rio de Janeiro
    - São Paulo
  - estrutura preparada para expansão futura

- **CEP**
  - tipo: texto com máscara
  - formato brasileiro

## 1.2 Reflexo no preview
O endereço pode aparecer de forma resumida em uma linha secundária, por exemplo:

- `Rua X, 123 - Bairro`
- ou apenas `Bairro • Cidade`

Caso o endereço ainda não esteja completo:
- mostrar placeholder discreto
- ou ocultar a linha até existirem dados suficientes

---

# 2. Identidade Visual

## 2.1 Upload de logo
### Regras
- formatos aceitos: `PNG`, `JPG`, `JPEG`
- tamanho máximo: `2MB`
- recorte preferencial quadrado
- exibir miniatura no formulário após upload

### Comportamento visual
- a logo deve aparecer em formato **redondo** no preview
- posicionamento sugerido:
  - sobreposta ao banner
  - alinhada à esquerda no cartão principal
- caso não exista logo:
  - mostrar círculo placeholder com fundo neutro
  - opcionalmente exibir ícone de imagem

## 2.2 Upload de banner
### Regras
- formatos aceitos: `PNG`, `JPG`, `JPEG`
- tamanho máximo: `2MB`
- dimensão recomendada: `1200x400`
- exibir preview no formulário após upload

### Comportamento visual
- ao subir o banner, ele deve aparecer imediatamente na área de **banner do preview**
- antes do upload, deve existir um placeholder skeleton
- aplicar `object-fit: cover`

## 2.3 Observação importante de UX
No lado esquerdo:
- manter o componente de upload com drag and drop
- mostrar nome do arquivo
- mostrar status:
  - enviando
  - enviado
  - erro no envio

No lado direito:
- assim que o upload concluir com sucesso, o preview já deve usar a URL retornada pelo backend

---

# 3. Configuração Rápida

## 3.1 Tipo de atendimento
Campos em formato de seleção múltipla:

- Delivery
- Retirada
- Consumo no local

### Regras
- permitir marcar 1 ou mais opções
- refletir no preview em formato de selos/chips informativos

### Exibição no preview
Exemplo:
- `Entrega • 30-40 min`
- `Retirada disponível`
- `Consumo no local`

Se mais de uma opção estiver ativa:
- priorizar `Delivery` na linha principal
- exibir as demais como chips auxiliares ou texto complementar

## 3.2 Tempo médio de entrega
### Opções iniciais
- 20-30 min
- 30-40 min
- 40-50 min
- 50-60 min

### Regras
- obrigatório caso `Delivery` esteja ativo
- refletir na linha principal de informações do preview

Exemplo:
- `Entrega • 30-40 min`

## 3.3 Pedido mínimo
### Campo
- monetário
- prefixo `R$`
- permitir digitação simples com máscara

### Regras
- obrigatório quando delivery estiver ativo
- valor salvo em decimal
- refletir no preview como:
  - `Pedido mínimo: R$ 25,00`

---

# 4. URL da Loja

## 4.1 Campo slug
Estrutura:
- prefixo fixo: `urbeat.com.br/`
- campo editável com slug da loja

### Regras
- aceitar apenas:
  - letras minúsculas
  - números
  - hífen
- remover espaços e caracteres especiais
- validar disponibilidade no backend

### Exemplo
- `urbeat.com.br/burger-house`

### Comportamento
- sugestão automática baseada no nome da loja
- permitir edição manual
- exibir status:
  - disponível
  - indisponível
  - verificando

---

## 📱 Especificação do Preview da Direita

## 1. Estrutura visual base
O preview da direita deve simular uma tela mobile de cardápio.

### Blocos do preview
1. **Topo/sistema mobile fake**
   - horário fictício
   - ícones visuais simples
   - apenas decorativo

2. **Área de banner**
   - exibe imagem de banner ou skeleton

3. **Logo circular**
   - sobreposta ao banner
   - exibe logo ou placeholder

4. **Header da loja**
   - nome da loja
   - categoria
   - avaliação mockada inicialmente
   - status operacional
   - tempo de entrega
   - pedido mínimo

5. **Abas mockadas de categorias**
   - Destaques
   - Combos
   - Burgers
   - Porções
   - Bebidas

6. **Lista de produtos skeleton**
   - inicialmente apenas estrutura visual
   - sem integração com cadastro de produtos neste momento

7. **Barra inferior do carrinho**
   - mockada e desabilitada
   - mostrar algo como `Ver carrinho • R$ 0,00`

---

## 2. Preview inicialmente em modo skeleton
Antes do usuário preencher os dados, o preview deve mostrar:

- banner cinza skeleton
- logo circular placeholder
- nome da loja com barra skeleton
- categoria com barra skeleton
- linha de status com skeleton
- cards de produtos skeleton

Objetivo:
- deixar claro que a tela de vendas está sendo montada em tempo real

---

## 3. Regra de substituição gradual do skeleton
À medida que os dados forem sendo preenchidos:

- **nome da loja** substitui o título placeholder
- **categoria** substitui subtítulo placeholder
- **banner** substitui área skeleton do topo
- **logo** substitui círculo placeholder
- **tempo de entrega** substitui informação mockada
- **pedido mínimo** substitui informação mockada
- **horários** definem o status de aberto/fechado
- **tipos de atendimento** preenchem a linha informativa

---

## ⏰ Horários e exibição no preview

Mesmo que a etapa completa de horários possa ser uma aba própria, o preview já deve estar preparado para refletir esses dados.

## Regras esperadas
Quando houver configuração de horários:
- calcular status atual com base no dia e hora local da loja
- exibir:
  - `Aberto agora`
  - `Fechado`
  - `Abre às 18:00`
  - `Fecha às 23:00`

## Se não houver horários configurados
- exibir texto neutro:
  - `Horário não configurado`
- ou ocultar o status operacional até a configuração existir

## Sugestão visual
- **Aberto agora** → selo verde
- **Fechado** → selo cinza/vermelho suave

---

## 💾 Salvamento automático

## Requisito
A tela deve possuir **autosave**.

### Comportamento esperado
- ao alterar qualquer campo, o frontend atualiza o estado local imediatamente
- após pequeno debounce, enviar atualização ao backend
- exibir status no topo:
  - `Salvando...`
  - `Salvo automaticamente`
  - `Erro ao salvar`

### Sugestão técnica
- debounce: `600ms` a `1200ms`
- uploads devem salvar imediatamente após sucesso

---

## 🏗️ Integração com Backend (.NET 9 + PostgreSQL)

## 1. Objetivo técnico
Criar suporte backend para armazenar as configurações da loja e servir os dados para o frontend e preview.

---

## 2. Entidade principal sugerida

### Tabela: `store_settings`
Responsável pela configuração geral da loja.

Campos sugeridos:

- `id` UUID PK
- `store_id` UUID FK
- `store_name` VARCHAR(120)
- `category` VARCHAR(80)
- `whatsapp` VARCHAR(20)
- `street` VARCHAR(150)
- `number` VARCHAR(20)
- `district` VARCHAR(100)
- `city` VARCHAR(100)
- `zip_code` VARCHAR(10)
- `banner_url` TEXT
- `logo_url` TEXT
- `supports_delivery` BOOLEAN
- `supports_pickup` BOOLEAN
- `supports_dine_in` BOOLEAN
- `delivery_time_min` INTEGER nullable
- `delivery_time_max` INTEGER nullable
- `minimum_order_value` NUMERIC(10,2) nullable
- `slug` VARCHAR(120)
- `is_slug_verified` BOOLEAN
- `created_at` TIMESTAMP
- `updated_at` TIMESTAMP

---

## 3. Tabela de horários sugerida

### Tabela: `store_business_hours`
Campos sugeridos:

- `id` UUID PK
- `store_id` UUID FK
- `day_of_week` SMALLINT
- `open_time` TIME
- `close_time` TIME
- `is_closed` BOOLEAN
- `created_at` TIMESTAMP
- `updated_at` TIMESTAMP

### Observação
Mesmo que a tela atual não implemente toda a aba de horários, o modelo já deve ser preparado para refletir no preview.

---

## 4. Endpoints sugeridos

### Buscar configuração atual
`GET /api/stores/{storeId}/settings`

Retorno:
```json
{
  "storeId": "uuid",
  "storeName": "Burger House",
  "category": "Hamburgueria",
  "whatsapp": "21999999999",
  "address": {
    "street": "Rua Exemplo",
    "number": "123",
    "district": "Centro",
    "city": "Rio de Janeiro",
    "zipCode": "20000-000"
  },
  "media": {
    "bannerUrl": "https://...",
    "logoUrl": "https://..."
  },
  "serviceOptions": {
    "delivery": true,
    "pickup": true,
    "dineIn": false
  },
  "deliveryTime": {
    "min": 30,
    "max": 40
  },
  "minimumOrderValue": 25.00,
  "slug": "burger-house"
}

```

---

## 5. Regrad de BackEnd

1. validar slug único
2. validar tamanho e formato de imagem
3. armazenar URLs de mídia de forma persistente
4. registrar updated_at a cada alteração
5. permitir atualização parcial sem quebrar o registro existente

--- 

## 6. Integração com Frontend

### Premissas

- A implementação deve complementar o frontend existente em:

c:\projetos\urbeat\frontend

- Sem recriar a aplicação do zero

### Estrutura sugerida de componentes

> Container principal

- StoreSetupPage

> Coluna Esquerda

- StoreSetupForm
- StoreInfoSection
- StoreMediaSection
- StoreQuickConfigSection
- StoreUrlSection

> Coluna direita

1. StorePreviewPanel

- MobileStorePreview
- PreviewHeader
- PreviewServiceInfo
- PreviewCategoryTabs
- PreviewProductSkeletonList
- PreviewCartBar

---
