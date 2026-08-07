#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────
# Urbeat — Emissão inicial do certificado Let's Encrypt
# ─────────────────────────────────────────────────────────────
# Pré-requisitos:
#   1. DNS de urbeat.com.br, www.urbeat.com.br, api.urbeat.com.br
#      deve apontar para o IP deste servidor (verifique antes!).
#   2. Stack rodando: `docker compose up -d nginx frontend_build webapi`
#   3. Nginx HTTP servindo /.well-known/acme-challenge (config padrão)
# ─────────────────────────────────────────────────────────────
set -euo pipefail

cd "$(dirname "$0")/../docker"

# Carregar .env para pegar o LETSENCRYPT_EMAIL
if [[ -f .env ]]; then
  set -o allexport
  # shellcheck disable=SC1091
  source .env
  set +o allexport
fi

EMAIL="${LETSENCRYPT_EMAIL:-admin@urbeat.com.br}"

echo "════════════════════════════════════════════════════════"
echo "  Emitindo certificado Let's Encrypt"
echo "  E-mail   : $EMAIL"
echo "  Domínios : urbeat.com.br, www.urbeat.com.br, api.urbeat.com.br"
echo "════════════════════════════════════════════════════════"

# 1. Verificar DNS
for h in urbeat.com.br www.urbeat.com.br api.urbeat.com.br; do
  ip=$(getent hosts "$h" | awk '{print $1}' | head -1 || true)
  if [[ -z "$ip" ]]; then
    echo "❌ DNS de $h NÃO resolve. Configure o registro A apontando para este servidor."
    exit 1
  fi
  echo "✓ $h -> $ip"
done

# 2. Verificar que nginx está respondendo HTTP
if ! curl -fsS "http://urbeat.com.br/.well-known/acme-challenge/_test" -o /dev/null 2>&1; then
  # 404 é OK (o arquivo não existe), o que importa é o nginx responder
  http_code=$(curl -s -o /dev/null -w '%{http_code}' "http://urbeat.com.br/.well-known/acme-challenge/_test" || echo "000")
  if [[ "$http_code" != "404" && "$http_code" != "200" ]]; then
    echo "❌ Nginx não está servindo /.well-known/acme-challenge (HTTP $http_code)"
    echo "   Suba a stack primeiro: docker compose up -d"
    exit 1
  fi
fi
echo "✓ Nginx respondendo na porta 80"

# 3. Emitir cert
echo ""
echo "→ Solicitando certificado..."
docker compose run --rm --entrypoint "" certbot \
  certbot certonly \
    --webroot \
    -w /var/www/certbot \
    --email "$EMAIL" \
    --agree-tos --no-eff-email \
    --rsa-key-size 4096 \
    -d urbeat.com.br \
    -d www.urbeat.com.br \
    -d api.urbeat.com.br

# 4. Ativar config HTTPS
if [[ -f nginx/conf.d/20-https.conf.disabled ]]; then
  echo "→ Ativando vhosts HTTPS..."
  mv nginx/conf.d/20-https.conf.disabled nginx/conf.d/20-https.conf
fi

# 5. Ativar redirects HTTP→HTTPS no 10-http.conf
if grep -q "# return 301 https" nginx/conf.d/10-http.conf; then
  echo "→ Habilitando redirect HTTP→HTTPS..."
  sed -i 's|# return 301 https|return 301 https|g' nginx/conf.d/10-http.conf
fi

# 6. Reload nginx
echo "→ Recarregando Nginx..."
docker compose exec nginx nginx -t
docker compose exec nginx nginx -s reload

# 7. Smoke test
echo ""
echo "→ Smoke test HTTPS..."
sleep 2
for h in urbeat.com.br www.urbeat.com.br api.urbeat.com.br; do
  code=$(curl -s -o /dev/null -w '%{http_code}' "https://$h/" || echo "000")
  echo "   https://$h/  → $code"
done

echo ""
echo "✓ Certificado emitido e HTTPS ativo!"
echo "  Renovação automática a cada 12h via container 'urbeat_certbot'"
