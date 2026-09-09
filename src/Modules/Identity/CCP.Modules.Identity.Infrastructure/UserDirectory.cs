using CCP.Modules.Identity.Contracts;
using CCP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CCP.Modules.Identity.Infrastructure;

/// <summary>
/// Implements Identity's public surface.
/// <para>
/// Two columns, read-only, and no path from here to a credential. Disabled and
/// locked accounts return nothing: writing to somebody whose access has been
/// withdrawn is the wrong side of a decision somebody already took.
/// </para>
/// </summary>
public sealed class UserDirectory(IdentityDbContext dbContext) : IUserDirectory
{
    public async Task<string?> GetEmailAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.Status == Domain.Users.UserStatus.Active)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<string?> GetDisplayNameAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);
}
