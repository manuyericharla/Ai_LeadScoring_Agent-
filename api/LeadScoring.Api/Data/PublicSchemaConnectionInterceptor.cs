using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace LeadScoring.Api.Data;

/// <summary>
/// Resets PostgreSQL search_path so shared company data resolves in <c>public</c>,
/// not a pooled tenant schema from prior requests.
/// </summary>
public sealed class PublicSchemaConnectionInterceptor : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (connection is NpgsqlConnection npg)
        {
            await using var cmd = npg.CreateCommand();
            cmd.CommandText = "SET search_path TO public";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }
}
