# TaskTrack — PRN232 Assignment 2 Backend

ASP.NET Core 8, EF Core and PostgreSQL. Forked from Assignment 1 into a separate Assignment 2 repository. AS1 public reads remain available; writes require a valid Staff/Admin JWT. Account management requires Admin.

## Local setup

Use the existing **separate AS2 database**. No initialization is needed for the deployed database or the configured local database. Legacy tasks keep a NULL creator.

Configure secrets outside Git (PowerShell, from this repository):

```powershell
$env:ConnectionStrings__DefaultConnection = 'Host=localhost;Port=5432;Database=qe190064_prn232_ass2;Username=postgres;Password=YOUR_DATABASE_PASSWORD'
$env:JWT_SECRET = 'YOUR_RANDOM_SECRET_WITH_AT_LEAST_32_UTF8_BYTES'
$env:ADMIN_EMAIL = 'YOUR_ADMIN_EMAIL'
$env:ADMIN_PASSWORD = 'YOUR_ADMIN_PASSWORD_AT_LEAST_8_CHARACTERS'
# Only for a NEW, EMPTY AS2 database created in PostgreSQL:
# dotnet run --project TaskTrack.API -- --init-db
dotnet run --project TaskTrack.API -- --seed-admin
dotnet run --project TaskTrack.API
```

Admin seed writes a PBKDF2 hash directly to PostgreSQL, is idempotent, and never promotes an existing Staff account automatically. Register only creates Staff. No real credentials belong in source.

`--init-db` creates the six tables from the existing EF Core C# model, with role validation, restricted foreign keys and normalized unique email/tag indexes. It accepts only a database name ending in `_ass2`, refuses any existing tables, and performs schema creation in one transaction. It does not copy AS1 sample data. To upgrade the previous AS2 schema, run `dotnet run --project TaskTrack.API -- --upgrade-db` once before starting this version; the additive upgrade is transactional and idempotent. Normal API startup does not change the schema. Restore a private backup when you need the existing data.

API: `http://localhost:5102/api`; Swagger: `http://localhost:5102/swagger`. The frontend runs on port 3012. Configure `CORS_ORIGINS` for the actual frontend origins. `DATABASE_URL` is supported when a connection string is absent. AS2 has its own User Secrets ID.

## Authentication and permissions

- `POST /api/auth/register`: FullName, Email, Password → 201; normalized duplicate email → 409.
- `POST /api/auth/login`: Email, Password → signed token, expiry and safe account data; bad credentials → 401.
- `GET /api/auth/me`: authenticated session verification.
- JWT: AccountID, Email, Role, FullName, TokenVersion and exp; access expires after 15 minutes. Deleted accounts, changed roles, password changes and logout invalidate old sessions.
- `POST /api/auth/refresh`: rotates the signed refresh token; only its SHA-256 hash is stored. Refresh expires after seven days from login. Reusing a token revokes its family. One active refresh session per account; a new login replaces the previous refresh session.
- `POST /api/auth/logout`: takes the refresh token and revokes that session; returns 204, including repeated logout.
- `PUT /api/auth/profile`: authenticated own-name update or password change with CurrentPassword/NewPassword. Email and Role cannot be changed here; password changes revoke existing tokens.
- All AS1 GET endpoints stay public. POST/PUT/DELETE require authentication.
- `GET /api/accounts`, `GET /api/accounts/{id}`, `PUT /api/accounts/{id}`, `DELETE /api/accounts/{id}` require Admin; Staff receives 403.
- Account update accepts FullName and/or Role. The acting account comes from the verified JWT; changing your own role returns 409, while changing your own name or submitting the unchanged role remains allowed. Demoting or deleting the last Admin also returns 409. Account changes acquire a PostgreSQL transaction/table lock before reading roles and counting Admins, so concurrent demotions/deletions cannot remove all Admins. Account deletion still returns 409 when the account created any task, including a soft-deleted task.
- Task CreatedByID and UpdatedByID are derived from JWT; clients cannot override them. Update and soft delete record the editor while preserving the creator. Linked records prevent other entity deletion.

## Verification

Build the solution, then configure `ConnectionStrings__DefaultConnection`, `JWT_SECRET`, `ADMIN_EMAIL` and `ADMIN_PASSWORD` for an empty, separate PostgreSQL test database such as `tasktrack_test_ass2`. Initialize, seed and run its API on port 5103 in its own terminal:

```powershell
dotnet restore QE190064_PRN232_Ass1_BE.sln
dotnet build QE190064_PRN232_Ass1_BE.sln --configuration Release --no-restore
dotnet TaskTrack.API/bin/Release/net8.0/TaskTrack.API.dll --init-db
dotnet TaskTrack.API/bin/Release/net8.0/TaskTrack.API.dll --seed-admin
dotnet TaskTrack.API/bin/Release/net8.0/TaskTrack.API.dll --urls http://127.0.0.1:5103
```

In another terminal, set the matching test variables and run:

```powershell
$env:API_BASE_URL = 'http://127.0.0.1:5103/api'
$env:TEST_DATABASE_URL = 'postgresql://YOUR_USER:YOUR_PASSWORD@127.0.0.1:5432/tasktrack_test_ass2'
$env:STAFF_EMAIL = 'YOUR_TEST_STAFF_EMAIL'
$env:STAFF_PASSWORD = 'YOUR_TEST_STAFF_PASSWORD'
# ADMIN_EMAIL, ADMIN_PASSWORD and JWT_SECRET must match the running test API.
dotnet test tests/TaskTrack.IntegrationTests.csproj --configuration Release --no-build --no-restore
```

The 18 API integration tests and five account-protection cases are written in C# with xUnit, HttpClient, EF Core and Npgsql; the backend needs no Node.js or standalone SQL setup files. The test API's database/JWT settings and the test credentials must match the environment above. Tests refuse remote hosts, database names not ending in `_ass2`, and the main local database `qe190064_prn232_ass2`. Each API test creates its own fixtures and removes only their IDs; no fixed AS1 sample IDs are required. Account-protection tests create and remove private schemas to check self-role changes, the last Admin and concurrent demotions/deletions. The rollback test injects a database failure only for its own task. Run this suite against an isolated local test instance, never production.

## Render deployment

Use the existing Dockerfile: Docker runtime, repository root as build context, and port 8080. Set `DATABASE_URL`, `JWT_SECRET`, `CORS_ORIGINS` and `ASPNETCORE_ENVIRONMENT=Production` in Render. Use the database internal URL for a service in the same region; allow the exact HTTPS Vercel origin in CORS.

The existing live AS2 database already has its schema and grading accounts; run the additive `--upgrade-db` command once for the previous AS2 schema, then redeploy without reinitializing or reseeding. For a new, empty AS2 database, run `dotnet TaskTrack.API.dll --init-db`, then `dotnet TaskTrack.API.dll --seed-admin` with private environment variables. Both commands exit; keep the normal web-service startup command. Register Staff through the live frontend after deployment. The assignment requires adding SystemAccount via migration/ALTER TABLE and seeding Admin directly; the two former SQL setup files were an implementation choice, not required deliverables.

Swagger remains available at `/swagger`; `/api/tags` is an existing database-backed health-check URL. `.dockerignore` excludes local environment files and production appsettings from the build context. Docker builds do not need database credentials or a JWT secret.

Live API: [Render AS2](https://qe190064-prn232-ass2-be.onrender.com/api/tags); [Swagger](https://qe190064-prn232-ass2-be.onrender.com/swagger). Deployment verified on 13 October 2026; database AS2 is separate from AS1 inside the existing Singapore PostgreSQL Free instance. That instance expires at 01:53 Vietnam time on 29 October 2026. Grading credentials are in the private submission document. See [DEPLOYMENT.md](DEPLOYMENT.md) for setup and recovery instructions.
