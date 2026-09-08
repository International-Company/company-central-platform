using System.Diagnostics.CodeAnalysis;
using CCP.Kernel.Primitives;
using CCP.Modules.Authorization.Application;
using CCP.Modules.Authorization.Application.Abstractions;
using CCP.Modules.Authorization.Domain.Scopes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CCP.Modules.Authorization.Infrastructure.Persistence;

/// <summary>
/// The permission version stamp, held in the database.
/// <para>
/// One row, one number that only increases. Every change to roles, role
/// permissions, grants or the organizational tree bumps it, and every cached
/// permission set carries the stamp it was computed at.
/// </para>
/// <para>
/// <b>Why not an in-memory counter.</b> With several application instances, an
/// in-memory version would let instance B keep serving access that instance A
/// revoked — the revocation would be invisible to it until something else
/// evicted the entry. Reading one row per request is the cost of that being
/// correct, and it is a fraction of the permission join it replaces.
/// </para>
/// </summary>
public sealed class PermissionVersionStore(
    AuthorizationDbContext dbContext,
    IClock clock) : IPermissionVersionStore
{
    public async Task<long> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        PermissionVersionRow? row = await dbContext.PermissionVersion
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == PermissionVersionRow.SingletonId, cancellationToken);

        // A missing row means the stamp has never been written. Treating that as
        // version zero is safe: every cached set would carry a different value
        // and be discarded, so the failure mode is recomputation, not stale
        // access.
        return row?.Version ?? 0;
    }

    public async Task BumpAsync(CancellationToken cancellationToken = default)
    {
        // A single atomic UPDATE rather than read-modify-write. Two concurrent
        // revocations must both raise the stamp; with read-modify-write one
        // could overwrite the other and leave a version that some cache still
        // matches.
        int updated = await dbContext.PermissionVersion
            .Where(v => v.Id == PermissionVersionRow.SingletonId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(v => v.Version, v => v.Version + 1)
                    .SetProperty(v => v.UpdatedAt, clock.UtcNow),
                cancellationToken);

        if (updated == 0)
        {
            // First bump on a fresh database.
            dbContext.PermissionVersion.Add(new PermissionVersionRow
            {
                Id = PermissionVersionRow.SingletonId,
                Version = 1,
                UpdatedAt = clock.UtcNow
            });

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>
/// In-process cache of resolved permissions.
/// <para>
/// Correctness does not depend on instances agreeing about cache contents: each
/// checks the shared version stamp itself, so a stale entry is detected wherever
/// it lives. That is what makes an in-process cache sufficient here, and a
/// distributed one unnecessary infrastructure (P1).
/// </para>
/// <para>
/// Entries are bounded and expire on their own, so a process that runs for
/// months does not accumulate a set for every user who ever signed in.
/// </para>
/// </summary>
public sealed class EffectivePermissionCache(IMemoryCache cache) : IEffectivePermissionCache
{
    private static readonly TimeSpan SlidingExpiration = TimeSpan.FromMinutes(20);

    public bool TryGet(Guid userId, [NotNullWhen(true)] out EffectivePermissions? permissions)
        => cache.TryGetValue(KeyFor(userId), out permissions) && permissions is not null;

    public void Set(EffectivePermissions permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        cache.Set(KeyFor(permissions.UserId), permissions, new MemoryCacheEntryOptions
        {
            SlidingExpiration = SlidingExpiration,

            // Bounded so one process cannot accumulate an entry per user
            // indefinitely. Eviction only costs a recomputation.
            Size = 1
        });
    }

    public void Invalidate(Guid userId) => cache.Remove(KeyFor(userId));

    private static string KeyFor(Guid userId) => $"authz:permissions:{userId:N}";
}
