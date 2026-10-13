using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace TaskTrack.IntegrationTests;

public class ApiIntegrationTests : IAsyncLifetime
{
    private readonly string prefix = "AS2Test" + Guid.NewGuid().ToString("N")[..8];
    private readonly HttpClient client;
    private readonly string connectionString;
    private readonly Dictionary<string, List<int>> ids = new()
    {
        ["accounts"] = [], ["tasks"] = [], ["projects"] = [], ["departments"] = [], ["tags"] = []
    };
    private JsonElement admin, staff;
    private bool failureTriggerCreated;
    private string AdminToken => admin.GetProperty("token").GetString()!;
    private string StaffToken => staff.GetProperty("token").GetString()!;
    private string FailureName => prefix + "Fail";

    public ApiIntegrationTests()
    {
        var api = new Uri(Environment.GetEnvironmentVariable("API_BASE_URL") ?? "http://127.0.0.1:5103/api");
        var database = new Uri(Required("TEST_DATABASE_URL"));
        if (!IsLocal(api.Host) || !IsLocal(database.Host) || database.Scheme is not ("postgres" or "postgresql") ||
            !database.AbsolutePath.EndsWith("_ass2", StringComparison.Ordinal) || database.AbsolutePath == "/qe190064_prn232_ass2")
            throw new InvalidOperationException("Use an isolated local AS2 API/database. The main local database and remote hosts are refused.");
        var credentials = database.UserInfo.Split(':', 2);
        if (credentials.Length != 2) throw new InvalidOperationException("TEST_DATABASE_URL requires database credentials.");
        connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = database.Host, Port = database.Port > 0 ? database.Port : 5432,
            Database = Uri.UnescapeDataString(database.AbsolutePath.Trim('/')),
            Username = Uri.UnescapeDataString(credentials[0]), Password = Uri.UnescapeDataString(credentials[1])
        }.ConnectionString;
        client = new HttpClient { BaseAddress = new Uri(api.AbsoluteUri.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task InitializeAsync()
    {
        admin = await LoginAsync(Required("ADMIN_EMAIL"), Required("ADMIN_PASSWORD"));
        Assert.Equal(1, admin.GetProperty("account").GetProperty("role").GetInt32());
        var staffCredentials = new { email = Required("STAFF_EMAIL"), password = Required("STAFF_PASSWORD") };
        using var response = await client.PostAsJsonAsync("auth/login", staffCredentials);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            await RequestAsync("/auth/register", "POST", new { fullName = "Integration Staff", staffCredentials.email, staffCredentials.password }, status: 201);
        else Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        staff = await LoginAsync(staffCredentials.email, staffCredentials.password);
        Assert.Equal(0, staff.GetProperty("account").GetProperty("role").GetInt32());
        var account = admin.GetProperty("account");
        Assert.Equal(account.GetProperty("accountId").GetInt32(), Convert.ToInt32(await SqlAsync(
            "SELECT \"AccountID\" FROM \"SystemAccount\" WHERE lower(\"Email\")=lower($1)", new NpgsqlParameter { Value = account.GetProperty("email").GetString()! })));
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (failureTriggerCreated) await RemoveFailureTriggerAsync();
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            foreach (var (table, column, group) in new[]
            {
                ("TaskTag", "TaskID", "tasks"), ("Task", "TaskID", "tasks"), ("Project", "ProjectID", "projects"),
                ("Department", "DepartmentID", "departments"), ("Tag", "TagID", "tags"), ("SystemAccount", "AccountID", "accounts")
            })
            {
                if (ids[group].Count == 0) continue;
                await using var command = new NpgsqlCommand($"DELETE FROM \"{table}\" WHERE \"{column}\"=ANY($1)", connection, transaction);
                command.Parameters.Add(new NpgsqlParameter { Value = ids[group].ToArray() });
                await command.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
        }
        finally { client.Dispose(); }
    }

    [Fact]
    public async Task PublicReadsKeepAs1RoutesAndRealProjectCounts()
    {
        var work = await CreateWorkAsync();
        foreach (var path in new[] { "/departments", "/departments/" + work.DepartmentId, "/departments/search?name=" + prefix, "/projects", "/projects/" + work.ProjectId, "/projects/department/" + work.DepartmentId, "/projects/search?status=1", "/tasks", "/tasks/" + work.TaskId, "/tasks/project/" + work.ProjectId, "/tasks/search?priority=0", "/tags" })
            await RequestAsync(path);
        var departments = await RequestAsync("/departments");
        Assert.Contains(departments.EnumerateArray().Single(item => item.GetProperty("departmentId").GetInt32() == work.DepartmentId).GetProperty("projects").EnumerateArray(), project => project.GetProperty("projectId").GetInt32() == work.ProjectId);
        await RequestAsync("/tasks/2147483647", status: 404);
    }

    [Fact]
    public async Task WritesRequireAuthenticationAndAccountsRequireAdmin()
    {
        foreach (var kind in new[] { "departments", "projects", "tasks", "tags" })
            foreach (var method in new[] { "POST", "PUT", "DELETE" })
                await RequestAsync("/" + kind + (method == "POST" ? "" : "/2147483647"), method, method == "DELETE" ? null : new { }, status: 401);
        foreach (var (path, method, body) in new (string, string, object?)[]
        {
            ("/accounts", "GET", null), ("/accounts/1", "GET", null),
            ("/accounts/1", "PUT", new { fullName = "Blocked", role = 1 }), ("/accounts/1", "DELETE", null)
        })
        {
            await RequestAsync(path, method, body, status: 401);
            await RequestAsync(path, method, body, StaffToken, 403);
        }
    }

    [Fact]
    public async Task RegistrationNormalizesEmailsHashesPasswordsAndPreventsRaces()
    {
        var email = prefix.ToLowerInvariant() + "@example.com";
        var account = await RegisterAsync(email.ToUpperInvariant());
        Assert.Equal(email, account.GetProperty("email").GetString());
        await RequestAsync("/auth/register", "POST", new { fullName = prefix, email, password = "TestPassword123!" }, status: 409);
        var hash = (string)(await SqlAsync("SELECT \"PasswordHash\" FROM \"SystemAccount\" WHERE \"AccountID\"=$1", new NpgsqlParameter { Value = account.GetProperty("accountId").GetInt32() }))!;
        Assert.NotEqual("TestPassword123!", hash);
        Assert.True(Convert.FromBase64String(hash).Length > 32);
        foreach (var payload in new[]
        {
            new { fullName = "", email, password = "TestPassword123!" },
            new { fullName = " ", email = prefix + "blank@example.com", password = "TestPassword123!" },
            new { fullName = prefix, email = "bad", password = "TestPassword123!" },
            new { fullName = prefix, email, password = "short" }
        }) await RequestAsync("/auth/register", "POST", payload, status: 400);
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync("auth/register", new { fullName = prefix, email = prefix + "race@example.com", password = "TestPassword123!" })));
        try
        {
            foreach (var response in responses.Where(response => (int)response.StatusCode == 201))
                ids["accounts"].Add((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accountId").GetInt32());
            Assert.Equal(new[] { 201, 409 }, responses.Select(response => (int)response.StatusCode).Order().ToArray());
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task LoginValidatesCredentialsClaimsSignaturesAndExpiry()
    {
        await RequestAsync("/auth/login", "POST", new { email = Required("STAFF_EMAIL"), password = "wrongpassword" }, status: 401);
        await RequestAsync("/auth/login", "POST", new { email = prefix + "absent@example.com", password = "wrongpassword" }, status: 401);
        var parts = StaffToken.Split('.');
        using var document = JsonDocument.Parse(DecodeBase64Url(parts[1]));
        var claims = document.RootElement;
        Assert.Equal(staff.GetProperty("account").GetProperty("accountId").GetInt32().ToString(), claims.GetProperty("AccountID").GetString());
        Assert.Equal(staff.GetProperty("account").GetProperty("email").GetString(), claims.GetProperty("Email").GetString());
        Assert.Equal("Staff", claims.GetProperty("Role").GetString());
        var expiry = claims.GetProperty("exp").GetInt64();
        Assert.InRange(expiry, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1, DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds() + 1);
        Assert.False(staff.GetProperty("account").TryGetProperty("passwordHash", out _));
        await RequestAsync("/auth/me", token: StaffToken);
        await RequestAsync("/auth/me", token: parts[0] + "." + parts[1] + ".invalid-signature", status: 401);
        var expiredClaims = claims.EnumerateObject().ToDictionary(property => property.Name, property => (object)property.Value.Clone());
        expiredClaims["exp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1;
        expiredClaims["nbf"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 100;
        var signingInput = parts[0] + "." + EncodeBase64Url(JsonSerializer.SerializeToUtf8Bytes(expiredClaims));
        var expiredToken = signingInput + "." + EncodeBase64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Required("JWT_SECRET")), Encoding.UTF8.GetBytes(signingInput)));
        await RequestAsync("/auth/me", token: expiredToken, status: 401);
        foreach (var kind in new[] { "departments", "projects", "tasks", "tags" })
            foreach (var method in new[] { "POST", "PUT", "DELETE" })
                foreach (var token in new[] { expiredToken, "invalid.token.signature" })
                    await RequestAsync("/" + kind + (method == "POST" ? "" : "/2147483647"), method, method == "DELETE" ? null : new { }, token, 401);
    }

    [Fact]
    public async Task StaffCrudPreservesLowPriorityAndUsesJwtOwnership()
    {
        var work = await CreateWorkAsync();
        var task = await RequestAsync("/tasks/" + work.TaskId);
        Assert.Equal(0, task.GetProperty("priority").GetInt32());
        Assert.Single(task.GetProperty("tags").EnumerateArray());
        Assert.Equal(work.OwnerId, Convert.ToInt32(await SqlAsync("SELECT \"CreatedByID\" FROM \"Task\" WHERE \"TaskID\"=$1", new NpgsqlParameter { Value = work.TaskId })));
        var departments = await RequestAsync("/departments/search?name=" + prefix);
        Assert.Contains(departments[0].GetProperty("projects").EnumerateArray(), project => project.GetProperty("projectId").GetInt32() == work.ProjectId);
    }

    [Fact]
    public async Task TagUpdatesAddRemoveClearAndRejectInvalidTagsAtomically()
    {
        var work = await CreateWorkAsync();
        Task<JsonElement> Update(int[] tags) => RequestAsync("/tasks/" + work.TaskId, "PUT", new { title = prefix, projectId = work.ProjectId, priority = 0, tagIds = tags }, StaffToken);
        var both = await Update([work.Tag1Id, work.Tag2Id]);
        Assert.Equal(new[] { work.Tag1Id, work.Tag2Id }.Order(), both.GetProperty("tags").EnumerateArray().Select(tag => tag.GetProperty("tagId").GetInt32()).Order());
        Assert.Equal(work.Tag2Id, (await Update([work.Tag2Id])).GetProperty("tags")[0].GetProperty("tagId").GetInt32());
        Assert.Empty((await Update([])).GetProperty("tags").EnumerateArray());
        await Update([work.Tag1Id]);
        await RequestAsync("/tasks/" + work.TaskId, "PUT", new { title = "Should never persist", projectId = work.ProjectId, priority = 3, tagIds = new[] { int.MaxValue } }, StaffToken, 400);
        var unchanged = await RequestAsync("/tasks/" + work.TaskId);
        Assert.Equal(prefix, unchanged.GetProperty("title").GetString());
        Assert.Equal(0, unchanged.GetProperty("priority").GetInt32());
        Assert.Equal(work.Tag1Id, unchanged.GetProperty("tags")[0].GetProperty("tagId").GetInt32());
    }

    [Fact]
    public async Task DatabaseFailureRollsBackTaskFieldsAndTagLinks()
    {
        var work = await CreateWorkAsync();
        failureTriggerCreated = true;
        // Limit the injected failure to this test's task, even if another local client is connected.
        await SqlAsync($"CREATE FUNCTION \"{FailureName}\"() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'injected test failure' USING ERRCODE='23503'; END; $$; CREATE TRIGGER \"{FailureName}\" BEFORE INSERT ON \"TaskTag\" FOR EACH ROW WHEN (NEW.\"TaskID\"={work.TaskId}) EXECUTE FUNCTION \"{FailureName}\"();");
        try
        {
            await RequestAsync("/tasks/" + work.TaskId, "PUT", new { title = "Should roll back", projectId = work.ProjectId, priority = 3, tagIds = new[] { work.Tag2Id } }, StaffToken, 409);
            var unchanged = await RequestAsync("/tasks/" + work.TaskId);
            Assert.Equal(prefix, unchanged.GetProperty("title").GetString());
            Assert.Equal(0, unchanged.GetProperty("priority").GetInt32());
            Assert.Equal(new[] { work.Tag1Id }, unchanged.GetProperty("tags").EnumerateArray().Select(tag => tag.GetProperty("tagId").GetInt32()).ToArray());
        }
        finally { await RemoveFailureTriggerAsync(); }
    }

    [Fact]
    public async Task AccountUpdatesRestrictFieldsAndInvalidateOldTokens()
    {
        var account = await RegisterAsync(prefix + "role@example.com");
        var accountId = account.GetProperty("accountId").GetInt32();
        var old = await LoginAsync(account.GetProperty("email").GetString()!, "TestPassword123!");
        var promoted = await RequestAsync("/accounts/" + accountId, "PUT", new { role = 1, email = "ignored@example.com", passwordHash = "ignored" }, AdminToken);
        Assert.Equal(account.GetProperty("fullName").GetString(), promoted.GetProperty("fullName").GetString());
        Assert.Equal(account.GetProperty("email").GetString(), promoted.GetProperty("email").GetString());
        await RequestAsync("/auth/me", token: old.GetProperty("token").GetString(), status: 401);
        var renamed = await RequestAsync("/accounts/" + accountId, "PUT", new { fullName = prefix + " renamed" }, AdminToken);
        Assert.Equal(1, renamed.GetProperty("role").GetInt32());
        await RequestAsync("/accounts/" + accountId, "PUT", new { }, AdminToken, 400);
        await RequestAsync("/accounts/" + accountId, "PUT", new { role = 2 }, AdminToken, 400);
        var current = await LoginAsync(account.GetProperty("email").GetString()!, "TestPassword123!");
        await RequestAsync("/accounts/" + accountId, "DELETE", token: AdminToken, status: 204);
        await RequestAsync("/auth/me", token: current.GetProperty("token").GetString(), status: 401);
        await RequestAsync("/accounts/" + accountId, token: AdminToken, status: 404);
        Assert.All((await RequestAsync("/accounts", token: AdminToken)).EnumerateArray(), item => Assert.False(item.TryGetProperty("passwordHash", out _)));
    }

    [Fact]
    public async Task SoftDeletedTasksStillPreventAccountAndParentDeletion()
    {
        var work = await CreateWorkAsync();
        await RequestAsync("/accounts/" + work.OwnerId, "DELETE", token: AdminToken, status: 409);
        await RequestAsync("/tasks/" + work.TaskId, "DELETE", token: StaffToken, status: 204);
        await RequestAsync("/tasks/" + work.TaskId, status: 404);
        Assert.DoesNotContain((await RequestAsync("/tasks")).EnumerateArray(), task => task.GetProperty("taskId").GetInt32() == work.TaskId);
        await RequestAsync("/accounts/" + work.OwnerId, "DELETE", token: AdminToken, status: 409);
        await RequestAsync("/tasks/" + work.TaskId, "PUT", new { title = prefix, projectId = work.ProjectId }, StaffToken, 404);
        await RequestAsync("/projects/" + work.ProjectId, "DELETE", token: StaffToken, status: 400);
        await RequestAsync("/departments/" + work.DepartmentId, "DELETE", token: StaffToken, status: 400);
    }

    [Fact]
    public async Task StaffDeletesUnlinkedRecordsAndTagUniquenessSurvivesRaces()
    {
        var department = await CreateAsync("departments", "departmentId", new { departmentName = prefix, departmentDescription = "Fixture" }, StaffToken);
        var project = await CreateAsync("projects", "projectId", new { projectName = prefix, startDate = "2026-10-10", departmentId = department }, StaffToken);
        var tag = await CreateAsync("tags", "tagId", new { tagName = prefix + "remove", color = "#2563EB" }, StaffToken);
        await RequestAsync("/tags/" + tag, "DELETE", token: StaffToken, status: 204);
        await RequestAsync("/projects/" + project, "DELETE", token: StaffToken, status: 204);
        await RequestAsync("/departments/" + department, "DELETE", token: StaffToken, status: 204);
        var name = prefix + "RaceTag";
        var responses = await Task.WhenAll(new[] { name, name.ToLowerInvariant() }.Select(async tagName =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "tags") { Content = JsonContent.Create(new { tagName }) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", StaffToken);
            return await client.SendAsync(request);
        }));
        try
        {
            foreach (var response in responses.Where(response => (int)response.StatusCode == 200))
                ids["tags"].Add((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tagId").GetInt32());
            Assert.Single(responses, response => (int)response.StatusCode == 200);
            Assert.Contains(responses, response => (int)response.StatusCode is 400 or 409);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task AdminCrudAndStaffEditsPreserveOriginalTaskCreator()
    {
        var department = await CreateAsync("departments", "departmentId", new { departmentName = prefix, departmentDescription = "Admin fixture" }, AdminToken);
        await RequestAsync("/departments/" + department, "PUT", new { departmentName = prefix, departmentDescription = "Edited" }, AdminToken);
        var project = await CreateAsync("projects", "projectId", new { projectName = prefix, startDate = "2026-10-10", departmentId = department }, AdminToken);
        await RequestAsync("/projects/" + project, "PUT", new { projectName = prefix, startDate = "2026-10-10", departmentId = department, status = 1 }, AdminToken);
        var tag = await CreateAsync("tags", "tagId", new { tagName = prefix, color = "#2563EB" }, AdminToken);
        await RequestAsync("/tags/" + tag, "PUT", new { tagName = prefix, color = "#16A34A" }, AdminToken);
        var task = await CreateAsync("tasks", "taskId", new { title = prefix, projectId = project, priority = 0, tagIds = new[] { tag } }, AdminToken);
        foreach (var token in new[] { AdminToken, StaffToken })
            await RequestAsync("/tasks/" + task, "PUT", new { title = prefix + " edited", projectId = project, priority = 0, tagIds = Array.Empty<int>() }, token);
        Assert.Equal(admin.GetProperty("account").GetProperty("accountId").GetInt32(), Convert.ToInt32(await SqlAsync("SELECT \"CreatedByID\" FROM \"Task\" WHERE \"TaskID\"=$1", new NpgsqlParameter { Value = task })));
        await RequestAsync("/tasks/" + task, "DELETE", token: AdminToken, status: 204);
        await SqlAsync("DELETE FROM \"Task\" WHERE \"TaskID\"=$1", new NpgsqlParameter { Value = task });
        await RequestAsync("/tags/" + tag, "DELETE", token: AdminToken, status: 204);
        await RequestAsync("/projects/" + project, "DELETE", token: AdminToken, status: 204);
        await RequestAsync("/departments/" + department, "DELETE", token: AdminToken, status: 204);
    }

    private async Task<JsonElement> RequestAsync(string path, string method = "GET", object? body = null, string? token = null, int status = 200)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path.TrimStart('/'));
        if (body is not null) request.Content = JsonContent.Create(body);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == status, $"{method} {path}: expected {status}, received {(int)response.StatusCode}.");
        if (string.IsNullOrEmpty(text)) return default;
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private Task<JsonElement> LoginAsync(string email, string password) => RequestAsync("/auth/login", "POST", new { email, password });

    private async Task<JsonElement> RegisterAsync(string email)
    {
        var account = await RequestAsync("/auth/register", "POST", new { fullName = prefix, email, password = "TestPassword123!", role = 1 }, status: 201);
        ids["accounts"].Add(account.GetProperty("accountId").GetInt32());
        Assert.Equal(0, account.GetProperty("role").GetInt32());
        Assert.False(account.TryGetProperty("passwordHash", out _));
        return account;
    }

    private async Task<int> CreateAsync(string kind, string key, object body, string token)
    {
        var id = (await RequestAsync("/" + kind, "POST", body, token)).GetProperty(key).GetInt32();
        ids[kind].Add(id);
        return id;
    }

    private async Task<Work> CreateWorkAsync()
    {
        var department = await CreateAsync("departments", "departmentId", new { departmentName = prefix, departmentDescription = "Integration fixture" }, StaffToken);
        await RequestAsync("/departments/" + department, "PUT", new { departmentName = prefix + " updated", departmentDescription = "Updated" }, StaffToken);
        var project = await CreateAsync("projects", "projectId", new { projectName = prefix, startDate = "2026-10-10", endDate = "2026-11-10", status = 0, departmentId = department }, StaffToken);
        await RequestAsync("/projects/" + project, "PUT", new { projectName = prefix + " updated", startDate = "2026-10-10", status = 1, departmentId = department }, StaffToken);
        var tag1 = await CreateAsync("tags", "tagId", new { tagName = prefix + "A", color = "#2563EB" }, StaffToken);
        var tag2 = await CreateAsync("tags", "tagId", new { tagName = prefix + "B", color = "#16A34A" }, StaffToken);
        await RequestAsync("/tags/" + tag1, "PUT", new { tagName = prefix + "A updated", color = "#2563EB" }, StaffToken);
        var owner = await RegisterAsync(prefix + "owner@example.com");
        var ownerLogin = await LoginAsync(owner.GetProperty("email").GetString()!, "TestPassword123!");
        var task = await CreateAsync("tasks", "taskId", new { title = prefix, projectId = project, priority = 0, tagIds = new[] { tag1, tag1 }, createdById = admin.GetProperty("account").GetProperty("accountId").GetInt32() }, ownerLogin.GetProperty("token").GetString()!);
        return new Work(department, project, tag1, tag2, task, owner.GetProperty("accountId").GetInt32());
    }

    private async Task<object?> SqlAsync(string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteScalarAsync();
    }

    private async Task RemoveFailureTriggerAsync()
    {
        await SqlAsync($"DROP TRIGGER IF EXISTS \"{FailureName}\" ON \"TaskTag\"; DROP FUNCTION IF EXISTS \"{FailureName}\"();");
        failureTriggerCreated = false;
    }

    private static string Required(string key) => Environment.GetEnvironmentVariable(key) is { Length: > 0 } value ? value : throw new InvalidOperationException($"Configure {key} for the isolated test instance.");
    private static bool IsLocal(string host) => host is "localhost" or "127.0.0.1";
    private static string EncodeBase64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] DecodeBase64Url(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));
    private record Work(int DepartmentId, int ProjectId, int Tag1Id, int Tag2Id, int TaskId, int OwnerId);
}
