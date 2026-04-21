using Microsoft.EntityFrameworkCore;

namespace GotchaDNS.Engine.Data;

public class GotchaDbContext : DbContext
{
    public GotchaDbContext(DbContextOptions<GotchaDbContext> options) : base(options) { }

    public DbSet<WhitelistEntry> Whitelist { get; set; } = null!;
    public DbSet<DnsLogEntity> DnsLogs { get; set; } = null!;
}

public class WhitelistEntry
{
    public int Id { get; set; }
    public string Domain { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class DnsLogEntity
{
    public int Id { get; set; }
    public string Timestamp { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
