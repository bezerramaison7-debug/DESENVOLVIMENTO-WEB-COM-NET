#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
rev="${REVISAO:-$(git rev-parse HEAD)}"
imagem="portal-tarefas:$rev"
container=portal-tarefas-${rev:0:10}-$$
trap 'docker rm -f "$container" >/dev/null 2>&1 || true' EXIT
docker build --label "org.opencontainers.image.revision=$rev" --tag "$imagem" .
docker run -d --name "$container" ${PORTAL_REDE_DOCKER:-} \
  -e 'ConnectionStrings__Portal=Data Source=/data/portal.db' "$imagem"
for _ in $(seq 1 12); do
  teste="$(docker inspect --format '{{.State.Health.Status}}' "$container")"
  if [ "$teste" = healthy ]; then
    docker inspect --format 'health={{.State.Health.Status}} user={{.Config.User}}' "$container"
    docker image inspect --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' "$imagem"
    exit 0
  fi
  sleep 5
done
docker logs "$container"
exit 1
