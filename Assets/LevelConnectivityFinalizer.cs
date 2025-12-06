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
        /// Attempts to automatically resolve small gaps/overlaps by nudging rooms along X/Z.
        /// Only applies corrections whose magnitude is less than or equal to <paramref name="maxSnapDistance"/>.
        /// </summary>
        /// <param name="rooms">Lookup of room ids to their generated GameObjects.</param>
        /// <param name="allowedGap">Horizontal distance considered a gap.</param>
        /// <param name="maxSnapDistance">Maximum distance to move a room when resolving an issue.</param>
        /// <returns>Applied adjustments keyed by room id.</returns>
        public static Dictionary<string, TransformAdjustment> ResolveMinorIssues(
            IDictionary<string, GameObject> rooms,
            float allowedGap = 0.02f,
            float maxSnapDistance = 0.25f)
        {
            var adjustments = new Dictionary<string, TransformAdjustment>();
            if (rooms == null || rooms.Count == 0)
                return adjustments;

            var boundsByRoom = BuildBoundsList(rooms, out var missingGeometry);
            foreach (var missing in missingGeometry)
            {
                // Missing geometry can't be auto-fixed; still reported separately.
                Debug.LogWarning($"Room '{missing}' has no renderers; unable to auto-fix.");
            }

            for (int i = 0; i < boundsByRoom.Count; i++)
            {
                for (int j = i + 1; j < boundsByRoom.Count; j++)
                {
                    var a = boundsByRoom[i];
                    var b = boundsByRoom[j];

                    if (a.bounds.Intersects(b.bounds))
                    {
                        Vector3 correction = ComputeOverlapCorrection(a.bounds, b.bounds);
                        if (correction == Vector3.zero || correction.magnitude > maxSnapDistance)
                            continue;

                        ApplyOffset(ref adjustments, b.id, rooms[b.id].transform.position - correction);
                        Debug.Log($"Auto-resolve: nudged '{b.id}' by {-correction} to remove overlap with '{a.id}'.");
                        continue;
                    }

                    float gap = CalculateHorizontalGap(a.bounds, b.bounds);
                    if (gap <= allowedGap || gap > maxSnapDistance)
                        continue;

                    Vector3 gapCorrection = ComputeGapCorrection(a.bounds, b.bounds);
                    if (gapCorrection == Vector3.zero)
                        continue;

                    ApplyOffset(ref adjustments, b.id, rooms[b.id].transform.position + gapCorrection);
                    Debug.Log($"Auto-resolve: moved '{b.id}' by {gapCorrection} to close gap with '{a.id}'.");
                }
            }

            ApplyAdjustments(adjustments, rooms);
            return adjustments;
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
            var boundsByRoom = BuildBoundsList(rooms, out var missingGeometry);
            foreach (string missing in missingGeometry)
            {
                issues.Add(new ConnectivityIssue
                {
                    RoomA = missing,
                    RoomB = string.Empty,
                    Type = ConnectivityIssueType.MissingGeometry,
                    Magnitude = 0f,
                    BoundsA = default,
                    BoundsB = default
                });
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

        private static List<(string id, Bounds bounds)> BuildBoundsList(
            IDictionary<string, GameObject> rooms,
            out List<string> missingGeometry)
        {
            missingGeometry = new List<string>();
            var boundsByRoom = new List<(string id, Bounds bounds)>();

            foreach (var kvp in rooms)
            {
                string roomId = string.IsNullOrEmpty(kvp.Key) ? kvp.Value?.name ?? "<unnamed>" : kvp.Key;
                if (kvp.Value == null)
                    continue;

                if (!TryGetCombinedBounds(kvp.Value, out Bounds bounds))
                {
                    missingGeometry.Add(roomId);
                    continue;
                }

                boundsByRoom.Add((roomId, bounds));
            }

            return boundsByRoom;
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

        private static Vector3 ComputeGapCorrection(Bounds a, Bounds b)
        {
            float gapX = IntervalGap(a.min.x, a.max.x, b.min.x, b.max.x);
            float gapZ = IntervalGap(a.min.z, a.max.z, b.min.z, b.max.z);

            // Prioritize the axis with the larger gap.
            if (gapX > gapZ && gapX > 0f)
            {
                return (a.center.x < b.center.x) ? new Vector3(-gapX, 0f, 0f) : new Vector3(gapX, 0f, 0f);
            }

            if (gapZ > 0f)
            {
                return (a.center.z < b.center.z) ? new Vector3(0f, 0f, -gapZ) : new Vector3(0f, 0f, gapZ);
            }

            return Vector3.zero;
        }

        private static Vector3 ComputeOverlapCorrection(Bounds a, Bounds b)
        {
            float overlapX = Math.Max(0f, Math.Min(a.max.x, b.max.x) - Math.Max(a.min.x, b.min.x));
            float overlapZ = Math.Max(0f, Math.Min(a.max.z, b.max.z) - Math.Max(a.min.z, b.min.z));
            float overlapY = Math.Max(0f, Math.Min(a.max.y, b.max.y) - Math.Max(a.min.y, b.min.y));

            // If the only overlap is vertical, ignore it to avoid separating floors stacked above each other.
            if (overlapX <= 0f && overlapZ <= 0f && overlapY > 0f)
                return Vector3.zero;

            // Nudge along the smallest overlapping horizontal axis.
            if (overlapX > 0f && overlapX <= overlapZ)
            {
                return (a.center.x < b.center.x) ? new Vector3(overlapX, 0f, 0f) : new Vector3(-overlapX, 0f, 0f);
            }

            if (overlapZ > 0f)
            {
                return (a.center.z < b.center.z) ? new Vector3(0f, 0f, overlapZ) : new Vector3(0f, 0f, -overlapZ);
            }

            return Vector3.zero;
        }

        private static void ApplyOffset(
            ref Dictionary<string, TransformAdjustment> adjustments,
            string roomId,
            Vector3 newPosition)
        {
            if (!adjustments.TryGetValue(roomId, out var adjustment))
            {
                adjustment = new TransformAdjustment();
            }

            adjustment.Position = newPosition;
            adjustments[roomId] = adjustment;
        }
    }
}
