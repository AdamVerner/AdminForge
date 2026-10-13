using System.Collections.Concurrent;

namespace AdminForge.Mcp;

/// <summary>
/// The refresh tokens presented in the last few seconds. A client that meets a 401 on two requests at
/// once refreshes twice with the same token; the second is a race, not a replay.
/// </summary>
/// <remarks>Held per process: with several instances behind a balancer, a race that crosses them still ends the connection.</remarks>
internal sealed class RefreshGrace
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<(string Cid, int Counter), DateTime> _presented = new();

    /// <summary>Starts the token's window unless a racing request already did; call it only while the token is the newest.</summary>
    public void Presented(string cid, int counter, DateTime now)
    {
        foreach (var (key, at) in _presented)
            if (now - at > Window)
                _presented.TryRemove(key, out _);
        _presented.TryAdd((cid, counter), now);
    }

    public bool Fresh(string cid, int counter, DateTime now) =>
        _presented.TryGetValue((cid, counter), out var at) && now - at <= Window;
}
