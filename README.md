# Hany Optics

An ASP.NET Core MVC app for managing an optical shop's day-to-day sales: customer lookup,
a multi-step new-order wizard (frame + lenses / frame only / lens replacement, with an
optional prescription), payments, and order/customer listings. Built against an existing
SQL Server schema whose stored procedures and triggers own most of the business rules
(stock reservation, totals, payment classification, status transitions).

## Layers

```
HanyOptics.sln
+-- src/
    +-- HanyOptics.Domain          Entities & enums. No dependencies on anything else.
    +-- HanyOptics.DataAccess      Two independent EF Core DbContexts:
    |     Identity/                 - ApplicationDbContext (ASP.NET Core Identity's own
    |                                  tables: AspNetUsers, AspNetRoles, ...).
    |     Persistence/               - HanyOpticsDbContext (business tables: orders,
    |                                  customers, frames, payments, ...).
    +-- HanyOptics.BusinessLogic    Services between Web and DataAccess - auth (JWT), the
    |                                order wizard, and read services for the Orders/
    |                                Customers pages. Business-critical writes (create
    |                                order, add item, add payment, change status) go
    |                                through the DB's own stored procedures rather than
    |                                being re-implemented here.
    +-- HanyOptics.Web              ASP.NET Core MVC: Controllers + Razor views, Arabic
                                     RTL UI, JWT-in-HttpOnly-cookie authentication.
```

Dependency direction: `Web -> BusinessLogic -> DataAccess -> Domain`. Controllers only ever
depend on `HanyOptics.BusinessLogic` interfaces - never on `DbContext`/Identity types
directly.

## Getting started

1. You need an existing SQL Server database with the HanyOptics schema (tables, triggers,
   and stored procedures - `sp_create_order`, `sp_add_order_item`, `sp_add_payment`,
   `sp_update_order_status`, etc.) already applied.
2. Copy `src/HanyOptics.Web/appsettings.json` to `appsettings.Development.json` in the same
   folder (gitignored - never commit real values there) and fill in:
   - `ConnectionStrings:HanyOpticsDb` - your real SQL Server connection string.
   - `SeedAdmin:Email` / `SeedAdmin:Password` - if set, a matching admin account is created
     on first run. Leave blank to skip seeding and register a user through the app instead.
3. Open `HanyOptics.sln` (.NET 10 SDK required), restore, and run the EF Core migration for
   Identity only (this creates just `AspNetUsers`/`AspNetRoles`, nothing else - the business
   tables are expected to already exist):
   `dotnet ef database update --project src/HanyOptics.DataAccess --startup-project src/HanyOptics.Web`
4. Set `HanyOptics.Web` as the startup project and run.
