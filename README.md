# TaskTrack — PRN232 Assignment 2 Backend

ASP.NET Core 8, EF Core and PostgreSQL. Forked from Assignment 1 into a separate Assignment 2 repository. AS1 public reads remain available; writes require a valid Staff/Admin JWT. Account management requires Admin.

## Local setup

Use a **separate AS2 database**. Run `database/001_initial.sql` only against an empty database, then `database/002_auth.sql`. The latter can run again without resetting data. Legacy tasks keep a NULL creator.

Configure secrets outside Git (PowerShell, from this repository):

```powershell
$env:ConnectionStrings__DefaultConnection = 'Host=localhost;Port=5432;Database=qe190064_prn232_ass2;Username=postgres;Password=YOUR_DATABASE_PASSWORD'
$env:JWT_SECRET = 'YOUR_RANDOM_SECRET_WITH_AT_LEAST_32_UTF8_BYTES'
$env:ADMIN_EMAIL = 'YOUR_ADMIN_EMAIL'
$env:ADMIN_PASSWORD = 'YOUR_ADMIN_PASSWORD_AT_LEAST_8_CHARACTERS'
dotnet run --project TaskTrack.API -- --seed-admin
dotnet run --project TaskTrack.API
```

Admin seed writes a PBKDF2 hash directly to PostgreSQL, is idempotent, and never promotes an existing Staff account automatically. Register only creates Staff. No real credentials belong in source.

API: `http://localhost:5102/api`; Swagger: `http://localhost:5102/swagger`. The frontend runs on port 3012. Configure `CORS_ORIGINS` for the actual frontend origins. `DATABASE_URL` is supported when a connection string is absent. AS2 has its own User Secrets ID.

## Authentication and permissions

- `POST /api/auth/register`: FullName, Email, Password → 201; normalized duplicate email → 409.
- `POST /api/auth/login`: Email, Password → signed token, expiry and safe account data; bad credentials → 401.
- `GET /api/auth/me`: authenticated session verification.
- JWT: AccountID, Email, Role, FullName and exp; expires after 24 hours. Deleted accounts and changed roles invalidate old tokens.
- All AS1 GET endpoints stay public. POST/PUT/DELETE require authentication.
- `GET /api/accounts`, `GET /api/accounts/{id}`, `PUT /api/accounts/{id}`, `DELETE /api/accounts/{id}` require Admin; Staff receives 403.
- Account update accepts FullName and/or Role. Account deletion returns 409 when the account created any task, including a soft-deleted task.
- Task creator is derived from JWT; the client cannot override it. Tasks use soft deletion. Linked records prevent other entity deletion.

## Verification

Start a separate local API on port 5103 against an empty test database such as `tasktrack_test_ass2`. Apply both schema scripts to that database and configure `ConnectionStrings__DefaultConnection`, `JWT_SECRET`, `ADMIN_EMAIL` and `ADMIN_PASSWORD` for the test instance. After building, seed and run it in its own terminal:

```powershell
dotnet TaskTrack.API/bin/Release/net8.0/TaskTrack.API.dll --seed-admin
dotnet TaskTrack.API/bin/Release/net8.0/TaskTrack.API.dll --urls http://127.0.0.1:5103
```

In another terminal, set the matching test variables and run:

```powershell
dotnet restore QE190064_PRN232_Ass1_BE.sln
dotnet build QE190064_PRN232_Ass1_BE.sln --configuration Release --no-restore
$env:API_BASE_URL = 'http://127.0.0.1:5103/api'
$env:TEST_DATABASE_URL = 'postgresql://YOUR_USER:YOUR_PASSWORD@127.0.0.1:5432/tasktrack_test_ass2'
$env:STAFF_EMAIL = 'YOUR_TEST_STAFF_EMAIL'
$env:STAFF_PASSWORD = 'YOUR_TEST_STAFF_PASSWORD'
# ADMIN_EMAIL, ADMIN_PASSWORD and JWT_SECRET must match the running test API.
dotnet test tests/TaskTrack.IntegrationTests.csproj --configuration Release --no-build --no-restore
```

The 11 API integration tests are written in C# with xUnit, HttpClient and Npgsql; the backend needs no Node.js. Prepare a separate local database such as `tasktrack_test_ass2` with both schema scripts, seed its Admin and run a separate API instance against that database on port 5103 before testing. The test API's database/JWT settings and the test credentials must match the environment above. Tests refuse remote hosts, database names not ending in `_ass2`, and the main local database `qe190064_prn232_ass2`. Each test creates its own fixtures and removes only their IDs; the rollback test injects a database failure only for its own task. Run this suite against an isolated local test instance, never production.

## Render deployment

Use the existing Dockerfile: Docker runtime, repository root as build context, and port 8080. Set `DATABASE_URL`, `JWT_SECRET`, `CORS_ORIGINS` and `ASPNETCORE_ENVIRONMENT=Production` in Render. Use the database internal URL for a service in the same region; allow the exact HTTPS Vercel origin in CORS.

Apply `database/002_auth.sql` to an existing AS1 schema. Apply `001_initial.sql` first only for a new, empty AS2 database. Seed Admin using `dotnet TaskTrack.API.dll --seed-admin` in the built image with private environment variables; this command exits after seeding, so keep the normal web-service startup command. Register Staff through the live frontend after deployment.

Swagger remains available at `/swagger`; `/api/tags` is an existing database-backed health-check URL. `.dockerignore` excludes local environment files and production appsettings from the build context. Docker builds do not need database credentials or a JWT secret.

Live API: [Render AS2](https://qe190064-prn232-ass2-be.onrender.com/api/tags); [Swagger](https://qe190064-prn232-ass2-be.onrender.com/swagger). Deployment verified on 13 October 2026; database AS2 is separate from AS1 inside the existing Singapore PostgreSQL Free instance. That instance expires at 01:53 Vietnam time on 29 October 2026. Grading credentials are in the private submission document. See [DEPLOYMENT.md](DEPLOYMENT.md) for setup and recovery instructions.
