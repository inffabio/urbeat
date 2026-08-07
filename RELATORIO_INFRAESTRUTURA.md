# Relatório do Processo de Infraestrutura Backend Urbeat

## 1. Planejamento
- Definição do stack: .NET 9, PostgreSQL, Nginx
- Escolha por containers Docker para portabilidade e facilidade de deploy
- Orquestração via Docker Compose
- Servidor alvo: Ubuntu 22.04 (192.168.1.15)

## 2. Estrutura de Arquivos Criada
- `docker/docker-compose.yml`: Orquestra containers do backend, banco e nginx
- `docker/nginx/nginx.conf`: Proxy reverso para o backend
- `infraestruturaBackend.md`: Guia de deploy e observações

## 3. Configuração dos Containers
- **webapi**: Container .NET 9, expõe porta 5000, conecta ao banco PostgreSQL
- **db**: Container PostgreSQL 16, persistência via volume, expõe porta 5432
- **nginx**: Container Nginx, faz proxy reverso para o backend, expõe porta 80

## 4. Passos para Deploy
1. Acesso ao servidor via SSH (`ssh Fabio@192.168.1.15`)
2. Instalação do Docker e Docker Compose
3. Transferência dos arquivos do projeto (SCP/SFTP/VS Code Remote SSH)
4. Subida dos containers com `docker-compose up -d`
5. Verificação dos serviços com `docker ps` e acesso via navegador

## 5. Observações de Segurança e Boas Práticas
- Recomenda-se alterar senhas padrão em produção
- Utilizar volumes e secrets seguros
- Limitar exposição da porta 5432 do PostgreSQL
- Realizar backups periódicos do volume do banco
- Monitorar logs dos containers

## 6. Possíveis Extensões Futuras
- Integração com CI/CD para automação de build e deploy
- Certificados SSL no Nginx para HTTPS
- Monitoramento com Prometheus/Grafana
- Escalabilidade via Docker Swarm/Kubernetes

---

Este relatório documenta todo o processo de infraestrutura realizado para o backend Urbeat. Consulte para auditoria, troubleshooting ou evolução futura.
