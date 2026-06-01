using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pulswerk.Billing;
using Xunit;

namespace Pulswerk.Billing.Tests
{
    /// <summary>
    /// Unit tests for BillingStore covering meter replacements, tariffs, RFID, tenants,
    /// and the segmented consumption algorithm assumptions.
    /// Each test class creates a fresh in-memory SQLite DB via a temp file.
    /// </summary>
    public sealed class BillingStoreFixture : IDisposable
    {
        public string DbPath { get; } = Path.GetTempFileName();
        public BillingStore Store { get; }

        public BillingStoreFixture()
        {
            Store = new BillingStore(DbPath);
        }

        public void Dispose()
        {
            Store.Dispose();
            if (File.Exists(DbPath)) File.Delete(DbPath);
        }
    }

    // ── Tariff Tests ──────────────────────────────────────────────────────────

    public class TariffTests : IDisposable
    {
        private readonly BillingStoreFixture _fx = new();

        [Fact]
        public void GetTariff_ReturnsDefault_WhenKeyMissing()
        {
            var val = _fx.Store.GetTariff("nonexistent_key", 99.99);
            Assert.Equal(99.99, val);
        }

        [Fact]
        public void SetAndGetTariff_RoundTrips()
        {
            _fx.Store.SetTariff("rate_per_kwh", 0.42);
            Assert.Equal(0.42, _fx.Store.GetTariff("rate_per_kwh", 0.0));
        }

        [Fact]
        public void SetTariff_Updates_ExistingValue()
        {
            _fx.Store.SetTariff("rate_per_kwh", 0.30);
            _fx.Store.SetTariff("rate_per_kwh", 0.35);
            Assert.Equal(0.35, _fx.Store.GetTariff("rate_per_kwh", 0.0));
        }

        [Fact]
        public void SeedDefaultTariffs_ArePresent_AfterInit()
        {
            // Default seeds run in ctor
            Assert.Equal(0.30, _fx.Store.GetTariff("rate_per_kwh", 0.0));
            Assert.Equal(10.00, _fx.Store.GetTariff("base_monthly_fee", 0.0));
        }

        public void Dispose() => _fx.Dispose();
    }

    // ── RFID Tests ────────────────────────────────────────────────────────────

    public class RfidTests : IDisposable
    {
        private readonly BillingStoreFixture _fx = new();

        [Fact]
        public void AddAndGetRfidMapping_RoundTrips()
        {
            _fx.Store.AddRfidMapping("04A1B2C3", "Alice");
            var map = _fx.Store.GetRfidMap();
            Assert.True(map.ContainsKey("04A1B2C3"));
            Assert.Equal("Alice", map["04A1B2C3"]);
        }

        [Fact]
        public void AddRfidMapping_Updates_ExistingTag()
        {
            _fx.Store.AddRfidMapping("04A1B2C3", "Alice");
            _fx.Store.AddRfidMapping("04A1B2C3", "Alice Smith");
            Assert.Equal("Alice Smith", _fx.Store.GetRfidMap()["04A1B2C3"]);
        }

        [Fact]
        public void IsRfidValid_ReturnsFalse_ForUnknownTag()
        {
            Assert.False(_fx.Store.IsRfidValid("UNKNOWN"));
        }

        [Fact]
        public void IsRfidValid_ReturnsTrue_ForRegisteredTag()
        {
            _fx.Store.AddRfidMapping("AABBCCDD", "Bob");
            Assert.True(_fx.Store.IsRfidValid("AABBCCDD"));
        }

        [Fact]
        public void DeleteRfidMapping_RemovesEntry()
        {
            _fx.Store.AddRfidMapping("DEAD1234", "ToDelete");
            _fx.Store.DeleteRfidMapping("DEAD1234");
            Assert.False(_fx.Store.GetRfidMap().ContainsKey("DEAD1234"));
        }

        [Fact]
        public void DeleteRfidMapping_Noop_ForMissingTag()
        {
            // Should not throw
            _fx.Store.DeleteRfidMapping("NO_SUCH_TAG");
        }

        public void Dispose() => _fx.Dispose();
    }

    // ── Tenant Tests ──────────────────────────────────────────────────────────

    public class TenantTests : IDisposable
    {
        private readonly BillingStoreFixture _fx = new();

        [Fact]
        public void AddAndGetTenants_RoundTrips()
        {
            _fx.Store.AddTenant("t1", "Apartment 1", "meter-1_kwh");
            var list = _fx.Store.GetTenants();
            Assert.Single(list);
            Assert.Equal("t1", list[0].Id);
            Assert.Equal("Apartment 1", list[0].Name);
            Assert.Equal("meter-1_kwh", list[0].MeterKey);
        }

        [Fact]
        public void AddTenant_Upserts_ExistingId()
        {
            _fx.Store.AddTenant("t1", "Old Name", "old-key");
            _fx.Store.AddTenant("t1", "New Name", "new-key");
            var list = _fx.Store.GetTenants();
            Assert.Single(list);
            Assert.Equal("New Name", list[0].Name);
            Assert.Equal("new-key", list[0].MeterKey);
        }

        [Fact]
        public void DeleteTenant_RemovesEntry()
        {
            _fx.Store.AddTenant("t1", "To Delete", "key");
            _fx.Store.DeleteTenant("t1");
            Assert.Empty(_fx.Store.GetTenants());
        }

        [Fact]
        public void DeleteTenant_Noop_ForMissingId()
        {
            _fx.Store.DeleteTenant("NO_SUCH_TENANT"); // must not throw
        }

        public void Dispose() => _fx.Dispose();
    }

    // ── Meter Replacement Tests ───────────────────────────────────────────────

    public class MeterReplacementTests : IDisposable
    {
        private readonly BillingStoreFixture _fx = new();

        private static long Ts(string iso) =>
            new DateTimeOffset(DateTime.Parse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind),
                               TimeSpan.Zero).ToUnixTimeMilliseconds();

        [Fact]
        public void AddAndGetMeterReplacements_RoundTrips()
        {
            _fx.Store.AddTenant("t1", "A", "k");
            _fx.Store.AddMeterReplacement("t1", Ts("2026-05-15T12:00:00Z"), 98751.5, 0.0, "New meter");

            var list = _fx.Store.GetMeterReplacements("t1");
            Assert.Single(list);
            var r = list[0];
            Assert.Equal("t1", r.TenantId);
            Assert.Equal(Ts("2026-05-15T12:00:00Z"), r.ReplacedAt);
            Assert.Equal(98751.5, r.OldFinalKwh);
            Assert.Equal(0.0, r.NewStartKwh);
            Assert.Equal("New meter", r.Note);
        }

        [Fact]
        public void GetMeterReplacements_ReturnsEmpty_ForUnknownTenant()
        {
            Assert.Empty(_fx.Store.GetMeterReplacements("no-such-tenant"));
        }

        [Fact]
        public void GetMeterReplacements_OrderedByReplacedAt_Ascending()
        {
            _fx.Store.AddMeterReplacement("t1", Ts("2026-06-01T00:00:00Z"), null, null, "second");
            _fx.Store.AddMeterReplacement("t1", Ts("2026-03-01T00:00:00Z"), null, null, "first");

            var list = _fx.Store.GetMeterReplacements("t1");
            Assert.Equal(2, list.Count);
            Assert.True(list[0].ReplacedAt < list[1].ReplacedAt);
        }

        [Fact]
        public void AddMeterReplacement_AllowsNullOptionalFields()
        {
            _fx.Store.AddMeterReplacement("t1", Ts("2026-05-15T00:00:00Z"), null, null, null);
            var r = _fx.Store.GetMeterReplacements("t1").Single();
            Assert.Null(r.OldFinalKwh);
            Assert.Null(r.NewStartKwh);
            Assert.Null(r.Note);
        }

        [Fact]
        public void DeleteMeterReplacement_RemovesCorrectEntry()
        {
            _fx.Store.AddMeterReplacement("t1", Ts("2026-05-15T00:00:00Z"), null, null, "to delete");
            _fx.Store.AddMeterReplacement("t1", Ts("2026-06-01T00:00:00Z"), null, null, "to keep");

            var all = _fx.Store.GetMeterReplacements("t1");
            int deleteId = all.First(r => r.Note == "to delete").Id;
            bool deleted = _fx.Store.DeleteMeterReplacement(deleteId);

            Assert.True(deleted);
            var remaining = _fx.Store.GetMeterReplacements("t1");
            Assert.Single(remaining);
            Assert.Equal("to keep", remaining[0].Note);
        }

        [Fact]
        public void DeleteMeterReplacement_ReturnsFalse_ForMissingId()
        {
            bool result = _fx.Store.DeleteMeterReplacement(99999);
            Assert.False(result);
        }

        [Fact]
        public void MultipleTenants_ReplacementsAreIsolated()
        {
            _fx.Store.AddMeterReplacement("t1", Ts("2026-05-15T00:00:00Z"), null, null, "tenant1");
            _fx.Store.AddMeterReplacement("t2", Ts("2026-05-20T00:00:00Z"), null, null, "tenant2");

            Assert.Single(_fx.Store.GetMeterReplacements("t1"));
            Assert.Single(_fx.Store.GetMeterReplacements("t2"));
            Assert.Equal("tenant1", _fx.Store.GetMeterReplacements("t1")[0].Note);
            Assert.Equal("tenant2", _fx.Store.GetMeterReplacements("t2")[0].Note);
        }

        public void Dispose() => _fx.Dispose();
    }

    // ── GetMeterReplacementsInRange Tests ─────────────────────────────────────

    public class MeterReplacementsInRangeTests : IDisposable
    {
        private readonly BillingStoreFixture _fx = new();

        private static long Ts(string iso) =>
            new DateTimeOffset(DateTime.Parse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind),
                               TimeSpan.Zero).ToUnixTimeMilliseconds();

        [Fact]
        public void InRange_ReturnsReplacements_WithinOpenInterval()
        {
            // Period: May 1 – Jun 1
            long periodStart = Ts("2026-05-01T00:00:00Z");
            long periodEnd   = Ts("2026-06-01T00:00:00Z");

            _fx.Store.AddMeterReplacement("t1", Ts("2026-04-30T00:00:00Z"), null, null, "before");  // excluded
            _fx.Store.AddMeterReplacement("t1", Ts("2026-05-15T12:00:00Z"), null, null, "inside");  // included
            _fx.Store.AddMeterReplacement("t1", Ts("2026-06-01T00:00:00Z"), null, null, "at-end");  // excluded (open interval)

            var result = _fx.Store.GetMeterReplacementsInRange("t1", periodStart, periodEnd);
            Assert.Single(result);
            Assert.Equal("inside", result[0].Note);
        }

        [Fact]
        public void InRange_ReturnsEmpty_WhenNoReplacementsExist()
        {
            long periodStart = Ts("2026-05-01T00:00:00Z");
            long periodEnd   = Ts("2026-06-01T00:00:00Z");
            Assert.Empty(_fx.Store.GetMeterReplacementsInRange("t1", periodStart, periodEnd));
        }

        [Fact]
        public void InRange_ReturnsMultiple_OrderedAscending()
        {
            long periodStart = Ts("2026-05-01T00:00:00Z");
            long periodEnd   = Ts("2026-06-01T00:00:00Z");

            _fx.Store.AddMeterReplacement("t1", Ts("2026-05-20T00:00:00Z"), null, null, "second");
            _fx.Store.AddMeterReplacement("t1", Ts("2026-05-10T00:00:00Z"), null, null, "first");

            var result = _fx.Store.GetMeterReplacementsInRange("t1", periodStart, periodEnd);
            Assert.Equal(2, result.Count);
            Assert.True(result[0].ReplacedAt < result[1].ReplacedAt);
            Assert.Equal("first",  result[0].Note);
            Assert.Equal("second", result[1].Note);
        }

        [Fact]
        public void InRange_DoesNotReturnOtherTenants()
        {
            long periodStart = Ts("2026-05-01T00:00:00Z");
            long periodEnd   = Ts("2026-06-01T00:00:00Z");

            _fx.Store.AddMeterReplacement("t1", Ts("2026-05-15T00:00:00Z"), null, null, "t1");
            _fx.Store.AddMeterReplacement("t2", Ts("2026-05-16T00:00:00Z"), null, null, "t2");

            var result = _fx.Store.GetMeterReplacementsInRange("t1", periodStart, periodEnd);
            Assert.Single(result);
            Assert.Equal("t1", result[0].Note);
        }

        public void Dispose() => _fx.Dispose();
    }

    // ── Segmented Consumption Algorithm Tests ─────────────────────────────────
    // These tests validate the billing algorithm logic in isolation,
    // mirroring what GenerateMonthlyInvoice does in ApiController.

    public class SegmentedConsumptionAlgorithmTests
    {
        /// <summary>Simulates the InfluxDB query result: list of (ts, value) newest-first.</summary>
        private static List<(long Ts, double Value)> InfluxRange(double start, double end, int nPoints = 10)
        {
            // Produce nPoints evenly between start and end, newest-first
            var step = (end - start) / (nPoints - 1);
            return Enumerable.Range(0, nPoints)
                .Select(i => ((long)(1_000_000 + i * 1000), start + i * step))
                .Reverse()
                .ToList();
        }

        /// <summary>Replicates the segmented billing loop from ApiController.</summary>
        private static double CalculateSegmentedKwh(
            List<long> breakpoints,
            Func<int, List<(long Ts, double Value)>> getSegmentPoints)
        {
            double total = 0.0;
            for (int i = 0; i < breakpoints.Count - 1; i++)
            {
                var pts = getSegmentPoints(i);
                if (pts.Count > 1)
                {
                    double segLatest   = pts.First().Value;
                    double segEarliest = pts.Last().Value;
                    total += Math.Max(0.0, segLatest - segEarliest);
                }
            }
            return total;
        }

        [Fact]
        public void NoReplacement_SingleSegment_EqualsLatestMinusEarliest()
        {
            // Old-style: [periodStart → periodEnd] → 98750 → 98900 = 150 kWh
            var breakpoints = new List<long> { 0L, 1_000_000L };
            var pts = InfluxRange(98750, 98900);

            double total = CalculateSegmentedKwh(breakpoints, _ => pts);
            Assert.Equal(150.0, total, precision: 2);
        }

        [Fact]
        public void OneReplacement_SumstwoBothSegments()
        {
            // Old meter:   0  → 500_000   values 98600 → 98750  = 150 kWh
            // New meter: 500_000 → 1_000_000  values   0 →   90  =  90 kWh
            // Total: 240 kWh
            var breakpoints = new List<long> { 0L, 500_000L, 1_000_000L };

            var seg0 = InfluxRange(98600, 98750);  // old meter segment
            var seg1 = InfluxRange(0, 90);          // new meter segment

            double total = CalculateSegmentedKwh(breakpoints, i => i == 0 ? seg0 : seg1);
            Assert.Equal(240.0, total, precision: 2);
        }

        [Fact]
        public void TwoReplacements_SumsThreeSegments()
        {
            var breakpoints = new List<long> { 0L, 300_000L, 700_000L, 1_000_000L };
            // seg0: 0 → 50, seg1: 0 → 80, seg2: 0 → 30 → total = 160
            var segs = new[] { InfluxRange(0, 50), InfluxRange(0, 80), InfluxRange(0, 30) };

            double total = CalculateSegmentedKwh(breakpoints, i => segs[i]);
            Assert.Equal(160.0, total, precision: 2);
        }

        [Fact]
        public void NegativeSegment_IsClampedToZero()
        {
            // A data anomaly (e.g. telemetry rollback) should never subtract from the total
            var breakpoints = new List<long> { 0L, 1_000_000L };
            var pts = InfluxRange(500, 300); // newest=300, earliest=500 → delta = -200

            double total = CalculateSegmentedKwh(breakpoints, _ => pts);
            Assert.Equal(0.0, total);
        }

        [Fact]
        public void EmptySegment_ContributesZero()
        {
            var breakpoints = new List<long> { 0L, 500_000L, 1_000_000L };
            // seg0 has data, seg1 has no readings (e.g. meter offline)
            var seg0 = InfluxRange(100, 200);
            var seg1 = new List<(long, double)>(); // empty

            double total = CalculateSegmentedKwh(breakpoints, i => i == 0 ? seg0 : seg1);
            Assert.Equal(100.0, total, precision: 2);
        }

        [Fact]
        public void SinglePointSegment_ContributesZero()
        {
            var breakpoints = new List<long> { 0L, 1_000_000L };
            // Only one data point — cannot compute delta, algorithm requires pts.Count > 1
            var pts = new List<(long, double)> { (500_000L, 42.0) };

            double total = CalculateSegmentedKwh(breakpoints, _ => pts);
            Assert.Equal(0.0, total);
        }

        [Fact]
        public void ReplacementAtPeriodBoundary_DoesNotDoublCount()
        {
            // Replacement is exactly at periodStart — GetMeterReplacementsInRange uses
            // strict > and < so it is excluded; only a single segment remains.
            // This is consistent: the boundary replacement would appear in the previous month.
            long periodStart = 0L;
            long periodEnd   = 1_000_000L;
            // No replacement inside (0, 1_000_000) open interval
            var breakpoints = new List<long> { periodStart, periodEnd };
            var pts = InfluxRange(500, 600);

            double total = CalculateSegmentedKwh(breakpoints, _ => pts);
            Assert.Equal(100.0, total, precision: 2);
        }
    }

    // ── Transaction Tests ─────────────────────────────────────────────────────

    public class TransactionTests : IDisposable
    {
        private readonly BillingStoreFixture _fx = new();

        [Fact]
        public void RecordAndGetTransactions_RoundTrips()
        {
            _fx.Store.RecordTransaction(1, "CP-001", 1, "04A1B2C3", 45.7);
            var list = _fx.Store.GetTransactions();
            Assert.Single(list);
            Assert.Equal(1, list[0].Id);
            Assert.Equal("CP-001", list[0].ChargepointId);
            Assert.Equal(1, list[0].ConnectorId);
            Assert.Equal("04A1B2C3", list[0].IdTag);
            Assert.Equal(45.7, list[0].Kwh);
        }

        [Fact]
        public void RecordTransaction_Replace_UpdatesExistingId()
        {
            _fx.Store.RecordTransaction(1, "CP-001", 1, "TAG", 10.0);
            _fx.Store.RecordTransaction(1, "CP-001", 1, "TAG", 25.0); // same id → replace
            var list = _fx.Store.GetTransactions();
            Assert.Single(list);
            Assert.Equal(25.0, list[0].Kwh);
        }

        [Fact]
        public void GetTransactions_ReturnedNewestFirst()
        {
            _fx.Store.RecordTransaction(1, "CP-A", 1, "TAG1", 10.0);
            _fx.Store.RecordTransaction(2, "CP-B", 1, "TAG2", 20.0);
            var list = _fx.Store.GetTransactions();
            // Descending order by timestamp → transaction 2 was recorded later
            Assert.Equal(2, list[0].Id);
        }

        public void Dispose() => _fx.Dispose();
    }

    // ── Curtailment Target Tests ──────────────────────────────────────────────

    public class CurtailmentTargetTests : IDisposable
    {
        private readonly BillingStoreFixture _fx = new();

        [Fact]
        public void AddAndGetCurtailmentTargets_RoundTrips()
        {
            _fx.Store.AddCurtailmentTarget("power_kw", 100.0, 150.0, 200.0);
            var list = _fx.Store.GetCurtailmentTargets();
            Assert.Single(list);
            Assert.Equal("power_kw", list[0].TelemetryKey);
            Assert.Equal(100.0, list[0].NormalValue);
            Assert.Equal(150.0, list[0].WarningValue);
            Assert.Equal(200.0, list[0].CriticalValue);
        }

        [Fact]
        public void AddCurtailmentTarget_Upserts_ExistingKey()
        {
            _fx.Store.AddCurtailmentTarget("power_kw", 100.0, 150.0, 200.0);
            _fx.Store.AddCurtailmentTarget("power_kw", 90.0, 140.0, 190.0);
            var list = _fx.Store.GetCurtailmentTargets();
            Assert.Single(list);
            Assert.Equal(90.0, list[0].NormalValue);
        }

        [Fact]
        public void DeleteCurtailmentTarget_RemovesEntry()
        {
            _fx.Store.AddCurtailmentTarget("power_kw", 100.0, 150.0, 200.0);
            _fx.Store.DeleteCurtailmentTarget("power_kw");
            Assert.Empty(_fx.Store.GetCurtailmentTargets());
        }

        public void Dispose() => _fx.Dispose();
    }
}
