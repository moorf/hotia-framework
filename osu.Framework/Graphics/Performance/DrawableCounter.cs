// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.
//
// Copyright (c) moorf. Modified 2026.
// Modifications released under the GNU General Public License v3.0.
// See the LICENCE.GPL3 file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;

namespace osu.Framework.Graphics.Performance
{
    internal class DrawableCounter
    {

        private static int countDrawables(Drawable d)
        {
            int count = 1;

            if (d is CompositeDrawable c)
            {
                foreach (var child in c.AliveInternalChildren)
                    count += countDrawables(child);
            }

            return count;
        }
        private static Dictionary<string, (int count, int maxDepth)> typeStats = new();
        private static int nextNodeId = 0;

        public class TreeNodeInfo
        {
            public int Id { get; set; }
            public required string Type { get; set; }
            public int Depth { get; set; }
        }

        public static List<TreeNodeInfo> TreeNodes { get; } = new();
        public class TreeEdge
        {
            public int ParentId { get; set; }
            public int ChildId { get; set; }
        }
        private static readonly JsonSerializerOptions options = new() { WriteIndented = true };
        public static List<TreeEdge> TreeEdges { get; } = new();
        public static void RecordTypeAndDepth(Drawable d, int depth = 0, int? parentId = null)
        {
            if (d == null)
                return;

            string name = d.GetType().Name;

            if (!typeStats.TryGetValue(name, out var stats))
            {
                typeStats[name] = (1, depth);
            }
            else
            {
                typeStats[name] = (
                    stats.count + 1,
                    Math.Max(stats.maxDepth, depth)
                );
            }

            int currentId = nextNodeId++;

            TreeNodes.Add(new TreeNodeInfo
            {
                Id = currentId,
                Type = name,
                Depth = depth
            });

            if (parentId.HasValue)
                TreeEdges.Add(new TreeEdge
                {
                    ParentId = parentId.Value,
                    ChildId = currentId
                });

            if (d is CompositeDrawable composite)
            {
                foreach (var child in composite.AliveInternalChildren)
                    RecordTypeAndDepth(child, depth + 1, currentId);
            }
        }

        public static void LogDrawables(Container Root)
        {
            int total = countDrawables(Root);
            Logger.Log($"Total Root children: {total}");

            typeStats = new();
            TreeNodes.Clear();
            TreeEdges.Clear();
            RecordTypeAndDepth(Root);
            var export = new
            {
                Nodes = TreeNodes,
                Edges = TreeEdges
            };

            File.WriteAllText(
                $"{Stopwatch.GetTimestamp()}.json",
                JsonSerializer.Serialize(export, options)
            );
            foreach (var x in typeStats)
            {
                Logger.Log($"{x.Key}: c: {x.Value.count} d: {x.Value.maxDepth}");
            }
        }
    }
}
