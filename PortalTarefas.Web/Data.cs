using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

public sealed class Tarefa
{
    public int Id { get; set; }
    public string Titulo { get; set; } = "";
    public string Descricao { get; set; } = "";
    public string Situacao { get; set; } = "Aberta";
    public string Prioridade { get; set; } = "Normal";
    public DateOnly? Prazo { get; set; }
    public string Equipe { get; set; } = "A";
    public Guid Versao { get; set; } = Guid.NewGuid();
}

public sealed class Auditoria
{
    public int Id { get; set; }
    public int TarefaId { get; set; }
    public string Operacao { get; set; } = "";
    public DateTime InstanteUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PortalTarefasDbContext(DbContextOptions<PortalTarefasDbContext> options)
    : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Tarefa> Tarefas => Set<Tarefa>();
    public DbSet<Auditoria> Auditorias => Set<Auditoria>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        var tarefa = model.Entity<Tarefa>();
        tarefa.Property(x => x.Titulo).IsRequired().HasMaxLength(100);
        tarefa.Property(x => x.Descricao).IsRequired().HasMaxLength(500);
        tarefa.Property(x => x.Situacao).HasMaxLength(12);
        tarefa.Property(x => x.Prioridade).HasMaxLength(10);
        tarefa.Property(x => x.Equipe).HasMaxLength(1);
        tarefa.Property(x => x.Versao).IsConcurrencyToken();
        tarefa.HasIndex(x => new { x.Situacao, x.Titulo });
    }
}

public class TarefaInputModel : IValidatableObject
{
    [Required(ErrorMessage = "Informe o título.")]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "Use entre 3 e 100 caracteres.")]
    public string Titulo { get; set; } = "";
    [StringLength(500, ErrorMessage = "Use até 500 caracteres.")]
    public string Descricao { get; set; } = "";
    [RegularExpression("Normal|Alta", ErrorMessage = "Selecione Normal ou Alta.")]
    public string Prioridade { get; set; } = "Normal";
    [RegularExpression("Aberta|Concluida", ErrorMessage = "Selecione uma situação válida.")]
    public string Situacao { get; set; } = "Aberta";
    public DateOnly? Prazo { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Prioridade == "Alta" && Prazo is null)
            yield return new ValidationResult("Defina um prazo para prioridade alta.", [nameof(Prazo)]);
    }
}

public sealed class AtualizarTarefa : TarefaInputModel
{
    public Guid Versao { get; set; }
}

public record ResumoTarefa(int Id, string Titulo, string Situacao, string Prioridade);
public record DetalheTarefa(int Id, string Titulo, string Descricao, string Situacao,
    string Prioridade, DateOnly? Prazo, string Equipe, Guid Versao);
public record PaginaTarefas(ResumoTarefa[] Itens, int Pagina, int Tamanho, int Total);
public record ContagemTarefas(string Situacao, int Total);

public sealed class ServicoTarefas(PortalTarefasDbContext db)
{
    public IQueryable<Tarefa> Consulta => db.Tarefas.AsNoTracking();
    public Task<Tarefa?> Buscar(int id, CancellationToken ct) =>
        db.Tarefas.SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Tarefa> Criar(TarefaInputModel input, string equipe, CancellationToken ct)
    {
        var tarefa = new Tarefa { Equipe = equipe };
        Aplicar(tarefa, input);
        db.Add(tarefa);
        await db.SaveChangesAsync(ct);
        return tarefa;
    }

    public async Task<bool> Atualizar(Tarefa tarefa, AtualizarTarefa input, CancellationToken ct)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        Aplicar(tarefa, input);
        db.Entry(tarefa).Property(x => x.Versao).OriginalValue = input.Versao;
        tarefa.Versao = Guid.NewGuid();
        db.Add(new Auditoria { TarefaId = tarefa.Id, Operacao = "Atualização" });
        try
        {
            await db.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transacao.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task<bool> Excluir(Tarefa tarefa, Guid versao, CancellationToken ct)
    {
        db.Entry(tarefa).Property(x => x.Versao).OriginalValue = versao;
        db.Remove(tarefa);
        try { await db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateConcurrencyException) { return false; }
    }

    public static DetalheTarefa Detalhe(Tarefa x) => new(x.Id, x.Titulo, x.Descricao,
        x.Situacao, x.Prioridade, x.Prazo, x.Equipe, x.Versao);

    private static void Aplicar(Tarefa tarefa, TarefaInputModel input)
    {
        tarefa.Titulo = input.Titulo.Trim();
        tarefa.Descricao = input.Descricao?.Trim() ?? "";
        tarefa.Prazo = input.Prazo;
        tarefa.Prioridade = input.Prioridade;
        tarefa.Situacao = input.Situacao;
    }
}
