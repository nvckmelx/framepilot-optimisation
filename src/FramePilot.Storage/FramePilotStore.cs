using System.Globalization;
using FramePilot.Core;
using FramePilot.Core.Changes;
using FramePilot.Core.Models;
using Microsoft.Data.Sqlite;

namespace FramePilot.Storage;

/// <summary>Local-first SQLite storage: snapshots, sessions, the change journal and settings. Nothing leaves the PC.</summary>
public sealed class FramePilotStore : IChangeJournal, IDisposable
{
    private readonly string _connectionString;
    private readonly object _gate = new();

    public string DatabasePath { get; }

    public FramePilotStore(string databasePath)
    {
        DatabasePath = databasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
        Migrate();
    }

    public static string DefaultDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FramePilot");

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        using var pragma = c.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        return c;
    }

    private void Migrate()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_info (version INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS snapshots (
                id TEXT PRIMARY KEY, collected_at TEXT NOT NULL, is_demo INTEGER NOT NULL, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS sessions (
                id TEXT PRIMARY KEY, game TEXT NOT NULL, started_at TEXT NOT NULL, run_group TEXT NOT NULL,
                is_demo INTEGER NOT NULL, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS changes (
                id TEXT PRIMARY KEY, batch_id TEXT NOT NULL, action_id TEXT NOT NULL, action_version INTEGER NOT NULL,
                rule_id TEXT, target_key TEXT NOT NULL, target_description TEXT NOT NULL,
                original_exists INTEGER NOT NULL, original_kind TEXT NOT NULL, original_data TEXT,
                planned_exists INTEGER NOT NULL, planned_kind TEXT NOT NULL, planned_data TEXT,
                state TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, error TEXT, note TEXT);
            CREATE INDEX IF NOT EXISTS ix_changes_target ON changes(target_key);
            CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT);
            CREATE TABLE IF NOT EXISTS audit_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT, at TEXT NOT NULL, kind TEXT NOT NULL, message TEXT NOT NULL);
            """;
        cmd.ExecuteNonQuery();
        using var v = c.CreateCommand();
        v.CommandText = "INSERT INTO schema_info(version) SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM schema_info);";
        v.ExecuteNonQuery();
    }

    private static string Ts(DateTimeOffset t) => t.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseTs(string s) => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    // ---------- snapshots ----------

    public void SaveSnapshot(SystemSnapshot s)
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO snapshots(id, collected_at, is_demo, json) VALUES ($id, $at, $demo, $json)";
            cmd.Parameters.AddWithValue("$id", s.Id.ToString());
            cmd.Parameters.AddWithValue("$at", Ts(s.CollectedAt));
            cmd.Parameters.AddWithValue("$demo", s.IsDemo ? 1 : 0);
            cmd.Parameters.AddWithValue("$json", Json.Serialize(s));
            cmd.ExecuteNonQuery();
        }
    }

    public SystemSnapshot? LatestSnapshot(bool demo)
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT json FROM snapshots WHERE is_demo = $demo ORDER BY collected_at DESC LIMIT 1";
            cmd.Parameters.AddWithValue("$demo", demo ? 1 : 0);
            var json = cmd.ExecuteScalar() as string;
            return json is null ? null : Json.Deserialize<SystemSnapshot>(json);
        }
    }

    // ---------- sessions ----------

    public void SaveSession(SessionRecord s)
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO sessions(id, game, started_at, run_group, is_demo, json) VALUES ($id, $g, $at, $grp, $demo, $json)";
            cmd.Parameters.AddWithValue("$id", s.Id.ToString());
            cmd.Parameters.AddWithValue("$g", s.Game.ToString());
            cmd.Parameters.AddWithValue("$at", Ts(s.StartedAt));
            cmd.Parameters.AddWithValue("$grp", s.Group.ToString());
            cmd.Parameters.AddWithValue("$demo", s.IsDemo ? 1 : 0);
            cmd.Parameters.AddWithValue("$json", Json.Serialize(s));
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<SessionRecord> Sessions(bool demo)
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT json FROM sessions WHERE is_demo = $demo ORDER BY started_at DESC";
            cmd.Parameters.AddWithValue("$demo", demo ? 1 : 0);
            var list = new List<SessionRecord>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var s = Json.Deserialize<SessionRecord>(r.GetString(0));
                if (s is not null) list.Add(s);
            }
            return list;
        }
    }

    public void DeleteSession(Guid id)
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM sessions WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id.ToString());
            cmd.ExecuteNonQuery();
        }
    }

    // ---------- change journal ----------

    public void Insert(ChangeRecord r) => UpsertChange(r, insert: true);
    public void Update(ChangeRecord r) => UpsertChange(r, insert: false);

    private void UpsertChange(ChangeRecord r, bool insert)
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = insert
                ? """
                  INSERT INTO changes(id, batch_id, action_id, action_version, rule_id, target_key, target_description,
                      original_exists, original_kind, original_data, planned_exists, planned_kind, planned_data,
                      state, created_at, updated_at, error, note)
                  VALUES ($id, $batch, $action, $ver, $rule, $key, $desc, $oe, $ok, $od, $pe, $pk, $pd, $state, $created, $updated, $error, $note)
                  """
                : """
                  UPDATE changes SET original_exists=$oe, original_kind=$ok, original_data=$od, planned_exists=$pe, planned_kind=$pk,
                      planned_data=$pd, state=$state, updated_at=$updated, error=$error, note=$note WHERE id=$id
                  """;
            cmd.Parameters.AddWithValue("$id", r.Id.ToString());
            cmd.Parameters.AddWithValue("$batch", r.BatchId.ToString());
            cmd.Parameters.AddWithValue("$action", r.ActionId);
            cmd.Parameters.AddWithValue("$ver", r.ActionVersion);
            cmd.Parameters.AddWithValue("$rule", (object?)r.RuleId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$key", r.TargetKey);
            cmd.Parameters.AddWithValue("$desc", r.TargetDescription);
            cmd.Parameters.AddWithValue("$oe", r.Original.Exists ? 1 : 0);
            cmd.Parameters.AddWithValue("$ok", r.Original.Kind.ToString());
            cmd.Parameters.AddWithValue("$od", (object?)r.Original.Data ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$pe", r.Planned.Exists ? 1 : 0);
            cmd.Parameters.AddWithValue("$pk", r.Planned.Kind.ToString());
            cmd.Parameters.AddWithValue("$pd", (object?)r.Planned.Data ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$state", r.State.ToString());
            cmd.Parameters.AddWithValue("$created", Ts(r.CreatedAt));
            cmd.Parameters.AddWithValue("$updated", Ts(r.UpdatedAt));
            cmd.Parameters.AddWithValue("$error", (object?)r.Error ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$note", (object?)r.Note ?? DBNull.Value);
            var n = cmd.ExecuteNonQuery();
            if (n != 1) throw new InvalidOperationException($"Change journal write affected {n} rows.");
        }
    }

    public IReadOnlyList<ChangeRecord> GetAll()
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT * FROM changes ORDER BY created_at";
            using var rd = cmd.ExecuteReader();
            var list = new List<ChangeRecord>();
            while (rd.Read()) list.Add(ReadChange(rd));
            return list;
        }
    }

    public ChangeRecord? Get(Guid id)
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT * FROM changes WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id.ToString());
            using var rd = cmd.ExecuteReader();
            return rd.Read() ? ReadChange(rd) : null;
        }
    }

    private static ChangeRecord ReadChange(SqliteDataReader rd)
    {
        string? S(string col) => rd.IsDBNull(rd.GetOrdinal(col)) ? null : rd.GetString(rd.GetOrdinal(col));
        return new ChangeRecord
        {
            Id = Guid.Parse(S("id")!),
            BatchId = Guid.Parse(S("batch_id")!),
            ActionId = S("action_id")!,
            ActionVersion = rd.GetInt32(rd.GetOrdinal("action_version")),
            RuleId = S("rule_id"),
            TargetKey = S("target_key")!,
            TargetDescription = S("target_description")!,
            Original = new SettingValue(rd.GetInt32(rd.GetOrdinal("original_exists")) == 1, Enum.Parse<SettingValueKind>(S("original_kind")!), S("original_data")),
            Planned = new SettingValue(rd.GetInt32(rd.GetOrdinal("planned_exists")) == 1, Enum.Parse<SettingValueKind>(S("planned_kind")!), S("planned_data")),
            State = Enum.Parse<ChangeState>(S("state")!),
            CreatedAt = ParseTs(S("created_at")!),
            UpdatedAt = ParseTs(S("updated_at")!),
            Error = S("error"),
            Note = S("note")
        };
    }

    // ---------- settings & audit ----------

    public string? GetSetting(string key)
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT value FROM settings WHERE key = $k";
            cmd.Parameters.AddWithValue("$k", key);
            return cmd.ExecuteScalar() as string;
        }
    }

    public void SetSetting(string key, string? value)
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO settings(key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value";
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$v", (object?)value ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public void Audit(string kind, string message)
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO audit_log(at, kind, message) VALUES ($at, $k, $m)";
            cmd.Parameters.AddWithValue("$at", Ts(DateTimeOffset.Now));
            cmd.Parameters.AddWithValue("$k", kind);
            cmd.Parameters.AddWithValue("$m", message);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Deletes measurements and snapshots. The change journal is kept unless explicitly requested, so rollback stays possible.</summary>
    public void DeleteLocalData(bool includeChangeJournal)
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM snapshots; DELETE FROM sessions; DELETE FROM audit_log;" + (includeChangeJournal ? " DELETE FROM changes;" : "");
            cmd.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
    }
}
