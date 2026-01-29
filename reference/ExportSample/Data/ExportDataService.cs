using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using ToolArchiviazioniMilestone.Data.Schema;
using ExportSample;

namespace ToolArchiviazioniMilestone.Data
{
    public sealed class ExportDataService
    {
        private const string GlobalPreferenceScope = "global";
        private const string LastExportSettingKey = "LastExportConfiguration";

        private readonly string _databasePath;
        private readonly object _syncRoot = new object();
        private bool _initialized;

        /// <summary>
        /// Costruttore di ExportDataService, inizializza il contesto senza effetti collaterali.
        /// </summary>
        public ExportDataService()
            : this(GetDefaultDatabasePath())
        {
        }

        /// <summary>
        /// Costruttore di ExportDataService, inizializza il contesto senza effetti collaterali.
        /// </summary>
        public ExportDataService(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("Percorso database non valido.", nameof(databasePath));

            _databasePath = databasePath;
        }

        public string DatabasePath
        {
            get { return _databasePath; }
        }

        /// <summary>
        /// Inizializza ialize con i default.
        /// </summary>
        public void Initialize()
        {
            lock (_syncRoot)
            {
                if (_initialized)
                    return;

                var directory = Path.GetDirectoryName(_databasePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                using (var connection = CreateConnection())
                {
                    connection.Open();
                    var migrator = new DatabaseSchemaMigrator();
                    migrator.Migrate(connection);
                }

                _initialized = true;
            }
        }

        /// <summary>
        /// Registra job started per tenerne traccia.
        /// </summary>
        public void RecordJobStarted(JobSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.JobId))
                return;

            EnsureInitialized();

            lock (_syncRoot)
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var transaction = connection.BeginTransaction())
                    {
                        var cmd = connection.CreateCommand();
                        cmd.Transaction = transaction;
                        cmd.CommandText =
                            "INSERT INTO jobs (" +
                            "public_id, id_lavoro, camera_name, server_address, started_utc, ended_utc, requested_start_utc, requested_end_utc, status, progress, output_folder, output_password, note, created_by, last_update_utc, procedimento_penale, rit_spec, target, magistrato, procura" +
                            ") VALUES (" +
                            "@public_id, @id_lavoro, @camera_name, @server_address, @started_utc, @ended_utc, @requested_start_utc, @requested_end_utc, @status, @progress, @output_folder, @output_password, @note, @created_by, @last_update_utc, @procedimento_penale, @rit_spec, @target, @magistrato, @procura" +
                            ") ON CONFLICT(public_id) DO UPDATE SET " +
                            "id_lavoro = excluded.id_lavoro, " +
                            "camera_name = excluded.camera_name, " +
                            "server_address = excluded.server_address, " +
                            "started_utc = excluded.started_utc, " +
                            "requested_start_utc = excluded.requested_start_utc, " +
                            "requested_end_utc = excluded.requested_end_utc, " +
                            "status = excluded.status, " +
                            "progress = excluded.progress, " +
                            "output_folder = excluded.output_folder, " +
                            "output_password = excluded.output_password, " +
                            "note = excluded.note, " +
                            "last_update_utc = excluded.last_update_utc, " +
                            "procedimento_penale = excluded.procedimento_penale, " +
                            "rit_spec = excluded.rit_spec, " +
                            "target = excluded.target, " +
                            "magistrato = excluded.magistrato, " +
                            "procura = excluded.procura;";

                        cmd.Parameters.AddWithValue("@public_id", snapshot.JobId);
                        cmd.Parameters.AddWithValue("@id_lavoro", snapshot.IdLavoro ?? string.Empty);
                        cmd.Parameters.AddWithValue("@camera_name", snapshot.CameraName ?? string.Empty);
                        cmd.Parameters.AddWithValue("@server_address", snapshot.ServerAddress ?? string.Empty);
                        cmd.Parameters.AddWithValue("@started_utc", ToUtcString(snapshot.StartTime));
                        cmd.Parameters.AddWithValue("@ended_utc", snapshot.EndTime.HasValue ? ToUtcString(snapshot.EndTime.Value) : (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@requested_start_utc", ToUtcString(snapshot.RequestedStartTime));
                        cmd.Parameters.AddWithValue("@requested_end_utc", snapshot.RequestedEndTime.HasValue ? ToUtcString(snapshot.RequestedEndTime.Value) : (object)DBNull.Value);
                        var normalizedStatus = NormalizeRunningStatus(snapshot.Status);
                        snapshot.Status = normalizedStatus;
                        cmd.Parameters.AddWithValue("@status", normalizedStatus);
                        cmd.Parameters.AddWithValue("@progress", snapshot.Progress);
                        cmd.Parameters.AddWithValue("@output_folder", snapshot.OutputFolder ?? string.Empty);
                        cmd.Parameters.AddWithValue("@output_password", snapshot.OutputPassword ?? string.Empty);
                        cmd.Parameters.AddWithValue("@note", snapshot.Note ?? string.Empty);
                        cmd.Parameters.AddWithValue("@created_by", snapshot.CreatedBy ?? string.Empty);
                        cmd.Parameters.AddWithValue("@last_update_utc", ToUtcString(DateTime.UtcNow));
                        cmd.Parameters.AddWithValue("@procedimento_penale", snapshot.ProcedimentoPenale ?? string.Empty);
                        cmd.Parameters.AddWithValue("@rit_spec", snapshot.RitSpec ?? string.Empty);
                        cmd.Parameters.AddWithValue("@target", snapshot.Target ?? string.Empty);
                        cmd.Parameters.AddWithValue("@magistrato", snapshot.Magistrato ?? string.Empty);
                        cmd.Parameters.AddWithValue("@procura", snapshot.Procura ?? string.Empty);

                        cmd.ExecuteNonQuery();

                        int jobId = GetJobId(connection, snapshot.JobId, transaction);
                        if (jobId > 0)
                        {
                            UpsertMetadata(connection, transaction, jobId, "CameraName", snapshot.CameraName);
                            UpsertMetadata(connection, transaction, jobId, "ServerAddress", snapshot.ServerAddress);
                            UpsertMetadata(connection, transaction, jobId, "IdLavoro", snapshot.IdLavoro);
                            UpsertMetadata(connection, transaction, jobId, "Magistrato", snapshot.Magistrato);
                            UpsertMetadata(connection, transaction, jobId, "Procura", snapshot.Procura);
                            UpsertMetadata(connection, transaction, jobId, "Target", snapshot.Target);
                        }

                        transaction.Commit();
                    }
                }
            }
        }

        /// <summary>
        /// Registra job progress per tenerne traccia.
        /// </summary>
        public void RecordJobProgress(string jobPublicId, int progress, string status)
        {
            if (string.IsNullOrWhiteSpace(jobPublicId))
                return;

            EnsureInitialized();

            lock (_syncRoot)
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "UPDATE jobs SET progress=@progress, status=@status, last_update_utc=@last_update WHERE public_id=@public_id;";
                        var normalizedStatus = NormalizeRunningStatus(status);
                        cmd.Parameters.AddWithValue("@progress", progress);
                        cmd.Parameters.AddWithValue("@status", normalizedStatus);
                        cmd.Parameters.AddWithValue("@last_update", ToUtcString(DateTime.UtcNow));
                        cmd.Parameters.AddWithValue("@public_id", jobPublicId);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        /// <summary>
        /// Registra job event per tenerne traccia.
        /// </summary>
        public void RecordJobEvent(string jobPublicId, string currentStatus, int? progress, string eventType, IDictionary<string, object> payload)
        {
            if (string.IsNullOrWhiteSpace(jobPublicId) || string.IsNullOrWhiteSpace(eventType))
                return;

            EnsureInitialized();

            lock (_syncRoot)
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var transaction = connection.BeginTransaction())
                    {
                        int jobId = GetJobId(connection, jobPublicId, transaction);
                        if (jobId <= 0)
                        {
                            transaction.Rollback();
                            return;
                        }

                        int? eventProgress = ExtractInt(payload, "value");
                        string payloadStatus = ExtractString(payload, "status");
                        string message = ExtractString(payload, "message");
                        string serializedPayload = payload != null ? JsonConvert.SerializeObject(payload) : null;

                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.Transaction = transaction;
                            cmd.CommandText =
                                "INSERT INTO job_events (job_id, event_type, event_utc, progress, status, message, payload_json) " +
                                "VALUES (@job_id, @event_type, @event_utc, @progress, @status, @message, @payload);";

                            cmd.Parameters.AddWithValue("@job_id", jobId);
                            cmd.Parameters.AddWithValue("@event_type", eventType);
                            cmd.Parameters.AddWithValue("@event_utc", ToUtcString(DateTime.UtcNow));
                            if (eventProgress.HasValue)
                                cmd.Parameters.AddWithValue("@progress", eventProgress.Value);
                            else if (progress.HasValue)
                                cmd.Parameters.AddWithValue("@progress", progress.Value);
                            else
                                cmd.Parameters.AddWithValue("@progress", DBNull.Value);

                            string statusValue = !string.IsNullOrWhiteSpace(payloadStatus) ? payloadStatus : currentStatus;
                            if (!string.IsNullOrWhiteSpace(statusValue))
                            {
                                statusValue = (eventType.Equals("error", StringComparison.OrdinalIgnoreCase) || LooksLikeFinalStatus(statusValue))
                                    ? NormalizeFinalStatus(statusValue)
                                    : NormalizeRunningStatus(statusValue);
                                cmd.Parameters.AddWithValue("@status", statusValue);
                            }
                            else
                            {
                                cmd.Parameters.AddWithValue("@status", DBNull.Value);
                            }

                            if (!string.IsNullOrWhiteSpace(message))
                                cmd.Parameters.AddWithValue("@message", message);
                            else
                                cmd.Parameters.AddWithValue("@message", DBNull.Value);

                            if (!string.IsNullOrWhiteSpace(serializedPayload))
                                cmd.Parameters.AddWithValue("@payload", serializedPayload);
                            else
                                cmd.Parameters.AddWithValue("@payload", DBNull.Value);

                            cmd.ExecuteNonQuery();
                        }

                        if (eventType.Equals("error", StringComparison.OrdinalIgnoreCase))
                        {
                            int errorCode = ExtractInt(payload, "code") ?? 0;
                            string detail = ExtractString(payload, "detail");
                            string errorMessage = message ?? ExtractString(payload, "error");
                            if (string.IsNullOrWhiteSpace(errorMessage))
                                errorMessage = ExtractString(payload, "message");
                            InsertJobError(connection, transaction, jobId, errorCode, errorMessage, detail);
                        }

                        string statusToApply = !string.IsNullOrWhiteSpace(currentStatus) ? currentStatus : payloadStatus;
                        if (!string.IsNullOrWhiteSpace(statusToApply))
                        {
                            statusToApply = (eventType.Equals("error", StringComparison.OrdinalIgnoreCase) || LooksLikeFinalStatus(statusToApply))
                                ? NormalizeFinalStatus(statusToApply)
                                : NormalizeRunningStatus(statusToApply);
                        }
                        else if (eventType.Equals("error", StringComparison.OrdinalIgnoreCase))
                        {
                            statusToApply = NormalizeFinalStatus("Errore");
                        }

                        if (!string.IsNullOrWhiteSpace(statusToApply) || progress.HasValue || eventProgress.HasValue)
                        {
                            using (var updateCmd = connection.CreateCommand())
                            {
                                updateCmd.Transaction = transaction;
                                updateCmd.CommandText =
                                    "UPDATE jobs SET " +
                                    (progress.HasValue || eventProgress.HasValue ? "progress=@progress, " : string.Empty) +
                                    (!string.IsNullOrWhiteSpace(statusToApply) ? "status=@status, " : string.Empty) +
                                    "last_update_utc=@last_update WHERE job_id=@job_id;";

                                if (progress.HasValue || eventProgress.HasValue)
                                {
                                    int progValue = eventProgress ?? progress ?? 0;
                                    updateCmd.Parameters.AddWithValue("@progress", progValue);
                                }

                                if (!string.IsNullOrWhiteSpace(statusToApply))
                                {
                                    updateCmd.Parameters.AddWithValue("@status", statusToApply);
                                }

                                updateCmd.Parameters.AddWithValue("@last_update", ToUtcString(DateTime.UtcNow));
                                updateCmd.Parameters.AddWithValue("@job_id", jobId);
                                updateCmd.ExecuteNonQuery();
                            }
                        }

                        transaction.Commit();
                    }
                }
            }
        }

        /// <summary>
        /// Registra job error per tenerne traccia.
        /// </summary>
        public void RecordJobError(string jobPublicId, int errorCode, string message, string detail)
        {
            if (string.IsNullOrWhiteSpace(jobPublicId))
                return;

            EnsureInitialized();

            lock (_syncRoot)
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var transaction = connection.BeginTransaction())
                    {
                        int jobId = GetJobId(connection, jobPublicId, transaction);
                        if (jobId > 0)
                        {
                            InsertJobError(connection, transaction, jobId, errorCode, message, detail);

                            using (var cmd = connection.CreateCommand())
                            {
                                cmd.Transaction = transaction;
                                cmd.CommandText = "UPDATE jobs SET status=@status, last_update_utc=@last_update WHERE job_id=@job_id;";
                                var finalStatus = NormalizeFinalStatus(message);
                                cmd.Parameters.AddWithValue("@status", finalStatus);
                                cmd.Parameters.AddWithValue("@last_update", ToUtcString(DateTime.UtcNow));
                                cmd.Parameters.AddWithValue("@job_id", jobId);
                                cmd.ExecuteNonQuery();
                            }
                        }

                        transaction.Commit();
                    }
                }
            }
        }

        /// <summary>
        /// Registra job cancellation per tenerne traccia.
        /// </summary>
        public void RecordJobCancellation(string jobPublicId, string status)
        {
            if (string.IsNullOrWhiteSpace(jobPublicId))
                return;

            EnsureInitialized();

            lock (_syncRoot)
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "UPDATE jobs SET status=@status, last_update_utc=@last_update WHERE public_id=@public_id;";
                        var normalizedStatus = NormalizeFinalStatus(string.IsNullOrWhiteSpace(status) ? "Annullato" : status);
                        cmd.Parameters.AddWithValue("@status", normalizedStatus);
                        cmd.Parameters.AddWithValue("@last_update", ToUtcString(DateTime.UtcNow));
                        cmd.Parameters.AddWithValue("@public_id", jobPublicId);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        /// <summary>
        /// Registra job completed per tenerne traccia.
        /// </summary>
        public void RecordJobCompleted(ArchiviazioneInfo info)
        {
            if (info == null || string.IsNullOrWhiteSpace(info.Id))
                return;

            EnsureInitialized();

            lock (_syncRoot)
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var transaction = connection.BeginTransaction())
                    {
                        var cmd = connection.CreateCommand();
                        cmd.Transaction = transaction;
                        cmd.CommandText =
                            "UPDATE jobs SET " +
                            "ended_utc=@ended_utc, status=@status, progress=@progress, output_folder=@output_folder, output_password=@output_password, note=@note, last_update_utc=@last_update, " +
                            "procedimento_penale=@procedimento_penale, rit_spec=@rit_spec, target=@target, magistrato=@magistrato, procura=@procura, " +
                            "id_lavoro=@id_lavoro, camera_name=@camera_name, server_address=@server_address, output_size_bytes=@output_size_bytes, duration_seconds=@duration_seconds, error_json=@error_json " +
                            "WHERE public_id=@public_id;";

                        long sizeBytes = ParseSizeToBytes(info.Dimensione);
                        if (sizeBytes <= 0 && !string.IsNullOrWhiteSpace(info.Cartella) && Directory.Exists(info.Cartella))
                        {
                            sizeBytes = CalculateDirectorySize(info.Cartella);
                        }

                        int? durationSeconds = null;
                        DateTime durationStart = info.DataAvvio != default(DateTime) ? info.DataAvvio : info.Inizio;
                        DateTime durationEnd = info.DataCompletamento != default(DateTime) ? info.DataCompletamento : info.Fine;
                        if (durationStart != default(DateTime) && durationEnd != default(DateTime))
                        {
                            var duration = durationEnd - durationStart;
                            if (duration < TimeSpan.Zero)
                                duration = TimeSpan.Zero;
                            durationSeconds = (int)Math.Round(duration.TotalSeconds);
                        }

                        string errorJson = null;
                        if (info.ErrorLog != null && info.ErrorLog.Count > 0)
                        {
                            errorJson = JsonConvert.SerializeObject(info.ErrorLog);
                        }

                        DateTime endedAt = info.DataCompletamento != default(DateTime) ? info.DataCompletamento : info.Fine;
                        if (endedAt == default(DateTime))
                            endedAt = DateTime.UtcNow;
                        cmd.Parameters.AddWithValue("@ended_utc", ToUtcString(endedAt));
                        var finalStatus = NormalizeFinalStatus(info.Stato);
                        info.Stato = finalStatus;
                        cmd.Parameters.AddWithValue("@status", finalStatus);
                        cmd.Parameters.AddWithValue("@progress", info.Progresso);
                        cmd.Parameters.AddWithValue("@output_folder", info.Cartella ?? string.Empty);
                        cmd.Parameters.AddWithValue("@output_password", info.Password ?? string.Empty);
                        cmd.Parameters.AddWithValue("@note", info.Note ?? string.Empty);
                        cmd.Parameters.AddWithValue("@last_update", ToUtcString(DateTime.UtcNow));
                        cmd.Parameters.AddWithValue("@procedimento_penale", info.ProcedimentoPenale ?? string.Empty);
                        cmd.Parameters.AddWithValue("@rit_spec", info.RitSpec ?? string.Empty);
                        cmd.Parameters.AddWithValue("@target", info.Target ?? string.Empty);
                        cmd.Parameters.AddWithValue("@magistrato", info.Magistrato ?? string.Empty);
                        cmd.Parameters.AddWithValue("@procura", info.Procura ?? string.Empty);
                        cmd.Parameters.AddWithValue("@id_lavoro", info.IdLavoro ?? string.Empty);
                        cmd.Parameters.AddWithValue("@camera_name", info.Telecamera ?? string.Empty);
                        cmd.Parameters.AddWithValue("@server_address", info.ServerAddress ?? string.Empty);
                        if (sizeBytes > 0)
                            cmd.Parameters.AddWithValue("@output_size_bytes", sizeBytes);
                        else
                            cmd.Parameters.AddWithValue("@output_size_bytes", DBNull.Value);
                        if (durationSeconds.HasValue && durationSeconds.Value >= 0)
                            cmd.Parameters.AddWithValue("@duration_seconds", durationSeconds.Value);
                        else
                            cmd.Parameters.AddWithValue("@duration_seconds", DBNull.Value);
                        if (!string.IsNullOrWhiteSpace(errorJson))
                            cmd.Parameters.AddWithValue("@error_json", errorJson);
                        else
                            cmd.Parameters.AddWithValue("@error_json", DBNull.Value);
                        cmd.Parameters.AddWithValue("@public_id", info.Id);

                        cmd.ExecuteNonQuery();

                        int jobId = GetJobId(connection, info.Id, transaction);
                        if (jobId > 0)
                        {
                            UpsertMetadata(connection, transaction, jobId, "CameraName", info.Telecamera);
                            UpsertMetadata(connection, transaction, jobId, "ServerAddress", info.ServerAddress);
                            UpsertMetadata(connection, transaction, jobId, "IdLavoro", info.IdLavoro);
                            UpsertMetadata(connection, transaction, jobId, "Dimensione", info.Dimensione);
                            UpsertMetadata(connection, transaction, jobId, "Magistrato", info.Magistrato);
                            UpsertMetadata(connection, transaction, jobId, "Procura", info.Procura);
                            UpsertMetadata(connection, transaction, jobId, "Target", info.Target);
                            UpsertMetadata(connection, transaction, jobId, "Durata", info.Durata);

                            if (info.ErrorLog != null && info.ErrorLog.Count > 0)
                            {
                                UpsertMetadata(connection, transaction, jobId, "ErrorLog", JsonConvert.SerializeObject(info.ErrorLog));
                            }

                            UpsertMetadata(connection, transaction, jobId, "ComputedSizeBytes", sizeBytes.ToString(CultureInfo.InvariantCulture));
                            InsertOutputRecord(connection, transaction, jobId, info.Cartella, sizeBytes);
                        }

                        transaction.Commit();
                    }
                }
            }
        }

        /// <summary>
        /// Esegue la logica delete job senza cambiare il comportamento.
        /// </summary>
        public void DeleteJob(string jobPublicId)
        {
            if (string.IsNullOrWhiteSpace(jobPublicId))
                return;

            EnsureInitialized();

            lock (_syncRoot)
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var transaction = connection.BeginTransaction())
                    {
                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.Transaction = transaction;
                            cmd.CommandText = "DELETE FROM jobs WHERE public_id=@public_id;";
                            cmd.Parameters.AddWithValue("@public_id", jobPublicId);
                            cmd.ExecuteNonQuery();
                        }
                        transaction.Commit();
                    }
                }
            }
        }

        /// <summary>
        /// Esegue la logica swap job public ids senza cambiare il comportamento.
        /// </summary>
        public void SwapJobPublicIds(string firstId, string secondId)
        {
            if (string.IsNullOrWhiteSpace(firstId) || string.IsNullOrWhiteSpace(secondId))
                return;

            if (string.Equals(firstId, secondId, StringComparison.OrdinalIgnoreCase))
                return;

            EnsureInitialized();

            lock (_syncRoot)
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var transaction = connection.BeginTransaction())
                    {
                        bool containsBoth;
                        using (var checkCmd = connection.CreateCommand())
                        {
                            checkCmd.Transaction = transaction;
                            checkCmd.CommandText = "SELECT COUNT(*) FROM jobs WHERE public_id IN (@first, @second);";
                            checkCmd.Parameters.AddWithValue("@first", firstId);
                            checkCmd.Parameters.AddWithValue("@second", secondId);
                            var count = Convert.ToInt32(checkCmd.ExecuteScalar(), CultureInfo.InvariantCulture);
                            containsBoth = count >= 2;
                        }

                        if (!containsBoth)
                        {
                            transaction.Rollback();
                            return;
                        }

                        string tempId = "__swap__" + Guid.NewGuid().ToString("N");

                        using (var updateCmd = connection.CreateCommand())
                        {
                            updateCmd.Transaction = transaction;
                            updateCmd.CommandText = "UPDATE jobs SET public_id=@temp WHERE public_id=@first;";
                            updateCmd.Parameters.AddWithValue("@temp", tempId);
                            updateCmd.Parameters.AddWithValue("@first", firstId);
                            updateCmd.ExecuteNonQuery();
                            updateCmd.Parameters.Clear();

                            updateCmd.CommandText = "UPDATE jobs SET public_id=@first WHERE public_id=@second;";
                            updateCmd.Parameters.AddWithValue("@first", firstId);
                            updateCmd.Parameters.AddWithValue("@second", secondId);
                            updateCmd.ExecuteNonQuery();
                            updateCmd.Parameters.Clear();

                            updateCmd.CommandText = "UPDATE jobs SET public_id=@second WHERE public_id=@temp;";
                            updateCmd.Parameters.AddWithValue("@second", secondId);
                            updateCmd.Parameters.AddWithValue("@temp", tempId);
                            updateCmd.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    }
                }
            }
        }

        /// <summary>
        /// Restituisce completed exports gia pronto.
        /// </summary>
        public IList<ArchiviazioneInfo> GetCompletedExports(int maxResults = 500)
        {
            EnsureInitialized();

            var result = new List<ArchiviazioneInfo>();

            lock (_syncRoot)
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText =
                            "SELECT job_id, public_id, id_lavoro, camera_name, server_address, started_utc, ended_utc, requested_start_utc, requested_end_utc, last_update_utc, status, progress, output_folder, output_password, note, procedimento_penale, rit_spec, target, magistrato, procura, output_size_bytes, duration_seconds, error_json " +
                            "FROM jobs " +
                            "WHERE status LIKE 'complet%' OR status LIKE 'termin%' OR status LIKE 'fall%' OR status LIKE 'erro%' OR status LIKE 'annull%' OR status LIKE 'cancel%' OR progress >= 100 " +
                            "ORDER BY COALESCE(ended_utc, last_update_utc) DESC " +
                            "LIMIT @limit;";

                        cmd.Parameters.AddWithValue("@limit", maxResults);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int jobId = Convert.ToInt32(reader["job_id"], CultureInfo.InvariantCulture);
                                string publicId = Convert.ToString(reader["public_id"], CultureInfo.InvariantCulture);
                                var started = ParseDateTime(reader["started_utc"]);
                                var ended = ParseDateTime(reader["ended_utc"]);
                                var requestedStart = ParseDateTime(reader["requested_start_utc"]);
                                var requestedEnd = ParseDateTime(reader["requested_end_utc"]);
                                var lastUpdate = ParseDateTime(reader["last_update_utc"]);

                                var info = new ArchiviazioneInfo
                                {
                                    Id = publicId,
                                    IdLavoro = Convert.ToString(reader["id_lavoro"], CultureInfo.InvariantCulture),
                                    Inizio = started,
                                    Fine = ended,
                                    PeriodoInizio = requestedStart != default(DateTime) ? requestedStart : started,
                                    PeriodoFine = requestedEnd != default(DateTime) ? requestedEnd : ended,
                                    DataAvvio = started,
                                    DataCompletamento = ended != default(DateTime) ? ended : (lastUpdate != default(DateTime) ? lastUpdate : started),
                                    Stato = Convert.ToString(reader["status"], CultureInfo.InvariantCulture),
                                    Progresso = reader["progress"] != DBNull.Value ? Convert.ToInt32(reader["progress"], CultureInfo.InvariantCulture) : 0,
                                    Cartella = Convert.ToString(reader["output_folder"], CultureInfo.InvariantCulture),
                                    Password = Convert.ToString(reader["output_password"], CultureInfo.InvariantCulture),
                                    Note = Convert.ToString(reader["note"], CultureInfo.InvariantCulture),
                                    ProcedimentoPenale = Convert.ToString(reader["procedimento_penale"], CultureInfo.InvariantCulture),
                                    RitSpec = Convert.ToString(reader["rit_spec"], CultureInfo.InvariantCulture),
                                    Target = Convert.ToString(reader["target"], CultureInfo.InvariantCulture),
                                    Magistrato = Convert.ToString(reader["magistrato"], CultureInfo.InvariantCulture),
                                    Procura = Convert.ToString(reader["procura"], CultureInfo.InvariantCulture),
                                    Metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                                };

                                if (string.IsNullOrWhiteSpace(info.Telecamera))
                                    info.Telecamera = Convert.ToString(reader["camera_name"], CultureInfo.InvariantCulture);

                                if (string.IsNullOrWhiteSpace(info.ServerAddress))
                                    info.ServerAddress = Convert.ToString(reader["server_address"], CultureInfo.InvariantCulture);

                                if (info.PeriodoInizio == default(DateTime))
                                    info.PeriodoInizio = info.Inizio;

                                if (info.PeriodoFine == default(DateTime))
                                    info.PeriodoFine = info.PeriodoInizio;

                                if (info.Inizio == default(DateTime))
                                    info.Inizio = info.PeriodoInizio != default(DateTime) ? info.PeriodoInizio : DateTime.Now;

                                if (info.Fine == default(DateTime))
                                    info.Fine = info.DataCompletamento != default(DateTime) ? info.DataCompletamento : info.PeriodoFine;

                                info.DataAvvio = info.DataAvvio == default(DateTime) ? info.Inizio : info.DataAvvio;
                                info.DataCompletamento = info.DataCompletamento == default(DateTime) ? info.Fine : info.DataCompletamento;
                                info.Durata = ComputeDuration(info.DataAvvio, info.DataCompletamento);

                                if (reader["duration_seconds"] != DBNull.Value)
                                {
                                    var seconds = Convert.ToInt32(reader["duration_seconds"], CultureInfo.InvariantCulture);
                                    if (seconds > 0)
                                        info.Durata = TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
                                }

                                LoadJobMetadata(connection, jobId, info);
                                LoadJobErrors(connection, jobId, info);

                                if (string.IsNullOrWhiteSpace(info.IdLavoro) && info.Metadata != null && info.Metadata.TryGetValue("IdLavoro", out var idLavoroObj))
                                    info.IdLavoro = Convert.ToString(idLavoroObj, CultureInfo.InvariantCulture);
                                if (string.IsNullOrWhiteSpace(info.IdLavoro))
                                    info.IdLavoro = publicId;

                                long computedSize = 0;
                                if (reader["output_size_bytes"] != DBNull.Value)
                                {
                                    computedSize = Convert.ToInt64(reader["output_size_bytes"], CultureInfo.InvariantCulture);
                                }
                                if (computedSize <= 0 && info.Metadata != null && info.Metadata.TryGetValue("ComputedSizeBytes", out var sizeObj) && sizeObj != null &&
                                    long.TryParse(Convert.ToString(sizeObj, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var metaSize))
                                {
                                    computedSize = metaSize;
                                }
                                if (string.IsNullOrWhiteSpace(info.Dimensione) && computedSize > 0)
                                {
                                    info.Dimensione = FormatSize(computedSize);
                                }

                                if (string.IsNullOrWhiteSpace(info.Telecamera) && info.Metadata != null && info.Metadata.TryGetValue("CameraName", out var cameraObj))
                                    info.Telecamera = Convert.ToString(cameraObj, CultureInfo.InvariantCulture);
                                if (string.IsNullOrWhiteSpace(info.ServerAddress) && info.Metadata != null && info.Metadata.TryGetValue("ServerAddress", out var serverObj))
                                    info.ServerAddress = Convert.ToString(serverObj, CultureInfo.InvariantCulture);
                                if (string.IsNullOrWhiteSpace(info.Procura) && info.Metadata != null && info.Metadata.TryGetValue("Procura", out var procuraObj))
                                    info.Procura = Convert.ToString(procuraObj, CultureInfo.InvariantCulture);
                                if (string.IsNullOrWhiteSpace(info.Magistrato) && info.Metadata != null && info.Metadata.TryGetValue("Magistrato", out var magistratoObj))
                                    info.Magistrato = Convert.ToString(magistratoObj, CultureInfo.InvariantCulture);

                                if ((info.ErrorLog == null || info.ErrorLog.Count == 0) && reader["error_json"] != DBNull.Value)
                                {
                                    var json = Convert.ToString(reader["error_json"], CultureInfo.InvariantCulture);
                                    if (!string.IsNullOrWhiteSpace(json))
                                    {
                                        try
                                        {
                                            var list = JsonConvert.DeserializeObject<List<string>>(json);
                                            if (list != null && list.Count > 0)
                                                info.ErrorLog = list;
                                        }
                                        catch { }
                                    }
                                }

                                result.Add(info);
                            }
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Restituisce active exports gia pronto.
        /// </summary>
        public IList<ArchiviazioneInfo> GetActiveExports(int maxResults = 200)
        {
            EnsureInitialized();

            var result = new List<ArchiviazioneInfo>();

            lock (_syncRoot)
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText =
                            "SELECT job_id, public_id, id_lavoro, camera_name, server_address, started_utc, requested_start_utc, requested_end_utc, last_update_utc, status, progress, output_folder, output_password, note, procedimento_penale, rit_spec, target, magistrato, procura, output_size_bytes, duration_seconds, error_json " +
                            "FROM jobs " +
                            "WHERE status IS NULL OR (status NOT LIKE 'complet%' AND status NOT LIKE 'termin%' AND status NOT LIKE 'fall%' AND status NOT LIKE 'erro%' AND status NOT LIKE 'annull%' AND status NOT LIKE 'cancel%') " +
                            "ORDER BY COALESCE(last_update_utc, started_utc) DESC " +
                            "LIMIT @limit;";

                        cmd.Parameters.AddWithValue("@limit", maxResults);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                int jobId = Convert.ToInt32(reader["job_id"], CultureInfo.InvariantCulture);
                                string publicId = Convert.ToString(reader["public_id"], CultureInfo.InvariantCulture);

                                DateTime requestedStart = ParseDateTime(reader["requested_start_utc"]);
                                DateTime started = ParseDateTime(reader["started_utc"]);
                                DateTime requestedEnd = ParseDateTime(reader["requested_end_utc"]);
                                DateTime lastUpdate = ParseDateTime(reader["last_update_utc"]);

                                var periodoInizio = requestedStart != default(DateTime) ? requestedStart : started;
                                var periodoFine = requestedEnd != default(DateTime) ? requestedEnd : periodoInizio;

                                var info = new ArchiviazioneInfo
                                {
                                    Id = publicId,
                                    IdLavoro = Convert.ToString(reader["id_lavoro"], CultureInfo.InvariantCulture),
                                    Inizio = started,
                                    Fine = default(DateTime),
                                    PeriodoInizio = periodoInizio,
                                    PeriodoFine = periodoFine,
                                    DataAvvio = started,
                                    DataCompletamento = lastUpdate != default(DateTime) ? lastUpdate : started,
                                    Stato = Convert.ToString(reader["status"], CultureInfo.InvariantCulture),
                                    Progresso = reader["progress"] != DBNull.Value ? Convert.ToInt32(reader["progress"], CultureInfo.InvariantCulture) : 0,
                                    Cartella = Convert.ToString(reader["output_folder"], CultureInfo.InvariantCulture),
                                    Password = Convert.ToString(reader["output_password"], CultureInfo.InvariantCulture),
                                    Note = Convert.ToString(reader["note"], CultureInfo.InvariantCulture),
                                    ProcedimentoPenale = Convert.ToString(reader["procedimento_penale"], CultureInfo.InvariantCulture),
                                    RitSpec = Convert.ToString(reader["rit_spec"], CultureInfo.InvariantCulture),
                                    Target = Convert.ToString(reader["target"], CultureInfo.InvariantCulture),
                                    Magistrato = Convert.ToString(reader["magistrato"], CultureInfo.InvariantCulture),
                                    Procura = Convert.ToString(reader["procura"], CultureInfo.InvariantCulture),
                                    Metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                                };

                                info.Stato = NormalizeRunningStatus(info.Stato);

                                if (string.IsNullOrWhiteSpace(info.Telecamera))
                                    info.Telecamera = Convert.ToString(reader["camera_name"], CultureInfo.InvariantCulture);

                                if (string.IsNullOrWhiteSpace(info.ServerAddress))
                                    info.ServerAddress = Convert.ToString(reader["server_address"], CultureInfo.InvariantCulture);

                                LoadJobMetadata(connection, jobId, info);
                                LoadJobErrors(connection, jobId, info);

                                if (string.IsNullOrWhiteSpace(info.IdLavoro) && info.Metadata != null && info.Metadata.TryGetValue("IdLavoro", out var idLavoroObj))
                                    info.IdLavoro = Convert.ToString(idLavoroObj, CultureInfo.InvariantCulture);
                                if (string.IsNullOrWhiteSpace(info.IdLavoro))
                                    info.IdLavoro = publicId;

                                if (info.PeriodoFine == default(DateTime))
                                    info.PeriodoFine = info.PeriodoInizio;

                                if (string.IsNullOrWhiteSpace(info.Stato))
                                    info.Stato = "In corso";

                                if (string.IsNullOrWhiteSpace(info.Telecamera) && info.Metadata != null && info.Metadata.TryGetValue("CameraName", out var cameraObj))
                                    info.Telecamera = Convert.ToString(cameraObj, CultureInfo.InvariantCulture);
                                if (string.IsNullOrWhiteSpace(info.ServerAddress) && info.Metadata != null && info.Metadata.TryGetValue("ServerAddress", out var serverObj))
                                    info.ServerAddress = Convert.ToString(serverObj, CultureInfo.InvariantCulture);
                                if (string.IsNullOrWhiteSpace(info.Procura) && info.Metadata != null && info.Metadata.TryGetValue("Procura", out var procuraObj))
                                    info.Procura = Convert.ToString(procuraObj, CultureInfo.InvariantCulture);
                                if (string.IsNullOrWhiteSpace(info.Magistrato) && info.Metadata != null && info.Metadata.TryGetValue("Magistrato", out var magistratoObj))
                                    info.Magistrato = Convert.ToString(magistratoObj, CultureInfo.InvariantCulture);

                                if ((info.ErrorLog == null || info.ErrorLog.Count == 0) && reader["error_json"] != DBNull.Value)
                                {
                                    var json = Convert.ToString(reader["error_json"], CultureInfo.InvariantCulture);
                                    if (!string.IsNullOrWhiteSpace(json))
                                    {
                                        try
                                        {
                                            var list = JsonConvert.DeserializeObject<List<string>>(json);
                                            if (list != null && list.Count > 0)
                                                info.ErrorLog = list;
                                        }
                                        catch { }
                                    }
                                }

                                result.Add(info);
                            }
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Carica preferences e aggiorna la UI senza fronzoli.
        /// </summary>
        public Dictionary<string, string> LoadPreferences()
        {
            EnsureInitialized();

            var preferences = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            lock (_syncRoot)
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT key, value FROM user_preferences WHERE scope=@scope;";
                        cmd.Parameters.AddWithValue("@scope", GlobalPreferenceScope);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string key = Convert.ToString(reader["key"], CultureInfo.InvariantCulture);
                                string value = reader["value"] as string ?? string.Empty;
                                preferences[key] = value;
                            }
                        }
                    }
                }
            }

            return preferences;
        }

        /// <summary>
        /// Salva preferences in modo sicuro.
        /// </summary>
        public void SavePreferences(IDictionary<string, string> preferences)
        {
            if (preferences == null)
                return;

            EnsureInitialized();

            lock (_syncRoot)
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var transaction = connection.BeginTransaction())
                    {
                        using (var deleteCmd = connection.CreateCommand())
                        {
                            deleteCmd.Transaction = transaction;
                            deleteCmd.CommandText = "DELETE FROM user_preferences WHERE scope=@scope;";
                            deleteCmd.Parameters.AddWithValue("@scope", GlobalPreferenceScope);
                            deleteCmd.ExecuteNonQuery();
                        }

                        foreach (var entry in preferences)
                        {
                            if (string.IsNullOrWhiteSpace(entry.Key))
                                continue;

                            using (var insertCmd = connection.CreateCommand())
                            {
                                insertCmd.Transaction = transaction;
                                insertCmd.CommandText =
                                    "INSERT INTO user_preferences (scope, key, value, updated_utc) VALUES (@scope, @key, @value, @updated);";

                                insertCmd.Parameters.AddWithValue("@scope", GlobalPreferenceScope);
                                insertCmd.Parameters.AddWithValue("@key", entry.Key);
                                insertCmd.Parameters.AddWithValue("@value", entry.Value ?? string.Empty);
                                insertCmd.Parameters.AddWithValue("@updated", ToUtcString(DateTime.UtcNow));
                                insertCmd.ExecuteNonQuery();
                            }
                        }

                        transaction.Commit();
                    }
                }
            }
        }

        /// <summary>
        /// Restituisce preference gia pronto.
        /// </summary>
        public string GetPreference(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            EnsureInitialized();

            lock (_syncRoot)
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT value FROM user_preferences WHERE scope=@scope AND key=@key;";
                        cmd.Parameters.AddWithValue("@scope", GlobalPreferenceScope);
                        cmd.Parameters.AddWithValue("@key", key);
                        var value = cmd.ExecuteScalar() as string;
                        return value ?? string.Empty;
                    }
                }
            }
        }

        /// <summary>
        /// Imposta preference usando i parametri passati.
        /// </summary>
        public void SetPreference(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            EnsureInitialized();

            lock (_syncRoot)
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var transaction = connection.BeginTransaction())
                    {
                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.Transaction = transaction;

                            if (string.IsNullOrWhiteSpace(value))
                            {
                                cmd.CommandText = "DELETE FROM user_preferences WHERE scope=@scope AND key=@key;";
                                cmd.Parameters.AddWithValue("@scope", GlobalPreferenceScope);
                                cmd.Parameters.AddWithValue("@key", key);
                                cmd.ExecuteNonQuery();
                            }
                            else
                            {
                                cmd.CommandText =
                                    "INSERT INTO user_preferences (scope, key, value, updated_utc) VALUES (@scope, @key, @value, @updated) " +
                                    "ON CONFLICT(scope, key) DO UPDATE SET value = excluded.value, updated_utc = excluded.updated_utc;";

                                cmd.Parameters.AddWithValue("@scope", GlobalPreferenceScope);
                                cmd.Parameters.AddWithValue("@key", key);
                                cmd.Parameters.AddWithValue("@value", value);
                                cmd.Parameters.AddWithValue("@updated", ToUtcString(DateTime.UtcNow));
                                cmd.ExecuteNonQuery();
                            }
                        }

                        transaction.Commit();
                    }
                }
            }
        }

        /// <summary>
        /// Salva last export configuration in modo sicuro.
        /// </summary>
        public void SaveLastExportConfiguration(ArchiviazioneInfo info)
        {
            EnsureInitialized();

            string serialized = info != null ? JsonConvert.SerializeObject(info) : null;

            lock (_syncRoot)
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var cmd = connection.CreateCommand())
                    {
                        if (string.IsNullOrWhiteSpace(serialized))
                        {
                            cmd.CommandText = "DELETE FROM app_settings WHERE key=@key;";
                            cmd.Parameters.AddWithValue("@key", LastExportSettingKey);
                            cmd.ExecuteNonQuery();
                        }
                        else
                        {
                            cmd.CommandText =
                                "INSERT INTO app_settings (key, value, updated_utc) VALUES (@key, @value, @updated) " +
                                "ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_utc = excluded.updated_utc;";

                            cmd.Parameters.AddWithValue("@key", LastExportSettingKey);
                            cmd.Parameters.AddWithValue("@value", serialized);
                            cmd.Parameters.AddWithValue("@updated", ToUtcString(DateTime.UtcNow));
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Carica last export configuration e aggiorna la UI senza fronzoli.
        /// </summary>
        public ArchiviazioneInfo LoadLastExportConfiguration()
        {
            EnsureInitialized();

            lock (_syncRoot)
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT value FROM app_settings WHERE key=@key;";
                        cmd.Parameters.AddWithValue("@key", LastExportSettingKey);
                        var value = cmd.ExecuteScalar() as string;
                        if (string.IsNullOrWhiteSpace(value))
                            return null;

                        try
                        {
                            return JsonConvert.DeserializeObject<ArchiviazioneInfo>(value);
                        }
                        catch
                        {
                            return null;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Restituisce most recent job gia pronto.
        /// </summary>
        public ArchiviazioneInfo GetMostRecentJob()
        {
            EnsureInitialized();

            lock (_syncRoot)
            {
                using (var connection = CreateConnection())
                {
                    connection.Open();
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText =
                            "SELECT job_id, public_id, id_lavoro, camera_name, server_address, started_utc, ended_utc, requested_start_utc, requested_end_utc, status, progress, output_folder, output_password, note, procedimento_penale, rit_spec, target, magistrato, procura, last_update_utc, output_size_bytes, duration_seconds, error_json " +
                            "FROM jobs " +
                            "ORDER BY COALESCE(last_update_utc, ended_utc, requested_end_utc, started_utc) DESC " +
                            "LIMIT 1;";

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (!reader.Read())
                                return null;

                            int jobId = Convert.ToInt32(reader["job_id"], CultureInfo.InvariantCulture);
                            string publicId = Convert.ToString(reader["public_id"], CultureInfo.InvariantCulture);

                            var info = new ArchiviazioneInfo
                            {
                                Id = publicId,
                                IdLavoro = Convert.ToString(reader["id_lavoro"], CultureInfo.InvariantCulture),
                                Inizio = ParseDateTime(reader["started_utc"]),
                                Fine = ParseDateTime(reader["ended_utc"]),
                                Stato = Convert.ToString(reader["status"], CultureInfo.InvariantCulture),
                                Progresso = reader["progress"] != DBNull.Value ? Convert.ToInt32(reader["progress"], CultureInfo.InvariantCulture) : 0,
                                Cartella = Convert.ToString(reader["output_folder"], CultureInfo.InvariantCulture),
                                Password = Convert.ToString(reader["output_password"], CultureInfo.InvariantCulture),
                                Note = Convert.ToString(reader["note"], CultureInfo.InvariantCulture),
                                ProcedimentoPenale = Convert.ToString(reader["procedimento_penale"], CultureInfo.InvariantCulture),
                                RitSpec = Convert.ToString(reader["rit_spec"], CultureInfo.InvariantCulture),
                                Target = Convert.ToString(reader["target"], CultureInfo.InvariantCulture),
                                Magistrato = Convert.ToString(reader["magistrato"], CultureInfo.InvariantCulture),
                                Procura = Convert.ToString(reader["procura"], CultureInfo.InvariantCulture),
                                Metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                            };

                            if (LooksLikeFinalStatus(info.Stato))
                                info.Stato = NormalizeFinalStatus(info.Stato);
                            else
                                info.Stato = NormalizeRunningStatus(info.Stato);

                            if (string.IsNullOrWhiteSpace(info.Telecamera))
                                info.Telecamera = Convert.ToString(reader["camera_name"], CultureInfo.InvariantCulture);

                            if (string.IsNullOrWhiteSpace(info.ServerAddress))
                                info.ServerAddress = Convert.ToString(reader["server_address"], CultureInfo.InvariantCulture);

                            DateTime lastUpdate = ParseDateTime(reader["last_update_utc"]);
                            if (info.Inizio == default(DateTime))
                                info.Inizio = ParseDateTime(reader["requested_start_utc"]);
                            if (info.Fine == default(DateTime))
                                info.Fine = ParseDateTime(reader["requested_end_utc"]);

                            info.DataCompletamento = info.Fine != default(DateTime) ? info.Fine :
                                (lastUpdate != default(DateTime) ? lastUpdate : info.Inizio);

                            info.Durata = ComputeDuration(info.Inizio, info.Fine);

                            if (reader["duration_seconds"] != DBNull.Value)
                            {
                                var seconds = Convert.ToInt32(reader["duration_seconds"], CultureInfo.InvariantCulture);
                                if (seconds > 0)
                                    info.Durata = TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
                            }

                            LoadJobMetadata(connection, jobId, info);
                            LoadJobErrors(connection, jobId, info);

                            if (string.IsNullOrWhiteSpace(info.IdLavoro) && info.Metadata != null && info.Metadata.TryGetValue("IdLavoro", out var idLavoroObj))
                                info.IdLavoro = Convert.ToString(idLavoroObj, CultureInfo.InvariantCulture);
                            if (string.IsNullOrWhiteSpace(info.IdLavoro))
                                info.IdLavoro = publicId;

                            long computedSize = 0;
                            if (reader["output_size_bytes"] != DBNull.Value)
                                computedSize = Convert.ToInt64(reader["output_size_bytes"], CultureInfo.InvariantCulture);
                            if (computedSize <= 0 && info.Metadata != null && info.Metadata.TryGetValue("ComputedSizeBytes", out var sizeObj) &&
                                sizeObj != null && long.TryParse(Convert.ToString(sizeObj, CultureInfo.InvariantCulture),
                                NumberStyles.Integer, CultureInfo.InvariantCulture, out var metaSize))
                            {
                                computedSize = metaSize;
                            }
                            if (string.IsNullOrWhiteSpace(info.Dimensione) && computedSize > 0)
                                info.Dimensione = FormatSize(computedSize);

                            if (string.IsNullOrWhiteSpace(info.Telecamera) && info.Metadata != null && info.Metadata.TryGetValue("CameraName", out var cameraObj))
                                info.Telecamera = Convert.ToString(cameraObj, CultureInfo.InvariantCulture);
                            if (string.IsNullOrWhiteSpace(info.ServerAddress) && info.Metadata != null && info.Metadata.TryGetValue("ServerAddress", out var serverObj))
                                info.ServerAddress = Convert.ToString(serverObj, CultureInfo.InvariantCulture);
                            if (string.IsNullOrWhiteSpace(info.Procura) && info.Metadata != null && info.Metadata.TryGetValue("Procura", out var procuraObj))
                                info.Procura = Convert.ToString(procuraObj, CultureInfo.InvariantCulture);
                            if (string.IsNullOrWhiteSpace(info.Magistrato) && info.Metadata != null && info.Metadata.TryGetValue("Magistrato", out var magistratoObj))
                                info.Magistrato = Convert.ToString(magistratoObj, CultureInfo.InvariantCulture);

                            if ((info.ErrorLog == null || info.ErrorLog.Count == 0) && reader["error_json"] != DBNull.Value)
                            {
                                var json = Convert.ToString(reader["error_json"], CultureInfo.InvariantCulture);
                                if (!string.IsNullOrWhiteSpace(json))
                                {
                                    try
                                    {
                                        var list = JsonConvert.DeserializeObject<List<string>>(json);
                                        if (list != null && list.Count > 0)
                                            info.ErrorLog = list;
                                    }
                                    catch { }
                                }
                            }

                            return info;
                        }
                    }
                }
            }
        }

        #region Helper Types

        public sealed class JobSnapshot
        {
            public string JobId { get; set; }
            public string IdLavoro { get; set; }
            public string CameraName { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime? EndTime { get; set; }
            public DateTime RequestedStartTime { get; set; }
            public DateTime? RequestedEndTime { get; set; }
            public string Status { get; set; }
            public int Progress { get; set; }
            public string OutputFolder { get; set; }
            public string OutputPassword { get; set; }
            public string ProcedimentoPenale { get; set; }
            public string RitSpec { get; set; }
            public string Target { get; set; }
            public string Magistrato { get; set; }
            public string Procura { get; set; }
            public string ServerAddress { get; set; }
            public string Note { get; set; }
            public string CreatedBy { get; set; }
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Si assicura che la parte initialized sia pronta prima di procedere.
        /// </summary>
        private void EnsureInitialized()
        {
            if (!_initialized)
            {
                Initialize();
            }
        }

        /// <summary>
        /// Crea connection al volo.
        /// </summary>
        private SQLiteConnection CreateConnection()
        {
            return new SQLiteConnection(string.Format("Data Source={0};Version=3;", _databasePath));
        }

        /// <summary>
        /// Restituisce default database path gia pronto.
        /// </summary>
        private static string GetDefaultDatabasePath()
        {
            string basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(basePath, "ToolArchiviazioniMilestone", "ToolArchMilestone.db");
        }

        /// <summary>
        /// Esegue la logica to utc string senza cambiare il comportamento.
        /// </summary>
        private static string ToUtcString(DateTime value)
        {
            return value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Esegue la logica parse date time senza cambiare il comportamento.
        /// </summary>
        private static DateTime ParseDateTime(object value)
        {
            if (value == null || value == DBNull.Value)
                return default(DateTime);

            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text))
                return default(DateTime);

            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                return parsed.ToLocalTime();

            if (DateTime.TryParse(text, out parsed))
                return parsed;

            return default(DateTime);
        }

        /// <summary>
        /// Calcola duration per fornirlo agli altri step.
        /// </summary>
        private static string ComputeDuration(DateTime start, DateTime end)
        {
            if (start == default(DateTime) || end == default(DateTime))
                return "N/D";

            if (end < start)
                end = start;

            TimeSpan span = end - start;
            return span.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Esegue la logica looks like final status senza cambiare il comportamento.
        /// </summary>
        private static bool LooksLikeFinalStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return false;

            string normalized = status.ToLowerInvariant();
            return normalized.Contains("complet") ||
                   normalized.Contains("termin") ||
                   normalized.Contains("erro") ||
                   normalized.Contains("fail") ||
                   normalized.Contains("fall") ||
                   normalized.Contains("annull") ||
                   normalized.Contains("cancel") ||
                   normalized.Contains("timeout");
        }

        /// <summary>
        /// Esegue la logica normalize running status senza cambiare il comportamento.
        /// </summary>
        private static string NormalizeRunningStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return "In corso";

            var normalized = status.Trim().ToLowerInvariant();

            if (LooksLikeFinalStatus(status))
                return NormalizeFinalStatus(status);

            if (normalized.Contains("coda") || normalized.Contains("queue") || normalized.Contains("attesa"))
                return "In coda";

            return "In corso";
        }

        /// <summary>
        /// Esegue la logica normalize final status senza cambiare il comportamento.
        /// </summary>
        private static string NormalizeFinalStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return "Completato";

            string normalized = status.Trim().ToLowerInvariant();
            if (normalized.Contains("annull") || normalized.Contains("cancel"))
                return "Annullato";
            if (normalized.Contains("erro") || normalized.Contains("fail") || normalized.Contains("fall") || normalized.Contains("timeout"))
                return "Errore";
            if (normalized.Contains("complet") || normalized.Contains("termin"))
                return "Completato";

            return "Completato";
        }

        /// <summary>
        /// Esegue la logica parse size to bytes senza cambiare il comportamento.
        /// </summary>
        private static long ParseSizeToBytes(string sizeText)
        {
            if (string.IsNullOrWhiteSpace(sizeText))
                return 0;

            string normalized = sizeText.Trim().ToUpperInvariant().Replace(" ", string.Empty);
            double value;
            string numericPart;

            if (normalized.EndsWith("TB"))
            {
                numericPart = normalized.Substring(0, normalized.Length - 2);
                if (double.TryParse(numericPart, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
                    return (long)(value * 1024L * 1024L * 1024L * 1024L);
            }
            else if (normalized.EndsWith("GB"))
            {
                numericPart = normalized.Substring(0, normalized.Length - 2);
                if (double.TryParse(numericPart, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
                    return (long)(value * 1024L * 1024L * 1024L);
            }
            else if (normalized.EndsWith("MB"))
            {
                numericPart = normalized.Substring(0, normalized.Length - 2);
                if (double.TryParse(numericPart, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
                    return (long)(value * 1024L * 1024L);
            }
            else if (normalized.EndsWith("KB"))
            {
                numericPart = normalized.Substring(0, normalized.Length - 2);
                if (double.TryParse(numericPart, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
                    return (long)(value * 1024L);
            }
            else if (normalized.EndsWith("B"))
            {
                numericPart = normalized.Substring(0, normalized.Length - 1);
                if (double.TryParse(numericPart, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
                    return (long)value;
            }
            else if (long.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var bytes))
            {
                return bytes;
            }

            return 0;
        }

        /// <summary>
        /// Esegue la logica format size senza cambiare il comportamento.
        /// </summary>
        private static string FormatSize(long bytes)
        {
            if (bytes <= 0)
                return "0 B";

            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < suffixes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", len, suffixes[order]);
        }

        /// <summary>
        /// Calcola directory size per fornirlo agli altri step.
        /// </summary>
        private static long CalculateDirectorySize(string directoryPath)
        {
            try
            {
                long total = 0;
                if (!Directory.Exists(directoryPath))
                    return 0;

                foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        total += info.Length;
                    }
                    catch
                    {
                        // Ignora file non accessibili
                    }
                }

                return total;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Restituisce job id gia pronto.
        /// </summary>
        private static int GetJobId(SQLiteConnection connection, string publicId, SQLiteTransaction transaction)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "SELECT job_id FROM jobs WHERE public_id=@public_id;";
                cmd.Parameters.AddWithValue("@public_id", publicId);
                object result = cmd.ExecuteScalar();
                if (result == null || result == DBNull.Value)
                    return -1;

                return Convert.ToInt32(result, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Esegue la logica upsert metadata senza cambiare il comportamento.
        /// </summary>
        private static void UpsertMetadata(SQLiteConnection connection, SQLiteTransaction transaction, int jobId, string key, string value)
        {
            if (jobId <= 0 || string.IsNullOrWhiteSpace(key))
                return;

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText =
                    "INSERT INTO job_metadata (job_id, key, value) VALUES (@job_id, @key, @value) " +
                    "ON CONFLICT(job_id, key) DO UPDATE SET value = excluded.value;";

                cmd.Parameters.AddWithValue("@job_id", jobId);
                cmd.Parameters.AddWithValue("@key", key);
                if (value != null)
                    cmd.Parameters.AddWithValue("@value", value);
                else
                    cmd.Parameters.AddWithValue("@value", DBNull.Value);

                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Esegue la logica insert output record senza cambiare il comportamento.
        /// </summary>
        private static void InsertOutputRecord(SQLiteConnection connection, SQLiteTransaction transaction, int jobId, string path, long sizeBytes)
        {
            if (jobId <= 0 || string.IsNullOrWhiteSpace(path))
                return;

            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText =
                    "INSERT INTO job_outputs (job_id, relative_path, size_bytes, media_type, created_utc) " +
                    "VALUES (@job_id, @relative_path, @size_bytes, @media_type, @created_utc) " +
                    "ON CONFLICT(job_id, relative_path) DO UPDATE SET size_bytes = excluded.size_bytes, media_type = excluded.media_type, created_utc = excluded.created_utc;";

                cmd.Parameters.AddWithValue("@job_id", jobId);
                cmd.Parameters.AddWithValue("@relative_path", path);
                if (sizeBytes > 0)
                    cmd.Parameters.AddWithValue("@size_bytes", sizeBytes);
                else
                    cmd.Parameters.AddWithValue("@size_bytes", DBNull.Value);

                cmd.Parameters.AddWithValue("@media_type", "folder");
                cmd.Parameters.AddWithValue("@created_utc", ToUtcString(DateTime.UtcNow));

                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Esegue la logica insert job error senza cambiare il comportamento.
        /// </summary>
        private static void InsertJobError(SQLiteConnection connection, SQLiteTransaction transaction, int jobId, int errorCode, string message, string detail)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText =
                    "INSERT INTO job_errors (job_id, event_id, severity, code, message, detail, created_utc) " +
                    "VALUES (@job_id, NULL, @severity, @code, @message, @detail, @created_utc);";

                cmd.Parameters.AddWithValue("@job_id", jobId);
                cmd.Parameters.AddWithValue("@severity", "error");
                if (errorCode != 0)
                    cmd.Parameters.AddWithValue("@code", errorCode);
                else
                    cmd.Parameters.AddWithValue("@code", DBNull.Value);

                cmd.Parameters.AddWithValue("@message", string.IsNullOrWhiteSpace(message) ? "Errore" : message);
                if (!string.IsNullOrWhiteSpace(detail))
                    cmd.Parameters.AddWithValue("@detail", detail);
                else
                    cmd.Parameters.AddWithValue("@detail", DBNull.Value);

                cmd.Parameters.AddWithValue("@created_utc", ToUtcString(DateTime.UtcNow));

                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Carica job metadata e aggiorna la UI senza fronzoli.
        /// </summary>
        private static void LoadJobMetadata(SQLiteConnection connection, int jobId, ArchiviazioneInfo info)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT key, value FROM job_metadata WHERE job_id=@job_id;";
                cmd.Parameters.AddWithValue("@job_id", jobId);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string key = Convert.ToString(reader["key"], CultureInfo.InvariantCulture);
                        string value = reader["value"] as string;

                        if (!string.IsNullOrEmpty(key))
                        {
                            info.Metadata[key] = value;
                        }

                        if (string.Equals(key, "CameraName", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
                        {
                            info.Telecamera = value;
                        }
                        else if (string.Equals(key, "ServerAddress", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
                        {
                            info.ServerAddress = value;
                        }
                        else if (string.Equals(key, "Target", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
                        {
                            info.Target = value;
                        }
                        else if (string.Equals(key, "Magistrato", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
                        {
                            info.Magistrato = value;
                        }
                        else if (string.Equals(key, "Procura", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
                        {
                            info.Procura = value;
                        }
                        else if (string.Equals(key, "Dimensione", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
                        {
                            info.Dimensione = value;
                        }
                        else if (string.Equals(key, "Durata", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
                        {
                            info.Durata = value;
                        }
                        else if (string.Equals(key, "ErrorLog", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
                        {
                            try
                            {
                                var list = JsonConvert.DeserializeObject<List<string>>(value);
                                if (list != null && list.Count > 0)
                                    info.ErrorLog = list;
                            }
                            catch
                            {
                                // Ignora errori di deserializzazione
                            }
                        }
                        else if (string.Equals(key, "IdLavoro", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value) && string.IsNullOrWhiteSpace(info.IdLavoro))
                        {
                            info.IdLavoro = value;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Carica job errors e aggiorna la UI senza fronzoli.
        /// </summary>
        private static void LoadJobErrors(SQLiteConnection connection, int jobId, ArchiviazioneInfo info)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT code, message, detail FROM job_errors WHERE job_id=@job_id ORDER BY created_utc;";
                cmd.Parameters.AddWithValue("@job_id", jobId);

                using (var reader = cmd.ExecuteReader())
                {
                    var errors = info.ErrorLog ?? new List<string>();

                    while (reader.Read())
                    {
                        string message = reader["message"] as string;
                        string detail = reader["detail"] as string;
                        int? code = reader["code"] != DBNull.Value ? (int?)Convert.ToInt32(reader["code"], CultureInfo.InvariantCulture) : null;

                        if (code.HasValue)
                        {
                            errors.Add(string.Format(CultureInfo.InvariantCulture, "Codice {0}", code.Value));
                        }

                        if (!string.IsNullOrWhiteSpace(message))
                            errors.Add(message);

                        if (!string.IsNullOrWhiteSpace(detail))
                            errors.Add(detail);
                    }

                    if (errors.Count > 0)
                        info.ErrorLog = errors;
                }
            }
        }

        /// <summary>
        /// Esegue la logica extract int senza cambiare il comportamento.
        /// </summary>
        private static int? ExtractInt(IDictionary<string, object> payload, string key)
        {
            if (payload == null || string.IsNullOrEmpty(key))
                return null;

            if (!payload.TryGetValue(key, out var value) || value == null)
                return null;

            if (value is int intValue)
                return intValue;

            if (value is long longValue)
                return (int)longValue;

            if (value is string text && int.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            return null;
        }

        /// <summary>
        /// Esegue la logica extract string senza cambiare il comportamento.
        /// </summary>
        private static string ExtractString(IDictionary<string, object> payload, string key)
        {
            if (payload == null || string.IsNullOrEmpty(key))
                return null;

            if (!payload.TryGetValue(key, out var value) || value == null)
                return null;

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        #endregion
    }
}

