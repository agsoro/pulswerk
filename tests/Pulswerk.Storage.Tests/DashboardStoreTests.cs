using System;
using System.Collections.Generic;
using Pulswerk.Core;
using Pulswerk.Storage;
using Xunit;

namespace Connector.Tests
{
    /// <summary>
    /// Tests for the DashboardStore (JSON file-based dashboard persistence).
    /// </summary>
    public class DashboardStoreTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly DashboardStore _store;

        public DashboardStoreTests()
        {
            _tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dash_test_{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(_tempDir);
            _store = new DashboardStore(_tempDir);
        }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(_tempDir, true); } catch { }
        }

        [Fact]
        public void GetAll_EmptyInitially()
        {
            var all = _store.GetAll();
            Assert.Empty(all);
        }

        [Fact]
        public void Create_ReturnsDashboard()
        {
            var dash = _store.Create("Test Dashboard", "A description");

            Assert.NotNull(dash);
            Assert.Equal("Test Dashboard", dash.Name);
            Assert.Equal("A description", dash.Description);
            Assert.NotNull(dash.Id);
            Assert.NotEmpty(dash.Id);
        }

        [Fact]
        public void Create_PersistsToDisk()
        {
            _store.Create("Dashboard 1", "Desc 1");

            // Create a new store instance pointing at the same dir
            var store2 = new DashboardStore(_tempDir);
            var all = store2.GetAll();

            Assert.Single(all);
            Assert.Equal("Dashboard 1", all[0].Name);
        }

        [Fact]
        public void GetById_ExistingDashboard()
        {
            var created = _store.Create("My Dash", "");

            var fetched = _store.GetById(created.Id);

            Assert.NotNull(fetched);
            Assert.Equal(created.Id, fetched!.Id);
            Assert.Equal("My Dash", fetched.Name);
        }

        [Fact]
        public void GetById_NonExistent_ReturnsNull()
        {
            var result = _store.GetById("nonexistent");
            Assert.Null(result);
        }

        [Fact]
        public void Save_UpdatesDashboard()
        {
            var dash = _store.Create("Original", "");
            dash.Name = "Updated";

            bool ok = _store.Save(dash);

            Assert.True(ok);
            var fetched = _store.GetById(dash.Id);
            Assert.Equal("Updated", fetched!.Name);
        }

        [Fact]
        public void Delete_RemovesDashboard()
        {
            var dash = _store.Create("To Delete", "");

            bool ok = _store.Delete(dash.Id);

            Assert.True(ok);
            Assert.Empty(_store.GetAll());
        }

        [Fact]
        public void Delete_NonExistent_ReturnsFalse()
        {
            bool ok = _store.Delete("nonexistent");
            Assert.False(ok);
        }

        [Fact]
        public void MultipleCreate_AllPersisted()
        {
            _store.Create("Dash 1", "");
            _store.Create("Dash 2", "");
            _store.Create("Dash 3", "");

            Assert.Equal(3, _store.GetAll().Count);
        }
    }
}
