// AlarmStore.cs – SQLite-backed alarm state machine
//
//  Replaces ThingsBoard's alarm API with a local persistent store.
//  State machine: ACTIVE_UNACK → ACTIVE_ACK → CLEARED
//  Deduplication: alarms grouped by (originator, type) — an active alarm
//  is updated rather than duplicated.

using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Data.Sqlite;

using Pulswerk.Core;

namespace Pulswerk.Storage
{
    public sealed class AlarmStore : IDisposable
    {
        private readonly SqliteConnection _db;
        private readonly object _lock = new();
        private bool _disposed;

        public AlarmStore(string dbPath)
        {
            _db = new SqliteConnection($"Data Source={dbPath}");
            _db.Open();

            // WAL mode for concurrent read/write
            using var pragma = _db.CreateCommand();
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            pragma.ExecuteNonQuery();

            InitSchema();
            Console.WriteLine($"  [AlarmStore] Initialized at {dbPath}");
        }

        private void InitSchema()
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS alarms (
                    id             TEXT    PRIMARY KEY,
                    type           TEXT    NOT NULL,
                    severity       TEXT    NOT NULL,
                    status         TEXT    NOT NULL DEFAULT 'ACTIVE_UNACK',
                    message        TEXT    NOT NULL,
                    originator     TEXT    NOT NULL,
                    origin_type    TEXT    NOT NULL DEFAULT 'DEVICE',
                    origin_key     TEXT,
                    details        TEXT,
                    created_at     INTEGER NOT NULL,
                    updated_at     INTEGER NOT NULL,
                    cleared_at     INTEGER,
                    ack_comment    TEXT,
                    bacnet_ack_key TEXT
                );

                CREATE INDEX IF NOT EXISTS idx_alarms_status
                    ON alarms(status);
                CREATE INDEX IF NOT EXISTS idx_alarms_origin
                    ON alarms(originator, type);
                """;
            cmd.ExecuteNonQuery();
        }

        // ── Create / Update ──────────────────────────────────────────────────

        /// <summary>
        /// Raises a new alarm or updates an existing active alarm.
        /// Idempotent: grouped by (originator, type). If an active alarm already
        /// exists for that pair, its severity/message/details are updated.
        /// </summary>
        public AlarmRecord CreateOrUpdate(
            string originator, string originType,
            string type, string severity, string message,
            Dictionary<string, object>? details = null,
            string? bacnetAckKey = null)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string? detailsJson = details != null ? JsonSerializer.Serialize(details) : null;

            lock (_lock)
            {
                // Check for existing active alarm
                using var findCmd = _db.CreateCommand();
                findCmd.CommandText = """
                    SELECT id FROM alarms
                    WHERE originator = @orig AND type = @type
                      AND status IN ('ACTIVE_UNACK', 'ACTIVE_ACK')
                    LIMIT 1
                    """;
                findCmd.Parameters.AddWithValue("@orig", originator);
                findCmd.Parameters.AddWithValue("@type", type);

                var existingId = findCmd.ExecuteScalar() as string;

                if (existingId != null)
                {
                    // Update existing alarm
                    using var updateCmd = _db.CreateCommand();
                    updateCmd.CommandText = """
                        UPDATE alarms SET
                            severity = @sev, message = @msg, details = @det,
                            updated_at = @now, bacnet_ack_key = COALESCE(@bak, bacnet_ack_key)
                        WHERE id = @id
                        """;
                    updateCmd.Parameters.AddWithValue("@id", existingId);
                    updateCmd.Parameters.AddWithValue("@sev", severity);
                    updateCmd.Parameters.AddWithValue("@msg", message);
                    updateCmd.Parameters.AddWithValue("@det", (object?)detailsJson ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@now", now);
                    updateCmd.Parameters.AddWithValue("@bak", (object?)bacnetAckKey ?? DBNull.Value);
                    updateCmd.ExecuteNonQuery();

                    return GetById(existingId)!;
                }
                else
                {
                    // Create new alarm
                    string id = Guid.NewGuid().ToString("N")[..16];
                    using var insertCmd = _db.CreateCommand();
                    insertCmd.CommandText = """
                        INSERT INTO alarms (id, type, severity, status, message,
                            originator, origin_type, details, created_at, updated_at,
                            bacnet_ack_key)
                        VALUES (@id, @type, @sev, 'ACTIVE_UNACK', @msg,
                            @orig, @ot, @det, @now, @now, @bak)
                        """;
                    insertCmd.Parameters.AddWithValue("@id", id);
                    insertCmd.Parameters.AddWithValue("@type", type);
                    insertCmd.Parameters.AddWithValue("@sev", severity);
                    insertCmd.Parameters.AddWithValue("@msg", message);
                    insertCmd.Parameters.AddWithValue("@orig", originator);
                    insertCmd.Parameters.AddWithValue("@ot", originType);
                    insertCmd.Parameters.AddWithValue("@det", (object?)detailsJson ?? DBNull.Value);
                    insertCmd.Parameters.AddWithValue("@now", now);
                    insertCmd.Parameters.AddWithValue("@bak", (object?)bacnetAckKey ?? DBNull.Value);
                    insertCmd.ExecuteNonQuery();

                    Console.WriteLine($"  [Alarm] Raised: [{severity}] {type} on {originType}/{originator}");
                    return GetById(id)!;
                }
            }
        }

        // ── State transitions ────────────────────────────────────────────────

        /// <summary>Acknowledge an alarm by its ID, optionally with a comment.</summary>
        public bool Acknowledge(string alarmId, string? comment = null)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = """
                    UPDATE alarms SET
                        status = 'ACTIVE_ACK', updated_at = @now,
                        ack_comment = CASE WHEN @comment IS NOT NULL THEN @comment ELSE ack_comment END
                    WHERE id = @id AND status = 'ACTIVE_UNACK'
                    """;
                cmd.Parameters.AddWithValue("@id", alarmId);
                cmd.Parameters.AddWithValue("@now", now);
                cmd.Parameters.AddWithValue("@comment", (object?)comment ?? DBNull.Value);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        /// <summary>Clear an alarm by its ID.</summary>
        public bool Clear(string alarmId)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = """
                    UPDATE alarms SET status = 'CLEARED', cleared_at = @now, updated_at = @now
                    WHERE id = @id AND status IN ('ACTIVE_UNACK', 'ACTIVE_ACK')
                    """;
                cmd.Parameters.AddWithValue("@id", alarmId);
                cmd.Parameters.AddWithValue("@now", now);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        /// <summary>Clear the active alarm matching originator + type.</summary>
        public bool ClearByOriginAndType(string originator, string type, string originType = "DEVICE")
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = """
                    UPDATE alarms SET status = 'CLEARED', cleared_at = @now, updated_at = @now
                    WHERE originator = @orig AND type = @type AND origin_type = @ot
                      AND status IN ('ACTIVE_UNACK', 'ACTIVE_ACK')
                    """;
                cmd.Parameters.AddWithValue("@orig", originator);
                cmd.Parameters.AddWithValue("@type", type);
                cmd.Parameters.AddWithValue("@ot", originType);
                cmd.Parameters.AddWithValue("@now", now);
                int n = cmd.ExecuteNonQuery();
                if (n > 0) Console.WriteLine($"  [Alarm] Cleared: {type} on {originType}/{originator}");
                return n > 0;
            }
        }

        // ── Queries ──────────────────────────────────────────────────────────

        /// <summary>Returns all active alarms (both unacknowledged and acknowledged).</summary>
        public List<AlarmRecord> GetAllActive()
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = """
                    SELECT * FROM alarms
                    WHERE status IN ('ACTIVE_UNACK', 'ACTIVE_ACK')
                    ORDER BY created_at DESC
                    """;
                return ReadRecords(cmd);
            }
        }

        /// <summary>Returns active alarms for a specific originator.</summary>
        public List<AlarmRecord> GetActiveForOriginator(string originator, string originType = "DEVICE")
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = """
                    SELECT * FROM alarms
                    WHERE originator = @orig AND origin_type = @ot
                      AND status IN ('ACTIVE_UNACK', 'ACTIVE_ACK')
                    ORDER BY created_at DESC
                    """;
                cmd.Parameters.AddWithValue("@orig", originator);
                cmd.Parameters.AddWithValue("@ot", originType);
                return ReadRecords(cmd);
            }
        }

        /// <summary>Returns a single alarm by ID.</summary>
        public AlarmRecord? GetById(string id)
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "SELECT * FROM alarms WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                var list = ReadRecords(cmd);
                return list.Count > 0 ? list[0] : null;
            }
        }

        /// <summary>Count of all active alarms.</summary>
        public int CountActive()
        {
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM alarms WHERE status IN ('ACTIVE_UNACK', 'ACTIVE_ACK')";
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        // ── Retention ────────────────────────────────────────────────────────

        /// <summary>Delete cleared alarms older than the specified number of days.</summary>
        public int PurgeCleared(int olderThanDays = 30)
        {
            long cutoff = DateTimeOffset.UtcNow.AddDays(-olderThanDays).ToUnixTimeMilliseconds();
            lock (_lock)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "DELETE FROM alarms WHERE status = 'CLEARED' AND cleared_at < @cutoff";
                cmd.Parameters.AddWithValue("@cutoff", cutoff);
                int n = cmd.ExecuteNonQuery();
                if (n > 0) Console.WriteLine($"  [AlarmStore] Purged {n} cleared alarms older than {olderThanDays}d.");
                return n;
            }
        }

        // ── Internal ─────────────────────────────────────────────────────────

        private static List<AlarmRecord> ReadRecords(SqliteCommand cmd)
        {
            var list = new List<AlarmRecord>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new AlarmRecord(
                    Id: reader.GetString(0),
                    Type: reader.GetString(1),
                    Severity: reader.GetString(2),
                    Status: reader.GetString(3),
                    Message: reader.GetString(4),
                    Originator: reader.GetString(5),
                    OriginType: reader.GetString(6),
                    OriginKey: reader.IsDBNull(7) ? null : reader.GetString(7),
                    Details: reader.IsDBNull(8) ? null : reader.GetString(8),
                    CreatedAt: reader.GetInt64(9),
                    UpdatedAt: reader.GetInt64(10),
                    ClearedAt: reader.IsDBNull(11) ? null : reader.GetInt64(11),
                    AckComment: reader.IsDBNull(12) ? null : reader.GetString(12),
                    BacnetAckKey: reader.IsDBNull(13) ? null : reader.GetString(13)
                ));
            }
            return list;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _db?.Close();
            _db?.Dispose();
        }
    }

    /// <summary>Represents a single alarm record in the store.</summary>
    public record AlarmRecord(
        string Id, string Type, string Severity, string Status,
        string Message, string Originator, string OriginType,
        string? OriginKey, string? Details,
        long CreatedAt, long UpdatedAt, long? ClearedAt,
        string? AckComment, string? BacnetAckKey);
}
