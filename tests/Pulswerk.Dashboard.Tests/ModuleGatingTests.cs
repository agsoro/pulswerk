using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Pulswerk.Core;
using Pulswerk.Dashboard;
using Pulswerk.Dashboard.Controllers;
using Pulswerk.Storage;
using Pulswerk.Billing;
using Xunit;

namespace Pulswerk.Dashboard.Tests
{
    public class ModuleGatingTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly AlarmStore _alarmStore;
        private readonly LogBuffer _logBuffer;
        private readonly FakeTelemetryStore _telemetryStore;

        public ModuleGatingTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"gating_test_{Guid.NewGuid():N}.db");
            _alarmStore = new AlarmStore(_dbPath);
            _logBuffer = new LogBuffer(10);
            _telemetryStore = new FakeTelemetryStore();
        }

        public void Dispose()
        {
            _alarmStore.Dispose();
            _telemetryStore.Dispose();
            try { File.Delete(_dbPath); } catch { }
        }

        private (ApiController controller, DashboardDataService service) CreateController(AppConfig config)
        {
            var dataService = new DashboardDataService(
                _logBuffer,
                config,
                _telemetryStore,
                _alarmStore,
                new ConcurrentDictionary<string, byte>(),
                new ConcurrentDictionary<string, DateTime>(),
                new Dictionary<string, IDeviceDriver>()
            );

            var tempDir = Path.Combine(Path.GetTempPath(), $"gating_store_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            var dashboardStore = new DashboardStore(tempDir);
            var billingStore = new BillingStore(Path.Combine(tempDir, "billing.db"));

            var controller = new ApiController(dataService, dashboardStore, billingStore);
            return (controller, dataService);
        }

        private ActionExecutingContext CreateActionContext(ApiController controller, string path, string? user = null, List<string>? groups = null)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Path = path;

            if (user != null)
            {
                httpContext.Request.Headers["Remote-User"] = user;
            }
            if (groups != null && groups.Count > 0)
            {
                httpContext.Request.Headers["Remote-Groups"] = string.Join(",", groups);
            }

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };

            var actionContext = new ActionContext(
                httpContext,
                new RouteData(),
                new ActionDescriptor()
            );

            return new ActionExecutingContext(
                actionContext,
                new List<IFilterMetadata>(),
                new Dictionary<string, object?>(),
                controller
            );
        }

        [Fact]
        public void DashboardAuth_ModulePermissions_FallbackDefaults()
        {
            var serverCfg = new ServerConfig(
                Port: 5000,
                Rights: new RightsConfig(
                    Enabled: true,
                    AllowAssetValueEdit: new List<string> { "operators" },
                    AllowConfigEdit: new List<string> { "admins" }
                )
            );

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["Remote-User"] = "operator-user";
            httpContext.Request.Headers["Remote-Groups"] = "operators";

            // When no specific module rights configured:
            // Alarms, Heartbeat, Dashboards, Assets, Telemetry should fall back to true (anyone allowed).
            Assert.True(DashboardAuth.CanAccessAlarms(httpContext, serverCfg));
            Assert.True(DashboardAuth.CanAccessHeartbeat(httpContext, serverCfg));
            Assert.True(DashboardAuth.CanAccessDashboards(httpContext, serverCfg));
            Assert.True(DashboardAuth.CanAccessAssets(httpContext, serverCfg));
            Assert.True(DashboardAuth.CanAccessTelemetry(httpContext, serverCfg));

            // Logs and Connections should fall back to CanEditConfig (admin-only)
            // Operator is not in admins, so should be blocked
            Assert.False(DashboardAuth.CanAccessLogs(httpContext, serverCfg));
            Assert.False(DashboardAuth.CanAccessConnections(httpContext, serverCfg));

            // Admin user should be allowed for all fallback-to-config modules
            var adminContext = new DefaultHttpContext();
            adminContext.Request.Headers["Remote-User"] = "admin-user";
            adminContext.Request.Headers["Remote-Groups"] = "admins";
            Assert.True(DashboardAuth.CanAccessLogs(adminContext, serverCfg));
            Assert.True(DashboardAuth.CanAccessConnections(adminContext, serverCfg));
        }

        [Fact]
        public void DashboardAuth_ModulePermissions_ExplicitRights()
        {
            var serverCfg = new ServerConfig(
                Port: 5000,
                Rights: new RightsConfig(
                    Enabled: true,
                    AllowAlarms: new List<string> { "alarm-viewers" },
                    AllowLogs: new List<string> { "log-viewers" }
                )
            );

            var userContext = new DefaultHttpContext();
            userContext.Request.Headers["Remote-User"] = "some-user";
            userContext.Request.Headers["Remote-Groups"] = "alarm-viewers";

            Assert.True(DashboardAuth.CanAccessAlarms(userContext, serverCfg));
            Assert.False(DashboardAuth.CanAccessLogs(userContext, serverCfg));
        }

        [Fact]
        public void ApiController_Gating_DisabledModule_ReturnsForbidden()
        {
            var config = new AppConfig(
                InfluxDb: null,
                Database: null,
                Polling: null,
                Connections: new(),
                Devices: new(),
                Server: new ServerConfig(
                    Port: 5000,
                    Rights: new RightsConfig(Enabled: false)
                ),
                Modules: new ModulesConfig(
                    Alarms: false, // Disabled
                    Logs: true
                )
            );

            var (controller, _) = CreateController(config);

            // Gate alarms endpoint
            var alarmsContext = CreateActionContext(controller, "/api/alarms");
            controller.OnActionExecuting(alarmsContext);
            Assert.NotNull(alarmsContext.Result);
            var objectResult = Assert.IsType<ObjectResult>(alarmsContext.Result);
            Assert.Equal(403, objectResult.StatusCode);

            // Gated logs endpoint (enabled, no rights restrict)
            var logsContext = CreateActionContext(controller, "/api/logs");
            controller.OnActionExecuting(logsContext);
            Assert.Null(logsContext.Result); // Allowed
        }

        [Fact]
        public void ApiController_Gating_UnauthorizedModule_ReturnsForbidden()
        {
            var config = new AppConfig(
                InfluxDb: null,
                Database: null,
                Polling: null,
                Connections: new(),
                Devices: new(),
                Server: new ServerConfig(
                    Port: 5000,
                    Rights: new RightsConfig(
                        Enabled: true,
                        AllowLogs: new List<string> { "admins" }
                    )
                ),
                Modules: new ModulesConfig(
                    Logs: true
                )
            );

            var (controller, _) = CreateController(config);

            // Non-admin trying to access logs
            var userContext = CreateActionContext(controller, "/api/logs", "user1", new List<string> { "operators" });
            controller.OnActionExecuting(userContext);
            Assert.NotNull(userContext.Result);
            var objectResult = Assert.IsType<ObjectResult>(userContext.Result);
            Assert.Equal(403, objectResult.StatusCode);

            // Admin accessing logs
            var adminContext = CreateActionContext(controller, "/api/logs", "admin1", new List<string> { "admins" });
            controller.OnActionExecuting(adminContext);
            Assert.Null(adminContext.Result); // Allowed
        }
    }
}
