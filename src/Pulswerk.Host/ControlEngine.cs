using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Pulswerk.Core;
using Pulswerk.Storage;

namespace Pulswerk.Host
{
    sealed class ControlEngine
    {
        readonly IReadOnlyList<ControlRuleConfig> _rules;
        readonly IReadOnlyList<DeviceConfig> _devices;
        readonly Dictionary<string, IDeviceDriver> _drivers;
        readonly Dictionary<string, ConnectionConfig> _connections;
        readonly Dictionary<string, SemaphoreSlim> _connectionLocks;
        readonly AlarmStore _alarmStore;
        readonly object _stateLock = new();
        readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, DateTime> _timestamps = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, DateTime> _deviceSeen = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, DateTime> _lastEvaluated = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, bool> _lastConditions = new(StringComparer.OrdinalIgnoreCase);

        public ControlEngine(
            IEnumerable<ControlRuleConfig>? rules,
            IEnumerable<DeviceConfig> devices,
            Dictionary<string, IDeviceDriver> drivers,
            Dictionary<string, ConnectionConfig> connections,
            Dictionary<string, SemaphoreSlim> connectionLocks,
            AlarmStore alarmStore)
        {
            _rules = (rules ?? Array.Empty<ControlRuleConfig>()).ToList();
            _devices = devices.ToList();
            _drivers = drivers;
            _connections = connections;
            _connectionLocks = connectionLocks;
            _alarmStore = alarmStore;
        }

        public bool HasRules => _rules.Count > 0;

        public void Update(IEnumerable<KeyValuePair<string, object>> values)
        {
            var now = DateTime.UtcNow;
            lock (_stateLock)
            {
                foreach (var pair in values)
                {
                    _values[pair.Key] = pair.Value;
                    _timestamps[pair.Key] = now;
                }
            }
        }

        public void MarkDeviceSeen(string deviceName)
        {
            var device = _devices.FirstOrDefault(candidate =>
                candidate.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase));
            if (device == null) return;

            lock (_stateLock) _deviceSeen[device.Id] = DateTime.UtcNow;
        }

        public void EvaluateDue()
        {
            foreach (var rule in _rules)
            {
                if (!rule.Enabled || IsNotDue(rule)) continue;

                Dictionary<string, object> values;
                Dictionary<string, DateTime> timestamps;
                Dictionary<string, DateTime> deviceSeen;
                lock (_stateLock)
                {
                    values = new Dictionary<string, object>(_values, StringComparer.OrdinalIgnoreCase);
                    timestamps = new Dictionary<string, DateTime>(_timestamps, StringComparer.OrdinalIgnoreCase);
                    deviceSeen = new Dictionary<string, DateTime>(_deviceSeen, StringComparer.OrdinalIgnoreCase);
                    _lastEvaluated[rule.Id] = DateTime.UtcNow;
                }

                bool condition = (rule.When == null || ControlRuleEvaluator.Evaluate(rule.When, values)) &&
                    SourcesAreFresh(rule, values, timestamps, deviceSeen);
                bool previousCondition;
                lock (_stateLock)
                {
                    _lastConditions.TryGetValue(rule.Id, out previousCondition);
                    _lastConditions[rule.Id] = condition;
                }

                if (!condition || (rule.OnChangeOnly && previousCondition)) continue;

                foreach (var action in rule.Actions)
                    ExecuteAction(rule, action, values);
            }
        }

        bool IsNotDue(ControlRuleConfig rule)
        {
            lock (_stateLock)
            {
                return _lastEvaluated.TryGetValue(rule.Id, out var last) &&
                    DateTime.UtcNow - last < TimeSpan.FromSeconds(rule.IntervalSeconds);
            }
        }

        bool SourcesAreFresh(
            ControlRuleConfig rule,
            IReadOnlyDictionary<string, object> values,
            IReadOnlyDictionary<string, DateTime> timestamps,
            IReadOnlyDictionary<string, DateTime> deviceSeen)
        {
            var sources = new List<string>();
            CollectSources(rule.When, sources);
            foreach (var action in rule.Actions)
                if (!string.IsNullOrWhiteSpace(action.ValueSource)) sources.Add(action.ValueSource);

            var now = DateTime.UtcNow;
            return sources.All(source =>
            {
                if (!values.ContainsKey(source)) return false;

                DateTime lastSeen = timestamps.TryGetValue(source, out var valueSeen)
                    ? valueSeen
                    : DateTime.MinValue;
                var device = IdentifyDevice(source);
                if (device != null && deviceSeen.TryGetValue(device.Id, out var deviceObservation) &&
                    deviceObservation > lastSeen)
                    lastSeen = deviceObservation;

                return now - lastSeen <= TimeSpan.FromSeconds(rule.SourceStaleSeconds);
            });
        }

        static void CollectSources(ControlConditionConfig? condition, List<string> sources)
        {
            if (condition == null) return;
            if (!string.IsNullOrWhiteSpace(condition.Source)) sources.Add(condition.Source);
            foreach (var child in condition.All ?? new()) CollectSources(child, sources);
            foreach (var child in condition.Any ?? new()) CollectSources(child, sources);
        }

        void ExecuteAction(
            ControlRuleConfig rule,
            ControlActionConfig action,
            IReadOnlyDictionary<string, object> values)
        {
            object? rawValue = action.ValueSource != null && values.TryGetValue(action.ValueSource, out var sourceValue)
                ? sourceValue
                : action.Value;
            if (!TryGetWriteValue(rawValue, out var value))
            {
                Log.Warning($"[Control] Rule '{rule.Id}' skipped: value for '{action.Target}' is not numeric or boolean.");
                return;
            }

            var device = _connections.Count == 0 ? null : IdentifyDevice(action.Target);
            if (device == null || !_drivers.TryGetValue(device.Name, out var driver) || driver is not IDeviceWriter writer)
            {
                RecordFailure(rule, action, $"target '{action.Target}' has no writable driver");
                return;
            }

            string driverKey = action.Target[(device.Id.Length + 1)..];
            if (!writer.IsWritable(driverKey))
            {
                RecordFailure(rule, action, $"target '{action.Target}' is not writable");
                return;
            }

            if (device.ConnectionId == null || !_connections.TryGetValue(device.ConnectionId, out var connection) ||
                !_connectionLocks.TryGetValue(device.ConnectionId, out var connectionLock))
            {
                RecordFailure(rule, action, $"target '{action.Target}' has no valid connection");
                return;
            }

            try
            {
                connectionLock.Wait();
                try { writer.Write(connection, device, driverKey, value); }
                finally { connectionLock.Release(); }

                _alarmStore.ClearByOriginAndType($"CONTROL:{rule.Id}", "Control Write Failure");
                Log.Info($"[Control] Rule '{rule.Id}' wrote {action.Target} = {value.ToString(CultureInfo.InvariantCulture)}");
            }
            catch (Exception ex)
            {
                RecordFailure(rule, action, ex.Message);
            }
        }

        DeviceConfig? IdentifyDevice(string target)
        {
            return _devices
                .Where(device => target.StartsWith(device.Id + "_", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(device => device.Id.Length)
                .FirstOrDefault();
        }

        void RecordFailure(ControlRuleConfig rule, ControlActionConfig action, string reason)
        {
            Log.Warning($"[Control] Rule '{rule.Id}' failed for '{action.Target}': {reason}");
            _alarmStore.CreateOrUpdate(
                $"CONTROL:{rule.Id}", "CONTROL", "Control Write Failure", "WARNING",
                $"Rule '{rule.Id}' could not write '{action.Target}': {reason}");
        }

        static bool TryGetWriteValue(object? rawValue, out double value)
        {
            value = 0;
            rawValue = ControlRuleEvaluator.UnwrapJsonValue(rawValue);
            if (rawValue is bool boolean) { value = boolean ? 1 : 0; return true; }
            return ControlRuleEvaluator.TryGetNumber(rawValue, out value);
        }
    }
}