import argparse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from threading import Lock

state = {"modo": "ok", "total": 0}
lock = Lock()

class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        with lock:
            if self.path == "/estado":
                status, body = 200, str(state["total"])
            elif self.path.startswith("/modo/"):
                state.update(modo=self.path.split("/")[-1], total=0)
                status, body = 200, "ok"
            else:
                state["total"] += 1
                mode = state["modo"]
                status = 503 if mode == "falha" or (mode == "transitoria" and state["total"] == 1) else 200
                if mode == "cliente":
                    status = 400
                body = "dependência didática"
        self.send_response(status)
        self.end_headers()
        self.wfile.write(body.encode())

    def log_message(self, *args):
        pass

if __name__ == "__main__":
    cli = argparse.ArgumentParser()
    cli.add_argument("--port", type=int, default=5189)
    args = cli.parse_args()
    ThreadingHTTPServer(("127.0.0.1", args.port), Handler).serve_forever()
