using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Post-process step: finds InvisibleConnector objects and moves room roots
/// so connectors with the same ID coincide in world space.
/// </summary>
public static class RoomAutoAligner
{
    public static void AlignRooms(Dictionary<string, GameObject> roomLookup)
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

        foreach (var kvp in groups)
        {
            List<InvisibleConnector> list = kvp.Value;
            if (list.Count < 2)
                continue; // Need at least 2 to align

            // Pick anchor: explicit isAnchor, otherwise first
            InvisibleConnector anchor = list.FirstOrDefault(c => c.isAnchor) ?? list[0];
            Transform anchorRoomRoot = FindRoomRoot(anchor.transform, roomRoots);
            if (anchorRoomRoot == null)
                continue;

            Vector3 anchorPos = anchor.transform.position;

            foreach (var c in list)
            {
                if (c == anchor)
                    continue;

                Transform roomRoot = FindRoomRoot(c.transform, roomRoots);
                if (roomRoot == null || roomRoot == anchorRoomRoot)
                    continue;

                // Offset needed to bring this connector to anchor position
                Vector3 delta = anchorPos - c.transform.position;

                switch (c.connectorType)
                {
                    case ConnectorType.Room:
                    case ConnectorType.Door:
                    case ConnectorType.Custom:
                        // For now: simple translation. Later you can add rotation logic here.
                        roomRoot.position += delta;
                        break;

                    case ConnectorType.Window:
                    case ConnectorType.Plumbing:
                        // Reserved for future specialized behavior.
                        roomRoot.position += delta;
                        break;
                }
            }
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
}
