using System;
using System.Collections.Generic;
using System.Data.SQLite;
using ToolArch.Shared.Schema;

namespace ToolArchiviazioniMilestone.Data.Schema
{
    /// <summary>
    /// Classe che gestisce database schema migrator all'interno del tool.
    /// </summary>
    public sealed class DatabaseSchemaMigrator
    {
        private static readonly IReadOnlyList<string> BaseCommands = new[]
        {
            "PRAGMA foreign_keys = ON;"
        };

        /// <summary>
        /// Esegue la logica migrate senza cambiare il comportamento.
        /// </summary>
        public void Migrate(SQLiteConnection connection)
        {
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            ExecuteCommands(connection, BaseCommands);
            EnsureTables(connection);
            EnsureColumns(connection);
            ExecuteCommands(connection, DatabaseSchemaDefinition.IndexDefinitions);
            ExecuteCommands(connection, DatabaseSchemaDefinition.ViewDefinitions);
        }

        /// <summary>
        /// Si assicura che la parte tables sia pronta prima di procedere.
        /// </summary>
        private static void EnsureTables(SQLiteConnection connection)
        {
            foreach (var definition in DatabaseSchemaDefinition.TableDefinitions)
            {
                ExecuteNonQuery(connection, definition.Value);
            }
        }

        /// <summary>
        /// Si assicura che la parte columns sia pronta prima di procedere.
        /// </summary>
        private static void EnsureColumns(SQLiteConnection connection)
        {
            var columnDefinitions = DatabaseSchemaDefinition.ColumnDefinitions;
            foreach (var tableEntry in columnDefinitions)
            {
                var tableName = tableEntry.Key;
                if (!TableExists(connection, tableName))
                {
                    continue;
                }

                var existingColumns = GetTableColumns(connection, tableName);
                foreach (var columnEntry in tableEntry.Value)
                {
                    if (!existingColumns.Contains(columnEntry.Key))
                    {
                        var sql = string.Format("ALTER TABLE {0} ADD COLUMN {1} {2};", tableName, columnEntry.Key, columnEntry.Value);
                        ExecuteNonQuery(connection, sql);
                    }
                }
            }
        }

        /// <summary>
        /// Esegue la logica table exists senza cambiare il comportamento.
        /// </summary>
        private static bool TableExists(SQLiteConnection connection, string tableName)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=@name;";
                cmd.Parameters.AddWithValue("@name", tableName);
                using (var reader = cmd.ExecuteReader())
                {
                    return reader.Read();
                }
            }
        }

        /// <summary>
        /// Restituisce table columns gia pronto.
        /// </summary>
        private static HashSet<string> GetTableColumns(SQLiteConnection connection, string tableName)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = string.Format("PRAGMA table_info({0});", tableName);
                using (var reader = cmd.ExecuteReader())
                {
                    var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    while (reader.Read())
                    {
                        var name = reader["name"] as string;
                        if (!string.IsNullOrEmpty(name))
                        {
                            columns.Add(name);
                        }
                    }

                    return columns;
                }
            }
        }

        /// <summary>
        /// Esegue la logica execute commands senza cambiare il comportamento.
        /// </summary>
        private static void ExecuteCommands(SQLiteConnection connection, IEnumerable<string> commands)
        {
            foreach (var command in commands)
            {
                ExecuteNonQuery(connection, command);
            }
        }

        /// <summary>
        /// Esegue la logica execute non query senza cambiare il comportamento.
        /// </summary>
        private static void ExecuteNonQuery(SQLiteConnection connection, string sql)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }
    }
}

