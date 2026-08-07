-- UrbeatDb é criado automaticamente via POSTGRES_DB no docker-compose.
-- Este script roda no primeiro startup do volume vazio.

-- Banco de logs estruturados (Serilog sink Postgres)
SELECT 'CREATE DATABASE "UrbeatLogs"'
WHERE NOT EXISTS (
  SELECT FROM pg_database WHERE datname = 'UrbeatLogs'
)\gexec

-- Extensões úteis no banco principal (estatísticas para postgres_exporter)
\connect "UrbeatDb"
CREATE EXTENSION IF NOT EXISTS pg_stat_statements;
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- Extensões no UrbeatLogs (apenas pgcrypto para Guid default)
\connect "UrbeatLogs"
CREATE EXTENSION IF NOT EXISTS pgcrypto;
