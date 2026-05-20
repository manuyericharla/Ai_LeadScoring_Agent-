using LeadScoring.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LeadScoring.Api.Services;

public class TenantDatabaseProvisioner(IConfiguration configuration, ILogger<TenantDatabaseProvisioner> logger)
    : ITenantDatabaseProvisioner
{
    public async Task ProvisionAsync(string schemaName, CancellationToken cancellationToken = default)
    {
        var masterConnection = configuration.GetConnectionString("Hiperbrains")
            ?? throw new InvalidOperationException("Connection string 'Hiperbrains' is missing.");

        try
        {
            await RecreateSchemaAsync(masterConnection, schemaName, cancellationToken);

            var options = new DbContextOptionsBuilder<LeadScoringDbContext>()
                .UseNpgsql(masterConnection, npgsql =>
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schemaName))
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
                .Options;

            await using var db = new LeadScoringDbContext(options, schemaName);
            var created = await db.Database.EnsureCreatedAsync(cancellationToken);
            logger.LogInformation(
                "Tenant schema {SchemaName} is ready (EnsureCreated={Created}).",
                schemaName,
                created);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to provision tenant schema {SchemaName}.", schemaName);
            await TryDropSchemaAsync(masterConnection, schemaName, cancellationToken);
            throw new AuthValidationException(
                "Could not set up your company workspace. Please try again in a moment or use a different company name.");
        }
    }

    private static async Task RecreateSchemaAsync(
        string connectionString,
        string schemaName,
        CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        await using (var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE", conn))
        {
            await drop.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schemaName}\"", conn))
        {
            await create.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task TryDropSchemaAsync(
        string connectionString,
        string schemaName,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(cancellationToken);
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE", conn);
            await drop.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception dropEx)
        {
            logger.LogWarning(dropEx, "Could not drop failed tenant schema {SchemaName}.", schemaName);
        }
    }
}
