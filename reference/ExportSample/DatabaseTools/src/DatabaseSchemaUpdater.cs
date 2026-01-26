using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using ToolArch.Shared.Schema;

namespace ToolArchUpdater
{
    /// <summary>
    /// Classe che gestisce database schema updater all'interno del tool.
    /// </summary>
    internal static class DatabaseSchemaUpdater
    {
        private const string DatabaseFileName = "ToolArchMilestone.db";

        private static readonly IReadOnlyList<string> BaseCommands = new[]
        {
            "PRAGMA foreign_keys = ON;"
        };

        private static readonly IReadOnlyDictionary<string, string> TableDefinitions = DatabaseSchemaDefinition.TableDefinitions;
        private static readonly IReadOnlyList<string> IndexDefinitions = DatabaseSchemaDefinition.IndexDefinitions;
        private static readonly IReadOnlyList<string> ViewDefinitions = DatabaseSchemaDefinition.ViewDefinitions;
        private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ColumnDefinitions = DatabaseSchemaDefinition.ColumnDefinitions;

        /// <summary>
        /// Esegue la logica main senza cambiare il comportamento.
        /// </summary>
        private static void Main()
        {
            try
            {
                var assemblyDirectory = AppDomain.CurrentDomain.BaseDirectory;
                var dataDirectory = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "data"));
                Directory.CreateDirectory(dataDirectory);

                var dbPath = Path.Combine(dataDirectory, DatabaseFileName);
                EnsureDatabaseFileExists(dbPath);

                using (var connection = new SQLiteConnection(string.Format("Data Source={0};Version=3;", dbPath)))
                {
                    connection.Open();

                    ExecuteCommands(connection, BaseCommands);
                    EnsureTables(connection);
                    EnsureColumns(connection);
                    ExecuteCommands(connection, IndexDefinitions);
                    ExecuteCommands(connection, ViewDefinitions);
                }

                Console.WriteLine("Aggiornamento schema completato con successo.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Errore durante l'aggiornamento del database:");
                Console.Error.WriteLine(ex);
                Environment.ExitCode = 1;
            }
        }

        /// <summary>
        /// Si assicura che la parte database file exists sia pronta prima di procedere.
        /// </summary>
        private static void EnsureDatabaseFileExists(string path)
        {
            if (!File.Exists(path))
            {
                SQLiteConnection.CreateFile(path);
            }
        }

        /// <summary>
        /// Si assicura che la parte tables sia pronta prima di procedere.
        /// </summary>
        private static void EnsureTables(SQLiteConnection connection)
        {
            foreach (var kvp in TableDefinitions)
            {
                ExecuteNonQuery(connection, kvp.Value);
            }
        }

        /// <summary>
        /// Si assicura che la parte columns sia pronta prima di procedere.
        /// </summary>
        private static void EnsureColumns(SQLiteConnection connection)
        {
            foreach (var kvp in ColumnDefinitions)
            {
                var tableName = kvp.Key;
                var columns = kvp.Value;

                if (!TableExists(connection, tableName))
                {
                    // Table will be created through EnsureTables, skip column check.
                    continue;
                }

                var existingColumns = GetTableColumns(connection, tableName);
                foreach (var column in columns)
                {
                    if (!existingColumns.Contains(column.Key))
                    {
                        var sql = string.Format("ALTER TABLE {0} ADD COLUMN {1} {2};", tableName, column.Key, column.Value);
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

