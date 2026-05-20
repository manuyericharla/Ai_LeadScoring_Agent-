using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace LeadScoring.Api.Data;

/// <summary>
/// Sets PostgreSQL search_path so tenant-scoped EF queries resolve tables in the company schema.
/// </summary>
public sealed class TenantSchemaConnectionInterceptor(string schemaName) : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (connection is NpgsqlConnection npg)
        {
            var escaped = schemaName.Replace("\"", "\"\"", StringComparison.Ordinal);
            await using var cmd = npg.CreateCommand();
            cmd.CommandText = $"SET search_path TO \"{escaped}\", public";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }
}
