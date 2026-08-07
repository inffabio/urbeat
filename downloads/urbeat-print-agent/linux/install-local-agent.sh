#!/usr/bin/env bash
set -euo pipefail

APP_NAME="urbeat-print-agent"
INSTALL_DIR="/opt/urbeat-print-agent"
SERVICE_PATH="/etc/systemd/system/${APP_NAME}.service"
CURRENT_DIR="$(cd "$(dirname "$0")" && pwd)"

echo "[urbeat-print-agent] instalando em ${INSTALL_DIR}"

sudo mkdir -p "${INSTALL_DIR}"
sudo cp -f "${CURRENT_DIR}/Urbeat.PrintAgent" "${INSTALL_DIR}/Urbeat.PrintAgent"

if [ -f "${CURRENT_DIR}/appsettings.json" ]; then
  sudo cp -f "${CURRENT_DIR}/appsettings.json" "${INSTALL_DIR}/appsettings.json"
fi

sudo chmod +x "${INSTALL_DIR}/Urbeat.PrintAgent"

sudo cp -f "${CURRENT_DIR}/urbeat-print-agent.service" "${SERVICE_PATH}"
sudo systemctl daemon-reload
sudo systemctl enable "${APP_NAME}"
sudo systemctl restart "${APP_NAME}"

echo "[urbeat-print-agent] instalado com sucesso"
echo "Verifique: sudo systemctl status ${APP_NAME}"
