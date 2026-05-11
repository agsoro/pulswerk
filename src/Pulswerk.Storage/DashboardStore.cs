// DashboardStore.cs – JSON file persistence for custom dashboards
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using Pulswerk.Core;

namespace Pulswerk.Storage
{
    /// <summary>
    /// Loads and saves user-created dashboard definitions from a JSON file.
    /// Thread-safe; keeps an in-memory cache and flushes on every mutation.
    /// </summary>
    public sealed class DashboardStore
    {
        private readonly string _filePath;
        private readonly object _lock = new();
        private List<DashboardDefinition> _dashboards = new();

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public DashboardStore(string dataDir)
        {
            if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
            _filePath = Path.Combine(dataDir, "dashboards.json");
            Load();
            Console.WriteLine($"  [DashboardStore] Loaded {_dashboards.Count} dashboard(s) from {_filePath}");
        }

        // ── Read ─────────────────────────────────────────────────────────────

        public List<DashboardDefinition> GetAll()
        {
            lock (_lock) return _dashboards.ToList();
        }

        public DashboardDefinition? GetById(string id)
        {
            lock (_lock) return _dashboards.FirstOrDefault(d => d.Id == id);
        }

        // ── Write ────────────────────────────────────────────────────────────

        public DashboardDefinition Create(string name, string description = "")
        {
            var dash = new DashboardDefinition
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                Name = name,
                Description = description,
                CreatedAt = DateTime.UtcNow.ToString("o"),
                UpdatedAt = DateTime.UtcNow.ToString("o"),
                Timewindow = new TimewindowConfig(),
                Widgets = new List<WidgetDefinition>()
            };

            lock (_lock)
            {
                _dashboards.Add(dash);
                Persist();
            }
            return dash;
        }

        public bool Save(DashboardDefinition dash)
        {
            lock (_lock)
            {
                var idx = _dashboards.FindIndex(d => d.Id == dash.Id);
                if (idx < 0) return false;

                dash.UpdatedAt = DateTime.UtcNow.ToString("o");
                _dashboards[idx] = dash;
                Persist();
            }
            return true;
        }

        public bool Delete(string id)
        {
            lock (_lock)
            {
                int removed = _dashboards.RemoveAll(d => d.Id == id);
                if (removed == 0) return false;
                Persist();
            }
            return true;
        }

        // ── Persistence ──────────────────────────────────────────────────────

        private void Load()
        {
            if (!File.Exists(_filePath))
            {
                _dashboards = new List<DashboardDefinition>();
                return;
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                var wrapper = JsonSerializer.Deserialize<DashboardFileWrapper>(json, _jsonOpts);
                _dashboards = wrapper?.Dashboards ?? new List<DashboardDefinition>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  [DashboardStore] Failed to load {_filePath}: {ex.Message}");
                _dashboards = new List<DashboardDefinition>();
            }
        }

        private void Persist()
        {
            try
            {
                var wrapper = new DashboardFileWrapper { Dashboards = _dashboards };
                var json = JsonSerializer.Serialize(wrapper, _jsonOpts);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  [DashboardStore] Failed to save {_filePath}: {ex.Message}");
            }
        }

        // ── File wrapper ─────────────────────────────────────────────────────

        private class DashboardFileWrapper
        {
            [JsonPropertyName("dashboards")]
            public List<DashboardDefinition> Dashboards { get; set; } = new();
        }
    }

    // ── Data model ───────────────────────────────────────────────────────────

    public class DashboardDefinition
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("createdAt")] public string CreatedAt { get; set; } = "";
        [JsonPropertyName("updatedAt")] public string UpdatedAt { get; set; } = "";

        [JsonPropertyName("timewindow")]
        public TimewindowConfig Timewindow { get; set; } = new();

        [JsonPropertyName("widgets")]
        public List<WidgetDefinition> Widgets { get; set; } = new();
    }

    public class TimewindowConfig
    {
        /// <summary>"realtime" or "history"</summary>
        [JsonPropertyName("mode")] public string Mode { get; set; } = "realtime";

        /// <summary>Rolling window duration in ms (e.g. 3600000 = 1 hour). Used in realtime mode.</summary>
        [JsonPropertyName("realtimeMs")] public long RealtimeMs { get; set; } = 3600000;

        /// <summary>Fixed start timestamp in ms (epoch). Used in history mode.</summary>
        [JsonPropertyName("historyFrom")] public long? HistoryFrom { get; set; }

        /// <summary>Fixed end timestamp in ms (epoch). Used in history mode.</summary>
        [JsonPropertyName("historyTo")] public long? HistoryTo { get; set; }
    }

    public class WidgetDefinition
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("type")] public string Type { get; set; } = ""; // "timeseries" | "latest-values" | "single-value"
        [JsonPropertyName("title")] public string Title { get; set; } = "";

        // gridstack position
        [JsonPropertyName("x")] public int X { get; set; }
        [JsonPropertyName("y")] public int Y { get; set; }
        [JsonPropertyName("w")] public int W { get; set; } = 6;
        [JsonPropertyName("h")] public int H { get; set; } = 4;

        /// <summary>Flexible per-widget-type configuration stored as raw JSON.</summary>
        [JsonPropertyName("config")]
        public JsonElement? Config { get; set; }
    }
}
