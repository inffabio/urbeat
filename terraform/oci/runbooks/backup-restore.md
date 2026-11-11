# Backup e restore

O bucket de state deve ser privado, versionado, retido e ter acesso mínimo. Teste
restore em ambiente controlado; nunca sobrescreva produção cegamente com state antigo.
Valide backup/restore PostgreSQL e volumes separadamente, preservando volumes existentes.
