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

                CREATE TABLE IF NOT EXISTS app_settings (
                    key   TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS customers (
                    id      TEXT PRIMARY KEY,
                    name    TEXT NOT NULL,
                    address TEXT,
                    email   TEXT,
                    note    TEXT
                );

                CREATE TABLE IF NOT EXISTS rfid_user_map (
                    id_tag      TEXT PRIMARY KEY,
                    user_name   TEXT NOT NULL,
                    customer_id TEXT
                );

                CREATE TABLE IF NOT EXISTS charging_transactions (
                    id             INTEGER PRIMARY KEY,
                    chargepoint_id TEXT NOT NULL,
                    connector_id   INTEGER NOT NULL,
                    id_tag         TEXT NOT NULL,
                    kwh            REAL NOT NULL,
                    timestamp      INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS tenants (
                    id          TEXT PRIMARY KEY,
                    name        TEXT NOT NULL,
                    meter_key   TEXT NOT NULL,
                    customer_id TEXT
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

            EnsureColumn("rfid_user_map", "customer_id", "TEXT");
            EnsureColumn("tenants", "customer_id", "TEXT");
            MigrateSettingsOutOfRfidTable();
        }

        /// <summary>
        /// Keys that older versions stored as pseudo RFID cards instead of real settings.
        /// </summary>
        private static readonly string[] LegacySettingKeys =
        {
            "energy_sources_config", "energy_consumers_config", "ems_energy_24h_state",
            "energy_control_grid_key", "energy_control_pv_key",
            "energy_control_battery_power_key", "energy_control_battery_soc_key",
            "energy_control_wb_key", "energy_control_wb_actual_key",
            "trajectory_main_meter_key"
        };

        /// <summary>
        /// Older versions stored EMS settings as pseudo RFID cards. Move those rows into
        /// app_settings so the RFID card list only contains real cards.
        /// </summary>
        private void MigrateSettingsOutOfRfidTable()
        {
            var placeholders = string.Join(", ", LegacySettingKeys.Select((_, i) => $"@k{i}"));

            using var cmd = _db.CreateCommand();
            cmd.CommandText = $"""
                INSERT OR IGNORE INTO app_settings (key, value)
                    SELECT id_tag, user_name FROM rfid_user_map WHERE id_tag IN ({placeholders});
                DELETE FROM rfid_user_map WHERE id_tag IN ({placeholders});
                """;
            for (int i = 0; i < LegacySettingKeys.Length; i++)
                cmd.Parameters.AddWithValue($"@k{i}", LegacySettingKeys[i]);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Adds a column to an existing table when upgrading a database created by an older version.</summary>
        private void EnsureColumn(string table, string column, string type)
        {
            using var check = _db.CreateCommand();
            check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = @col";
            check.Parameters.AddWithValue("@col", column);
            if (Convert.ToInt32(check.ExecuteScalar()) > 0) return;

            using var alter = _db.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
            alter.ExecuteNonQuery();
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

        public List<RfidMapping> GetRfidMappings()
        {
            lock (_lock)
            {
                var list = new List<RfidMapping>();
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "SELECT id_tag, user_name, customer_id FROM rfid_user_map ORDER BY user_name";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new RfidMapping(
                        IdTag:      reader.GetString(0),
                        UserName:   reader.GetString(1),
                        CustomerId: reader.IsDBNull(2) ? null : reader.GetString(2)
                    ));
                }
                return list;
            }
        }

        public void AddRfidMapping(string idTag, string userName, string? customerId = null)
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO rfid_user_map (id_tag, user_name, customer_id) VALUES (@id, @name, @cust)
                    ON CONFLICT(id_tag) DO UPDATE SET
                        user_name = excluded.user_name,
                        customer_id = excluded.customer_id
                    """;
                cmd.Parameters.AddWithValue("@id", idTag);
                cmd.Parameters.AddWithValue("@name", userName);
                cmd.Parameters.AddWithValue("@cust", (object?)customerId ?? DBNull.Value);
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

        // ── Energy Control / General Settings ───────────────────────────────

        public string GetSetting(string key, string defaultValue)
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "SELECT value FROM app_settings WHERE key = @key";
                cmd.Parameters.AddWithValue("@key", key);
                var val = cmd.ExecuteScalar();
                return val != null ? val.ToString()! : defaultValue;
            }
        }

        public void SetSetting(string key, string value)
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO app_settings (key, value) VALUES (@key, @value)
                    ON CONFLICT(key) DO UPDATE SET value = excluded.value
                    """;
                cmd.Parameters.AddWithValue("@key", key);
                cmd.Parameters.AddWithValue("@value", value);
                cmd.ExecuteNonQuery();
            }
        }

        // ── Customers ────────────────────────────────────────────────────────

        public List<CustomerRecord> GetCustomers()
        {
            lock (_lock)
            {
                var list = new List<CustomerRecord>();
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "SELECT id, name, address, email, note FROM customers ORDER BY name";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new CustomerRecord(
                        Id:      reader.GetString(0),
                        Name:    reader.GetString(1),
                        Address: reader.IsDBNull(2) ? null : reader.GetString(2),
                        Email:   reader.IsDBNull(3) ? null : reader.GetString(3),
                        Note:    reader.IsDBNull(4) ? null : reader.GetString(4)
                    ));
                }
                return list;
            }
        }

        public void AddCustomer(string id, string name, string? address, string? email, string? note)
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO customers (id, name, address, email, note)
                    VALUES (@id, @name, @addr, @email, @note)
                    ON CONFLICT(id) DO UPDATE SET
                        name    = excluded.name,
                        address = excluded.address,
                        email   = excluded.email,
                        note    = excluded.note
                    """;
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@addr", (object?)address ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@email", (object?)email ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@note", (object?)note ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>Deletes a customer and detaches any RFID keys / tenants that referenced it.</summary>
        public void DeleteCustomer(string id)
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = """
                    UPDATE rfid_user_map SET customer_id = NULL WHERE customer_id = @id;
                    UPDATE tenants       SET customer_id = NULL WHERE customer_id = @id;
                    DELETE FROM customers WHERE id = @id;
                    """;
                cmd.Parameters.AddWithValue("@id", id);
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
                cmd.CommandText = "SELECT id, name, meter_key, customer_id FROM tenants";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new TenantRecord(
                        Id:         reader.GetString(0),
                        Name:       reader.GetString(1),
                        MeterKey:   reader.GetString(2),
                        CustomerId: reader.IsDBNull(3) ? null : reader.GetString(3)
                    ));
                }
                return list;
            }
        }

        public void AddTenant(string id, string name, string meterKey, string? customerId = null)
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO tenants (id, name, meter_key, customer_id) VALUES (@id, @name, @key, @cust)
                    ON CONFLICT(id) DO UPDATE SET
                        name = excluded.name,
                        meter_key = excluded.meter_key,
                        customer_id = excluded.customer_id
                    """;
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@key", meterKey);
                cmd.Parameters.AddWithValue("@cust", (object?)customerId ?? DBNull.Value);
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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _db.Close();
            // Microsoft.Data.Sqlite pools connections by default, so Close() returns the
            // handle to the pool instead of releasing the file. Without clearing the pool
            // the database file (and its -wal/-shm siblings) stays locked on Windows.
            SqliteConnection.ClearPool(_db);
            _db.Dispose();
        }
    }

    public record ChargingTransaction(int Id, string ChargepointId, int ConnectorId, string IdTag, double Kwh, long Timestamp);
    public record TenantRecord(string Id, string Name, string MeterKey, string? CustomerId = null);
    public record RfidMapping(string IdTag, string UserName, string? CustomerId = null);
    public record CustomerRecord(string Id, string Name, string? Address, string? Email, string? Note);
    public record MeterReplacement(int Id, string TenantId, long ReplacedAt, double? OldFinalKwh, double? NewStartKwh, string? Note);
}
