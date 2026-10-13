using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TaskTrack.Repo;
using TaskTrack.Repo.Models;
using TaskTrack.Repo.Repositories;
using TaskTrack.Service.Dtos;
using TaskTrack.Service.Services;
using Xunit;

namespace TaskTrack.IntegrationTests;

#pragma warning disable EF1002 // SQL identifiers are generated from a GUID here, never from user input.
public class AccountProtectionTests : IAsyncLifetime
{
    private readonly string schema = "admin_guard_" + Guid.NewGuid().ToString("N");
    private readonly string connectionString;

    public AccountProtectionTests()
    {
        var database = new Uri(Environment.GetEnvironmentVariable("TEST_DATABASE_URL") ?? throw new InvalidOperationException("Configure TEST_DATABASE_URL."));
        if (database.Host is not ("localhost" or "127.0.0.1") || !database.AbsolutePath.EndsWith("_ass2") || database.AbsolutePath == "/qe190064_prn232_ass2")
            throw new InvalidOperationException("Use an isolated local AS2 test database.");
        var credentials = database.UserInfo.Split(':', 2);
        connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = database.Host, Port = database.Port > 0 ? database.Port : 5432,
            Database = Uri.UnescapeDataString(database.AbsolutePath.Trim('/')),
            Username = Uri.UnescapeDataString(credentials[0]), Password = Uri.UnescapeDataString(credentials[1]), SearchPath = schema
        }.ConnectionString;
    }

    private TaskManagementDbContext CreateDb() => new(new DbContextOptionsBuilder<TaskManagementDbContext>().UseNpgsql(connectionString).Options);
    private static AccountService Service(TaskManagementDbContext db) => new(new AccountRepository(db), new PasswordHasher<SystemAccount>());

    public async Task InitializeAsync()
    {
        await using var db = CreateDb();
        await db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA \"{schema}\"");
        await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());
        db.Accounts.AddRange(Enumerable.Range(1, 3).Select(id => new SystemAccount
        {
            AccountId = id, FullName = "Account " + id, Email = $"account{id}@example.com", Role = (short)(id < 3 ? 1 : 0), PasswordHash = "test-only"
        }));
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = CreateDb();
        await db.Database.ExecuteSqlRawAsync($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE");
    }

    [Fact]
    public async Task SelfRoleChangeRollsBackNameButOwnNameAndUnchangedRoleStillWork()
    {
        await using var db = CreateDb();
        var service = Service(db);
        var error = await Assert.ThrowsAsync<AccountConflictException>(() => service.UpdateAsync(1, new() { FullName = "Must not persist", Role = 0 }, 1, default));
        Assert.Equal("You cannot change your own role.", error.Message);
        Assert.Equal("Account 1", (await service.GetAsync(1, default)).FullName);
        var updated = await service.UpdateAsync(1, new() { FullName = "Updated name", Role = 1 }, 1, default);
        Assert.Equal("Updated name", updated.FullName);
        Assert.Equal(1, updated.Role);
        Assert.Equal(0, (await db.Accounts.AsNoTracking().SingleAsync(x => x.AccountId == 1)).TokenVersion);
        Assert.Equal("Staff name", (await service.UpdateProfileAsync(3, new() { FullName = "Staff name" }, default)).FullName);
    }

    [Fact]
    public async Task LastAdminCannotBeDemotedOrDeletedAndOtherAccountsRemainEditable()
    {
        await using var db = CreateDb();
        var service = Service(db);
        await service.UpdateAsync(2, new() { Role = 0 }, 1, default);
        var roleError = await Assert.ThrowsAsync<AccountConflictException>(() => service.UpdateAsync(1, new() { Role = 0 }, 2, default));
        Assert.Equal("At least one Admin must remain.", roleError.Message);
        var deleteError = await Assert.ThrowsAsync<AccountConflictException>(() => service.DeleteAsync(1, default));
        Assert.Equal("At least one Admin must remain.", deleteError.Message);
        await service.DeleteAsync(3, default);
        Assert.Equal(1, await db.Accounts.CountAsync(x => x.Role == 1));
        Assert.Equal(1, (await service.UpdateAsync(2, new() { Role = 1 }, 1, default)).Role);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task ConcurrentChangesLeaveOneAdmin(bool deleteFirst, bool deleteSecond)
    {
        async Task<bool> Change(int id, bool delete)
        {
            await using var db = CreateDb();
            try
            {
                if (delete) await Service(db).DeleteAsync(id, default);
                else await Service(db).UpdateAsync(id, new() { Role = 0 }, id == 1 ? 2 : 1, default);
                return true;
            }
            catch (AccountConflictException) { return false; }
        }
        var changes = await Task.WhenAll(Change(1, deleteFirst), Change(2, deleteSecond));
        Assert.Single(changes, success => success);
        await using var verify = CreateDb();
        Assert.Equal(1, await verify.Accounts.CountAsync(x => x.Role == 1));
    }
}
#pragma warning restore EF1002
