#!/bin/bash
# Este script é executado automaticamente pelo entrypoint do PostgreSQL 
# após a inicialização do banco de dados, mas antes de ele começar a aceitar conexões.

set -e

PG_HBA_PATH="/var/lib/postgresql/data/pg_hba.conf"

echo "Aplicando configuração personalizada de pg_hba.conf..."

cat > "$PG_HBA_PATH" << 'EOF'
# TYPE  DATABASE        USER            ADDRESS                 METHOD
# Conexões locais
local   all             all                                     trust
# Conexões IPv4 locais
host    all             all             127.0.0.1/32            trust
# Conexões IPv6 locais
host    all             all             ::1/128                 trust
# Conexões da rede Docker (IPv4 e IPv6) usando autenticação segura
host    all             all             0.0.0.0/0               scram-sha-256
host    all             all             ::/0                    scram-sha-256
# Replicação
local   replication     all                                     trust
host    replication     all             127.0.0.1/32            trust
host    replication     all             ::1/128                 trust
host    replication     all             0.0.0.0/0               scram-sha-256
host    replication     all             ::/0                    scram-sha-256
EOF

echo "pg_hba.conf atualizado com sucesso. Permitindo conexões da rede Docker com scram-sha-256."
