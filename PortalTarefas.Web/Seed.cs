using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Polly.CircuitBreaker;

public static class Seed
{
    private static Tarefa[] Exemplos() =>
    [
        new() { Id = 1, Titulo = "Conferir documentação", Descricao = "Revisar o manual", Equipe = "A" },
        new() { Id = 2, Titulo = "Publicar agenda", Descricao = "Equipe B", Equipe = "B" },
        new() { Id = 3, Titulo = "Revisar laboratório", Descricao = "Equipamentos", Equipe = "A", Situacao = "Concluida" },
        new() { Id = 4, Titulo = "Organizar bancada", Descricao = "Atividade prática", Equipe = "A" }
    ];

    public static void Inserir(DbContext db)
    {
        if (db.Set<Tarefa>().Any()) return;
        db.AddRange(Exemplos());
        db.SaveChanges();
    }

    public static async Task InserirAsync(DbContext db, CancellationToken ct)
    {
        if (await db.Set<Tarefa>().AnyAsync(ct)) return;
        db.AddRange(Exemplos());
        await db.SaveChangesAsync(ct);
    }
}

public sealed class BancoSaude(IServiceScopeFactory factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct)
    {
        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalTarefasDbContext>();
        return await db.Database.CanConnectAsync(ct) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
    }
}

public sealed class DependenciaSaude(IHttpClientFactory factory, ILogger<DependenciaSaude> log) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct)
    {
        try
        {
            using var response = await factory.CreateClient("dependencia").GetAsync("", ct);
            return response.IsSuccessStatusCode ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
        }
        catch (BrokenCircuitException)
        {
            log.LogWarning("Circuito da dependência aberto");
            return HealthCheckResult.Unhealthy();
        }
        catch (HttpRequestException)
        {
            log.LogWarning("Dependência indisponível");
            return HealthCheckResult.Unhealthy();
        }
    }
}
