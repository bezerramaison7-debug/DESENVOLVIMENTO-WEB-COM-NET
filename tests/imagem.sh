#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

rev="${REVISAO:-$(git rev-parse HEAD)}"
imagem="portal-tarefas:$rev"
container=portal-tarefas-${rev:0:10}-$$
temporario="$(mktemp -d)"
limpar() {
  local status=$?
  if (( status != 0 )); then
    docker logs "$container" >&2 || true
  fi
  docker rm -f "$container" >/dev/null 2>&1 || true
  rm -rf "$temporario"
  return "$status"
}
trap limpar EXIT

docker build --label "org.opencontainers.image.revision=$rev" --tag "$imagem" .
docker run -d --name "$container" --publish 127.0.0.1::8080 \
  -e 'ConnectionStrings__Portal=Data Source=/data/portal.db' "$imagem"

status=starting
for _ in $(seq 1 12); do
  status="$(docker inspect --format '{{.State.Health.Status}}' "$container")"
  [[ "$status" == healthy ]] && break
  sleep 5
done
[[ "$status" == healthy ]]
docker inspect --format 'health={{.State.Health.Status}} user={{.Config.User}}' "$container"

imagem_id="$(docker inspect --format '{{.Image}}' "$container")"
revisao_implantada="$(docker image inspect --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' "$imagem_id")"
[[ "$revisao_implantada" == "$rev" ]]
printf 'ambiente=teste temporário | contêiner=%s | revisão=%s | imagem=%s\n' \
  "$container" "$revisao_implantada" "$imagem_id"

base="http://$(docker port "$container" 8080/tcp)"
validar_http() {
  local rota="$1" status_http
  status_http="$(curl --noproxy 127.0.0.1 --silent --show-error --fail --max-time 10 \
    --output "$temporario/resposta" --write-out '%{http_code}' "$base$rota")"
  [[ "$status_http" == 200 ]]
}

validar_http /tarefas
grep -Fq '<title>Portal de Tarefas</title>' "$temporario/resposta"
echo 'GET /tarefas HTTP 200 | página do Portal de Tarefas confirmada'

validar_http /api/tarefas
python3 - "$temporario/resposta" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as resposta:
    dados = json.load(resposta)
assert dados["total"] > 0 and dados["itens"] and dados["itens"][0]["titulo"]
PY
echo 'GET /api/tarefas HTTP 200 | lista inicial de tarefas confirmada'

for rota in /health/live /health/ready; do
  validar_http "$rota"
  grep -Fxq Healthy "$temporario/resposta"
  printf 'GET %s HTTP 200 | Healthy\n' "$rota"
done
