// Services/AssetTreeService.cs
// GetAssetTrees, PopulateTree, MergeDtoIntoTree, ResolvePathSumKeys.

using System;
using System.Collections.Generic;
using System.Linq;
using Pulswerk.Core;

namespace Pulswerk.Dashboard
{
    public partial class DashboardDataService
    {
        // ── Public tree API ───────────────────────────────────────────────────

        public List<AssetNodeDto> GetAssetTrees(bool includeLiveValues = true)
        {
            var root = new AssetNodeDto { Name = "Root", Type = "Root" };

            foreach (var device in Config.Devices)
            {
                AssetNodeDto? deviceTree = null;

                if (device.DeviceType == "virtual")
                {
                    deviceTree = new AssetNodeDto
                    {
                        Id = device.Id,
                        Name = device.Name,
                        Type = "Device",
                        IsView = true
                    };
                }
                else if (Drivers.TryGetValue(device.Name, out var driver))
                {
                    deviceTree = driver.GetAssetHierarchy(device);
                }

                if (deviceTree == null) continue;

                // Inject virtual / formula points if configured
                if (device.Telemetries != null)
                {
                    foreach (var dp in device.Telemetries)
                    {
                        var tDto = new TelemetryDto
                        {
                            Id = $"{device.Id}_{dp.Id}",
                            Key = $"{device.Id}_{dp.Id}",
                            Name = dp.Name,
                            FullName = $"{device.Name} {dp.Name}",
                            Units = dp.Units ?? "",
                            Type = "Calculated",
                            Description = $"Formula: {dp.Formula}",
                            LastUpdate = "Live",
                            Value = includeLiveValues
                                ? GetLiveValueForFormula(dp.Formula, dp.Units, device)
                                : "---"
                        };

                        if (dp.Path != null && dp.Path.Count > 0)
                        {
                            tDto.ParentPath = dp.Path
                                .Take(dp.Path.Count - 1)
                                .Select(seg => new PathSegmentDto { Id = AssetNodeDto.PathSegmentId(seg), Name = seg })
                                .ToList();
                            tDto.ParentId = AssetNodeDto.PathSegmentId(dp.Path.Last());

                            var folderNode = new AssetNodeDto
                            {
                                Id = AssetNodeDto.PathSegmentId(dp.Path.Last()),
                                Name = dp.Path.Last(),
                                Type = "Folder",
                                IsView = true
                            };
                            folderNode.Telemetries.Add(tDto);
                            MergeDtoIntoTree(root, folderNode, dp.Path.Take(dp.Path.Count - 1).ToList());
                        }
                        else
                        {
                            if (device.Path != null && device.Path.Count > 0)
                            {
                                tDto.ParentPath = device.Path
                                    .Select(seg => new PathSegmentDto { Id = AssetNodeDto.PathSegmentId(seg), Name = seg })
                                    .ToList();
                                tDto.ParentId = AssetNodeDto.PathSegmentId(device.Path.Last());
                            }
                            deviceTree.Telemetries.Add(tDto);
                        }
                    }
                }

                // Populate live values for all points in the tree
                PopulateTree(deviceTree);

                // Merge into the global hierarchy
                if (device.Path != null && device.Path.Count > 0)
                    MergeDtoIntoTree(root, deviceTree, device.Path);
                else
                    root.Children.Add(deviceTree);
            }

            return root.Children;
        }

        // ── Private tree helpers ──────────────────────────────────────────────

        private void PopulateTree(AssetNodeDto node)
        {
            foreach (var p in node.Telemetries)
            {
                if (p.Type != "Calculated")
                {
                    p.Value = GetLatestValue(p.Key);
                    p.LastUpdate = FormatLastUpdate(p.Key);
                }
            }

            foreach (var child in node.Children)
                PopulateTree(child);
        }

        private void MergeDtoIntoTree(AssetNodeDto parent, AssetNodeDto node, List<string> path)
        {
            if (path == null || path.Count == 0)
            {
                var existing = parent.Children.FirstOrDefault(c => c.Name == node.Name);
                if (existing != null)
                {
                    existing.Telemetries.AddRange(node.Telemetries);
                    foreach (var child in node.Children)
                        MergeDtoIntoTree(existing, child, new List<string>());
                }
                else
                {
                    parent.Children.Add(node);
                }
                return;
            }

            string segment = path[0];
            var nextNode = parent.Children.FirstOrDefault(c => c.Name == segment);
            if (nextNode == null)
            {
                nextNode = new AssetNodeDto
                {
                    Id = AssetNodeDto.PathSegmentId(segment),
                    Name = segment,
                    Type = "Folder",
                    IsView = true
                };
                parent.Children.Add(nextNode);
            }

            var remainingPath = path.Skip(1).ToList();
            if (remainingPath.Count == 0)
            {
                if (node.Name == segment)
                {
                    nextNode.Telemetries.AddRange(node.Telemetries);
                    foreach (var child in node.Children)
                        MergeDtoIntoTree(nextNode, child, new List<string>());
                }
                else
                {
                    MergeDtoIntoTree(nextNode, node, new List<string>());
                }
            }
            else
            {
                MergeDtoIntoTree(nextNode, node, remainingPath);
            }
        }

        // ── PathSum key resolver (shared by ConsumptionService + TelemetryService) ──

        private List<string> ResolvePathSumKeys(string pathSumExpr, List<AvailableTelemetryDto>? allKeys = null)
        {
            var match = System.Text.RegularExpressions.Regex.Match(pathSumExpr,
                @"pathsum\s*\(\s*['""](.+?)['""]\s*,\s*['""](.+?)['""]\s*\)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (!match.Success) return new List<string>();

            var fullPathExpr = match.Groups[1].Value.Replace("/", " › ");
            var unitPattern = match.Groups[2].Value.ToLowerInvariant();

            string pathPattern = fullPathExpr;
            string? keySuffixPattern = null;

            int lastIdx = fullPathExpr.LastIndexOf(" › ");
            if (lastIdx >= 0)
            {
                string tail = fullPathExpr.Substring(lastIdx + 3);
                if (tail.Contains("*"))
                {
                    pathPattern = fullPathExpr.Substring(0, lastIdx);
                    keySuffixPattern = tail;
                }
            }
            else if (fullPathExpr.Contains("*"))
            {
                pathPattern = "*";
                keySuffixPattern = fullPathExpr;
            }

            string ToRegex(string glob) =>
                "^" + System.Text.RegularExpressions.Regex.Escape(glob).Replace("\\*", ".*") + "$";

            bool IsMatch(string pattern, string value)
            {
                if (pattern == "*" || pattern == "") return true;
                if (!pattern.Contains("*"))
                    return (value ?? "").Contains(pattern, StringComparison.OrdinalIgnoreCase);
                return System.Text.RegularExpressions.Regex.IsMatch(
                    value ?? "", ToRegex(pattern),
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }

            var resolved = new List<string>();
            var keysList = allKeys ?? GetAvailableTelemetries();
            foreach (var ak in keysList)
            {
                if (IsMatch(pathPattern, ak.Path ?? ""))
                {
                    bool keyMatch = keySuffixPattern == null || IsMatch(keySuffixPattern, ak.Key ?? "");
                    bool unitMatch = IsMatch(unitPattern, ak.Units ?? "") || IsMatch(unitPattern, ak.Key ?? "");
                    if (keyMatch && unitMatch)
                        resolved.Add(ak.Key ?? "");
                }
            }
            return resolved;
        }
    }
}
