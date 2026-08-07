# Infraestrutura Backend Urbeat (.NET 9 + PostgreSQL + Nginx)

## Visão Geral
- Backend: .NET 9 (container)
- Banco de dados: PostgreSQL (container)
- Proxy reverso: Nginx (container)
- Orquestração: Docker Compose
- Servidor: Ubuntu 22.04 (IP: 192.168.1.15, usuário: Fabio)

## Estrutura de Arquivos
```
docker/
  docker-compose.yml
  nginx/
    nginx.conf
```

## docker-compose.yml (exemplo)
```yaml
version: '3.9'
services:
  webapi:
    image: mcr.microsoft.com/dotnet/aspnet:9.0
    container_name: urbeat_webapi
    restart: always
    ports:
      - "5000:80"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ConnectionStrings__DefaultConnection=Host=db;Database=urbeat;Username=postgres;Password=postgres
    volumes:
      - ../backend/src/Urbeat.WebApi:/app
    working_dir: /app
    command: ["dotnet", "Urbeat.WebApi.dll"]
    depends_on:
      - db
  db:
    image: postgres:16
    container_name: urbeat_db
    restart: always
    environment:
      POSTGRES_DB: urbeat
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports:
      - "5432:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data
  nginx:
    image: nginx:latest
    container_name: urbeat_nginx
    restart: always
    ports:
      - "80:80"
    volumes:
      - ./nginx/nginx.conf:/etc/nginx/nginx.conf:ro
    depends_on:
      - webapi
volumes:
  pgdata:
```

## nginx.conf (exemplo)
```nginx
user  nginx;
worker_processes  auto;
error_log  /var/log/nginx/error.log warn;
pid        /var/run/nginx.pid;

events {
    worker_connections  1024;
}

http {
    include       /etc/nginx/mime.types;
    default_type  application/octet-stream;
    sendfile        on;
    keepalive_timeout  65;

    server {
        listen 80;
        server_name _;

        location / {
            proxy_pass         http://webapi:80;
            proxy_http_version 1.1;
            proxy_set_header   Upgrade $http_upgrade;
            proxy_set_header   Connection keep-alive;
            proxy_set_header   Host $host;
            proxy_cache_bypass $http_upgrade;
        }
    }
}
```

## Passos para Deploy
1. **Acesse o servidor via SSH:**
   - `ssh Fabio@192.168.1.15` (senha: Mond@y08)
2. **Instale Docker e Docker Compose:**
   - `sudo apt update && sudo apt install docker.io docker-compose -y`
3. **Transfira os arquivos do projeto:**
   - Use SCP/SFTP ou VS Code Remote SSH para copiar a pasta `docker/` e o backend.
4. **Suba os containers:**
   - `cd docker`
   - `sudo docker-compose up -d`
5. **Verifique os serviços:**
   - `sudo docker ps`
   - Acesse `http://192.168.1.15` para testar o backend via Nginx.

## Observações
- Ajuste variáveis de ambiente conforme necessário (senhas, nomes de banco, etc).
- Para produção, utilize volumes e secrets seguros.
- O backend deve estar buildado para Linux x64.
- O Nginx faz proxy reverso para o container webapi.
- O PostgreSQL expõe a porta 5432 para acesso externo (opcional, pode ser removido em produção).

---

Consulte este arquivo para dúvidas de infraestrutura e deploy.