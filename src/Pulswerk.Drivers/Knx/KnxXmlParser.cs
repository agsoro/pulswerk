using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Pulswerk.Core;

namespace Pulswerk.Drivers.Knx
{
    public record ParsedKnxPoint(
        string Address,
        string Name,
        string Description,
        string Dpt,
        List<string> Path
    );

    public static class KnxXmlParser
    {
        public static List<ParsedKnxPoint> Parse(string filePath)
        {
            var points = new List<ParsedKnxPoint>();

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                Log.Warning($"[KNX XML] File not found or empty path: {filePath}");
                return points;
            }

            try
            {
                var doc = XDocument.Load(filePath);
                var root = doc.Root;
                if (root == null)
                    return points;

                // Traverse the tree namespace-agnostically by checking LocalName
                var currentPath = new List<string>();
                ParseElement(root, currentPath, points);
            }
            catch (Exception ex)
            {
                Log.Error($"[KNX XML] Failed to parse ETS XML export from '{filePath}': {ex.Message}");
            }

            return points;
        }

        private static void ParseElement(XElement element, List<string> currentPath, List<ParsedKnxPoint> points)
        {
            string localName = element.Name.LocalName;

            if (localName == "GroupRange")
            {
                string name = element.Attribute("Name")?.Value?.Trim() ?? "Unnamed Group";
                var newPath = new List<string>(currentPath) { name };

                foreach (var child in element.Elements())
                {
                    ParseElement(child, newPath, points);
                }
            }
            else if (localName == "GroupAddress")
            {
                string address = element.Attribute("Address")?.Value?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(address))
                    return; // Skip if no address

                string name = element.Attribute("Name")?.Value?.Trim() ?? $"KNX Point {address}";
                if (string.IsNullOrWhiteSpace(name) || IsFreePlaceholder(name))
                    return; // Skip unused/free entries exported by ETS

                string description = element.Attribute("Description")?.Value?.Trim() ?? "";
                
                // Read either DPT, DPTs or DatapointType (ETS ga-export uses DPTs plural)
                string dptAttr = (element.Attribute("DPT")?.Value
                              ?? element.Attribute("DPTs")?.Value
                              ?? element.Attribute("DatapointType")?.Value
                              ?? "").Trim();

                string normalizedDpt = KnxDpt.NormalizeDpt(dptAttr);

                points.Add(new ParsedKnxPoint(
                    Address: address,
                    Name: name,
                    Description: description,
                    Dpt: normalizedDpt,
                    Path: currentPath
                ));
            }
            else
            {
                // For root (GroupAddress-Export) or any other wrapper elements, just process children without updating path
                foreach (var child in element.Elements())
                {
                    ParseElement(child, currentPath, points);
                }
            }
        }

        private static bool IsFreePlaceholder(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return true;

            var trimmed = name.Trim().Trim('-', '_').Trim();
            return string.IsNullOrWhiteSpace(trimmed)
                || trimmed.Equals("---frei---", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("frei", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("---free---", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("free", StringComparison.OrdinalIgnoreCase);
        }
    }
}
