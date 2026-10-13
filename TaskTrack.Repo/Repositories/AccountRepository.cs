using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TaskTrack.Repo.Models;

namespace TaskTrack.Repo.Repositories;

public class AccountRepository(TaskManagementDbContext db)
{
    public async Task<IDbContextTransaction> BeginAccountChangeAsync(CancellationToken ct)
    {
        var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // Serialize account changes so concurrent demotions/deletions cannot remove all Admins.
            await db.Database.ExecuteSqlRawAsync("LOCK TABLE \"SystemAccount\" IN SHARE ROW EXCLUSIVE MODE", ct);
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    public Task<int> CountAdminsAsync(CancellationToken ct) => db.Accounts.CountAsync(x => x.Role == 1, ct);
    public Task<List<SystemAccount>> GetAllAsync(CancellationToken ct) => db.Accounts.AsNoTracking().OrderBy(x => x.AccountId).ToListAsync(ct);
    public Task<SystemAccount?> FindAsync(int id, CancellationToken ct) => db.Accounts.FirstOrDefaultAsync(x => x.AccountId == id, ct);
    public Task<SystemAccount?> FindByEmailAsync(string email, CancellationToken ct) => db.Accounts.FirstOrDefaultAsync(x => x.Email.ToLower() == email, ct);
    public Task<bool> HasTasksAsync(int id, CancellationToken ct) => db.Tasks.AnyAsync(x => x.CreatedById == id, ct);
    public Task AddAsync(SystemAccount account, CancellationToken ct) => db.Accounts.AddAsync(account, ct).AsTask();
    public void Remove(SystemAccount account) => db.Accounts.Remove(account);
    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    public Task<SystemAccount?> ReadAsync(int id, CancellationToken ct) => db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.AccountId == id, ct);

    public Task<int> StartSessionAsync(SystemAccount account, string hash, DateTime expires, Guid family, CancellationToken ct) =>
        db.Accounts.Where(x => x.AccountId == account.AccountId && x.TokenVersion == account.TokenVersion && x.PasswordHash == account.PasswordHash)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.RefreshTokenHash, hash).SetProperty(x => x.RefreshTokenExpiresAt, expires).SetProperty(x => x.RefreshTokenFamilyId, family), ct);

    public Task<int> RotateAsync(int id, int version, Guid family, string previousHash, string nextHash, CancellationToken ct) =>
        db.Accounts.Where(x => x.AccountId == id && x.TokenVersion == version && x.RefreshTokenFamilyId == family && x.RefreshTokenHash == previousHash && x.RefreshTokenExpiresAt > DateTime.UtcNow)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.RefreshTokenHash, nextHash), ct);

    public Task<int> RevokeAsync(int id, int version, Guid family, CancellationToken ct) =>
        db.Accounts.Where(x => x.AccountId == id && x.TokenVersion == version && x.RefreshTokenFamilyId == family)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.TokenVersion, x => x.TokenVersion + 1)
                .SetProperty(x => x.RefreshTokenHash, (string?)null).SetProperty(x => x.RefreshTokenExpiresAt, (DateTime?)null).SetProperty(x => x.RefreshTokenFamilyId, (Guid?)null), ct);

    public Task<int> UpdateAsync(int id, string? name, short? role, CancellationToken ct) =>
        db.Accounts.Where(x => x.AccountId == id).ExecuteUpdateAsync(set => set
            .SetProperty(x => x.FullName, x => name ?? x.FullName).SetProperty(x => x.Role, x => role ?? x.Role)
            .SetProperty(x => x.TokenVersion, x => role != null && role != x.Role ? x.TokenVersion + 1 : x.TokenVersion)
            .SetProperty(x => x.RefreshTokenHash, x => role != null && role != x.Role ? null : x.RefreshTokenHash)
            .SetProperty(x => x.RefreshTokenExpiresAt, x => role != null && role != x.Role ? null : x.RefreshTokenExpiresAt)
            .SetProperty(x => x.RefreshTokenFamilyId, x => role != null && role != x.Role ? null : x.RefreshTokenFamilyId), ct);

    public Task<int> ChangePasswordAsync(int id, string previousHash, string nextHash, string? name, CancellationToken ct) =>
        db.Accounts.Where(x => x.AccountId == id && x.PasswordHash == previousHash).ExecuteUpdateAsync(set => set
            .SetProperty(x => x.FullName, x => name ?? x.FullName).SetProperty(x => x.PasswordHash, nextHash)
            .SetProperty(x => x.TokenVersion, x => x.TokenVersion + 1)
            .SetProperty(x => x.RefreshTokenHash, (string?)null).SetProperty(x => x.RefreshTokenExpiresAt, (DateTime?)null).SetProperty(x => x.RefreshTokenFamilyId, (Guid?)null), ct);
}
