using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;

[ApiController, Route("api/tarefas")]
public sealed class ApiController(ServicoTarefas tarefas, IOutputCacheStore cache, IAuthorizationService autorizacao) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PaginaTarefas>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    public async Task<ActionResult<PaginaTarefas>> Listar(
        [FromQuery, Range(1, 1000000)] int pagina = 1, [FromQuery, Range(1, 50)] int tamanho = 10,
        [FromQuery] string ordenarPor = "titulo", [FromQuery] string direcao = "asc",
        [FromQuery] string? busca = null, [FromQuery] string? situacao = null, CancellationToken ct = default)
    {
        if (ordenarPor is not ("titulo" or "id") || direcao is not ("asc" or "desc") ||
            situacao is not (null or "Aberta" or "Concluida"))
            return Problem(statusCode: 400, title: "Filtros ou ordenação inválidos.");
        var consulta = tarefas.Consulta;
        if (!string.IsNullOrWhiteSpace(busca))
            consulta = consulta.Where(x => x.Titulo.Contains(busca));
        if (situacao is not null) consulta = consulta.Where(x => x.Situacao == situacao);
        var total = await consulta.CountAsync(ct);
        var ordenada = (ordenarPor, direcao) switch
        {
            ("titulo", "asc") => consulta.OrderBy(x => x.Titulo),
            ("titulo", "desc") => consulta.OrderByDescending(x => x.Titulo),
            ("id", "asc") => consulta.OrderBy(x => x.Id),
            _ => consulta.OrderByDescending(x => x.Id)
        };
        var itens = await ordenada.ThenBy(x => x.Id).Skip((pagina - 1) * tamanho).Take(tamanho)
            .Select(x => new ResumoTarefa(x.Id, x.Titulo, x.Situacao, x.Prioridade)).ToArrayAsync(ct);
        return new PaginaTarefas(itens, pagina, tamanho, total);
    }

    [Authorize, HttpGet("minhas")]
    [ProducesResponseType<ResumoTarefa[]>(200), ProducesResponseType<ProblemDetails>(401)]
    public async Task<ResumoTarefa[]> Minhas(CancellationToken ct)
    {
        var equipe = User.Equipe();
        return await tarefas.Consulta.Where(x => x.Equipe == equipe).OrderBy(x => x.Id)
            .Select(x => new ResumoTarefa(x.Id, x.Titulo, x.Situacao, x.Prioridade)).ToArrayAsync(ct);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType<DetalheTarefa>(200), ProducesResponseType<ProblemDetails>(404)]
    public async Task<ActionResult<DetalheTarefa>> Obter(int id, CancellationToken ct)
    {
        var item = await tarefas.Buscar(id, ct);
        return item is null ? Problem(statusCode: 404, title: "Tarefa inexistente.") : ServicoTarefas.Detalhe(item);
    }

    [Authorize, ValidateAntiForgeryToken, HttpPost]
    [ProducesResponseType<DetalheTarefa>(201), ProducesResponseType<ValidationProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    public async Task<ActionResult<DetalheTarefa>> Criar(TarefaInputModel input, CancellationToken ct)
    {
        using var activity = Telemetria.Traces.StartActivity("tarefas.criar");
        var item = await tarefas.Criar(input, User.Equipe(), ct);
        await cache.EvictByTagAsync("situacoes", ct);
        Telemetria.Criadas.Add(1);
        return CreatedAtAction(nameof(Obter), new { id = item.Id }, ServicoTarefas.Detalhe(item));
    }

    [Authorize, ValidateAntiForgeryToken, HttpPut("{id:int}")]
    [ProducesResponseType(204), ProducesResponseType<ValidationProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401), ProducesResponseType<ProblemDetails>(403)]
    [ProducesResponseType<ProblemDetails>(404), ProducesResponseType<ProblemDetails>(409)]
    public async Task<IActionResult> Editar(int id, AtualizarTarefa input, CancellationToken ct)
    {
        var item = await tarefas.Buscar(id, ct);
        if (item is null) return Problem(statusCode: 404, title: "Tarefa inexistente.");
        if (!await User.PodeEditar(item, autorizacao)) return Forbid();
        return await tarefas.Atualizar(item, input, ct) ? NoContent() :
            Problem(statusCode: 409, title: "A tarefa mudou. Recarregue os dados antes de tentar novamente.");
    }

    [Authorize(Roles = "Administrador"), ValidateAntiForgeryToken, HttpDelete("{id:int}")]
    [ProducesResponseType(204), ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(403), ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType<ProblemDetails>(409)]
    public async Task<IActionResult> Excluir(int id, [FromQuery] Guid versao, CancellationToken ct)
    {
        var item = await tarefas.Buscar(id, ct);
        if (item is null) return Problem(statusCode: 404, title: "Tarefa inexistente.");
        return await tarefas.Excluir(item, versao, ct) ? NoContent() : Problem(statusCode: 409, title: "Versão desatualizada.");
    }
}

[ApiController, Route("diagnostico")]
public sealed class DiagnosticoController(IWebHostEnvironment environment, PortalTarefasDbContext db) : ControllerBase
{
    [HttpGet("mvc")]
    public IActionResult Mvc() => Ok(new { aplicacao = "PortalTarefas", estado = "Ativo" });

    [HttpGet("falha")]
    public IActionResult Falha()
    {
        if (!environment.IsDevelopment()) return NotFound();
        throw new InvalidOperationException("Marcador interno de teste");
    }

    [HttpGet("painel")]
    public async Task<IActionResult> Painel([FromQuery] string situacao = "Aberta", CancellationToken ct = default)
    {
        var ef = await db.Tarefas.AsNoTracking().Where(x => x.Situacao == situacao)
            .GroupBy(x => x.Situacao).Select(g => new ContagemTarefas(g.Key, g.Count())).ToArrayAsync(ct);
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var comando = db.Database.GetDbConnection().CreateCommand();
            comando.CommandText = "SELECT Situacao, COUNT(*) FROM Tarefas WHERE Situacao = @situacao GROUP BY Situacao";
            var parametro = comando.CreateParameter();
            parametro.ParameterName = "@situacao";
            parametro.Value = situacao;
            comando.Parameters.Add(parametro);
            await using var leitor = await comando.ExecuteReaderAsync(ct);
            var ado = new List<ContagemTarefas>();
            while (await leitor.ReadAsync(ct)) ado.Add(new(leitor.GetString(0), leitor.GetInt32(1)));
            return Ok(new { ef, ado, iguais = ef.SequenceEqual(ado) });
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
