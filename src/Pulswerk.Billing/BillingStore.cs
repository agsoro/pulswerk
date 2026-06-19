using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using Pulswerk.Core;

namespace Pulswerk.Billing
{
    public sealed class BillingStore : IDisposable
    {
        private readonly SqliteConnection _db;
        private readonly object _lock = new();
        private bool _disposed;

        public BillingStore(string dbPath)
        {
            _db = new SqliteConnection($"Data Source={dbPath}");
            _db.Open();

            using var pragma = _db.CreateCommand();
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            pragma.ExecuteNonQuery();

            InitSchema();
            SeedDefaultTariffs();
            Log.Info($"[BillingStore] Initialized SQLite at {dbPath}");
        }

        private void InitSchema()
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS tariff_config (
                    key   TEXT PRIMARY KEY,
                    value REAL NOT NULL
                );

                CREATE TABLE IF NOT EXISTS rfid_user_map (
                    id_tag    TEXT PRIMARY KEY,
                    user_name TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS charging_transactions (
                    id             INTEGER PRIMARY KEY,
                    chargepoint_id TEXT NOT NULL,
                    connector_id   INTEGER NOT NULL,
                    id_tag         TEXT NOT NULL,
                    kwh            REAL NOT NULL,
                    timestamp      INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS curtailment_targets (
                    telemetry_key   TEXT PRIMARY KEY,
                    normal_value    REAL NOT NULL,
                    warning_value   REAL NOT NULL,
                    critical_value  REAL NOT NULL
                );

                CREATE TABLE IF NOT EXISTS tenants (
                    id        TEXT PRIMARY KEY,
                    name      TEXT NOT NULL,
                    meter_key TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS trajectory_targets_15min (
                    timestamp  INTEGER PRIMARY KEY,
                    target_kwh REAL NOT NULL
                );

                CREATE TABLE IF NOT EXISTS meter_replacements (
                    id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    tenant_id      TEXT    NOT NULL,
                    replaced_at    INTEGER NOT NULL,
                    old_final_kwh  REAL,
                    new_start_kwh  REAL,
                    note           TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_replacements_tenant
                    ON meter_replacements(tenant_id, replaced_at);
                """;
            cmd.ExecuteNonQuery();
        }

        private void SeedDefaultTariffs()
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = """
                    INSERT OR IGNORE INTO tariff_config (key, value) VALUES ('rate_per_kwh', 0.30);
                    INSERT OR IGNORE INTO tariff_config (key, value) VALUES ('base_monthly_fee', 10.00);
                    """;
                cmd.ExecuteNonQuery();
            }
        }

        // ── Tariffs ──────────────────────────────────────────────────────────

        public double GetTariff(string key, double defaultValue)
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "SELECT value FROM tariff_config WHERE key = @key";
                cmd.Parameters.AddWithValue("@key", key);
                var val = cmd.ExecuteScalar();
                return val != null ? Convert.ToDouble(val) : defaultValue;
            }
        }

        public void SetTariff(string key, double value)
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO tariff_config (key, value) VALUES (@key, @val)
                    ON CONFLICT(key) DO UPDATE SET value = excluded.value
                    """;
                cmd.Parameters.AddWithValue("@key", key);
                cmd.Parameters.AddWithValue("@val", value);
                cmd.ExecuteNonQuery();
            }
        }

        // ── RFID / Users ──────────────────────────────────────────────────────

        public bool IsRfidValid(string idTag)
        {
            lock (_lock)
            {
                // Only recognized RFID cards work (per user request)
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM rfid_user_map WHERE id_tag = @id";
                cmd.Parameters.AddWithValue("@id", idTag);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        public Dictionary<string, string> GetRfidMap()
        {
            lock (_lock)
            {
                var map = new Dictionary<string, string>();
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "SELECT id_tag, user_name FROM rfid_user_map";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    map[reader.GetString(0)] = reader.GetString(1);
                }
                return map;
            }
        }

        public void AddRfidMapping(string idTag, string userName)
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO rfid_user_map (id_tag, user_name) VALUES (@id, @name)
                    ON CONFLICT(id_tag) DO UPDATE SET user_name = excluded.user_name
                    """;
                cmd.Parameters.AddWithValue("@id", idTag);
                cmd.Parameters.AddWithValue("@name", userName);
                cmd.ExecuteNonQuery();
            }
        }

        public void DeleteRfidMapping(string idTag)
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "DELETE FROM rfid_user_map WHERE id_tag = @id";
                cmd.Parameters.AddWithValue("@id", idTag);
                cmd.ExecuteNonQuery();
            }
        }

        // ── Tenants ──────────────────────────────────────────────────────────

        public List<TenantRecord> GetTenants()
        {
            lock (_lock)
            {
                var list = new List<TenantRecord>();
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "SELECT id, name, meter_key FROM tenants";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new TenantRecord(
                        Id: reader.GetString(0),
                        Name: reader.GetString(1),
                        MeterKey: reader.GetString(2)
                    ));
                }
                return list;
            }
        }

        public void AddTenant(string id, string name, string meterKey)
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO tenants (id, name, meter_key) VALUES (@id, @name, @key)
                    ON CONFLICT(id) DO UPDATE SET
                        name = excluded.name,
                        meter_key = excluded.meter_key
                    """;
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@key", meterKey);
                cmd.ExecuteNonQuery();
            }
        }

        public void DeleteTenant(string id)
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "DELETE FROM tenants WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
        }

        // ── Transactions ──────────────────────────────────────────────────────

        public void RecordTransaction(int transId, string chargepointId, int connectorId, string idTag, double kwh)
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = """
                    INSERT OR REPLACE INTO charging_transactions (id, chargepoint_id, connector_id, id_tag, kwh, timestamp)
                    VALUES (@id, @cp, @conn, @tag, @kwh, @ts)
                    """;
                cmd.Parameters.AddWithValue("@id", transId);
                cmd.Parameters.AddWithValue("@cp", chargepointId);
                cmd.Parameters.AddWithValue("@conn", connectorId);
                cmd.Parameters.AddWithValue("@tag", idTag);
                cmd.Parameters.AddWithValue("@kwh", kwh);
                cmd.Parameters.AddWithValue("@ts", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                cmd.ExecuteNonQuery();
            }
        }

        public List<ChargingTransaction> GetTransactions()
        {
            lock (_lock)
            {
                var list = new List<ChargingTransaction>();
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "SELECT id, chargepoint_id, connector_id, id_tag, kwh, timestamp FROM charging_transactions ORDER BY timestamp DESC, id DESC";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new ChargingTransaction(
                        Id: reader.GetInt32(0),
                        ChargepointId: reader.GetString(1),
                        ConnectorId: reader.GetInt32(2),
                        IdTag: reader.GetString(3),
                        Kwh: reader.GetDouble(4),
                        Timestamp: reader.GetInt64(5)
                    ));
                }
                return list;
            }
        }

        // ── Curtailment Targets ──────────────────────────────────────────────

        public List<CurtailmentTarget> GetCurtailmentTargets()
        {
            lock (_lock)
            {
                var list = new List<CurtailmentTarget>();
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "SELECT telemetry_key, normal_value, warning_value, critical_value FROM curtailment_targets";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new CurtailmentTarget(
                        TelemetryKey: reader.GetString(0),
                        NormalValue: reader.GetDouble(1),
                        WarningValue: reader.GetDouble(2),
                        CriticalValue: reader.GetDouble(3)
                    ));
                }
                return list;
            }
        }

        public void AddCurtailmentTarget(string key, double normal, double warning, double critical)
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO curtailment_targets (telemetry_key, normal_value, warning_value, critical_value)
                    VALUES (@key, @norm, @warn, @crit)
                    ON CONFLICT(telemetry_key) DO UPDATE SET
                        normal_value = excluded.normal_value,
                        warning_value = excluded.warning_value,
                        critical_value = excluded.critical_value
                    """;
                cmd.Parameters.AddWithValue("@key", key);
                cmd.Parameters.AddWithValue("@norm", normal);
                cmd.Parameters.AddWithValue("@warn", warning);
                cmd.Parameters.AddWithValue("@crit", critical);
                cmd.ExecuteNonQuery();
            }
        }

        public void DeleteCurtailmentTarget(string key)
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "DELETE FROM curtailment_targets WHERE telemetry_key = @key";
                cmd.Parameters.AddWithValue("@key", key);
                cmd.ExecuteNonQuery();
            }
        }

        // ── Meter Replacements ────────────────────────────────────────────────

        public void AddMeterReplacement(string tenantId, long replacedAt, double? oldFinalKwh, double? newStartKwh, string? note)
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO meter_replacements (tenant_id, replaced_at, old_final_kwh, new_start_kwh, note)
                    VALUES (@tid, @rat, @old, @new, @note)
                    """;
                cmd.Parameters.AddWithValue("@tid",  tenantId);
                cmd.Parameters.AddWithValue("@rat",  replacedAt);
                cmd.Parameters.AddWithValue("@old",  oldFinalKwh.HasValue ? (object)oldFinalKwh.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@new",  newStartKwh.HasValue ? (object)newStartKwh.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@note", note != null ? (object)note : DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        public List<MeterReplacement> GetMeterReplacements(string tenantId)
        {
            lock (_lock)
            {
                var list = new List<MeterReplacement>();
                using var cmd = _db.CreateCommand();
                cmd.CommandText = """
                    SELECT id, tenant_id, replaced_at, old_final_kwh, new_start_kwh, note
                    FROM meter_replacements
                    WHERE tenant_id = @tid
                    ORDER BY replaced_at ASC
                    """;
                cmd.Parameters.AddWithValue("@tid", tenantId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new MeterReplacement(
                        Id:           reader.GetInt32(0),
                        TenantId:     reader.GetString(1),
                        ReplacedAt:   reader.GetInt64(2),
                        OldFinalKwh:  reader.IsDBNull(3) ? null : reader.GetDouble(3),
                        NewStartKwh:  reader.IsDBNull(4) ? null : reader.GetDouble(4),
                        Note:         reader.IsDBNull(5) ? null : reader.GetString(5)
                    ));
                }
                return list;
            }
        }

        /// <summary>Returns all replacements within [fromTs, toTs) for any tenant — used by invoice calculation.</summary>
        public List<MeterReplacement> GetMeterReplacementsInRange(string tenantId, long fromTs, long toTs)
        {
            lock (_lock)
            {
                var list = new List<MeterReplacement>();
                using var cmd = _db.CreateCommand();
                cmd.CommandText = """
                    SELECT id, tenant_id, replaced_at, old_final_kwh, new_start_kwh, note
                    FROM meter_replacements
                    WHERE tenant_id = @tid
                      AND replaced_at > @from
                      AND replaced_at < @to
                    ORDER BY replaced_at ASC
                    """;
                cmd.Parameters.AddWithValue("@tid",  tenantId);
                cmd.Parameters.AddWithValue("@from", fromTs);
                cmd.Parameters.AddWithValue("@to",   toTs);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new MeterReplacement(
                        Id:           reader.GetInt32(0),
                        TenantId:     reader.GetString(1),
                        ReplacedAt:   reader.GetInt64(2),
                        OldFinalKwh:  reader.IsDBNull(3) ? null : reader.GetDouble(3),
                        NewStartKwh:  reader.IsDBNull(4) ? null : reader.GetDouble(4),
                        Note:         reader.IsDBNull(5) ? null : reader.GetString(5)
                    ));
                }
                return list;
            }
        }

        public bool DeleteMeterReplacement(int id)
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "DELETE FROM meter_replacements WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        // ── 15-Min Trajectory Targets ────────────────────────────────────────

        public List<TrajectoryTarget15Min> GetTrajectoryTargets15Min()
        {
            lock (_lock)
            {
                var list = new List<TrajectoryTarget15Min>();
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "SELECT timestamp, target_kwh FROM trajectory_targets_15min ORDER BY timestamp ASC";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new TrajectoryTarget15Min(
                        Timestamp: reader.GetInt64(0),
                        TargetKwh: reader.GetDouble(1)
                    ));
                }
                return list;
            }
        }

        public void SetTrajectoryTargets15Min(List<TrajectoryTarget15Min> targets)
        {
            lock (_lock)
            {
                using var tx = _db.BeginTransaction();
                try
                {
                    using var deleteCmd = _db.CreateCommand();
                    deleteCmd.Transaction = tx;
                    deleteCmd.CommandText = "DELETE FROM trajectory_targets_15min";
                    deleteCmd.ExecuteNonQuery();

                    using var insertCmd = _db.CreateCommand();
                    insertCmd.Transaction = tx;
                    insertCmd.CommandText = "INSERT INTO trajectory_targets_15min (timestamp, target_kwh) VALUES (@ts, @target)";
                    
                    var tsParam = insertCmd.Parameters.Add("@ts", SqliteType.Integer);
                    var targetParam = insertCmd.Parameters.Add("@target", SqliteType.Real);

                    foreach (var t in targets)
                    {
                        tsParam.Value = t.Timestamp;
                        targetParam.Value = t.TargetKwh;
                        insertCmd.ExecuteNonQuery();
                    }

                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }

        public double GetTrajectoryTargetForTimestamp(long tsMs)
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "SELECT target_kwh FROM trajectory_targets_15min WHERE timestamp <= @ts ORDER BY timestamp DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@ts", tsMs);
                var val = cmd.ExecuteScalar();
                if (val != null) return Convert.ToDouble(val);

                cmd.CommandText = "SELECT target_kwh FROM trajectory_targets_15min ORDER BY timestamp ASC LIMIT 1";
                cmd.Parameters.Clear();
                val = cmd.ExecuteScalar();
                return val != null ? Convert.ToDouble(val) : double.NaN;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _db?.Close();
            _db?.Dispose();
        }
    }

    public record ChargingTransaction(int Id, string ChargepointId, int ConnectorId, string IdTag, double Kwh, long Timestamp);
    public record CurtailmentTarget(string TelemetryKey, double NormalValue, double WarningValue, double CriticalValue);
    public record TenantRecord(string Id, string Name, string MeterKey);
    public record TrajectoryTarget15Min(long Timestamp, double TargetKwh);
    public record MeterReplacement(int Id, string TenantId, long ReplacedAt, double? OldFinalKwh, double? NewStartKwh, string? Note);
}
