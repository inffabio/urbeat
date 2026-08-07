# ─────────────────────────────────────────────────────────────
# Stage 1: BUILD (Node 22)
# ─────────────────────────────────────────────────────────────
FROM node:22-alpine AS build
WORKDIR /app

# Cache de dependências
COPY package*.json ./
RUN npm ci --no-audit --no-fund

# Código-fonte
COPY . .

# Build de produção (gera dist/frontend/browser)
RUN npm run build -- --configuration production

# ─────────────────────────────────────────────────────────────
# Stage 2: RUNTIME — alpine simples, distribui os assets
# ─────────────────────────────────────────────────────────────
FROM alpine:3.20 AS runtime
RUN apk add --no-cache busybox

# Angular 20 + Ionic outputs to dist/<project-name>/browser
# Copy to an internal directory first during build
WORKDIR /app/dist
COPY --from=build /app/dist/frontend/browser/. .

# Default command copia os assets para o volume montado em /shared em tempo de execução
# Isso sobrescreve qualquer arquivo antigo no diretório do host
CMD ["sh", "-c", "echo '✅ Copiando assets Angular/Ionic para o volume...' && mkdir -p /shared && rm -rf /shared/* && cp -r /app/dist/. /shared/ && echo 'Concluído!' && ls -la /shared"]
