namespace GotchaDNS.Engine;

public interface IWhitelistRepository
{
    Task AddAsync(string domain);
    Task<bool> ExistsAsync(string domain);
    Task RemoveAsync(string domain);
    Task<IEnumerable<string>> ListAsync();
}
