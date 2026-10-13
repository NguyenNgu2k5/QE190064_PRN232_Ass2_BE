using Microsoft.AspNetCore.Identity;
using TaskTrack.Repo.Models;
using TaskTrack.Repo.Repositories;
using TaskTrack.Service.Dtos;

namespace TaskTrack.Service.Services;

public class AccountService(AccountRepository repo, IPasswordHasher<SystemAccount> hasher)
{
    public async Task<AccountResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await repo.FindByEmailAsync(email, ct) is not null) throw new AccountConflictException("Email already exists.");
        var account = new SystemAccount { FullName = Name(request.FullName), Email = email, Role = 0 };
        account.PasswordHash = hasher.HashPassword(account, request.Password);
        await repo.AddAsync(account, ct);
        await repo.SaveAsync(ct);
        return Map(account);
    }

    public async Task<AccountResponse?> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var account = await repo.FindByEmailAsync(request.Email.Trim().ToLowerInvariant(), ct);
        if (account is null || hasher.VerifyHashedPassword(account, account.PasswordHash, request.Password) == PasswordVerificationResult.Failed) return null;
        return Map(account);
    }

    public async Task<IReadOnlyList<AccountResponse>> GetAllAsync(CancellationToken ct) => (await repo.GetAllAsync(ct)).Select(Map).ToList();
    public async Task<AccountResponse> GetAsync(int id, CancellationToken ct) => Map(await repo.FindAsync(id, ct) ?? throw new KeyNotFoundException("Account not found."));

    public async Task<AccountResponse> UpdateAsync(int id, AccountUpdateRequest request, CancellationToken ct)
    {
        if (request.FullName is null && request.Role is null) throw new InvalidOperationException("Provide a full name or role.");
        if (await repo.UpdateAsync(id, request.FullName is null ? null : Name(request.FullName), request.Role, ct) == 0) throw new KeyNotFoundException("Account not found.");
        return Map((await repo.ReadAsync(id, ct))!);
    }

    public async Task<AccountResponse> UpdateProfileAsync(int id, ProfileRequest request, CancellationToken ct)
    {
        if (request.FullName is null && request.NewPassword is null) throw new InvalidOperationException("Provide a full name or new password.");
        var name = request.FullName is null ? null : Name(request.FullName);
        if (request.NewPassword is null) return await UpdateAsync(id, new AccountUpdateRequest { FullName = name }, ct);
        var account = await repo.ReadAsync(id, ct) ?? throw new KeyNotFoundException("Account not found.");
        if (string.IsNullOrEmpty(request.CurrentPassword) || hasher.VerifyHashedPassword(account, account.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
            throw new InvalidOperationException("Current password is incorrect.");
        var hash = hasher.HashPassword(account, request.NewPassword);
        if (await repo.ChangePasswordAsync(id, account.PasswordHash, hash, name, ct) == 0) throw new InvalidOperationException("Password changed in another request. Try again.");
        return Map((await repo.ReadAsync(id, ct))!);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var account = await repo.FindAsync(id, ct) ?? throw new KeyNotFoundException("Account not found.");
        if (await repo.HasTasksAsync(id, ct)) throw new AccountConflictException("This account created tasks and cannot be deleted, including tasks that were soft-deleted.");
        repo.Remove(account);
        await repo.SaveAsync(ct);
    }

    public async Task SeedAdminAsync(string name, string email, string password, CancellationToken ct)
    {
        var request = new RegisterRequest { FullName = name, Email = email.Trim().ToLowerInvariant(), Password = password };
        System.ComponentModel.DataAnnotations.Validator.ValidateObject(request, new System.ComponentModel.DataAnnotations.ValidationContext(request), true);
        var existing = await repo.FindByEmailAsync(request.Email, ct);
        if (existing is not null)
        {
            if (existing.Role != 1) throw new InvalidOperationException("An existing Staff account uses the seed email; it will not be elevated automatically.");
            return;
        }
        var account = new SystemAccount { FullName = Name(name), Email = request.Email, Role = 1 };
        account.PasswordHash = hasher.HashPassword(account, password);
        await repo.AddAsync(account, ct);
        await repo.SaveAsync(ct);
    }

    private static string Name(string value) => value.Trim().Length > 0 ? value.Trim() : throw new InvalidOperationException("Full name is required.");
    public static AccountResponse Map(SystemAccount account) => new(account.AccountId, account.FullName, account.Email, account.Role, account.CreatedDate);
}

public class AccountConflictException(string message) : InvalidOperationException(message);
