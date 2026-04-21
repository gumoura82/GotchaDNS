using GotchaDNS.Engine;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// Permite que a Interface (Photino/Vite) converse com o Motor sem ser bloqueada
builder.Services.AddCors(options => {
    options.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

builder.Services.AddHostedService<DnsMotorService>();

// Configura DB e EF Core (SQLite local)
builder.Services.AddDbContext<GotchaDNS.Engine.Data.GotchaDbContext>(options =>
    options.UseSqlite("Data Source=gotcha.db;"));

// Repository/DAO mínimo para whitelist
builder.Services.AddScoped<GotchaDNS.Engine.IWhitelistRepository, GotchaDNS.Engine.WhitelistRepository>();

var app = builder.Build();
app.UseCors("AllowAll");

// Aplica migrações automáticas ao iniciar (cria DB se necessário)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GotchaDNS.Engine.Data.GotchaDbContext>();
    db.Database.EnsureCreated();
}

// A Ponte: Aqui o TypeScript puxa os logs reais da memória do C#
app.MapGet("/api/logs", () => {
    return Results.Ok(DnsMotorService.BlockedLogs.Reverse());
});

// A Rota do botão "Liberar"
app.MapPost("/api/whitelist", async (WhitelistRequest req, IWhitelistRepository repo) => {
    var domain = (req.domain ?? string.Empty).Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(domain) || !System.Text.RegularExpressions.Regex.IsMatch(domain, "^[a-z0-9.-]+$"))
    {
        return Results.BadRequest("domain inválido");
    }

    Console.WriteLine($"\n[UI] Usuário clicou para liberar o domínio: {domain}\n");
    await repo.AddAsync(domain);
    // Também atualiza a store em memória para uso imediato
    WhitelistStore.Add(domain);
    return Results.Ok();
});

app.MapGet("/api/whitelist", async (IWhitelistRepository repo) =>
{
    var list = await repo.ListAsync();
    return Results.Ok(list);
});

app.MapDelete("/api/whitelist", async (string domain, IWhitelistRepository repo) =>
{
    await repo.RemoveAsync(domain);
    WhitelistStore.Remove(domain);
    return Results.Ok();
});

app.Run("http://localhost:5005");

// Estruturas de dados
record WhitelistRequest(string domain);
public record DnsLogEntry(int id, string timestamp, string domain, string clientIp, string reason);