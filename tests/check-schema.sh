#!/usr/bin/env bash
set -euo pipefail
npm --prefix PortalTarefas.Frontend run generate
git diff --exit-code -- PortalTarefas.Frontend/src/contrato.ts PortalTarefas.Frontend/src/rotas.ts
