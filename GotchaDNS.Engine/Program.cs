using GotchaDNS.Engine;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// Permite que a Interface (Photino/Vite) converse com o Motor sem ser bloqueada
builder.Services.AddCors(options => {
    options.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

builder.Services.AddHostedService<DnsMotorService>();

var app = builder.Build();
app.UseCors("AllowAll");

// A Ponte: Aqui o TypeScript puxa os logs reais da memória do C#
app.MapGet("/api/logs", () => {
    return Results.Ok(DnsMotorService.BlockedLogs.Reverse());
});

// A Rota do botão "Liberar"
app.MapPost("/api/whitelist", (WhitelistRequest req) => {
    Console.WriteLine($"\n[UI] Usuário clicou para liberar o domínio: {req.domain}\n");
    return Results.Ok();
});

app.Run("http://localhost:5005");

// Estruturas de dados
record WhitelistRequest(string domain);
public record DnsLogEntry(int id, string timestamp, string domain, string clientIp, string reason);