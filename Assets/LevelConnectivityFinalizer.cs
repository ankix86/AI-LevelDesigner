using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AILevelDesign
{
    /// <summary>
    /// Performs lightweight validation on generated rooms to detect overlaps and gaps
    /// between their bounding boxes. Also exposes helpers to apply manual adjustments
    /// after validation so problematic pieces can be nudged into place.
    /// </summary>
    public static class LevelConnectivityFinalizer
    {
        public enum ConnectivityIssueType
        {
            MissingGeometry,
            Overlap,
            Gap
        }

        public sealed class ConnectivityIssue
        {
            public string RoomA { get; set; }
            public string RoomB { get; set; }
            public ConnectivityIssueType Type { get; set; }
            public float Magnitude { get; set; }
            public Bounds BoundsA { get; set; }
            public Bounds BoundsB { get; set; }

            public override string ToString()
            {
                string pair = string.IsNullOrEmpty(RoomB) ? RoomA : $"{RoomA} <-> {RoomB}";
                string descriptor = Type == ConnectivityIssueType.Overlap
                    ? $"overlap depth {Magnitude:0.###}"
                    : Type == ConnectivityIssueType.Gap
                        ? $"gap {Magnitude:0.###}"
                        : "missing renderers";
                return $"{pair}: {descriptor}";
            }
        }

        public struct TransformAdjustment
        {
            public Vector3? Position;
            public Vector3? RotationEuler;
            public Vector3? Scale;
        }

        /// <summary>
        /// Validates the provided rooms and returns a list of gap/overlap issues.
        /// Rooms without any renderers will be reported with the MissingGeometry type.
        /// </summary>
        /// <param name="rooms">Lookup of room ids to their generated GameObjects.</param>
        /// <param name="allowedGap">Maximum tolerated horizontal separation before a gap is reported.</param>
        public static List<ConnectivityIssue> ValidateRooms(
            IDictionary<string, GameObject> rooms,
            float allowedGap = 0.02f)
        {
            var issues = new List<ConnectivityIssue>();
            var boundsByRoom = new List<(string id, Bounds bounds)>();

            foreach (var kvp in rooms)
            {
                string roomId = string.IsNullOrEmpty(kvp.Key) ? kvp.Value?.name ?? "<unnamed>" : kvp.Key;
                if (kvp.Value == null)
                    continue;

                if (!TryGetCombinedBounds(kvp.Value, out Bounds bounds))
                {
                    issues.Add(new ConnectivityIssue
                    {
                        RoomA = roomId,
                        RoomB = string.Empty,
                        Type = ConnectivityIssueType.MissingGeometry,
                        Magnitude = 0f,
                        BoundsA = default,
                        BoundsB = default
                    });
                    continue;
                }

                boundsByRoom.Add((roomId, bounds));
            }

            for (int i = 0; i < boundsByRoom.Count; i++)
            {
                for (int j = i + 1; j < boundsByRoom.Count; j++)
                {
                    var a = boundsByRoom[i];
                    var b = boundsByRoom[j];

                    if (a.bounds.Intersects(b.bounds))
                    {
                        float overlap = CalculateOverlapDepth(a.bounds, b.bounds);
                        issues.Add(new ConnectivityIssue
                        {
                            RoomA = a.id,
                            RoomB = b.id,
                            Type = ConnectivityIssueType.Overlap,
                            Magnitude = overlap,
                            BoundsA = a.bounds,
                            BoundsB = b.bounds
                        });
                        continue;
                    }

                    float horizontalGap = CalculateHorizontalGap(a.bounds, b.bounds);
                    if (horizontalGap > allowedGap && horizontalGap < float.PositiveInfinity)
                    {
                        issues.Add(new ConnectivityIssue
                        {
                            RoomA = a.id,
                            RoomB = b.id,
                            Type = ConnectivityIssueType.Gap,
                            Magnitude = horizontalGap,
                            BoundsA = a.bounds,
                            BoundsB = b.bounds
                        });
                    }
                }
            }

            return issues;
        }

        /// <summary>
        /// Logs a readable report for the given validation results. Helpful during editor use
        /// to quickly identify which rooms need manual adjustment.
        /// </summary>
        public static void LogReport(IEnumerable<ConnectivityIssue> issues)
        {
            var issueList = issues?.ToList() ?? new List<ConnectivityIssue>();
            if (issueList.Count == 0)
            {
                Debug.Log("Level connectivity: no overlaps or gaps detected between rooms.");
                return;
            }

            int overlaps = issueList.Count(i => i.Type == ConnectivityIssueType.Overlap);
            int gaps = issueList.Count(i => i.Type == ConnectivityIssueType.Gap);
            int missing = issueList.Count(i => i.Type == ConnectivityIssueType.MissingGeometry);

            Debug.LogWarning(
                $"Level connectivity: {overlaps} overlaps, {gaps} gaps, {missing} missing geometry issues detected.");

            foreach (var issue in issueList)
            {
                Debug.LogWarning($" - {issue}");
            }
        }

        /// <summary>
        /// Applies targeted transform adjustments to fix issues found during validation.
        /// Only the properties provided in each adjustment are changed.
        /// </summary>
        public static void ApplyAdjustments(
            IDictionary<string, TransformAdjustment> adjustments,
            IDictionary<string, GameObject> rooms)
        {
            if (adjustments == null || rooms == null)
                return;

            foreach (var kvp in adjustments)
            {
                if (!rooms.TryGetValue(kvp.Key, out GameObject room) || room == null)
                    continue;

                Transform t = room.transform;
                if (kvp.Value.Position.HasValue)
                    t.position = kvp.Value.Position.Value;
                if (kvp.Value.RotationEuler.HasValue)
                    t.rotation = Quaternion.Euler(kvp.Value.RotationEuler.Value);
                if (kvp.Value.Scale.HasValue)
                    t.localScale = kvp.Value.Scale.Value;
            }
        }

        private static bool TryGetCombinedBounds(GameObject root, out Bounds combined)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
            {
                combined = default;
                return false;
            }

            combined = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                combined.Encapsulate(renderers[i].bounds);
            }

            return true;
        }

        private static float CalculateOverlapDepth(Bounds a, Bounds b)
        {
            float dx = Math.Max(0f, Math.Min(a.max.x, b.max.x) - Math.Max(a.min.x, b.min.x));
            float dy = Math.Max(0f, Math.Min(a.max.y, b.max.y) - Math.Max(a.min.y, b.min.y));
            float dz = Math.Max(0f, Math.Min(a.max.z, b.max.z) - Math.Max(a.min.z, b.min.z));
            return Math.Min(dx, Math.Min(dy, dz));
        }

        private static float CalculateHorizontalGap(Bounds a, Bounds b)
        {
            float gapX = IntervalGap(a.min.x, a.max.x, b.min.x, b.max.x);
            float gapZ = IntervalGap(a.min.z, a.max.z, b.min.z, b.max.z);
            float gapY = IntervalGap(a.min.y, a.max.y, b.min.y, b.max.y);

            // If the rooms are vertically separated, we treat them as intentionally stacked.
            if (gapY > 0f)
                return float.PositiveInfinity;

            return Math.Max(gapX, gapZ);
        }

        private static float IntervalGap(float minA, float maxA, float minB, float maxB)
        {
            if (maxA < minB)
                return minB - maxA;
            if (maxB < minA)
                return minA - maxB;
            return 0f;
        }
    }
}
