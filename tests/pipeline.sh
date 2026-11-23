#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
revision="$(git rev-parse HEAD)"
staging="$(mktemp -d)"
saida="${PORTAL_EVIDENCIAS:-$staging/evidencias}"
mkdir -p "$saida"
pids=()
trap 'for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; done; rm -rf "$staging"' EXIT
export PORTAL_EVIDENCIAS="$saida"

echo "RESTORE .NET $(dotnet --version)"
dotnet restore PortalTarefas.sln
echo "BUILD ASP.NET Core"
dotnet build PortalTarefas.sln -c Release --no-restore
echo "INSTALL Node $(node --version) / npm $(npm --version)"
npm --prefix PortalTarefas.Frontend ci --no-audit --no-fund
npm --prefix PortalTarefas.Frontend run build
./tests/check-schema.sh

mkdir -p PortalTarefas.Web/wwwroot
cp -r PortalTarefas.Frontend/dist/. PortalTarefas.Web/wwwroot/
dotnet dev-certs https
python3 tests/dependencia.py &
pids+=("$!")
export Laboratorio__Senha
Laboratorio__Senha="$(python3 -c 'import secrets; print("Aa7!" + secrets.token_hex(18))')"
export PORTAL_TEST_DB="$staging/test.db"
export ConnectionStrings__Portal="Data Source=$PORTAL_TEST_DB"
export Portal__Dependencia=http://127.0.0.1:5189/dependencia
export OTEL_TRACES_SAMPLER=always_on
unset OTEL_TRACES_SAMPLER_ARG
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS='http://localhost:5080;https://localhost:7143'
export ASPNETCORE_HTTPS_PORT=7143
export NO_PROXY="${NO_PROXY:-},localhost,127.0.0.1"
export no_proxy="$NO_PROXY"
start_backend() {
  (
    cd PortalTarefas.Web
    exec dotnet bin/Release/net10.0/PortalTarefas.Web.dll
  ) >>"$staging/servidor.log" 2>&1 &
  servidor=$!
  pids+=("$servidor")
  for _ in $(seq 1 90); do
    if curl -kfsS https://localhost:7143/health/live >/dev/null 2>&1; then return; fi
    kill -0 "$servidor" 2>/dev/null || break
    sleep 0.3
  done
  tail -20 "$staging/servidor.log" >&2
  return 1
}

start_backend
echo "TESTE transição HTTP para HTTPS"
test "$(curl -s -o /dev/null -w '%{http_code}' http://localhost:5080/health/live)" = 307
kill "$servidor"
wait "$servidor" || true
start_backend
echo "TESTE contrato OpenAPI publicado"
curl -kfSs https://localhost:7143/openapi/v1.json -o "$staging/openapi.json"
python3 - "$staging/openapi.json" <<'PY2'
import json, sys
a = json.load(open("evidencias/openapi.json"))
b = json.load(open(sys.argv[1]))
assert a == b, "Contrato registrado difere da aplicação."
print("Contrato da aplicação igual ao esquema do cliente.")
PY2
echo "TESTE idempotência do seed após reinício"
python3 - <<'PY'
import os, sqlite3
with sqlite3.connect(os.environ["PORTAL_TEST_DB"]) as db:
    ids = [x[0] for x in db.execute("select Id from Tarefas order by Id")]
    assert ids == [1,2,3,4], ids
    print("Seed após reiniciar:", ids)
PY
echo "TESTE de integração HTTP"
python3 tests/http_checks.py
echo "TESTE de navegação no Chromium"
npm --prefix PortalTarefas.Frontend test
sleep 6
kill "$servidor"
wait "$servidor" || true
python3 tests/telemetria.py "$staging/servidor.log" "$saida/telemetria.txt"
if [ "${PORTAL_FALHAR_TESTE:-0}" != 0 ]; then
  echo "TESTE deliberadamente reprovado antes da publicação."
  exit 1
fi

echo "PUBLISH $revision"
mkdir -p artifacts
dotnet publish PortalTarefas.Web -c Release --no-restore -o "$staging/publicado"
printf '%s\n' "$revision" > "$staging/publicado/REVISAO"
tar -czf "artifacts/portal-$revision.tar.gz" -C "$staging/publicado" .
sha256sum "artifacts/portal-$revision.tar.gz"

echo "SMOKE do artefato em Production"
(
  cd "$staging/publicado"
  if env -u ConnectionStrings__Portal ASPNETCORE_ENVIRONMENT=Production dotnet PortalTarefas.Web.dll >"$staging/opcoes.log" 2>&1; then
    exit 1
  fi
  python3 -c 'import sys; assert "Configure ConnectionStrings__Portal" in open(sys.argv[1]).read()' "$staging/opcoes.log"
)
(
  cd "$staging/publicado"
  exec env -u Laboratorio__Senha ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS='http://127.0.0.1:7188'     Portal__RedirecionarHttps=false Portal__Dependencia='http://127.0.0.1:7188/health/live'     ConnectionStrings__Portal="Data Source=$staging/producao.db" dotnet PortalTarefas.Web.dll
) > "$staging/producao.log" 2>&1 &
pids+=("$!")
for _ in $(seq 1 50); do
  if curl -fsS http://127.0.0.1:7188/health/ready > "$staging/ready.txt" 2>/dev/null; then break; fi
  sleep .3
done
test "$(cat "$staging/ready.txt")" = Healthy
test "$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:7188/openapi/v1.json)" = 404
curl -fsS http://127.0.0.1:7188/tarefas | grep '<title>Portal de Tarefas</title>' >/dev/null
echo "Production: configuração ausente recusada; readiness 200 Healthy; SPA 200; OpenAPI oculto (404)."
