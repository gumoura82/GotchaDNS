using System.Collections.Concurrent;

namespace GotchaDNS.Engine;

public static class WhitelistStore
{
    private static readonly ConcurrentDictionary<string, byte> _whitelist = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsWhitelisted(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return false;
        return _whitelist.ContainsKey(domain);
    }

    public static void Add(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return;
        _whitelist[domain] = 1;
    }

    public static void Remove(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return;
        _whitelist.TryRemove(domain, out _);
    }

    public static IEnumerable<string> List() => _whitelist.Keys;
}
