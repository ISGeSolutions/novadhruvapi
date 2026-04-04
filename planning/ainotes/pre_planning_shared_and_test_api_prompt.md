# Generate Shared Infrastructure + Test API for .NET 10+ Microservices

I need you to generate **production-ready .NET 10.0+ code** using **C# 13** for:

1. **A complete shared infrastructure project** that will be used by all microservices APIs
2. **A basic test API project** to validate the shared infrastructure

---

## Company & Product Information

- **Company Name:** ISG
- **Product Name:** Dhruv
- **Namespace Prefix:** `Isg.Dhruv`

All namespaces should follow the pattern: `Isg.Dhruv.[ProjectName].[SubNamespace]`

---

## Part 1: Shared Infrastructure Project

### Project Name
`Isg.Dhruv.Shared.Infrastructure`

### Purpose
This shared project contains **cross-cutting infrastructure concerns** that will be used by 10+ microservices APIs. It must:
- Have **no domain knowledge**
- Be **database-agnostic** (support SQL Server, PostgreSQL, MySQL from day one)
- Enforce **tenant isolation** in all data access
- Support **multi-database** environments via dialect abstraction
- Be **lean and focused** (infrastructure only, no business logic)

### Core Components to Generate

#### 1. Database Abstraction Layer
- `ISqlDialect` interface with methods for:
  - UTC date/time functions
  - UUID/GUID handling
  - Pagination (OFFSET/LIMIT vs FETCH NEXT)
  - Column type definitions (UUID, Text, Decimal, DateTime, Boolean, Int, BigInt)
  - Locking mechanisms (FOR UPDATE, SKIP LOCKED)
  - Case-insensitive string comparison
  - Last inserted ID retrieval
- `SqlServerDialect` implementation
- `PostgreSqlDialect` implementation  
- `MySqlDialect` implementation
- `IDbConnectionFactory` for creating database connections based on provider
- `ITenantScopedConnection` for tenant-aware database operations

#### 2. Tenant Context
- `ITenantContext` interface
- `TenantContext` implementation (mutable, scoped)
- `TenantResolutionMiddleware` (extracts tenant_id from JWT claims)

#### 3. Configuration Support
- `DatabaseOptions` class for database configuration
- `EncryptedConfigurationProvider` for decrypting connection strings (supports "ENC:" prefix)
- Configuration validation helpers

#### 4. Common Models
- `Result` and `Result<T>` pattern for operation outcomes
- `PagedResult<T>` for pagination

#### 5. Extension Methods
- `ServiceCollectionExtensions` for registering database services
- `WebApplicationExtensions` for middleware registration

### Technical Requirements
- ✅ Use **British English** for all identifiers (colour, authorisation, etc.)
- ✅ File-scoped namespaces
- ✅ Nullable reference types enabled
- ✅ Comprehensive XML documentation
- ✅ CancellationToken support on all async methods
- ✅ Proper error handling and logging
- ✅ Target .NET 10 (or latest available)

### Database Connection String Format
Configuration should support:
```json
{
  "Database": {
    "Provider": "SqlServer",
    "SqlServer": {
      "ConnectionString": "ENC:base64encryptedvalue=="
    },
    "PostgreSql": {
      "ConnectionString": "ENC:base64encryptedvalue=="
    },
    "MySql": {
      "ConnectionString": "ENC:base64encryptedvalue=="
    }
  }
}
```

### Tenant Resolution
- Extract `tenant_id` from JWT claim
- Validate tenant exists and is active
- Populate scoped `ITenantContext`
- Return 401 if tenant claim missing
- Return 400 if tenant format invalid

### Query Tenant Scoping
Every query executed through `ITenantScopedConnection` must:
- Automatically inject `TenantId` parameter
- Validate SQL contains "tenant_id" in WHERE clause (compile-time check)
- Throw exception if tenant context not resolved

---

## Part 2: Test API Project

### Project Name
`TestApi` (or `Isg.Dhruv.TestApi`)

### Purpose
A minimal API project to validate the shared infrastructure works correctly with all three database providers.

### Required Endpoints

**All endpoints must use API versioning with the URL pattern: `/api/v1/[resource]`**

#### 1. Hello World Endpoint
```
GET /api/v1/hello
```
Returns:
```json
{
  "message": "Hello from Dhruv Test API",
  "version": "1.0",
  "timestamp": "2026-01-09T14:30:00Z",
  "environment": "Development"
}
```

#### 2. SQL Server Test Endpoint
```
GET /api/v1/test/sqlserver
```
Executes query:
```sql
SELECT code, value FROM sales97.dbo.pointer
```
Returns result as JSON array.

#### 3. PostgreSQL Test Endpoint
```
GET /api/v1/test/postgresql
```
Executes query:
```sql
SELECT code, value FROM sales97.pointer
```
(Note: `sales97` is a schema name in PostgreSQL, not database.tablename)

Returns result as JSON array.

#### 4. MySQL Test Endpoint
```
GET /api/v1/test/mysql
```
Executes query:
```sql
SELECT code, value FROM sales97.dbo.pointer
```
Returns result as JSON array.

### API Versioning Requirements

- Use **URL-based versioning** (e.g., `/api/v1/...`, `/api/v2/...`)
- Current version is **v1**
- Version should be part of the route template
- Include API version in response metadata where appropriate
- Prepare for future versioning (v2, v3, etc.)

### Technical Requirements for Test API

#### Project Structure
```
TestApi/
  ├── Program.cs                    # Main entry point with versioned endpoint definitions
  ├── appsettings.json              # Configuration with all three DB connections
  ├── appsettings.Development.json  # Development overrides
  ├── TestApi.csproj                # Project file with dependencies
  └── Models/
      └── PointerDto.cs             # DTO for pointer table results
```

#### Dependencies
- Reference the shared infrastructure project
- Dapper (for database queries)
- Microsoft.Data.SqlClient (SQL Server)
- Npgsql (PostgreSQL)
- MySqlConnector (MySQL)
- Minimal APIs (built into .NET)

#### Configuration Structure
```json
{
  "Database": {
    "Provider": "SqlServer",
    "SqlServer": {
      "ConnectionString": "Server=localhost;Database=sales97;User Id=sa;Password=YourPassword;TrustServerCertificate=true"
    },
    "PostgreSql": {
      "ConnectionString": "Host=localhost;Database=sales97;Username=postgres;Password=postgres"
    },
    "MySql": {
      "ConnectionString": "Server=localhost;Database=sales97;User=root;Password=mysql"
    }
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

#### Endpoint Behavior
- Each database test endpoint should:
  - Create a connection using the appropriate dialect
  - Execute the query specific to that database provider
  - Return results as JSON
  - Handle errors gracefully (return 500 with error message if query fails)
  - Log the query execution time

#### Expected DTO Structure
```csharp
public record PointerDto(string Code, string Value);
```

### Error Handling
- If database connection fails, return 500 with clear error message
- If query fails, return 500 with SQL error details
- Log all errors with appropriate context

### Logging
- Log startup configuration (which DB provider is active)
- Log each query execution with timing
- Log errors with full exception details

---

## Database Schema Assumptions

The test assumes a table exists with this structure:

**SQL Server & MySQL:**
```sql
CREATE TABLE sales97.dbo.pointer (
    code VARCHAR(50) NOT NULL,
    value VARCHAR(255) NOT NULL
);
```

**PostgreSQL:**
```sql
CREATE SCHEMA IF NOT EXISTS sales97;

CREATE TABLE sales97.pointer (
    code VARCHAR(50) NOT NULL,
    value VARCHAR(255) NOT NULL
);
```

**Sample Data:**
```sql
INSERT INTO pointer (code, value) VALUES ('TEST1', 'Test Value 1');
INSERT INTO pointer (code, value) VALUES ('TEST2', 'Test Value 2');
INSERT INTO pointer (code, value) VALUES ('SYSTEM', 'System Configuration');
```

---

## Generation Requirements

### 1. Shared Infrastructure Project

Generate complete, production-ready code including:

**Core Files:**
- `ISqlDialect.cs` - Complete interface with all database abstraction methods
- `SqlServerDialect.cs` - Full SQL Server implementation
- `PostgreSqlDialect.cs` - Full PostgreSQL implementation
- `MySqlDialect.cs` - Full MySQL implementation
- `IDbConnectionFactory.cs` - Factory interface for creating connections
- `DbConnectionFactory.cs` - Factory implementation
- `ITenantScopedConnection.cs` - Tenant-aware connection interface
- `TenantScopedConnection.cs` - Tenant-aware connection implementation
- `ITenantContext.cs` - Tenant context interface
- `TenantContext.cs` - Tenant context implementation (mutable, scoped)
- `TenantResolutionMiddleware.cs` - JWT tenant extraction middleware
- `DatabaseOptions.cs` - Configuration model
- `EncryptedConfigurationProvider.cs` - Decrypt "ENC:" prefixed values
- `Result.cs` - Result pattern implementation
- `PagedResult.cs` - Pagination model
- `ServiceCollectionExtensions.cs` - DI registration extensions
- `WebApplicationExtensions.cs` - Middleware registration extensions
- `Acme.Shared.Infrastructure.csproj` - Project file with all dependencies

**Each file must include:**
- Complete implementation (no placeholders)
- XML documentation on all public members
- Proper error handling
- Logging where appropriate
- British English spelling

### 2. Test API Project

Generate complete, runnable API including:

**Files:**
- `Program.cs` - Complete minimal API setup with all 4 endpoints
- `PointerDto.cs` - DTO for query results
- `appsettings.json` - Full configuration with all three DB providers
- `appsettings.Development.json` - Development-specific settings
- `TestApi.csproj` - Complete project file

**Endpoint Implementation Details:**

Each test endpoint should follow this pattern with **API versioning**:
```csharp
var v1 = app.MapGroup("/api/v1");

v1.MapGet("/test/sqlserver", async (
    IDbConnectionFactory connectionFactory,
    ILogger<Program> logger) =>
{
    try
    {
        var sw = Stopwatch.StartNew();
        
        using var connection = connectionFactory.CreateConnection("SqlServer");
        await connection.OpenAsync();
        
        const string sql = "SELECT code, value, [update] dep_date, [crdate] updated_on FROM sales97.dbo.pointer";
        var results = await connection.QueryAsync<PointerDto>(sql);
        
        sw.Stop();
        
        logger.LogInformation(
            "SQL Server query executed in {ElapsedMs}ms, returned {Count} rows",
            sw.ElapsedMilliseconds,
            results.Count()
        );
        
        return Results.Ok(new
        {
            provider = "SqlServer",
            version = "v1",
            executionTimeMs = sw.ElapsedMilliseconds,
            rowCount = results.Count(),
            data = results
        });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "SQL Server query failed");
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Database Query Failed"
        );
    }
})
.WithName("TestSqlServer")
.WithTags("Database Tests")
.Produces<object>(200)
.ProducesProblem(500);
```

### 3. Documentation

Include a README.md that explains:
- Project structure and purpose
- How to configure each database provider
- How to run the test API
- How to test each endpoint
- Expected responses
- Common troubleshooting steps

### 4. Project File Structure

Show the complete solution structure:
```
/
├── src/
│   ├── Isg.Dhruv.Shared.Infrastructure/
│   │   ├── Data/
│   │   │   ├── ISqlDialect.cs
│   │   │   ├── SqlServerDialect.cs
│   │   │   ├── PostgreSqlDialect.cs
│   │   │   ├── MySqlDialect.cs
│   │   │   ├── IDbConnectionFactory.cs
│   │   │   ├── DbConnectionFactory.cs
│   │   │   ├── ITenantScopedConnection.cs
│   │   │   ├── TenantScopedConnection.cs
│   │   │   └── DatabaseOptions.cs
│   │   ├── Tenant/
│   │   │   ├── ITenantContext.cs
│   │   │   ├── TenantContext.cs
│   │   │   └── TenantResolutionMiddleware.cs
│   │   ├── Configuration/
│   │   │   └── EncryptedConfigurationProvider.cs
│   │   ├── Models/
│   │   │   ├── Result.cs
│   │   │   └── PagedResult.cs
│   │   ├── Extensions/
│   │   │   ├── ServiceCollectionExtensions.cs
│   │   │   └── WebApplicationExtensions.cs
│   │   └── Isg.Dhruv.Shared.Infrastructure.csproj
│   │
│   └── TestApi/
│       ├── Models/
│       │   └── PointerDto.cs
│       ├── Program.cs
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       └── TestApi.csproj
│
└── README.md
```

---

## Specific Implementation Notes

### SQL Dialect Differences

**Date/Time:**
- SQL Server: `GETUTCDATE()`
- PostgreSQL: `NOW() AT TIME ZONE 'UTC'` or `CURRENT_TIMESTAMP AT TIME ZONE 'UTC'`
- MySQL: `UTC_TIMESTAMP()`

**UUID/GUID:**
- SQL Server: `UNIQUEIDENTIFIER`, `NEWID()`
- PostgreSQL: `UUID`, `gen_random_uuid()`
- MySQL: `CHAR(36)` or `BINARY(16)`, generate in application

**Pagination:**
- SQL Server: `OFFSET n ROWS FETCH NEXT m ROWS ONLY`
- PostgreSQL: `LIMIT m OFFSET n`
- MySQL: `LIMIT m OFFSET n`

**Locking:**
- SQL Server: `WITH (UPDLOCK, ROWLOCK)`
- PostgreSQL: `FOR UPDATE`, `FOR UPDATE SKIP LOCKED`
- MySQL: `FOR UPDATE`, `FOR UPDATE SKIP LOCKED`

**Schema References:**
- SQL Server: `database.schema.table` (e.g., `sales97.dbo.pointer`)
- PostgreSQL: `schema.table` (e.g., `sales97.pointer`)
- MySQL: `database.table` or `schema.table` (e.g., `sales97.pointer`)

### Connection Factory Implementation

The factory should:
1. Read provider from configuration
2. Read connection string for that provider
3. Decrypt if prefixed with "ENC:"
4. Create appropriate connection type:
   - SqlConnection for SQL Server
   - NpgsqlConnection for PostgreSQL
   - MySqlConnection for MySQL
5. Apply dialect to connection
6. Return IDbConnection

### Tenant Scoped Connection

Should wrap the underlying IDbConnection and:
- Store the current TenantId from ITenantContext
- Intercept all query/execute methods
- Validate SQL contains "tenant_id" (optional, for safety)
- Automatically add TenantId to parameters
- Delegate actual execution to underlying connection

---

## NuGet Packages Required

### Shared Infrastructure Project
```xml
<PackageReference Include="Dapper" Version="2.1.35" />
<PackageReference Include="Microsoft.Data.SqlClient" Version="5.2.0" />
<PackageReference Include="Npgsql" Version="8.0.1" />
<PackageReference Include="MySqlConnector" Version="2.3.5" />
<PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Options" Version="8.0.0" />
```

### Test API Project
```xml
<ProjectReference Include="..\Acme.Shared.Infrastructure\Acme.Shared.Infrastructure.csproj" />
<PackageReference Include="Dapper" Version="2.1.35" />
```

---

## Expected Output Format

Please provide:

### 1. Overview
Brief explanation of the architecture and design decisions.

### 2. Complete File Listing
Show every file that will be generated with its full path.

### 3. Source Code
Provide complete, production-ready code for every file:
- No placeholders or TODOs
- Full implementations
- Comprehensive XML documentation
- Error handling
- Logging

### 4. Configuration Files
Complete appsettings.json files with examples.

### 5. Project Files
Complete .csproj files with all dependencies.

### 6. README.md
Complete documentation including:
- How to build and run
- How to configure databases
- How to test each endpoint
- Troubleshooting guide

### 7. Usage Examples
Show how to:
- Register services in Program.cs
- Use ITenantScopedConnection in a repository
- Execute queries with each dialect
- Test the endpoints with curl/HTTP

---

## Testing the Generated Code

After generation, I should be able to:

1. **Build the projects:**
   ```bash
   dotnet build
   ```

2. **Run the test API:**
   ```bash
   cd src/TestApi
   dotnet run
   ```

3. **Test the endpoints:**
   ```bash
   curl http://localhost:5000/api/v1/hello
   curl http://localhost:5000/api/v1/test/sqlserver
   curl http://localhost:5000/api/v1/test/postgresql
   curl http://localhost:5000/api/v1/test/mysql
   ```

4. **See working results** from each database provider (assuming the databases are running and configured).

---

## Important Reminders

✅ Use **British English** throughout (authorise, colour, initialise, etc.)  
✅ Include **complete implementations** - no placeholders  
✅ Add **comprehensive XML documentation** on all public APIs  
✅ Use **file-scoped namespaces** (namespace X;)  
✅ Enable **nullable reference types** (#nullable enable)  
✅ Include **CancellationToken** parameters on all async methods  
✅ Use **ILogger** for all logging  
✅ Handle **errors gracefully** with proper exception types  
✅ Follow **.NET naming conventions** (PascalCase for public, camelCase for private)  
✅ Use **records for DTOs** where appropriate  
✅ Include **proper disposal** of resources (IDisposable, using statements)

---

**Please generate the complete shared infrastructure project and test API project now.**