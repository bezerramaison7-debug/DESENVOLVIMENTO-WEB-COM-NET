import assert from "node:assert/strict";
import {access, mkdir} from "node:fs/promises";
import {test} from "node:test";
import {chromium} from "playwright";

const base = process.env.PORTAL_TEST_BASE || "https://localhost:7143";
const out = process.env.PORTAL_EVIDENCIAS || "../evidencias";
const senha = process.env.Laboratorio__Senha;
const localChromium = await access("/usr/bin/chromium").then(() => "/usr/bin/chromium", () => undefined);
const navegador = await chromium.launch({executablePath: process.env.CHROMIUM_PATH || localChromium});
await mkdir(out, {recursive:true});
async function contexto() {
  return navegador.newContext({ignoreHTTPSErrors:true, viewport:{width:1100,height:720}});
}
async function ver(page, text) {
  await page.getByText(text, {exact:true}).first().waitFor({state:"visible"});
}
async function capturar(page, name) {await page.screenshot({path: out + "/" + name + ".png"});}
async function entrar(page, perfil) {
  await page.getByLabel("Usuário").fill(perfil);
  await page.getByLabel("Senha").fill(senha);
  await page.getByRole("button",{name:"Entrar", exact:true}).click();
}

test("visitante observa estados, filtros, página por URL e contrato OpenAPI", async () => {
  const ctx = await contexto();
  const page = await ctx.newPage();
  await page.addInitScript(() => {
    if (!location.pathname.startsWith("/tarefas")) return;
    const original = window.requestAnimationFrame.bind(window);
    let primeiro = true;
    window.requestAnimationFrame = (callback) => {
      if (!primeiro) return original(callback);
      primeiro = false;
      return window.setTimeout(() => callback(performance.now()), 600);
    };
  });
  await page.route("**/api/tarefas?**", async route => {
    await new Promise(resolve=>setTimeout(resolve, 900));
    await route.continue();
  });
  await page.goto(base + "/tarefas?tamanho=1");
  await ver(page,"Preparando a consulta…");
  await capturar(page,"r5-inicial");
  await ver(page,"Carregando tarefas…");
  await capturar(page,"r5-carregando");
  await page.locator("ul.lista li").first().waitFor();
  await capturar(page,"r5-sucesso");
  await page.getByRole("button",{name:"Próxima"}).click();
  await page.waitForURL(/pagina=2/);
  await page.getByLabel("Buscar por título").fill("Sem resultado");
  await page.getByRole("button",{name:"Filtrar"}).click();
  await page.waitForURL(/busca=Sem\+resultado/);
  await ver(page,"Nenhuma tarefa encontrada.");
  await capturar(page,"r5-vazio");
  await page.goto(base + "/tarefas/1");
  await ver(page,"Conferir documentação");
  await page.goto(base + "/tarefas?ordenarPor=naoPermitido");
  await ver(page,"Filtros ou ordenação inválidos.");
  await capturar(page,"r5-http-erro");
  await page.unroute("**/api/tarefas?**");
  await page.route("**/api/tarefas?**", route=>route.abort());
  await page.goto(base + "/tarefas");
  await ver(page,"Falha de rede. Tente novamente.");
  await capturar(page,"r5-rede-erro");
  await page.goto(base + "/tarefas/9999");
  await ver(page,"Tarefa inexistente.");
  await page.goto(base + "/docs");
  await page.getByRole("button",{name:"Consultar"}).click();
  await page.locator("#output").filter({hasText:"200"}).waitFor();
  await capturar(page,"r4-documentacao");
  await ctx.close();
});

test("usuário comum usa telas MVC, validação Razor e cadastro React", async () => {
  const ctx = await contexto();
  const page = await ctx.newPage();
  await page.goto(base + "/mvc/Tarefas");
  await ver(page,"Conferir documentação");
  await capturar(page,"r2-lista");
  await page.getByRole("link",{name:"Conferir documentação"}).click();
  await capturar(page,"r2-detalhes");
  assert.equal((await ctx.request.get(base + "/mvc/Tarefas/Details/9999")).status(),404);
  await page.goto(base + "/mvc/Tarefas/Login");
  await entrar(page,"usuario");
  await page.waitForURL(/\/mvc(\/Tarefas)?\/?$/);
  await page.getByRole("link",{name:"Nova tarefa"}).click();
  await page.getByLabel("Descrição").fill("Descrição preservada");
  await page.getByRole("button",{name:"Salvar"}).click();
  await ver(page,"Informe o título.");
  assert.equal(await page.getByLabel("Descrição").inputValue(),"Descrição preservada");
  await page.getByLabel("Título").fill("Tarefa via MVC");
  await page.getByLabel("Prioridade").selectOption("Alta");
  await page.getByRole("button",{name:"Salvar"}).click();
  await ver(page,"Defina um prazo para prioridade alta.");
  await capturar(page,"r2-validacao");
  await page.getByLabel("Prazo").fill("2026-12-01");
  await page.getByRole("button",{name:"Salvar"}).click();
  await ver(page,"Tarefa via MVC");
  await page.getByRole("link",{name:"Editar tarefa"}).click();
  await page.getByLabel("Título").fill("Tarefa MVC editada");
  await page.getByRole("button",{name:"Salvar"}).click();
  await ver(page,"Tarefa MVC editada");
  await page.goto(base + "/tarefas/nova");
  await page.getByRole("heading",{name:"Nova tarefa"}).waitFor();
  assert.equal(await page.getByText("Administrador",{exact:true}).count(),0);
  await page.getByRole("button",{name:"Salvar tarefa"}).click();
  await ver(page,"Informe o título.");
  assert.equal(await page.getByRole("alert").evaluate(node=>document.activeElement===node), true);
  await capturar(page,"r5-validacao");
  await page.getByLabel("Título").fill("Tarefa via React");
  await page.evaluate(()=>window.portalMarcaDeNavegacao = 42);
  await page.getByRole("button",{name:"Salvar tarefa"}).click();
  await ver(page,"Tarefa via React");
  assert.equal(await page.evaluate(()=>window.portalMarcaDeNavegacao),42);
  await ctx.close();
});

test("administrador restaura sessão, retorna ao formulário e encerra acesso", async () => {
  const ctx = await contexto();
  const page = await ctx.newPage();
  await page.goto(base + "/tarefas/nova");
  await page.waitForURL("**/entrar");
  await entrar(page,"admin");
  await page.waitForURL("**/tarefas/nova");
  await ver(page,"Administrador");
  await page.reload();
  await ver(page,"Administrador");
  await capturar(page,"r6-admin");
  await page.getByRole("button",{name:"Sair"}).click();
  await page.waitForURL("**/tarefas");
  await page.goto(base + "/tarefas/nova");
  await page.waitForURL("**/entrar");
  await ctx.close();
});

test.after(()=>navegador.close());
