using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

public static class Telemetria
{
    public const string Nome = "PortalTarefas";
    public static readonly ActivitySource Traces = new(Nome);
    public static readonly Meter Meter = new(Nome);
    public static readonly Counter<long> Criadas = Meter.CreateCounter<long>("portal.tarefas.criadas");
    public static readonly Histogram<double> Duracao = Meter.CreateHistogram<double>("portal.http.duracao", "ms");
}

public sealed class CorrelacaoMiddleware(RequestDelegate next, ILogger<CorrelacaoMiddleware> log)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var id = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Correlation-ID"] = id;
            return Task.CompletedTask;
        });
        var inicio = Stopwatch.GetTimestamp();
        using var scope = log.BeginScope(new Dictionary<string, object?> { ["correlationId"] = id });
        log.LogInformation("Entrada {Method} {Path}", context.Request.Method, context.Request.Path);
        try { await next(context); }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(inicio).TotalMilliseconds;
            Telemetria.Duracao.Record(elapsed, new KeyValuePair<string, object?>("http.status_code", context.Response.StatusCode));
            log.LogInformation("Saída {Method} {Path} {Status} {ElapsedMs}", context.Request.Method,
                context.Request.Path, context.Response.StatusCode, elapsed);
        }
    }
}

public static class Identidades
{
    public const string Politica = "EditarOutraEquipe";
    public static string Equipe(this ClaimsPrincipal user) => user.FindFirstValue("equipe") ?? "";
    public static async Task<bool> PodeEditar(this ClaimsPrincipal user, Tarefa tarefa,
        Microsoft.AspNetCore.Authorization.IAuthorizationService autorizacao) =>
        user.Identity?.IsAuthenticated == true &&
        (user.Equipe() == tarefa.Equipe ||
         (await Microsoft.AspNetCore.Authorization.AuthorizationServiceExtensions.AuthorizeAsync(
             autorizacao, user, Politica)).Succeeded);

    public static async Task InicializarAsync(IServiceProvider services, bool ambienteDesenvolvimento)
    {
        var db = services.GetRequiredService<PortalTarefasDbContext>();
        await db.Database.MigrateAsync();
        var config = services.GetRequiredService<IConfiguration>();
        var senha = config["Laboratorio:Senha"];
        if (!ambienteDesenvolvimento || string.IsNullOrEmpty(senha)) return;
        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Usuario", "Administrador" })
            if (!await roles.RoleExistsAsync(role)) Checar(await roles.CreateAsync(new(role)));
        foreach (var (nome, perfil) in new[] { ("usuario", "Usuario"), ("admin", "Administrador") })
        {
            if (await users.FindByNameAsync(nome) is not null) continue;
            var user = new IdentityUser { UserName = nome };
            Checar(await users.CreateAsync(user, senha));
            Checar(await users.AddToRoleAsync(user, perfil));
            Checar(await users.AddClaimAsync(user, new Claim("equipe", "A")));
            if (perfil == "Administrador")
                Checar(await users.AddClaimAsync(user, new Claim("tarefas", "editar-outra-equipe")));
        }
    }
    private static void Checar(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException("Revise a configuração externa das contas de laboratório.");
    }
}
