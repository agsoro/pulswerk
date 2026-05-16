using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Pulswerk.Core;

namespace Pulswerk.Dashboard
{
    /// <summary>
    /// Centralized authorization helper for identity-based access control.
    /// Reads Remote-* headers (provided by a reverse proxy or identity provider)
    /// and checks group membership against the configured <see cref="RightsConfig"/>.
    /// </summary>
    public static class DashboardAuth
    {
        /// <summary>
        /// Returns the authenticated user's groups from the Authelia
        /// Remote-Groups header (already sanitized by the anti-spoofing middleware).
        /// </summary>
        public static List<string> GetGroups(HttpContext ctx, AuthConfig? auth = null)
        {
            var raw = ctx.Request.Headers["Remote-Groups"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(raw))
                return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            
            return auth?.DefaultGroups ?? new List<string>();
        }

        /// <summary>
        /// Returns the authenticated username, or null if not authenticated.
        /// </summary>
        public static string? GetUser(HttpContext ctx, AuthConfig? auth = null)
        {
            var user = ctx.Request.Headers["Remote-User"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(user)) return user;
            return auth?.DefaultUser;
        }

        /// <summary>
        /// Checks whether the current request is allowed to edit data point values.
        /// </summary>
        public static bool CanWriteValue(HttpContext ctx, ServerConfig? cfg)
        {
            return HasGroupPermission(ctx, cfg, r => r.AllowAssetValueEdit);
        }

        /// <summary>
        /// Checks whether the current request is allowed to acknowledge alarms.
        /// Falls back to AllowAssetValueEdit.
        /// </summary>
        public static bool CanAckAlarm(HttpContext ctx, ServerConfig? cfg)
        {
            return HasGroupPermission(ctx, cfg, r => r.AllowAlarmAck ?? r.AllowAssetValueEdit);
        }

        /// <summary>
        /// Checks whether the current request is allowed to edit dashboards
        /// (create, save, delete).  Falls back to AllowAssetValueEdit if AllowDashboardEdit
        /// is not configured separately.
        /// </summary>
        public static bool CanEditDashboard(HttpContext ctx, ServerConfig? cfg)
        {
            return HasGroupPermission(ctx, cfg, r => r.AllowDashboardEdit ?? r.AllowAssetValueEdit);
        }

        /// <summary>
        /// Checks whether the current request is allowed to pin/unpin favorites.
        /// Falls back to AllowAssetValueEdit if AllowFavoriteEdit is not configured separately.
        /// </summary>
        public static bool CanEditFavorites(HttpContext ctx, ServerConfig? cfg)
        {
            return HasGroupPermission(ctx, cfg, r => r.AllowFavoriteEdit ?? r.AllowAssetValueEdit);
        }

        private static bool HasGroupPermission(HttpContext ctx, ServerConfig? cfg,
            Func<RightsConfig, List<string>?> getAllowedGroups)
        {
            var rights = cfg?.Rights;
            // No rights config or disabled → everything allowed (backward compatible)
            if (rights == null || !rights.Enabled) return true;

            var allowedGroups = getAllowedGroups(rights);
            // No group list at all → deny (explicit: enabled but no groups configured)
            if (allowedGroups == null || allowedGroups.Count == 0) return false;

            var userGroups = GetGroups(ctx, cfg?.Auth);
            if (userGroups.Count == 0) return false; // not authenticated

            // Case-insensitive intersection check
            return userGroups.Any(ug =>
                allowedGroups.Any(ag => string.Equals(ug, ag, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
