using System;
using System.Collections.Generic;

namespace ToolArch.Shared.Schema
{
    /// <summary>
    /// Classe che gestisce database schema definition all'interno del tool.
    /// </summary>
    public static class DatabaseSchemaDefinition
    {
        private static readonly IReadOnlyDictionary<string, string> _tableDefinitions = BuildTableDefinitions();
        private static readonly IReadOnlyList<string> _indexDefinitions = BuildIndexDefinitions();
        private static readonly IReadOnlyList<string> _viewDefinitions = BuildViewDefinitions();
        private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _columnDefinitions = BuildColumnDefinitions();

        public static IReadOnlyDictionary<string, string> TableDefinitions
        {
            get { return _tableDefinitions; }
        }

        public static IReadOnlyList<string> IndexDefinitions
        {
            get { return _indexDefinitions; }
        }

        public static IReadOnlyList<string> ViewDefinitions
        {
            get { return _viewDefinitions; }
        }

        public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ColumnDefinitions
        {
            get { return _columnDefinitions; }
        }

        /// <summary>
        /// Compone table definitions pronto all'uso.
        /// </summary>
        private static IReadOnlyDictionary<string, string> BuildTableDefinitions()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            dict.Add("servers", @"
                CREATE TABLE IF NOT EXISTS servers (
                    server_id INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    address TEXT NOT NULL UNIQUE,
                    last_connected_utc TEXT,
                    sdk_version TEXT,
                    note TEXT
                );");

            dict.Add("cameras", @"
                CREATE TABLE IF NOT EXISTS cameras (
                    camera_id INTEGER PRIMARY KEY,
                    server_id INTEGER NOT NULL REFERENCES servers(server_id) ON DELETE CASCADE,
                    milestone_fqid TEXT NOT NULL UNIQUE,
                    name TEXT NOT NULL,
                    description TEXT,
                    enabled INTEGER NOT NULL DEFAULT 1,
                    last_seen_utc TEXT,
                    CONSTRAINT fk_cameras_server FOREIGN KEY (server_id) REFERENCES servers(server_id) ON DELETE CASCADE
                );");

            dict.Add("audio_sources", @"
                CREATE TABLE IF NOT EXISTS audio_sources (
                    audio_source_id INTEGER PRIMARY KEY,
                    server_id INTEGER NOT NULL REFERENCES servers(server_id) ON DELETE CASCADE,
                    milestone_fqid TEXT NOT NULL UNIQUE,
                    name TEXT NOT NULL,
                    description TEXT,
                    enabled INTEGER NOT NULL DEFAULT 1,
                    last_seen_utc TEXT,
                    CONSTRAINT fk_audio_sources_server FOREIGN KEY (server_id) REFERENCES servers(server_id) ON DELETE CASCADE
                );");

            dict.Add("legal_cases", @"
                CREATE TABLE IF NOT EXISTS legal_cases (
                    case_id INTEGER PRIMARY KEY,
                    code TEXT NOT NULL UNIQUE,
                    court TEXT,
                    description TEXT,
                    opened_date TEXT,
                    closed_date TEXT,
                    notes TEXT
                );");

            dict.Add("rit_specs", @"
                CREATE TABLE IF NOT EXISTS rit_specs (
                    rit_id INTEGER PRIMARY KEY,
                    label TEXT NOT NULL UNIQUE,
                    type TEXT CHECK(type IN ('RIT','SPEC')),
                    issued_date TEXT,
                    notes TEXT
                );");

            dict.Add("targets", @"
                CREATE TABLE IF NOT EXISTS targets (
                    target_id INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    type TEXT,
                    address TEXT,
                    contact TEXT,
                    notes TEXT
                );");

            dict.Add("magistrates", @"
                CREATE TABLE IF NOT EXISTS magistrates (
                    magistrate_id INTEGER PRIMARY KEY,
                    full_name TEXT NOT NULL,
                    office TEXT,
                    email TEXT,
                    phone TEXT
                );");

            dict.Add("jobs", @"
                CREATE TABLE IF NOT EXISTS jobs (
                    job_id INTEGER PRIMARY KEY,
                    public_id TEXT NOT NULL UNIQUE,
                    id_lavoro TEXT,
                    camera_name TEXT,
                    server_address TEXT,
                    output_size_bytes INTEGER,
                    duration_seconds INTEGER,
                    error_json TEXT,
                    server_id INTEGER REFERENCES servers(server_id),
                    started_utc TEXT NOT NULL,
                    ended_utc TEXT,
                    requested_start_utc TEXT,
                    requested_end_utc TEXT,
                    status TEXT NOT NULL,
                    progress INTEGER NOT NULL DEFAULT 0 CHECK(progress BETWEEN 0 AND 100),
                    output_folder TEXT,
                    output_password TEXT,
                    note TEXT,
                    created_by TEXT,
                    last_update_utc TEXT NOT NULL,
                    case_id INTEGER REFERENCES legal_cases(case_id),
                    rit_id INTEGER REFERENCES rit_specs(rit_id),
                    target_id INTEGER REFERENCES targets(target_id),
                    magistrate_id INTEGER REFERENCES magistrates(magistrate_id),
                    procedimento_penale TEXT,
                    rit_spec TEXT,
                    target TEXT,
                    magistrato TEXT,
                    procura TEXT,
                    FOREIGN KEY (server_id) REFERENCES servers(server_id),
                    FOREIGN KEY (case_id) REFERENCES legal_cases(case_id),
                    FOREIGN KEY (rit_id) REFERENCES rit_specs(rit_id),
                    FOREIGN KEY (target_id) REFERENCES targets(target_id),
                    FOREIGN KEY (magistrate_id) REFERENCES magistrates(magistrate_id)
                );");

            dict.Add("job_cameras", @"
                CREATE TABLE IF NOT EXISTS job_cameras (
                    job_id INTEGER NOT NULL REFERENCES jobs(job_id) ON DELETE CASCADE,
                    camera_id INTEGER NOT NULL REFERENCES cameras(camera_id),
                    role TEXT DEFAULT 'primary',
                    PRIMARY KEY (job_id, camera_id),
                    CONSTRAINT fk_job_cameras_job FOREIGN KEY (job_id) REFERENCES jobs(job_id) ON DELETE CASCADE,
                    CONSTRAINT fk_job_cameras_camera FOREIGN KEY (camera_id) REFERENCES cameras(camera_id)
                );");

            dict.Add("job_audio_sources", @"
                CREATE TABLE IF NOT EXISTS job_audio_sources (
                    job_id INTEGER NOT NULL REFERENCES jobs(job_id) ON DELETE CASCADE,
                    audio_source_id INTEGER REFERENCES audio_sources(audio_source_id),
                    source_name TEXT NOT NULL,
                    milestone_fqid TEXT,
                    PRIMARY KEY (job_id, source_name),
                    CONSTRAINT fk_job_audio_job FOREIGN KEY (job_id) REFERENCES jobs(job_id) ON DELETE CASCADE
                );");

            dict.Add("job_time_segments", @"
                CREATE TABLE IF NOT EXISTS job_time_segments (
                    segment_id INTEGER PRIMARY KEY,
                    job_id INTEGER NOT NULL REFERENCES jobs(job_id) ON DELETE CASCADE,
                    segment_order INTEGER NOT NULL,
                    start_utc TEXT NOT NULL,
                    end_utc TEXT NOT NULL,
                    UNIQUE(job_id, segment_order),
                    CONSTRAINT fk_job_time_segments_job FOREIGN KEY (job_id) REFERENCES jobs(job_id) ON DELETE CASCADE
                );");

            dict.Add("job_events", @"
                CREATE TABLE IF NOT EXISTS job_events (
                    event_id INTEGER PRIMARY KEY,
                    job_id INTEGER NOT NULL REFERENCES jobs(job_id) ON DELETE CASCADE,
                    event_type TEXT NOT NULL,
                    event_utc TEXT NOT NULL,
                    progress INTEGER,
                    status TEXT,
                    message TEXT,
                    payload_json TEXT,
                    CONSTRAINT fk_job_events_job FOREIGN KEY (job_id) REFERENCES jobs(job_id) ON DELETE CASCADE
                );");

            dict.Add("job_errors", @"
                CREATE TABLE IF NOT EXISTS job_errors (
                    error_id INTEGER PRIMARY KEY,
                    job_id INTEGER NOT NULL REFERENCES jobs(job_id) ON DELETE CASCADE,
                    event_id INTEGER REFERENCES job_events(event_id),
                    severity TEXT NOT NULL,
                    code TEXT,
                    message TEXT NOT NULL,
                    detail TEXT,
                    created_utc TEXT NOT NULL,
                    CONSTRAINT fk_job_errors_job FOREIGN KEY (job_id) REFERENCES jobs(job_id) ON DELETE CASCADE,
                    CONSTRAINT fk_job_errors_event FOREIGN KEY (event_id) REFERENCES job_events(event_id)
                );");

            dict.Add("job_outputs", @"
                CREATE TABLE IF NOT EXISTS job_outputs (
                    output_id INTEGER PRIMARY KEY,
                    job_id INTEGER NOT NULL REFERENCES jobs(job_id) ON DELETE CASCADE,
                    relative_path TEXT NOT NULL,
                    size_bytes INTEGER,
                    checksum TEXT,
                    media_type TEXT,
                    created_utc TEXT,
                    UNIQUE(job_id, relative_path),
                    CONSTRAINT fk_job_outputs_job FOREIGN KEY (job_id) REFERENCES jobs(job_id) ON DELETE CASCADE
                );");

            dict.Add("job_validations", @"
                CREATE TABLE IF NOT EXISTS job_validations (
                    validation_id INTEGER PRIMARY KEY,
                    job_id INTEGER NOT NULL REFERENCES jobs(job_id) ON DELETE CASCADE,
                    performed_utc TEXT NOT NULL,
                    performed_by INTEGER REFERENCES operators(operator_id),
                    type TEXT NOT NULL,
                    result TEXT NOT NULL,
                    detail TEXT,
                    CONSTRAINT fk_job_validations_job FOREIGN KEY (job_id) REFERENCES jobs(job_id) ON DELETE CASCADE
                );");

            dict.Add("job_delivery", @"
                CREATE TABLE IF NOT EXISTS job_delivery (
                    delivery_id INTEGER PRIMARY KEY,
                    job_id INTEGER NOT NULL REFERENCES jobs(job_id) ON DELETE CASCADE,
                    delivered_to TEXT,
                    delivery_type TEXT,
                    signature_path TEXT,
                    delivery_utc TEXT,
                    notes TEXT,
                    CONSTRAINT fk_job_delivery_job FOREIGN KEY (job_id) REFERENCES jobs(job_id) ON DELETE CASCADE
                );");

            dict.Add("job_documents", @"
                CREATE TABLE IF NOT EXISTS job_documents (
                    document_id INTEGER PRIMARY KEY,
                    job_id INTEGER REFERENCES jobs(job_id) ON DELETE CASCADE,
                    template_id INTEGER REFERENCES document_templates(template_id),
                    saved_path TEXT,
                    generated_utc TEXT NOT NULL,
                    metadata_json TEXT,
                    CONSTRAINT fk_job_documents_job FOREIGN KEY (job_id) REFERENCES jobs(job_id) ON DELETE CASCADE,
                    CONSTRAINT fk_job_documents_template FOREIGN KEY (template_id) REFERENCES document_templates(template_id)
                );");

            dict.Add("job_metadata", @"
                CREATE TABLE IF NOT EXISTS job_metadata (
                    job_id INTEGER NOT NULL REFERENCES jobs(job_id) ON DELETE CASCADE,
                    key TEXT NOT NULL,
                    value TEXT,
                    PRIMARY KEY (job_id, key),
                    CONSTRAINT fk_job_metadata_job FOREIGN KEY (job_id) REFERENCES jobs(job_id) ON DELETE CASCADE
                );");

            dict.Add("user_preferences", @"
                CREATE TABLE IF NOT EXISTS user_preferences (
                    scope TEXT NOT NULL,
                    key TEXT NOT NULL,
                    value TEXT,
                    updated_utc TEXT NOT NULL,
                    PRIMARY KEY (scope, key)
                );");

            dict.Add("app_settings", @"
                CREATE TABLE IF NOT EXISTS app_settings (
                    key TEXT PRIMARY KEY,
                    value TEXT,
                    updated_utc TEXT NOT NULL
                );");

            dict.Add("document_templates", @"
                CREATE TABLE IF NOT EXISTS document_templates (
                    template_id INTEGER PRIMARY KEY,
                    code TEXT NOT NULL UNIQUE,
                    description TEXT,
                    file_hash TEXT,
                    stored_file BLOB,
                    version TEXT,
                    created_utc TEXT NOT NULL
                );");

            dict.Add("generated_documents", @"
                CREATE TABLE IF NOT EXISTS generated_documents (
                    generated_document_id INTEGER PRIMARY KEY,
                    job_id INTEGER REFERENCES jobs(job_id) ON DELETE CASCADE,
                    template_id INTEGER REFERENCES document_templates(template_id),
                    saved_path TEXT,
                    generated_utc TEXT NOT NULL,
                    metadata_json TEXT,
                    CONSTRAINT fk_generated_documents_job FOREIGN KEY (job_id) REFERENCES jobs(job_id) ON DELETE CASCADE,
                    CONSTRAINT fk_generated_documents_template FOREIGN KEY (template_id) REFERENCES document_templates(template_id)
                );");

            dict.Add("operators", @"
                CREATE TABLE IF NOT EXISTS operators (
                    operator_id INTEGER PRIMARY KEY,
                    username TEXT NOT NULL UNIQUE,
                    display_name TEXT,
                    role TEXT,
                    last_login_utc TEXT
                );");

            dict.Add("audit_log", @"
                CREATE TABLE IF NOT EXISTS audit_log (
                    audit_id INTEGER PRIMARY KEY,
                    operator_id INTEGER REFERENCES operators(operator_id),
                    action TEXT NOT NULL,
                    entity_type TEXT NOT NULL,
                    entity_id TEXT,
                    timestamp_utc TEXT NOT NULL,
                    detail TEXT,
                    CONSTRAINT fk_audit_log_operator FOREIGN KEY (operator_id) REFERENCES operators(operator_id)
                );");

            dict.Add("notifications", @"
                CREATE TABLE IF NOT EXISTS notifications (
                    notification_id INTEGER PRIMARY KEY,
                    job_id INTEGER REFERENCES jobs(job_id) ON DELETE CASCADE,
                    title TEXT NOT NULL,
                    body TEXT,
                    due_utc TEXT,
                    status TEXT CHECK(status IN ('pending','done','dismissed')) DEFAULT 'pending',
                    CONSTRAINT fk_notifications_job FOREIGN KEY (job_id) REFERENCES jobs(job_id) ON DELETE CASCADE
                );");

            dict.Add("attachment_library", @"
                CREATE TABLE IF NOT EXISTS attachment_library (
                    attachment_id INTEGER PRIMARY KEY,
                    job_id INTEGER REFERENCES jobs(job_id) ON DELETE CASCADE,
                    filename TEXT NOT NULL,
                    file_hash TEXT,
                    stored_file BLOB,
                    mime_type TEXT,
                    uploaded_utc TEXT NOT NULL,
                    description TEXT,
                    CONSTRAINT fk_attachment_library_job FOREIGN KEY (job_id) REFERENCES jobs(job_id) ON DELETE CASCADE
                );");

            return dict;
        }

        /// <summary>
        /// Compone index definitions pronto all'uso.
        /// </summary>
        private static IReadOnlyList<string> BuildIndexDefinitions()
        {
            return new[]
            {
                "CREATE INDEX IF NOT EXISTS idx_cameras_server ON cameras(server_id);",
                "CREATE INDEX IF NOT EXISTS idx_audio_sources_server ON audio_sources(server_id);",
                "CREATE INDEX IF NOT EXISTS idx_jobs_server ON jobs(server_id);",
                "CREATE INDEX IF NOT EXISTS idx_jobs_status ON jobs(status, started_utc);",
                "CREATE INDEX IF NOT EXISTS idx_job_events_job ON job_events(job_id, event_utc);",
                "CREATE INDEX IF NOT EXISTS idx_job_errors_job ON job_errors(job_id, created_utc);",
                "CREATE INDEX IF NOT EXISTS idx_job_outputs_job ON job_outputs(job_id);",
                "CREATE INDEX IF NOT EXISTS idx_job_metadata_key ON job_metadata(key);",
                "CREATE INDEX IF NOT EXISTS idx_notifications_status ON notifications(status, due_utc);",
                "CREATE INDEX IF NOT EXISTS idx_attachment_library_job ON attachment_library(job_id);"
            };
        }

        /// <summary>
        /// Compone view definitions pronto all'uso.
        /// </summary>
        private static IReadOnlyList<string> BuildViewDefinitions()
        {
            return new[]
            {
                @"
                CREATE VIEW IF NOT EXISTS v_job_summary AS
                SELECT
                    j.job_id,
                    j.public_id,
                    j.status,
                    j.progress,
                    j.started_utc,
                    j.ended_utc,
                    j.procedimento_penale,
                    j.rit_spec,
                    j.target,
                    COALESCE(SUM(o.size_bytes), 0) AS total_size_bytes
                FROM jobs j
                LEFT JOIN job_outputs o ON o.job_id = j.job_id
                GROUP BY j.job_id;
            ",
                @"
                CREATE VIEW IF NOT EXISTS v_job_last_event AS
                SELECT e.*
                FROM job_events e
                INNER JOIN (
                    SELECT job_id, MAX(event_utc) AS max_utc
                    FROM job_events
                    GROUP BY job_id
                ) grouped ON grouped.job_id = e.job_id AND grouped.max_utc = e.event_utc;
            "
            };
        }

        /// <summary>
        /// Compone column definitions pronto all'uso.
        /// </summary>
        private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> BuildColumnDefinitions()
        {
            var dict = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            var jobsColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            jobsColumns.Add("job_id", "INTEGER");
            jobsColumns.Add("public_id", "TEXT");
            jobsColumns.Add("id_lavoro", "TEXT");
            jobsColumns.Add("camera_name", "TEXT");
            jobsColumns.Add("server_address", "TEXT");
            jobsColumns.Add("output_size_bytes", "INTEGER");
            jobsColumns.Add("duration_seconds", "INTEGER");
            jobsColumns.Add("error_json", "TEXT");
            jobsColumns.Add("server_id", "INTEGER");
            jobsColumns.Add("started_utc", "TEXT");
            jobsColumns.Add("ended_utc", "TEXT");
            jobsColumns.Add("requested_start_utc", "TEXT");
            jobsColumns.Add("requested_end_utc", "TEXT");
            jobsColumns.Add("status", "TEXT");
            jobsColumns.Add("progress", "INTEGER");
            jobsColumns.Add("output_folder", "TEXT");
            jobsColumns.Add("output_password", "TEXT");
            jobsColumns.Add("note", "TEXT");
            jobsColumns.Add("created_by", "TEXT");
            jobsColumns.Add("last_update_utc", "TEXT");
            jobsColumns.Add("case_id", "INTEGER");
            jobsColumns.Add("rit_id", "INTEGER");
            jobsColumns.Add("target_id", "INTEGER");
            jobsColumns.Add("magistrate_id", "INTEGER");
            jobsColumns.Add("procedimento_penale", "TEXT");
            jobsColumns.Add("rit_spec", "TEXT");
            jobsColumns.Add("target", "TEXT");
            jobsColumns.Add("magistrato", "TEXT");
            jobsColumns.Add("procura", "TEXT");

            var preferencesColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            preferencesColumns.Add("scope", "TEXT");
            preferencesColumns.Add("key", "TEXT");
            preferencesColumns.Add("value", "TEXT");
            preferencesColumns.Add("updated_utc", "TEXT");

            dict.Add("jobs", jobsColumns);
            dict.Add("user_preferences", preferencesColumns);

            return dict;
        }
    }
}

