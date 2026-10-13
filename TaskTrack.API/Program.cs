using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;
using TaskTrack.API;
using TaskTrack.Repo;
using TaskTrack.Repo.Models;
using TaskTrack.Repo.Repositories;
using TaskTrack.Service.Interfaces;
using TaskTrack.Service.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>() });
});
builder.Services.AddDbContext<TaskManagementDbContext>(options => options.UseNpgsql(DatabaseConnection.Resolve(builder.Configuration)));
builder.Services.AddScoped<DepartmentRepository>();
builder.Services.AddScoped<ProjectRepository>();
builder.Services.AddScoped<TaskRepository>();
builder.Services.AddScoped<TagRepository>();
builder.Services.AddScoped<AccountRepository>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<IPasswordHasher<SystemAccount>, PasswordHasher<SystemAccount>>();
builder.Services.AddScoped<TokenIssuer>();
builder.Services.AddScoped<IDepartmentService, DepartmentService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<ITagService, TagService>();

var jwtKey = TokenIssuer.Key(builder.Configuration);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true, IssuerSigningKey = jwtKey,
        ValidateIssuer = true, ValidIssuer = TokenIssuer.Issuer, ValidateAudience = true, ValidAudience = TokenIssuer.Audience,
        ValidateLifetime = true, RequireExpirationTime = true, ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.Zero, RoleClaimType = "Role", NameClaimType = "FullName"
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            if (!int.TryParse(context.Principal?.FindFirstValue("AccountID"), out var id) || !int.TryParse(context.Principal?.FindFirstValue("TokenVersion"), out var version)) { context.Fail("Invalid account."); return; }
            var repo = context.HttpContext.RequestServices.GetRequiredService<AccountRepository>();
            var account = await repo.FindAsync(id, context.HttpContext.RequestAborted);
            if (account is null || account.TokenVersion != version || TokenIssuer.RoleName(account.Role) != context.Principal?.FindFirstValue("Role")) context.Fail("Account or session changed. Sign in again.");
        }
    };
});
builder.Services.AddAuthorization();
var origins = (builder.Configuration["CORS_ORIGINS"] ?? "http://localhost:3012").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options => options.AddPolicy("frontend", policy => policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
// shortcut: two explicit AS2 schema versions; use EF migrations when more upgrades are needed.
if (args.Contains("--init-db"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>();
    if (!db.Database.GetDbConnection().Database.EndsWith("_ass2", StringComparison.Ordinal))
        throw new InvalidOperationException("Initialize only a separate database whose name ends with _ass2.");
    var tables = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::integer AS \"Value\" FROM information_schema.tables WHERE table_schema = current_schema()").SingleAsync();
    if (tables != 0) throw new InvalidOperationException("Database already has tables. Use the existing schema; initialization was refused without changing data.");
    await using var transaction = await db.Database.BeginTransactionAsync();
    await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());
    // Expression indexes keep concurrent email/tag writes unique after trimming and ignoring case.
    await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX \"UX_SystemAccount_Email\" ON \"SystemAccount\" (LOWER(BTRIM(\"Email\"))); CREATE UNIQUE INDEX \"UX_Tag_Name_Normalized\" ON \"Tag\" (LOWER(BTRIM(\"TagName\")));");
    await transaction.CommitAsync();
    Console.WriteLine("Empty AS2 database initialized from the EF Core model. No sample data was inserted.");
    return;
}
if (args.Contains("--upgrade-db"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TaskManagementDbContext>();
    if (!db.Database.GetDbConnection().Database.EndsWith("_ass2", StringComparison.Ordinal)) throw new InvalidOperationException("Upgrade only a separate AS2 database whose name ends with _ass2.");
    await using var transaction = await db.Database.BeginTransactionAsync();
    await db.Database.ExecuteSqlRawAsync("""
        ALTER TABLE "SystemAccount" ADD COLUMN IF NOT EXISTS "TokenVersion" integer NOT NULL DEFAULT 0,
            ADD COLUMN IF NOT EXISTS "RefreshTokenHash" character varying(64),
            ADD COLUMN IF NOT EXISTS "RefreshTokenExpiresAt" timestamp with time zone,
            ADD COLUMN IF NOT EXISTS "RefreshTokenFamilyId" uuid;
        ALTER TABLE "Task" ADD COLUMN IF NOT EXISTS "UpdatedByID" integer;
        CREATE INDEX IF NOT EXISTS "IX_Task_UpdatedByID" ON "Task" ("UpdatedByID");
        """);
    var exists = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::integer AS \"Value\" FROM pg_constraint WHERE conrelid = '\"Task\"'::regclass AND conname = 'FK_Task_UpdatedBy'").SingleAsync();
    if (exists == 0) await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Task\" ADD CONSTRAINT \"FK_Task_UpdatedBy\" FOREIGN KEY (\"UpdatedByID\") REFERENCES \"SystemAccount\" (\"AccountID\") ON DELETE SET NULL");
    await transaction.CommitAsync();
    Console.WriteLine("AS2 schema upgraded. Existing records were preserved.");
    return;
}
if (args.Contains("--seed-admin"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AccountService>().SeedAdminAsync(
        builder.Configuration["ADMIN_FULL_NAME"] ?? "Administrator",
        builder.Configuration["ADMIN_EMAIL"] ?? throw new InvalidOperationException("Configure ADMIN_EMAIL."),
        builder.Configuration["ADMIN_PASSWORD"] ?? throw new InvalidOperationException("Configure ADMIN_PASSWORD."), CancellationToken.None);
    Console.WriteLine("Admin seed completed. Credentials were not printed.");
    return;
}
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, message) = error switch
    {
        AccountConflictException ex => (409, ex.Message),
        KeyNotFoundException ex => (404, ex.Message),
        DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } => (409, "A record with these values already exists."),
        DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } } => (409, "This record is linked to other records, or the referenced record no longer exists."),
        InvalidOperationException ex => (400, ex.Message),
        _ => (500, "The request could not be completed. Please try again.")
    };
    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new { message });
}));
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
