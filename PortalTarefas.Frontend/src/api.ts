import type { components } from "./contrato";
import {rotas} from "./rotas";
export type Resumo = components["schemas"]["ResumoTarefa"];
export type Detalhe = components["schemas"]["DetalheTarefa"];
export type Pagina = components["schemas"]["PaginaTarefas"];
export type Entrada = components["schemas"]["TarefaInputModel"];
export type Sessao = components["schemas"]["Sessao"];

export class Falha extends Error {
  constructor(message: string, readonly status = 0, readonly campos: Record<string, string[]> = {}) {super(message);}
}
export async function enviar<T>(path: string, init?: RequestInit): Promise<T> {
  let resposta: Response;
  try { resposta = await fetch((import.meta.env.VITE_API_BASE || "") + path, {credentials: "same-origin", ...init}); }
  catch (e) {
    if (e instanceof DOMException && e.name === "AbortError") throw e;
    throw new Falha("Falha de rede. Tente novamente.");
  }
  if (!resposta.ok) {
    const problema = resposta.headers.get("content-type")?.includes("json") ? await resposta.json().catch(() => null) : null;
    throw new Falha(problema?.title || "A solicitação falhou.", resposta.status, problema?.errors);
  }
  return resposta.status === 204 ? undefined as T : resposta.json();
}
export async function gravar<T>(path: string, body?: unknown): Promise<T> {
  const csrf = await enviar<{token: string}>("/api/sessao/csrf");
  return enviar<T>(path, {method: "POST", headers: {"X-CSRF-TOKEN": csrf.token, "Content-Type": "application/json"},
    body: body === undefined ? undefined : JSON.stringify(body)});
}
export const tarefas = {
  listar: (query: URLSearchParams, signal: AbortSignal) =>
    enviar<Pagina>(rotas["get/api/tarefas"] + "?" + query.toString(), {signal}),
  obter: (id: string, signal: AbortSignal) =>
    enviar<Detalhe>(rotas["get/api/tarefas/{id}"].replace("{id}", encodeURIComponent(id)), {signal}),
  criar: (entrada: Entrada) => gravar<Detalhe>(rotas["post/api/tarefas"], entrada)
};
