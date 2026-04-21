using GotchaDNS.Engine.Data;
using Microsoft.EntityFrameworkCore;

namespace GotchaDNS.Engine;

public class WhitelistRepository : IWhitelistRepository
{
    private readonly GotchaDbContext _db;

    public WhitelistRepository(GotchaDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return;
        var existing = await _db.Whitelist.FirstOrDefaultAsync(w => w.Domain == domain);
        if (existing != null) return;
        _db.Whitelist.Add(new Data.WhitelistEntry { Domain = domain, CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();
    }

    public async Task<bool> ExistsAsync(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return false;
        return await _db.Whitelist.AnyAsync(w => w.Domain == domain);
    }

    public async Task RemoveAsync(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return;
        var existing = await _db.Whitelist.FirstOrDefaultAsync(w => w.Domain == domain);
        if (existing == null) return;
        _db.Whitelist.Remove(existing);
        await _db.SaveChangesAsync();
    }

    public async Task<IEnumerable<string>> ListAsync()
    {
        return await _db.Whitelist.Select(w => w.Domain).ToListAsync();
    }
}
