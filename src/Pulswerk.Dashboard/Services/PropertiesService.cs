// Services/PropertiesService.cs
// GetPropertiesAsync — live property reads from field devices.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pulswerk.Core;

namespace Pulswerk.Dashboard
{
    public partial class DashboardDataService
    {
        public async Task<List<PropertyDto>> GetPropertiesAsync(string key)
        {
            var device = IdentifyDeviceFromTelemetryKey(key);
            if (device == null) return new List<PropertyDto>();

            if (Drivers.TryGetValue(device.Name, out var driver))
            {
                var conn = Config.Connections.FirstOrDefault(c => c.Id == device.ConnectionId);
                if (conn != null)
                    return await driver.GetExtendedPropertiesAsync(conn, device, key);
            }

            return new List<PropertyDto>();
        }
    }
}
