namespace BuildingBlocks.Caching.Redis;

/// <summary>
/// A best-effort mutual exclusion lock across service instances.
///
/// Used in InventoryService to serialise reservations for one product across replicas.
/// Note "best effort": this is a single-node Redis lock, so a Redis failover can hand
/// the same lock to two holders. That is acceptable here only because the database
/// row-level lock underneath is the real guarantee — the Redis lock just keeps
/// contention off the database. Never rely on it alone for correctness.
/// </summary>
public interface IDistributedLock
{
    /// <summary>Returns null if the lock could not be taken within <paramref name="waitTime"/>.</summary>
    Task<IAsyncDisposable?> AcquireAsync(
        string resource, TimeSpan expiry, TimeSpan waitTime, CancellationToken ct = default);
}
