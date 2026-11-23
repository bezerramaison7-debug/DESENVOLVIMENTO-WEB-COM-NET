using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Http.Resilience;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Polly;

if (args is ["--probe"])
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    try
    {
        using var response = await client.GetAsync("http://127.0.0.1:8080/health/ready");
        Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (HttpRequestException) { Environment.ExitCode = 1; }
    catch (TaskCanceledException) { Environment.ExitCode = 1; }
    return;
}

var builder = WebApplication.CreateBuilder(args);
var dbConnection = builder.Configuration.GetConnectionString("Portal");
if (string.IsNullOrWhiteSpace(dbConnection))
    throw new InvalidOperationException("Configure ConnectionStrings__Portal antes de iniciar.");
var dependency = builder.Configuration["Portal:Dependencia"];
if (!Uri.TryCreate(dependency, UriKind.Absolute, out var dependencyUri) ||
    dependencyUri.Scheme is not ("http" or "https"))
    throw new InvalidOperationException("Configure Portal__Dependencia com uma URI HTTP ou HTTPS.");

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(x => x.IncludeScopes = true);
builder.Services.AddProblemDetails();
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<MedirAcao>();
builder.Services.AddScoped<ServicoTarefas>();
builder.Services.AddDbContextPool<PortalTarefasDbContext>(x => x.UseSqlite(dbConnection)
    .UseSeeding((db, _) => Seed.Inserir(db))
    .UseAsyncSeeding(async (db, _, ct) => await Seed.InserirAsync(db, ct)));
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 12;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
}).AddEntityFrameworkStores<PortalTarefasDbContext>();
builder.Services.ConfigureApplicationCookie(x =>
{
    x.Cookie.HttpOnly = true;
    x.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    x.Cookie.SameSite = SameSiteMode.Lax;
    x.LoginPath = "/entrar";
    x.Events.OnRedirectToLogin = ctx => ResponderOuRedirecionar(ctx, 401);
    x.Events.OnRedirectToAccessDenied = ctx => ResponderOuRedirecionar(ctx, 403);
});
builder.Services.Configure<SecurityStampValidatorOptions>(x => x.ValidationInterval = TimeSpan.Zero);
builder.Services.AddAntiforgery(x =>
{
    x.HeaderName = "X-CSRF-TOKEN";
    x.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});
builder.Services.AddAuthorization(x => x.AddPolicy(Identidades.Politica,
    policy => policy.RequireClaim("tarefas", "editar-outra-equipe")));
builder.Services.AddCors(x => x.AddPolicy("FrontendLocal", policy =>
    policy.WithOrigins("http://localhost:5173").WithMethods("GET").WithHeaders("Content-Type")));
builder.Services.AddOpenApi();
builder.Services.AddRateLimiter(x =>
{
    x.RejectionStatusCode = 429;
    x.AddFixedWindowLimiter("publico", policy =>
    {
        policy.Window = TimeSpan.FromSeconds(10);
        policy.PermitLimit = 5;
        policy.QueueLimit = 0;
    });
});
builder.Services.AddOutputCache(x => x.AddPolicy("situacoes", p =>
    p.Expire(TimeSpan.FromSeconds(20)).Tag("situacoes")));
builder.Services.AddHttpClient("dependencia", x => x.BaseAddress = dependencyUri)
    .AddStandardResilienceHandler(x =>
    {
        x.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
        x.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(8);
        x.Retry.MaxRetryAttempts = 2;
        x.Retry.Delay = TimeSpan.FromMilliseconds(150);
        x.Retry.DisableForUnsafeHttpMethods();
        x.CircuitBreaker.MinimumThroughput = 3;
        x.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
        x.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(12);
    });
builder.Services.AddHealthChecks()
    .AddCheck<BancoSaude>("banco", tags: ["ready"])
    .AddCheck<DependenciaSaude>("dependencia", tags: ["ready"]);

var telemetry = builder.Services.AddOpenTelemetry();
telemetry.WithTracing(x =>
{
    x.AddSource(Telemetria.Nome).AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation(o => o.EnrichWithHttpRequestMessage = (activity, request) =>
        {
            // Keep request metadata independent from query values.
            activity.SetTag("portal.dependencia.metodo", request.Method.Method);
        });
    if (builder.Configuration.GetValue<bool>("Portal:ExportarTelemetria")) x.AddConsoleExporter();
});
telemetry.WithMetrics(x =>
{
    x.AddMeter(Telemetria.Nome).AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
    if (builder.Configuration.GetValue<bool>("Portal:ExportarTelemetria"))
        x.AddConsoleExporter((_, reader) => reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 5000);
});

var app = builder.Build();
using (var scope = app.Services.CreateScope())
    await Identidades.InicializarAsync(scope.ServiceProvider, app.Environment.IsDevelopment());

app.UseMiddleware<CorrelacaoMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (builder.Configuration.GetValue("Portal:RedirecionarHttps", true)) app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseCors("FrontendLocal");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseOutputCache();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapGet("/docs", () => Results.Content("""
        <!doctype html><html lang="pt-BR"><meta charset="utf-8"><title>Contrato da API</title>
        <main><h1>Portal de Tarefas — API</h1><a href="/openapi/v1.json">Documento OpenAPI</a>
        <p>Inspeção local de recursos e operações do contrato.</p><ul id="paths"></ul>
        <label>Consulta GET pública <select id="resource">
        <option>/api/tarefas</option><option>/api/tarefas/1</option></select></label>
        <button id="run">Consultar</button><pre id="output"></pre></main>
        <script>
        fetch('/openapi/v1.json').then(r=>r.json()).then(s=>{
        for (const [route, ops] of Object.entries(s.paths)) {
        const item=document.createElement('li');item.textContent=Object.keys(ops).join(', ').toUpperCase()+' '+route;
        document.querySelector('#paths').append(item);}
        });
        document.querySelector('#run').onclick=async()=> {
        const response=await fetch(document.querySelector('#resource').value);
        document.querySelector('#output').textContent=response.status+'\n'+JSON.stringify(await response.json(),null,2);
        };
        </script></html>
        """, "text/html; charset=utf-8")).ExcludeFromDescription();
}

var diagnostico = app.MapGroup("/diagnostico");
diagnostico.MapPost("/tarefas", async (TarefaInputModel input, HttpContext context, IAntiforgery csrf,
    ServicoTarefas servico, CancellationToken ct) =>
{
    if (!await csrf.IsRequestValidAsync(context)) return Results.Problem(statusCode: 400, title: "Validação da solicitação inválida.");
    var erros = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
    if (!System.ComponentModel.DataAnnotations.Validator.TryValidateObject(input,
        new(input), erros, validateAllProperties: true))
        return Results.ValidationProblem(erros.SelectMany(x => x.MemberNames.Select(m => (m, x.ErrorMessage!)))
            .GroupBy(x => x.m).ToDictionary(x => x.Key, x => x.Select(e => e.Item2).ToArray()));
    var tarefa = await servico.Criar(input, context.User.Equipe(), ct);
    return Results.Created($"/api/tarefas/{tarefa.Id}", ServicoTarefas.Detalhe(tarefa));
}).RequireAuthorization().ExcludeFromDescription();
diagnostico.MapGet("/minimal", () => new { aplicacao = "PortalTarefas", estado = "Ativo" });
diagnostico.MapGet("/binding/{id:int}", async (int id, string? situacao,
    [FromHeader(Name = "X-Cliente")] string? cliente, ServicoTarefas servico, CancellationToken ct) =>
{
    var item = await servico.Consulta.FirstOrDefaultAsync(x => x.Id == id, ct);
    return item is null ? Results.Problem(statusCode: 404, title: "Tarefa inexistente")
        : Results.Ok(new { id, situacao, cliente, tarefa = ServicoTarefas.Detalhe(item) });
}).ExcludeFromDescription();
diagnostico.MapGet("/limite", () => Results.Ok(new { estado = "disponível" }))
    .RequireRateLimiting("publico");
app.MapGet("/api/situacoes", () => new { estados = new[] { "Aberta", "Concluida" }, gerado = Guid.NewGuid() })
    .CacheOutput("situacoes");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = x => x.Tags.Contains("ready") });
app.MapControllers();
app.MapControllerRoute("mvc", "mvc/{controller=Tarefas}/{action=Index}/{id?}");
app.MapGet("/", () => Results.Redirect(System.IO.File.Exists(Path.Combine(app.Environment.WebRootPath, "index.html"))
    ? "/tarefas" : "/mvc/Tarefas"));
app.MapFallbackToFile("tarefas/{*path:nonfile}", "index.html");
app.MapFallbackToFile("entrar", "index.html");
await app.RunAsync();

static Task ResponderOuRedirecionar(Microsoft.AspNetCore.Authentication.RedirectContext<
    Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions> context, int status)
{
    if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/diagnostico")) context.Response.StatusCode = status;
    else context.Response.Redirect(context.RedirectUri);
    return Task.CompletedTask;
}
