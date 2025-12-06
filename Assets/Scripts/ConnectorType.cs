using UnityEngine;

public enum ConnectorType
{
    Room,      // general room-to-room alignment
    Door,      // door-to-door alignment
    Window,    // optional future use
    Plumbing,  // optional future use
    Custom     // anything else
}

[DisallowMultipleComponent]
public class InvisibleConnector : MonoBehaviour
{
    [Tooltip("All connectors with the same ID will try to snap together.")]
    public string connectorId = "00";

    [Tooltip("Behavior/grouping hint for the auto-aligner.")]
    public ConnectorType connectorType = ConnectorType.Room;

    [Tooltip("If true, this room is treated as the anchor and will NOT move. Other rooms with the same ID move to match this connector.")]
    public bool isAnchor = false;

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = isAnchor ? Color.green : Color.yellow;
        Gizmos.DrawSphere(transform.position, 0.1f);

        if (!string.IsNullOrEmpty(connectorId))
        {
            UnityEditor.Handles.Label(transform.position + Vector3.up * 0.2f, connectorId);
        }
    }
#endif
}
