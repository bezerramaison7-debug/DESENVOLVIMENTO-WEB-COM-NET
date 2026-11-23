import json
import re
import sys
from pathlib import Path

text = Path(sys.argv[1]).read_text()
rows = []
for line in text.splitlines():
    if not line.startswith("{"):
        continue
    try:
        event = json.loads(line)
    except json.JSONDecodeError:
        continue
    message = event.get("Message", "")
    if message.startswith(("Entrada GET /diagnostico/falha", "Saída GET /diagnostico/falha", "MVC TarefasController.", "Circuito da dependência aberto", "Saída POST /api/tarefas 201")):
        sc = event.get("Scopes", [])
        cid = next((scope.get("correlationId") for scope in sc if scope.get("correlationId")), "")
        rows.append(f"{message} | correlação={cid}")
    if "OnRetry" in message:
        rows.append(message.split(", Handled:")[0])
    if "OnCircuitOpened" in message:
        rows.append(message.split(", Handled:")[0])
assert any("diagnostico/falha 500" in x for x in rows), rows
assert any("Circuito da dependência aberto" in x for x in rows), rows
assert any("MVC TarefasController.Create" in x and "False" in x for x in rows), rows
dados = json.loads((Path(sys.argv[2]).parent / "http.json").read_text())
id_falha = dados[0]["resultado"]["correlacao"]
assert any(id_falha in row and "diagnostico/falha 500" in row for row in rows)
traces = set(re.findall(r"Activity\.TraceId:[ \t]+([a-f0-9]{32})", text))
ids_logs = {row.split("correlação=")[-1] for row in rows if "Saída POST /api/tarefas 201" in row}
comum = sorted(traces.intersection(ids_logs))
assert comum, f"Trace de cadastro sem par no log: {len(traces)} traces, {len(ids_logs)} correlações"
rows.append(f"Activity.TraceId correlacionado ao POST /api/tarefas 201: {comum[0]}")
patterns = [
    r"(?m)^Activity\.DisplayName:[ \t]+tarefas\.criar\b",
    r"(?m)^.*portal\.tarefas\.criadas.*$",
    r"(?m)^.*portal\.http\.duracao.*$",
]
for pattern in patterns:
    proof = re.search(pattern, text)
    if proof is None:
        candidatos = [x for x in text.splitlines() if x.startswith(("Activity.DisplayName", "Metric Name"))]
        raise AssertionError(f"Telemetria ausente: {pattern}. Sinais: {list(dict.fromkeys(candidatos))[:20]}")
    rows.append(proof.group())
Path(sys.argv[2]).write_text("\n".join(dict.fromkeys(rows)) + "\n")
