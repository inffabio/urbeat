# 08 — Logo Centralizado e Política de Mídia (Cloudinary)

Duas decisões que valem para o front da loja e para o backend.

---

## 1. Logo da loja centralizado

### 1.1 Decisão

A **logo da loja passa a ser centralizada** (topo do painel da loja, sobreposta ao banner, alinhada ao **centro**), substituindo o alinhamento à direita atual. Isso vale para:

1. **Front da loja do cliente** (tela do cardápio).
2. **Todos os previews de pré-visualização da loja**, especialmente:
   - **Primeira tela do cadastro da loja** (preview lateral "Preview da sua loja").
   - **Tela de Publicar** (preview do resumo).
   - Qualquer outra tela que exiba a pré-visualização da loja.

> Objetivo: consistência visual entre o que o lojista vê no preview e o que o cliente vê na loja publicada. O protótipo NovaVersaoFront já adota a **logo centralizada** (círculo sobre o banner, `left: 50%; transform: translateX(-50%)`).

### 1.2 Estado atual (a corrigir)

| Local | Arquivo | Situação atual |
|---|---|---|
| Front do cliente | `frontend/src/app/features/store/store-page.component.scss` (`.logo-wrapper`, ~linha 24) | **À direita** (`right: var(--space-5)`). |
| Preview — cadastro (1ª tela) | `frontend/src/app/features/store-config/store-config-page.component.html` (`.store-logo-wrap`, ~linha 262) + scss | Conferir/alinhar ao **centro**. |
| Preview — publicar | `frontend/src/app/features/store-config/publish/store-publish-page.component.html` (`.store-logo-wrap`, ~linha 317) + scss | Conferir/alinhar ao **centro**. |

### 1.3 Mudança de CSS necessária

No front do cliente (`store-page.component.scss`), trocar o alinhamento do `.logo-wrapper`:

```scss
/* De (direita): */
.logo-wrapper {
  position: absolute;
  right: var(--space-5);
  bottom: calc(var(--logo-overlap) * -1);
  /* ... */
}

/* Para (centralizado): */
.logo-wrapper {
  position: absolute;
  left: 50%;
  transform: translateX(-50%);
  bottom: calc(var(--logo-overlap) * -1);
  /* ... */
}
```

E, como o logo deixa de ficar no canto, o cabeçalho da loja (nome, tipo de comida, métricas) deve ficar **centralizado** abaixo da logo — alinhar `.store-info`/`.store-name` com `text-align: center`.

Nos previews (`store-config` e `publish`), aplicar o mesmo: `.store-logo-wrap` centralizado (`margin-inline: auto` / `justify-content: center`) e textos do cabeçalho centralizados, replicando o layout do cliente.

### 1.4 Checklist de aplicação
- [ ] `store-page` (cliente): logo centralizada + cabeçalho centralizado.
- [ ] Preview do cadastro (1ª tela): logo centralizada.
- [ ] Preview do publicar: logo centralizada.
- [ ] Qualquer outro preview que venha a existir deve seguir o mesmo padrão (logo central).

---

## 2. Política de exclusão de mídia no Cloudinary

### 2.1 Regra geral (obrigatória)

**Toda imagem removida do sistema deve ser removida também do Cloudinary.** Nunca deixar arquivos órfãos. Isso inclui:

- Trocar a logo/banner da loja → apagar a imagem antiga.
- Trocar a foto de um produto → apagar a antiga.
- Excluir um produto → apagar a imagem do produto.
- **Excluir a loja completamente → apagar TODAS as imagens da loja** (logo, banner e imagens de todos os produtos).

O serviço responsável já existe: `IImageUploadService.DeleteAsync(imageUrl)` → `CloudinaryImageUploadService` (extrai o `publicId` da URL e chama `Cloudinary.DestroyAsync`).

### 2.2 O que já está implementado

| Ação | Onde | Apaga no Cloudinary? |
|---|---|---|
| Atualizar produto (troca de imagem) | `ProductService` (~linha 195/279) | ✅ apaga a antiga |
| Upload de imagem de produto (substituição) | `ProductService` (~linha 345) | ✅ apaga a antiga |
| Excluir produto | `ProductService.DeleteAsync` (~linha 373) | ✅ apaga a do produto |
| Atualizar loja (troca de logo/banner) | `StoreService` (~linha 194/198) | ✅ apaga a antiga |

> Observação: todas as chamadas são **best-effort** (envolvidas em `try/catch`), para que uma falha no Cloudinary não quebre a operação principal. Recomenda-se **logar** as falhas (o serviço já faz `LogWarning`).

### 2.3 Gap a implementar: exclusão completa da loja

**Hoje não existe** endpoint/serviço para **apagar a loja inteira**, portanto não há limpeza das imagens no Cloudinary nesse cenário. Quando essa funcionalidade for criada (ou quando for solicitado "apagar a loja completamente"), ela **deve**:

1. Coletar todas as URLs de imagem da loja:
   - `Store.LogoUrl`, `Store.BannerUrl`.
   - `Product.ImageUrl` de **todos** os produtos da loja.
   - (Se houver outras mídias no futuro, incluí-las.)
2. Chamar `IImageUploadService.DeleteAsync(url)` para **cada** uma (best-effort, com log de falhas).
3. Só então remover os registros do banco (produtos, categorias, endereço, horários, áreas de entrega, a própria loja).

**Alternativa recomendada (mais robusta):** organizar os uploads em uma **pasta por loja** no Cloudinary (ex.: `stores/{storeId}/...`) e, na exclusão total, **apagar a pasta inteira** por prefixo (`DeleteResourcesByPrefix` + `DeleteFolder`). Isso evita depender de varrer URL por URL e garante que nada fique órfão.

### 2.4 Especificação sugerida (backend)

```
StoreService.DeleteStoreAsync(ownerUserId, storeId):
  1. valida propriedade (owner) da loja
  2. coleta imagens: logo, banner, imagens de todos os produtos
  3. para cada URL: try { _imageUploadService.DeleteAsync(url) } catch { log }
     (ou: _imageUploadService.DeleteFolderAsync($"stores/{storeId}") )
  4. remove em cascata: produtos, categorias, endereço, horários, áreas de entrega, pagamento, loja
  5. auditar (AuditLog) a exclusão
```

Sugere-se adicionar ao `IImageUploadService` um método utilitário para exclusão em lote/pasta:
```
Task DeleteManyAsync(IEnumerable<string> imageUrls, CancellationToken ct);
Task DeleteFolderAsync(string folder, CancellationToken ct); // opcional (por prefixo)
```

### 2.5 Testes exigidos (AGENTS.md)
- Excluir produto → `DeleteAsync` chamado com a URL do produto.
- Trocar logo/banner/foto → `DeleteAsync` chamado com a URL **antiga**.
- Excluir loja completa → `DeleteAsync` chamado para logo, banner e **cada** imagem de produto (ou `DeleteFolderAsync` da pasta da loja); e os registros removidos do banco.
- Falha no Cloudinary não deve interromper a operação (best-effort) — validar que a exclusão no banco ocorre mesmo assim.
