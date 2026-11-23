import http.cookiejar
import json
import os
import sqlite3
import ssl
import time
import urllib.error
import urllib.request
from urllib.parse import urlsplit
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

base = os.environ.get("PORTAL_TEST_BASE", "https://localhost:7143")
dep = os.environ.get("PORTAL_TEST_DEP", "http://127.0.0.1:5189")
db = Path(os.environ["PORTAL_TEST_DB"])
senha = os.environ["Laboratorio__Senha"]
evidencias = Path(os.environ.get("PORTAL_EVIDENCIAS", "evidencias"))
evidencias.mkdir(parents=True, exist_ok=True)
resultados = []

class Cliente:
    def __init__(self):
        self.cookies = http.cookiejar.CookieJar()
        self.client = urllib.request.build_opener(
            urllib.request.ProxyHandler({}), urllib.request.HTTPCookieProcessor(self.cookies),
            urllib.request.HTTPSHandler(context=ssl._create_unverified_context()))

    def req(self, path, *, method="GET", data=None, headers=None, csrf=False):
        headers = dict(headers or {})
        if csrf:
            headers["X-CSRF-TOKEN"] = self.req("/api/sessao/csrf")[2]["token"]
        if data is not None:
            headers["Content-Type"] = "application/json"
        request = urllib.request.Request(base + path, method=method, headers=headers,
                                         data=None if data is None else json.dumps(data).encode())
        try:
            response = self.client.open(request, timeout=12)
        except urllib.error.HTTPError as exc:
            response = exc
        with response:
            raw = response.read().decode()
            body = json.loads(raw) if raw and "json" in response.headers.get("Content-Type", "") else raw
            return response.status, response.headers, body

def registra(nome, detalhe):
    resultados.append({"teste": nome, "resultado": detalhe})
    print("OK", nome, detalhe, flush=True)

def cod(cliente, path, status, **kwargs):
    r = cliente.req(path, **kwargs)
    assert r[0] == status, (path, status, r[0], r[2])
    return r

def modo(value):
    urllib.request.urlopen(f"{dep}/modo/{value}", timeout=3).close()

def contador():
    with urllib.request.urlopen(dep + "/estado", timeout=3) as r:
        return int(r.read())

def login(c, usuario, senha, status):
    return cod(c, "/api/sessao/login", status, method="POST", data={"usuario": usuario, "senha": senha}, csrf=True)

def main():
    anon, comum, admin, outra_sessao = Cliente(), Cliente(), Cliente(), Cliente()
    minimal = cod(anon, "/diagnostico/minimal", 200)[2]
    assert minimal == cod(anon, "/diagnostico/mvc", 200)[2]
    ok = cod(anon, "/diagnostico/binding/1?situacao=Aberta", 200, headers={"X-Cliente": "laboratorio"})[2]
    assert ok["cliente"] == "laboratorio" and ok["situacao"] == "Aberta"
    cod(anon, "/diagnostico/binding/9999", 404)
    cod(anon, "/diagnostico/binding/abc", 404)
    falha = cod(anon, "/diagnostico/falha", 500)
    assert "Marcador interno" not in str(falha[2])
    origem = {"Origin": "http://localhost:5173"}
    assert cod(anon, "/api/tarefas", 200, headers=origem)[1]["Access-Control-Allow-Origin"] == origem["Origin"]
    assert cod(anon, "/api/tarefas", 200, headers={"Origin": "https://exemplo.invalid"})[1]["Access-Control-Allow-Origin"] is None
    registra("R1 pipeline, binding, exceção, CORS", {"comparacao": minimal, "binding": ok, "falha": falha[2], "correlacao": falha[1]["X-Correlation-ID"]})

    cod(anon, "/api/sessao", 401)
    assert login(comum, "usuario", "invalida", 401)[2]["title"] == login(comum, "inexistente", "invalida", 401)[2]["title"]
    login(comum, "usuario", senha, 204)
    login(admin, "admin", senha, 204)
    login(outra_sessao, "admin", senha, 204)
    assert not cod(comum, "/api/sessao", 200)[2]["administrador"]
    assert cod(admin, "/api/sessao", 200)[2]["editarOutraEquipe"]
    cod(anon, "/api/tarefas/minhas", 401)
    assert 2 not in [x["id"] for x in cod(comum, "/api/tarefas/minhas", 200)[2]]
    cod(anon, "/api/tarefas", 401, method="POST", data={"titulo": "Sem sessão"})
    cod(comum, "/api/tarefas", 400, method="POST", data={"titulo": "Sem CSRF"})
    r1 = cod(comum, "/diagnostico/tarefas", 201, method="POST", data={"titulo": "Tarefa Minimal"}, csrf=True)
    r1falha = cod(comum, "/diagnostico/tarefas", 400, method="POST", data={"titulo": ""}, csrf=True)
    registra("R1 POST JSON válido e inválido", {"local": r1[1]["Location"], "erros": r1falha[2]["errors"]})

    with sqlite3.connect(db) as conn:
        nomes = [r[0] for r in conn.execute("select MigrationId from __EFMigrationsHistory order by MigrationId")]
        assert len(nomes) == 2, nomes
        assert conn.execute("select count(*) from tarefas where Id <= 4").fetchone()[0] == 4
        antes = conn.execute("select count(*) from auditorias where TarefaId=2").fetchone()[0]
    painel = cod(anon, "/diagnostico/painel", 200)[2]
    assert painel["iguais"]
    original = cod(comum, "/api/tarefas/2", 200)[2]
    original_segundo = cod(outra_sessao, "/api/tarefas/2", 200)[2]
    assert original["versao"] == original_segundo["versao"]
    edicao = {**original, "titulo":"Publicar agenda revisada"}
    cod(comum, "/api/tarefas/2", 403, method="PUT", data=edicao, csrf=True)
    cod(admin, "/api/tarefas/2", 204, method="PUT", data=edicao, csrf=True)
    conflito = cod(outra_sessao, "/api/tarefas/2", 409, method="PUT", data={**original_segundo, "titulo": "Edição defasada"}, csrf=True)[2]
    atual = cod(admin, "/api/tarefas/2", 200)[2]
    assert atual["titulo"] == edicao["titulo"] and atual["versao"] != original["versao"]
    with sqlite3.connect(db) as conn:
        depois = conn.execute("select count(*) from auditorias where TarefaId=2").fetchone()[0]
        assert depois - antes == 1
    registra("R3 migrations, projeção e transação com rollback", {"migrations": nomes, "seed":4, "painel": painel, "auditoriasAntes": antes, "auditoriasDepois": depois, "conflito": conflito["title"]})

    vazio = cod(anon, "/api/tarefas?busca=nenhumresultado", 200)[2]
    assert vazio["itens"] == []
    for path in ["/api/tarefas?pagina=0", "/api/tarefas?tamanho=51", "/api/tarefas?ordenarPor=coluna"]:
        assert cod(anon, path, 400)[1]["Content-Type"].startswith("application/problem+json")
    minimo = cod(anon, "/api/tarefas?tamanho=1", 200)[2]
    assert len(minimo["itens"]) == 1
    cod(anon, "/api/tarefas?tamanho=50", 200)
    ordenados = cod(anon, "/api/tarefas?ordenarPor=id&direcao=desc", 200)[2]["itens"]
    assert [x["id"] for x in ordenados] == sorted((x["id"] for x in ordenados), reverse=True)
    combinados = cod(anon, "/api/tarefas?busca=Revisar&situacao=Concluida", 200)[2]
    assert [x["id"] for x in combinados["itens"]] == [3]
    inexistente = cod(anon, "/api/tarefas/9999", 404)[2]
    valido = cod(comum, "/api/tarefas", 201, method="POST", data={"titulo":"Tarefa REST"}, csrf=True)
    location = urlsplit(valido[1]["Location"]).path
    cod(comum, location, 200)
    invalido = cod(comum, "/api/tarefas", 400, method="POST", data={"titulo":""}, csrf=True)
    cod(comum, location + "?versao=" + valido[2]["versao"], 403, method="DELETE", csrf=True)
    cod(admin, location + "?versao=" + valido[2]["versao"], 204, method="DELETE", csrf=True)
    cod(anon, location, 404)
    registra("R4 contrato REST", {"paginaVazia": vazio, "filtros": combinados, "recursoAusente": inexistente, "validacao": invalido[2]["errors"], "location": valido[1]["Location"], "exclusao": 204})

    cookie_anterior = "; ".join(f"{c.name}={c.value}" for c in comum.cookies)
    cod(comum, "/api/sessao/logout", 204, method="POST", csrf=True)
    cod(comum, "/api/sessao", 401)
    cod(anon, "/api/sessao", 401, headers={"Cookie": cookie_anterior})
    registra("R6 identidade e autorização", {"anonimo": 401, "usuarioOutraEquipe": 403, "adminOutraEquipe": 204, "perfilAutorizado": 200, "cookieRevogado": 401, "semCsrf": 400})

    cod(anon, "/health/live", 200)
    modo("ok")
    cod(anon, "/health/ready", 200)
    modo("transitoria")
    cod(anon, "/health/ready", 200)
    tentativas = contador()
    assert tentativas == 2, tentativas
    modo("cliente")
    cod(anon, "/health/ready", 503)
    assert contador() == 1
    modo("falha")
    for _ in range(4):
        cod(anon, "/health/ready", 503)
    volume = contador()
    cod(anon, "/health/ready", 503)
    assert contador() == volume, (volume, contador())
    cod(anon, "/health/live", 200)
    registra("R7 retry, circuito e Health Checks", {"tentativasTransitoria": tentativas, "status400SemRetry": 1, "chamadasAntesDoCircuito": volume, "readinessFalha": 503, "liveness": 200})
    with ThreadPoolExecutor(max_workers=8) as exe:
        status = list(exe.map(lambda _: Cliente().req("/diagnostico/limite")[0], range(8)))
    assert status.count(200) == 5 and status.count(429) == 3, status
    primeiro = cod(anon, "/api/situacoes", 200)[2]
    assert primeiro == cod(Cliente(), "/api/situacoes", 200)[2]
    login(comum, "usuario", senha, 204)
    cod(comum, "/api/tarefas", 201, method="POST", data={"titulo": "Invalidar cache público"}, csrf=True)
    assert primeiro != cod(anon, "/api/situacoes", 200)[2]
    registra("R7 carga limitada e cache", {"http200": 5, "http429": 3, "cache": "mesmo conteúdo entre clientes anônimos e identificador trocado após criação"})

if __name__ == "__main__":
    try:
        main()
    finally:
        (evidencias / "http.json").write_text(json.dumps(resultados, ensure_ascii=False, indent=2), encoding="utf-8")
