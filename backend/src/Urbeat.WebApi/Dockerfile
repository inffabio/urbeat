# ─────────────────────────────────────────────────────────────
# Stage 1: BUILD (SDK)
# ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restaurar dependências camada-a-camada (cache otimizado)
COPY src/Urbeat.Domain/Urbeat.Domain.csproj                 src/Urbeat.Domain/
COPY src/Urbeat.Application/Urbeat.Application.csproj       src/Urbeat.Application/
COPY src/Urbeat.Infrastructure/Urbeat.Infrastructure.csproj src/Urbeat.Infrastructure/
COPY src/Urbeat.WebApi/Urbeat.WebApi.csproj                 src/Urbeat.WebApi/

RUN dotnet restore "src/Urbeat.WebApi/Urbeat.WebApi.csproj"

# Copiar o restante e publicar
COPY src/ src/
RUN dotnet publish "src/Urbeat.WebApi/Urbeat.WebApi.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false \
    /p:DebugType=None \
    /p:DebugSymbols=false

# ─────────────────────────────────────────────────────────────
# Stage 2: RUNTIME (ASP.NET)
# ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# curl para healthcheck do docker-compose
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Usuário não-root
RUN groupadd -r urbeat && useradd -r -g urbeat -d /app urbeat \
    && chown -R urbeat:urbeat /app
USER urbeat

COPY --from=build --chown=urbeat:urbeat /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_GCServer=1 \
    DOTNET_GCHeapCount=2 \
    TZ=America/Sao_Paulo

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=5 \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Urbeat.WebApi.dll"]
