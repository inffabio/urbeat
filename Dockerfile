# ═══════════════════════════════════════════════════════════
# URBEAT - ROOT DOCKERFILE (Workspace Reference)
# This file is primarily for reference. 
# Actual builds are handled by backend/src/Urbeat.WebApi/Dockerfile 
# and frontend/Dockerfile via docker-compose.yml.
# ═══════════════════════════════════════════════════════════

FROM alpine:3.20
RUN echo "Urbeat Workspace - Use 'docker compose up --build' to run the full stack."
CMD ["sh", "-c", "echo '✅ Urbeat Development Environment Ready'"]
