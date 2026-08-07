# [MVP] [Catálogo] RF23 - Upload de imagens de produtos e logo

**Épico:** Catálogo e produtos  
**Fase:** MVP  
**Perfil:** Vendedor  
**Prioridade:** Alta  

## Descrição
Permitir enviar imagens da logomarca da loja e dos produtos.

## Regras de negócio
- Formatos permitidos:
  - JPG
  - PNG
  - WEBP
- Deve haver limite de tamanho.
- Arquivo inválido deve ser rejeitado.

## Critérios de aceite
- Imagem válida é enviada com sucesso.
- URL é salva corretamente.
- Imagem aparece no sistema.
- Upload inválido é bloqueado.

## Checklist técnico
- [x] Abstrair serviço de Storage (Criar `IImageUploadService`)
- [x] Criar endpoint de upload em formato genérico (Ex: Upload da foto do produto em `POST /api/stores/{storeId}/products/{productId}/images`) com limite via `[RequestSizeLimit]`
- [x] Validar extensão via HashSet no backend (`.jpg`, `.jpeg`, `.png`, `.webp`) e validar tamanho
- [x] Salvar URL final hospedada utilizando `CloudinaryImageUploadService`
- [x] Exibir preview no Angular

## Dependências
- RF09 - Cadastro da loja
- RF20 - Cadastro de produtos

## Próximo card sugerido
- RF20 - Cadastro de produtos
- RF10 - Edição dos dados da loja

## Observações técnicas
- Implementação substituiu storage local por Cloudinary via pacote nativo de SDK. O serviço `CloudinaryImageUploadService` foi implementado no projeto Infrastructure, e a configuração no DI mudou pra este novo provider. Os dados de login vêm de `appsettings.json`.