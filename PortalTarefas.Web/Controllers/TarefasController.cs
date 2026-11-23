using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

public sealed class MedirAcao(ILogger<MedirAcao> log) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var inicio = Stopwatch.GetTimestamp();
        var executed = await next();
        log.LogInformation("MVC {Action} {Resultado} {ModelStateValido} {ElapsedMs}", context.ActionDescriptor.DisplayName,
            executed.Result?.GetType().Name, context.ModelState.IsValid, Stopwatch.GetElapsedTime(inicio).TotalMilliseconds);
    }
}

[ServiceFilter(typeof(MedirAcao))]
public sealed class TarefasController(ServicoTarefas tarefas, IWebHostEnvironment env,
    SignInManager<IdentityUser> signIn, IAuthorizationService autorizacao) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await tarefas.Consulta.OrderBy(x => x.Titulo).ToArrayAsync(ct));

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var tarefa = await tarefas.Buscar(id, ct);
        return tarefa is null ? NotFound() : View(tarefa);
    }

    [HttpGet]
    public IActionResult Login() => env.IsDevelopment() ? View() : NotFound();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string usuario, string senha)
    {
        if (!env.IsDevelopment()) return NotFound();
        var tentativa = await signIn.PasswordSignInAsync(usuario, senha, false, true);
        if (tentativa.Succeeded) return RedirectToAction(nameof(Index));
        ModelState.AddModelError("", "Credenciais inválidas.");
        return View();
    }

    [HttpGet]
    public IActionResult Create() => PodeGravar() ? View("Form", new AtualizarTarefa()) : Challenge();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AtualizarTarefa modelo, CancellationToken ct)
    {
        if (!PodeGravar()) return Challenge();
        if (!ModelState.IsValid) return View("Form", modelo);
        var tarefa = await tarefas.Criar(modelo, User.Equipe(), ct);
        return RedirectToAction(nameof(Details), new { id = tarefa.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        if (!PodeGravar()) return Challenge();
        var tarefa = await tarefas.Buscar(id, ct);
        if (tarefa is null) return NotFound();
        if (!await User.PodeEditar(tarefa, autorizacao)) return Forbid();
        return View("Form", new AtualizarTarefa
        {
            Titulo = tarefa.Titulo, Descricao = tarefa.Descricao, Situacao = tarefa.Situacao,
            Prazo = tarefa.Prazo, Prioridade = tarefa.Prioridade, Versao = tarefa.Versao
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, AtualizarTarefa modelo, CancellationToken ct)
    {
        if (!PodeGravar()) return Challenge();
        var tarefa = await tarefas.Buscar(id, ct);
        if (tarefa is null) return NotFound();
        if (!await User.PodeEditar(tarefa, autorizacao)) return Forbid();
        if (!ModelState.IsValid) return View("Form", modelo);
        if (await tarefas.Atualizar(tarefa, modelo, ct)) return RedirectToAction(nameof(Details), new { id });
        ModelState.AddModelError("", "A tarefa mudou. Recarregue a edição antes de salvar.");
        Response.StatusCode = 409;
        return View("Form", modelo);
    }

    private bool PodeGravar() => User.Identity?.IsAuthenticated == true;
}
