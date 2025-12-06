using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Post-process step: finds InvisibleConnector objects and moves room roots
/// so connectors with the same ID coincide in world space.
/// </summary>
public static class RoomAutoAligner
{
    public static void AlignRooms(Dictionary<string, GameObject> roomLookup, float wallThickness = 0.1f)
    {
        if (roomLookup == null || roomLookup.Count == 0)
            return;

        // All room root transforms (hall, bedroom_1, etc.)
        HashSet<Transform> roomRoots = new HashSet<Transform>(
            roomLookup.Values.Where(go => go != null).Select(go => go.transform)
        );

        InvisibleConnector[] allConnectors = GameObject.FindObjectsOfType<InvisibleConnector>(true);
        if (allConnectors == null || allConnectors.Length == 0)
            return;

        // Group connectors by ID
        Dictionary<string, List<InvisibleConnector>> groups = new Dictionary<string, List<InvisibleConnector>>();

        foreach (var c in allConnectors)
        {
            if (c == null || string.IsNullOrEmpty(c.connectorId))
                continue;

            if (!groups.TryGetValue(c.connectorId, out var list))
            {
                list = new List<InvisibleConnector>();
                groups[c.connectorId] = list;
            }
            list.Add(c);
        }

        // Iterate a few times to settle multi-connector constraints
        const int maxIterations = 4;
        const float moveEpsilon = 1e-4f;

        for (int iter = 0; iter < maxIterations; iter++)
        {
            bool anyMoved = false;

            foreach (var kvp in groups)
            {
                List<InvisibleConnector> list = kvp.Value;
                if (list.Count < 2)
                    continue; // Need at least 2 to align

                // Pick anchor: explicit isAnchor, otherwise deterministic by InstanceID
                InvisibleConnector anchor = list.FirstOrDefault(c => c.isAnchor) ??
                                            list.OrderBy(c => c.transform.GetInstanceID()).First();

                Transform anchorRoomRoot = FindRoomRoot(anchor.transform, roomRoots);
                if (anchorRoomRoot == null)
                    continue;

                Vector3 anchorPos = anchor.transform.position;
                Vector3 anchorForward = anchor.transform.forward;

                foreach (var c in list)
                {
                    if (c == anchor)
                        continue;

                    Transform roomRoot = FindRoomRoot(c.transform, roomRoots);
                    if (roomRoot == null || roomRoot == anchorRoomRoot)
                        continue;

                    // Rotate/translate room so this connector faces and snaps to the anchor
                    bool moved = AlignRoomToAnchor(anchorPos, anchorForward, c, roomRoot, wallThickness, moveEpsilon);
                    anyMoved |= moved;
                }
            }

            if (!anyMoved)
                break;
        }
    }

    private static Transform FindRoomRoot(Transform t, HashSet<Transform> roomRoots)
    {
        while (t != null && !roomRoots.Contains(t))
        {
            t = t.parent;
        }
        return t;
    }

    /// <summary>
    /// Rotate/translate target room so its connector forward looks at the anchor connector,
    /// then snap with a small pull-back to avoid wall overlap.
    /// </summary>
    private static bool AlignRoomToAnchor(
        Vector3 anchorPos,
        Vector3 anchorForward,
        InvisibleConnector targetConnector,
        Transform targetRoomRoot,
        float wallThickness,
        float moveEpsilon)
    {
        // 1) Forward from connector (defined when created; outward normal of wall)
        Vector3 targetForward = targetConnector.transform.forward;
        Vector3 targetForwardXZ = new Vector3(targetForward.x, 0f, targetForward.z);
        if (targetForwardXZ.sqrMagnitude < 1e-6f)
            targetForwardXZ = Vector3.forward;
        targetForwardXZ.Normalize();

        // 2) Use anchor forward to align opposing faces
        Vector3 anchorForwardXZ = new Vector3(anchorForward.x, 0f, anchorForward.z);
        if (anchorForwardXZ.sqrMagnitude < 1e-6f)
            anchorForwardXZ = Vector3.forward;
        anchorForwardXZ.Normalize();

        // 3) Yaw so target forward faces opposite of anchor forward (outward vs inward)
        float angle = Vector3.SignedAngle(targetForwardXZ, -anchorForwardXZ, Vector3.up);
        Quaternion toFacing = Quaternion.AngleAxis(angle, Vector3.up);
        Vector3 pivot = targetConnector.transform.position;

        Vector3 newPos = pivot + toFacing * (targetRoomRoot.position - pivot);
        Quaternion newRot = toFacing * targetRoomRoot.rotation;

        // 4) Snap with small separation to avoid wall overlap
        Vector3 desiredConnectorPos = anchorPos - anchorForwardXZ * (wallThickness * 0.5f);
        Vector3 delta = desiredConnectorPos - targetConnector.transform.position;

        newPos += delta;

        bool moved = (newPos - targetRoomRoot.position).sqrMagnitude > moveEpsilon ||
                     Quaternion.Angle(newRot, targetRoomRoot.rotation) > 0.01f;

        if (moved)
        {
            targetRoomRoot.position = newPos;
            targetRoomRoot.rotation = newRot;
        }

        return moved;
    }
}
