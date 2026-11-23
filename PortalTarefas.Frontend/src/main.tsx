import React, {createContext, useContext, useEffect, useRef, useState} from "react";
import {createRoot} from "react-dom/client";
import {BrowserRouter, Link, Navigate, Route, Routes, useLocation, useNavigate, useParams, useSearchParams} from "react-router-dom";
import {enviar, Falha, gravar, tarefas} from "./api";
import type {Entrada, Sessao} from "./api";
import "./style.css";

type Estado<T> = {tipo: "idle" | "loading"} | {tipo: "ok"; valor: T} | {tipo: "error"; falha: Falha};
const mensagem = (e: unknown) => e instanceof Falha ? e : new Falha("Falha ao interpretar a resposta.");
function useRequisicao<T>(pedido: (signal: AbortSignal) => Promise<T>, chave: string) {
  const [estado, definir] = useState<Estado<T>>({tipo: "idle"});
  useEffect(() => {
    const controle = new AbortController();
    const iniciar = requestAnimationFrame(() => {
      definir({tipo: "loading"});
      pedido(controle.signal).then(valor => {
        if (!controle.signal.aborted) definir({tipo: "ok", valor});
      }, e => {
        if (!controle.signal.aborted) definir({tipo: "error", falha: mensagem(e)});
      });
    });
    return () => { cancelAnimationFrame(iniciar); controle.abort(); };
  }, [chave]);
  return estado;
}

const Contexto = createContext<{
  sessao: Sessao | null;
  carregando: boolean;
  atualizar: () => Promise<void>;
}>({sessao: null, carregando: true, atualizar: async () => {}});
function Aplicacao() {
  const [sessao, setSessao] = useState<Sessao | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erroSessao, setErroSessao] = useState("");
  const ir = useNavigate();
  async function atualizar() {
    try {setSessao(await enviar<Sessao>("/api/sessao")); setErroSessao("");}
    catch (e) {setSessao(null); if (mensagem(e).status !== 401) setErroSessao(mensagem(e).message);}
    finally {setCarregando(false);}
  }
  useEffect(() => {void atualizar()}, []);
  return <Contexto.Provider value={{sessao, carregando, atualizar}}>
    <header><nav><Link to="/tarefas">Portal de Tarefas</Link><a href="/mvc/Tarefas">Interface MVC</a>
      {sessao ? <><Link to="/tarefas/nova">Nova tarefa</Link>
        {sessao.administrador && <span className="badge">Administrador</span>}
        <span>Conta {sessao.usuario}</span><button onClick={async () => {
          try {await gravar("/api/sessao/logout"); await atualizar(); ir("/tarefas");}
          catch (e) {setErroSessao(mensagem(e).message);}
        }}>Sair</button></> : !carregando && <Link to="/entrar">Entrar</Link>}
    </nav></header>
    <main>{erroSessao && <p role="alert">{erroSessao}</p>}
      <Routes>
        <Route path="/tarefas" element={<Lista/>}/>
        <Route path="/tarefas/nova" element={<Privada><Cadastro/></Privada>}/>
        <Route path="/tarefas/:id" element={<Detalhes/>}/>
        <Route path="/entrar" element={<Login/>}/>
        <Route path="/" element={<Navigate to="/tarefas" replace/>}/>
        <Route path="*" element={<h1>Página inexistente</h1>}/>
      </Routes>
    </main>
  </Contexto.Provider>;
}

function Lista() {
  const [query, setQuery] = useSearchParams();
  const [tentativa, tentar] = useState(0);
  const estado = useRequisicao(signal => tarefas.listar(query, signal), query.toString() + ":" + tentativa);
  const pagina = Number(query.get("pagina") || "1");
  const chaveFiltro = JSON.stringify(["busca", "situacao", "ordenarPor", "direcao"].map(nome => query.get(nome)));
  return <>
    <h1>Tarefas</h1>
    <form className="filtros" key={chaveFiltro} onSubmit={e => {
      e.preventDefault();
      const dados = new FormData(e.currentTarget);
      const filtro = new URLSearchParams();
      for (const [key, value] of dados) if (value) filtro.set(key, String(value));
      const tamanho = query.get("tamanho");
      if (tamanho) filtro.set("tamanho", tamanho);
      setQuery(filtro);
    }}>
      <label>Buscar por título<input name="busca" defaultValue={query.get("busca") || ""}/></label>
      <label>Situação<select name="situacao" defaultValue={query.get("situacao") || ""}>
        <option value="">Todas</option><option>Aberta</option><option value="Concluida">Concluída</option>
      </select></label>
      <label>Ordenação<select name="ordenarPor" defaultValue={query.get("ordenarPor") || "titulo"}>
        <option value="titulo">Título</option><option value="id">Identificador</option></select></label>
      <label>Direção<select name="direcao" defaultValue={query.get("direcao") || "asc"}>
        <option value="asc">Crescente</option><option value="desc">Decrescente</option></select></label>
      <button>Filtrar</button>
    </form>
    {estado.tipo === "idle" && <p role="status">Preparando a consulta…</p>}
    {estado.tipo === "loading" && <p role="status">Carregando tarefas…</p>}
    {estado.tipo === "error" && <div className="erro"><p role="alert">{estado.falha.message}</p>
      <button onClick={() => tentar(tentativa + 1)}>Tentar novamente</button></div>}
    {estado.tipo === "ok" && <>
      {estado.valor.itens.length ? <ul className="lista">{estado.valor.itens.map(item =>
        <li key={item.id}><Link to={"/tarefas/" + item.id}>{item.titulo}</Link><span>{item.situacao}</span></li>
      )}</ul> : <p role="status">Nenhuma tarefa encontrada.</p>}
      <div className="paginas"><button disabled={pagina <= 1} onClick={() => mudar(pagina - 1)}>Anterior</button>
        <span>Página {pagina} · {estado.valor.total} resultado(s)</span>
        <button disabled={pagina * Number(estado.valor.tamanho) >= Number(estado.valor.total)} onClick={() => mudar(pagina + 1)}>Próxima</button></div>
    </>}
  </>;
  function mudar(p: number) { const nova = new URLSearchParams(query); nova.set("pagina", String(p)); setQuery(nova); }
}

function Detalhes() {
  const {id = ""} = useParams();
  const estado = useRequisicao(signal => tarefas.obter(id, signal), id);
  return <><Link to="/tarefas">Voltar à lista</Link>
    {estado.tipo === "idle" || estado.tipo === "loading" ? <p role="status">Carregando detalhes…</p> : null}
    {estado.tipo === "error" && <h1 role="alert">{estado.falha.status === 404 ? "Tarefa inexistente." : estado.falha.message}</h1>}
    {estado.tipo === "ok" && <><h1>{estado.valor.titulo}</h1><p>{estado.valor.descricao}</p>
      <dl><dt>Situação</dt><dd>{estado.valor.situacao}</dd><dt>Prazo</dt><dd>{estado.valor.prazo || "Sem prazo"}</dd>
      <dt>Prioridade</dt><dd>{estado.valor.prioridade}</dd><dt>Equipe</dt><dd>{estado.valor.equipe}</dd></dl></>}
  </>;
}

function Privada({children}: {children: React.ReactNode}) {
  const {sessao, carregando} = useContext(Contexto);
  const local = useLocation();
  if (carregando) return <p role="status">Verificando a sessão…</p>;
  return sessao ? children : <Navigate to="/entrar" state={{voltar: local.pathname + local.search}} replace/>;
}

function Login() {
  const {atualizar} = useContext(Contexto);
  const ir = useNavigate();
  const local = useLocation();
  const [erro, setErro] = useState("");
  const [enviando, setEnviando] = useState(false);
  return <><h1>Entrar</h1><form className="vertical" onSubmit={async e => {
    e.preventDefault();
    const dados = new FormData(e.currentTarget);
    setErro(""); setEnviando(true);
    try {
      await gravar("/api/sessao/login", Object.fromEntries(dados)); await atualizar();
      const voltar = local.state?.voltar;
      ir(typeof voltar === "string" && voltar.startsWith("/tarefas") ? voltar : "/tarefas", {replace: true});
    } catch (falha) {setErro(mensagem(falha).message);}
    finally {setEnviando(false);}
  }}>
    <label>Usuário<input name="usuario" autoComplete="username" required/></label>
    <label>Senha<input name="senha" type="password" autoComplete="current-password" required/></label>
    {erro && <p role="alert">{erro}</p>}<button disabled={enviando}>{enviando ? "Entrando…" : "Entrar"}</button>
  </form></>;
}

function Cadastro() {
  const [erro, setErro] = useState<Falha | null>(null);
  const [enviando, setEnviando] = useState(false);
  const alerta = useRef<HTMLDivElement>(null);
  const ir = useNavigate();
  useEffect(() => {if (erro) alerta.current?.focus();}, [erro]);
  return <><h1>Nova tarefa</h1>
    <form className="vertical" onSubmit={async e => {
      e.preventDefault();
      const dados = Object.fromEntries(new FormData(e.currentTarget)) as Entrada;
      setErro(null); setEnviando(true);
      try {
        const tarefa = await tarefas.criar({...dados, prazo: dados.prazo || null});
        ir("/tarefas/" + tarefa.id);
      } catch (falha) {setErro(mensagem(falha));}
      finally {setEnviando(false);}
    }}>
      <label>Título<input name="titulo"/></label><label>Descrição<textarea name="descricao"/></label>
      <label>Prioridade<select name="prioridade"><option>Normal</option><option>Alta</option></select></label>
      <label>Prazo<input type="date" name="prazo"/></label>
      {erro && <div className="erro" role="alert" tabIndex={-1} ref={alerta}><strong>{erro.message}</strong>
        {Object.values(erro.campos).flat().map(m => <p key={m}>{m}</p>)}</div>}
      <button disabled={enviando}>{enviando ? "Salvando…" : "Salvar tarefa"}</button>
    </form>
  </>;
}

createRoot(document.getElementById("root")!).render(<BrowserRouter><Aplicacao/></BrowserRouter>);
