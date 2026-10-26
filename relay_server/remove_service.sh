#!/usr/bin/env bash
# Copyright 2026 Nils Kopal
# SPDX-License-Identifier: Apache-2.0
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy at https://www.apache.org/licenses/LICENSE-2.0
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See LICENSE and NOTICE for permissions and attribution.
#
# Stops and unregisters the optional relay service without removing emulator user data.
#

set -euo pipefail

SERVICE_NAME="${SERVICE_NAME:-snes-relay-server}"
SERVICE_USER="${SERVICE_USER:-snesrelay}"
SERVICE_GROUP="${SERVICE_GROUP:-snesrelay}"
INSTALL_DIR="${INSTALL_DIR:-/opt/snes-relay-server}"
CONFIG_DIR="${CONFIG_DIR:-/etc/snes-relay-server}"
LOG_DIR="${LOG_DIR:-/var/log/snes-relay-server}"
UNIT_FILE="/etc/systemd/system/${SERVICE_NAME}.service"
PURGE=0

if [[ "${1:-}" == "--purge" ]]; then
    PURGE=1
fi

if [[ "${EUID}" -ne 0 ]]; then
    exec sudo \
        SERVICE_NAME="${SERVICE_NAME}" \
        SERVICE_USER="${SERVICE_USER}" \
        SERVICE_GROUP="${SERVICE_GROUP}" \
        INSTALL_DIR="${INSTALL_DIR}" \
        CONFIG_DIR="${CONFIG_DIR}" \
        LOG_DIR="${LOG_DIR}" \
        bash "$0" "$@"
fi

if command -v systemctl >/dev/null 2>&1; then
    if systemctl list-unit-files "${SERVICE_NAME}.service" >/dev/null 2>&1; then
        systemctl disable --now "${SERVICE_NAME}.service" >/dev/null 2>&1 || true
    fi
fi

rm -f "${UNIT_FILE}"
rm -rf "${INSTALL_DIR}"

if [[ "${PURGE}" -eq 1 ]]; then
    rm -rf "${CONFIG_DIR}" "${LOG_DIR}"
    if id -u "${SERVICE_USER}" >/dev/null 2>&1; then
        userdel "${SERVICE_USER}" >/dev/null 2>&1 || true
    fi
    if getent group "${SERVICE_GROUP}" >/dev/null; then
        groupdel "${SERVICE_GROUP}" >/dev/null 2>&1 || true
    fi
fi

if command -v systemctl >/dev/null 2>&1; then
    systemctl daemon-reload
    systemctl reset-failed "${SERVICE_NAME}.service" >/dev/null 2>&1 || true
fi

echo "Removed ${SERVICE_NAME} service and ${INSTALL_DIR}."
if [[ "${PURGE}" -eq 0 ]]; then
    echo "Preserved config/certificates in ${CONFIG_DIR} and logs in ${LOG_DIR}."
    echo "Run with --purge to remove those as well."
else
    echo "Purged config/certificates, logs, and service user where possible."
fi
