# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

SamReporting: a Blazor Server app (.NET 8) for Sun American Mortgage internal reporting. Single project `blazor-webapp/SamReporting.csproj` under solution `blazor.sln`.

## Commands

Run from the `blazor-webapp/` directory:

- Build: `dotnet build`
- Run (dev, hot reload): `dotnet watch run`
- Run (normal): `dotnet run`
- Restore: `dotnet restore`
- Publish: `dotnet publish -c Release`

No test project exists yet.

## Architecture

Interactive Server Blazor — pages are rendered server-side with `AddInteractiveServerRenderMode()` (see [Program.cs](blazor-webapp/Program.cs)). Routing via [Components/Routes.razor](blazor-webapp/Components/Routes.razor); layout at [Components/Layout/MainLayout.razor](blazor-webapp/Components/Layout/MainLayout.razor).

Data access layer:

- [SqlService](blazor-webapp/Services/SqlService.cs) — thin wrapper around `Microsoft.Data.SqlClient`. Registered as singleton. Reads connection string `SamReporting` from config. Exposes two `QueryAsync` overloads: a generic dictionary-returning one and a mapper-based one. All queries must be parameterized via the `(string name, object? value)[]` parameter tuple.
- Domain services (e.g., [HistoricalService](blazor-webapp/Services/HistoricalService.cs)) are registered **scoped** and compose `SqlService`. They own the SQL as `const string` fields and map rows into strongly-typed records in [Models/](blazor-webapp/Models/).

Data source: SQL Server view `vw_EncompassLoan_Silver` (loan/funding data). Date-range queries use `@start`/`@end` parameters; aggregation queries bucket loans by product, purpose, channel, and sales transaction type using `CASE` expressions — update the `GROUP BY` alongside the `SELECT` when changing bucket logic (they are duplicated).

Connection string lives in [appsettings.json](blazor-webapp/appsettings.json) under `ConnectionStrings:SamReporting` and currently targets `AMB-SQL\AMBSQL` with integrated auth — development requires Windows auth access to that server.

## Conventions

- Nullable reference types and implicit usings are enabled.
- Register new data services in `Program.cs` — scoped for per-request domain services, singleton for stateless infra like `SqlService`.
- Page-scoped CSS goes next to the razor file as `{Component}.razor.css` (Blazor CSS isolation).
