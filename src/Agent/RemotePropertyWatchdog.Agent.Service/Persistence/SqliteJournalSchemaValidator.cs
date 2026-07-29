using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace RemotePropertyWatchdog.Agent.Service;

internal static class SqliteJournalSchemaValidator
{
    public static async Task<bool> ValidateAsync(
        SqliteConnection candidate,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection expected = new(
            new SqliteConnectionStringBuilder
            {
                DataSource = ":memory:",
                Mode = SqliteOpenMode.Memory,
                Pooling = false
            }.ToString());
        await expected.OpenAsync(cancellationToken);

        await using (SqliteCommand command = expected.CreateCommand())
        {
            command.CommandText = $"""
                {SqliteJournalSchema.MigrationVersion1}
                INSERT INTO schema_migrations(version, name, applied_at_utc)
                VALUES (1, $migration_name, 'canonical');
                PRAGMA user_version = 1;
                """;
            command.Parameters.AddWithValue("$migration_name", SqliteJournalSchema.MigrationVersion1Name);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (await ReadUserVersionAsync(candidate, cancellationToken) !=
            await ReadUserVersionAsync(expected, cancellationToken))
        {
            return false;
        }

        string expectedDefinition = await ReadDefinitionSignatureAsync(expected, cancellationToken);
        string candidateDefinition = await ReadDefinitionSignatureAsync(candidate, cancellationToken);
        if (!StringComparer.Ordinal.Equals(expectedDefinition, candidateDefinition))
        {
            return false;
        }

        MigrationRecordSnapshot[] expectedMigrations = await ReadMigrationsAsync(expected, cancellationToken);
        MigrationRecordSnapshot[] candidateMigrations = await ReadMigrationsAsync(candidate, cancellationToken);
        return expectedMigrations.SequenceEqual(candidateMigrations);
    }

    private static async Task<int> ReadUserVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static async Task<string> ReadDefinitionSignatureAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        SchemaObjectSnapshot[] objects = await ReadSchemaObjectsAsync(connection, cancellationToken);
        List<TableSnapshot> tables = [];
        foreach (SchemaObjectSnapshot schemaObject in objects.Where(static value => value.Type == "table"))
        {
            tables.Add(await ReadTableAsync(connection, schemaObject.Name, cancellationToken));
        }

        DefinitionSnapshot snapshot = new(objects, [.. tables]);
        return JsonSerializer.Serialize(snapshot);
    }

    private static async Task<SchemaObjectSnapshot[]> ReadSchemaObjectsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        List<SchemaObjectSnapshot> objects = [];
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT type, name, sql
            FROM sqlite_schema
            WHERE type IN ('table', 'index', 'view', 'trigger')
              AND name NOT GLOB 'sqlite_*'
            ORDER BY type, name;
            """;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            objects.Add(new SchemaObjectSnapshot(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : NormalizeSql(reader.GetString(2))));
        }

        return [.. objects];
    }

    private static async Task<TableSnapshot> ReadTableAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        TablePropertiesSnapshot properties = await ReadTablePropertiesAsync(connection, tableName, cancellationToken);
        ColumnSnapshot[] columns = await ReadColumnsAsync(connection, tableName, cancellationToken);
        ForeignKeySnapshot[] foreignKeys = await ReadForeignKeysAsync(connection, tableName, cancellationToken);
        IndexSnapshot[] indexes = await ReadIndexesAsync(connection, tableName, cancellationToken);
        return new TableSnapshot(tableName, properties, columns, foreignKeys, indexes);
    }

    private static async Task<TablePropertiesSnapshot> ReadTablePropertiesAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT type, ncol, wr, strict
            FROM pragma_table_list
            WHERE schema = 'main' AND name = $table_name;
            """;
        command.Parameters.AddWithValue("$table_name", tableName);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new TablePropertiesSnapshot("missing", -1, false, false);
        }

        return new TablePropertiesSnapshot(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetInt32(2) != 0,
            reader.GetInt32(3) != 0);
    }

    private static async Task<ColumnSnapshot[]> ReadColumnsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        List<ColumnSnapshot> columns = [];
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT cid, name, type, [notnull], dflt_value, pk, hidden
            FROM pragma_table_xinfo($table_name)
            ORDER BY cid;
            """;
        command.Parameters.AddWithValue("$table_name", tableName);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new ColumnSnapshot(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3) != 0,
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5),
                reader.GetInt32(6)));
        }

        return [.. columns];
    }

    private static async Task<ForeignKeySnapshot[]> ReadForeignKeysAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        List<ForeignKeySnapshot> foreignKeys = [];
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, seq, [table], [from], [to], on_update, on_delete, match
            FROM pragma_foreign_key_list($table_name)
            ORDER BY id, seq;
            """;
        command.Parameters.AddWithValue("$table_name", tableName);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            foreignKeys.Add(new ForeignKeySnapshot(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7)));
        }

        return [.. foreignKeys];
    }

    private static async Task<IndexSnapshot[]> ReadIndexesAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        List<(string Name, bool IsUnique, string Origin, bool IsPartial)> inventory = [];
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT name, [unique], origin, partial
                FROM pragma_index_list($table_name)
                ORDER BY name;
                """;
            command.Parameters.AddWithValue("$table_name", tableName);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                inventory.Add((
                    reader.GetString(0),
                    reader.GetInt32(1) != 0,
                    reader.GetString(2),
                    reader.GetInt32(3) != 0));
            }
        }

        List<IndexSnapshot> indexes = [];
        foreach ((string name, bool isUnique, string origin, bool isPartial) in inventory)
        {
            indexes.Add(new IndexSnapshot(
                name,
                isUnique,
                origin,
                isPartial,
                await ReadIndexColumnsAsync(connection, name, cancellationToken)));
        }

        return [.. indexes];
    }

    private static async Task<IndexColumnSnapshot[]> ReadIndexColumnsAsync(
        SqliteConnection connection,
        string indexName,
        CancellationToken cancellationToken)
    {
        List<IndexColumnSnapshot> columns = [];
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT seqno, cid, name, [desc], coll, [key]
            FROM pragma_index_xinfo($index_name)
            ORDER BY seqno;
            """;
        command.Parameters.AddWithValue("$index_name", indexName);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new IndexColumnSnapshot(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetInt32(3) != 0,
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5) != 0));
        }

        return [.. columns];
    }

    private static async Task<MigrationRecordSnapshot[]> ReadMigrationsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        List<MigrationRecordSnapshot> migrations = [];
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT version, name FROM schema_migrations ORDER BY version, name;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            migrations.Add(new MigrationRecordSnapshot(reader.GetInt32(0), reader.GetString(1)));
        }

        return [.. migrations];
    }

    private static string NormalizeSql(string sql)
    {
        StringBuilder normalized = new(sql.Length);
        bool pendingWhitespace = false;
        char quote = '\0';
        for (int index = 0; index < sql.Length; index++)
        {
            char current = sql[index];
            if (quote != '\0')
            {
                normalized.Append(current);
                if (quote == ']' && current == ']')
                {
                    quote = '\0';
                }
                else if (quote != ']' && current == quote)
                {
                    if (index + 1 < sql.Length && sql[index + 1] == quote)
                    {
                        normalized.Append(sql[++index]);
                    }
                    else
                    {
                        quote = '\0';
                    }
                }

                continue;
            }

            if (char.IsWhiteSpace(current))
            {
                pendingWhitespace = normalized.Length > 0;
                continue;
            }

            if (pendingWhitespace)
            {
                normalized.Append(' ');
                pendingWhitespace = false;
            }

            normalized.Append(current);
            quote = current switch
            {
                '\'' or '"' or '`' => current,
                '[' => ']',
                _ => '\0'
            };
        }

        return normalized.ToString();
    }

    private sealed record DefinitionSnapshot(
        SchemaObjectSnapshot[] Objects,
        TableSnapshot[] Tables);

    private sealed record SchemaObjectSnapshot(string Type, string Name, string? Sql);

    private sealed record TableSnapshot(
        string Name,
        TablePropertiesSnapshot Properties,
        ColumnSnapshot[] Columns,
        ForeignKeySnapshot[] ForeignKeys,
        IndexSnapshot[] Indexes);

    private sealed record TablePropertiesSnapshot(
        string Type,
        int ColumnCount,
        bool WithoutRowId,
        bool IsStrict);

    private sealed record ColumnSnapshot(
        int Ordinal,
        string Name,
        string DeclaredType,
        bool IsNotNull,
        string? DefaultValue,
        int PrimaryKeyOrdinal,
        int HiddenKind);

    private sealed record ForeignKeySnapshot(
        int Id,
        int Sequence,
        string ReferencedTable,
        string SourceColumn,
        string? TargetColumn,
        string OnUpdate,
        string OnDelete,
        string Match);

    private sealed record IndexSnapshot(
        string Name,
        bool IsUnique,
        string Origin,
        bool IsPartial,
        IndexColumnSnapshot[] Columns);

    private sealed record IndexColumnSnapshot(
        int Sequence,
        int ColumnId,
        string? Name,
        bool IsDescending,
        string? Collation,
        bool IsKeyColumn);

    private sealed record MigrationRecordSnapshot(int Version, string Name);
}
