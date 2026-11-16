# Compressão unificada de imagens — design

## Objetivo

Aplicar compressão automática a todos os uploads de imagens do Urbeat, buscando
um arquivo final abaixo de 1 MiB sem reduzir dimensões quando isso for viável e
sem perda visual perceptível relevante.

## Escopo

Inclui os uploads de logo, banner e imagens de produtos existentes, além de
deixar o utilitário reutilizável para novos pontos de upload. Não inclui
migração retroativa das imagens já armazenadas nem alterações no print-agent,
OCI ou em outros projetos independentes.

## Arquitetura proposta

1. **Frontend:** evoluir `image-compression.utils.ts` para aceitar um alvo de
   tamanho (950 KiB, deixando margem para o limite de 1 MiB), manter a proporção
   e tentar qualidade progressiva. A dimensão original será preservada primeiro;
   somente se necessário serão reduzidas gradualmente. Arquivos já menores que
   o alvo não serão reprocessados.
2. **Formatos:** JPEG, PNG e WebP serão processados no navegador. Transparência
   será preservada quando o formato de saída permitir. SVG e AVIF não serão
   reencodados automaticamente; continuarão sujeitos à validação de formato e
   tamanho do servidor.
3. **Pontos de integração:** aplicar o utilitário antes do envio de logo,
   banner e produto, removendo a validação frontend que rejeita prematuramente
   imagens grandes antes da tentativa de compressão.
4. **Backend:** manter a validação autoritativa e alinhar os limites dos tipos
   para 1 MiB quando a imagem chegar ao endpoint. O backend continuará
   validando extensão, MIME, conteúdo e limite, independentemente do cliente.
   A transformação de entrada existente do Cloudinary será preservada.

## Fluxo

Seleção → validação básica de extensão/MIME → compressão adaptativa → preview e
upload → validação autoritativa do backend. Se a compressão falhar por suporte
do navegador, timeout ou erro de decodificação, o arquivo original não será
enviado silenciosamente quando exceder o limite; o usuário receberá mensagem
clara para escolher uma imagem menor.

## Compatibilidade e segurança

- A extensão do arquivo gerado deve continuar compatível com o MIME e com o
  validador do backend.
- Não aceitar arquivo inválido apenas porque a compressão falhou.
- Preservar comportamento de seleção assíncrona, preview, remoção e troca rápida
  de arquivos no configurador da loja.
- Evitar alteração de URLs existentes e não imprimir conteúdo de arquivos ou
  dados sensíveis nos logs.

## Testes e aceite

- Testes unitários do utilitário para arquivo menor que o alvo, compressão
  progressiva, limite de tamanho, preservação de proporção, transparência,
  formatos não suportados, timeout e falha de canvas.
- Testes dos fluxos de produto, logo e banner verificando que o arquivo enviado
  é o comprimido.
- Testes backend atualizados para o limite unificado e para rejeição de arquivo
  ainda maior que 1 MiB, formato inválido e conteúdo corrompido.
- `npx jest --no-coverage` e `npx ng build --configuration production`.
- `dotnet build backend/Urbeat.sln` e testes backend direcionados.

Critério de aceite: qualquer raster suportado que possa ser reduzido pelo
navegador deve ser enviado com no máximo 1 MiB, mantendo suas dimensões sempre
que possível; nenhum upload deve contornar as validações do servidor.

## Atualização (2026-11-16)

- **AVIF:** decodificado no navegador e reencodado como AVIF quando o navegador
  expõe esse encoder; caso contrário, WebP e, por último, PNG. O arquivo é
  renomeado para manter extensão e MIME coerentes com o formato real.
- **SVG:** comentários, metadados e instruções de processamento são removidos
  (sem tocar em conteúdo renderizável). Se ainda exceder o alvo, um SVG
  estruturalmente seguro é rasterizado para WebP/PNG usando `width`, `height` ou
  `viewBox`. SVG com conteúdo ativo ou referências externas não é rasterizado,
  preservando a política de segurança do backend.
- **Limite de produto:** permanece 1 MiB. Evidência: `ProductMaxBytes =
  StoreMediaUploadValidator.MaxBytes`, os dois endpoints de imagem chamam
  `StoreMediaUploadValidator.Validate` e os testes de integração exigem as
  mensagens de 1 MB. O antigo `PRODUCT_IMAGE_MAX_BYTES = 6 MiB` era um resíduo
  deprecado e sem uso, removido do frontend. As menções a 6 MB no Postman são
  documentação histórica e não representam o contrato executável.
- **Fallback:** o arquivo original é retornado quando o navegador não consegue
  decodificar/reencodar; o chamador rejeita com mensagem clara. Nenhuma
  validação do backend foi enfraquecida.
