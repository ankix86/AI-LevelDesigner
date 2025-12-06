using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;
using AILevelDesign;

public class AILevelGenerator : EditorWindow
{
    private string jsonFileName = "level.json";
    private float defaultRoomHeight = 3f;
    private float defaultStairHeight = 3f;
    private const float defaultWallThickness = 0.1f;
    private const float defaultSlabThickness = 0.1f;
    private bool autoDoorInference = true; // kept for compat (not heavily used now)
    private Vector2 scroll;

    [MenuItem("Tools/AI Level Generator")]
    public static void ShowWindow()
    {
        GetWindow<AILevelGenerator>("AI Level Generator");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("StreamingAssets JSON", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        jsonFileName = EditorGUILayout.TextField("File Name", jsonFileName);
        if (GUILayout.Button("Browse", GUILayout.Width(70)))
        {
            string picked = EditorUtility.OpenFilePanel("Select Level JSON", Application.streamingAssetsPath, "json");
            if (!string.IsNullOrEmpty(picked))
            {
                jsonFileName = Path.GetFileName(picked);
            }
            GUI.FocusControl(null);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Defaults", EditorStyles.boldLabel);
        defaultRoomHeight = EditorGUILayout.FloatField("Room Height (Y)", defaultRoomHeight);
        defaultStairHeight = EditorGUILayout.FloatField("Stair Height (Y)", defaultStairHeight);
        autoDoorInference = EditorGUILayout.Toggle("Auto Door World->Local (legacy)", autoDoorInference);

        EditorGUILayout.Space();
        if (GUILayout.Button("Generate Level", GUILayout.Height(32)))
        {
            GenerateFromJson();
        }

        EditorGUILayout.Space();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.HelpBox(
            $"JSON will be loaded from: {Path.Combine(Application.streamingAssetsPath, jsonFileName)}\n" +
            "Rooms use size[x,z] from JSON with default height applied.\n" +
            "Positions are [x, z, y] with y measured from floor for doors.\n" +
            "Doors use size [width, height, thickness].",
            MessageType.Info);
        EditorGUILayout.EndScrollView();
    }

    private void GenerateFromJson()
    {
        string fullPath = Path.Combine(Application.streamingAssetsPath, jsonFileName);
        if (!File.Exists(fullPath))
        {
            EditorUtility.DisplayDialog("AI Level Generator", $"File not found:\n{fullPath}", "OK");
            return;
        }

        try
        {
            string json = File.ReadAllText(fullPath);
            LevelData data = JsonConvert.DeserializeObject<LevelData>(json);
            if (data == null)
            {
                EditorUtility.DisplayDialog("AI Level Generator", "Failed to parse JSON.", "OK");
                return;
            }

            BuildLevel(data);
        }
        catch (Exception ex)
        {
            Debug.LogError($"AI Level Generator: {ex.Message}\n{ex.StackTrace}");
            EditorUtility.DisplayDialog("AI Level Generator", "Error parsing JSON, check console for details.", "OK");
        }
    }

    private void BuildLevel(LevelData data)
    {
        Dictionary<string, GameObject> roomLookup = new Dictionary<string, GameObject>();
        List<string> roomIssues = new List<string>();
        int roomIndex = 0;

        if (data.floors != null)
        {
            foreach (Floor floor in data.floors)
            {
                if (floor.rooms == null) continue;

                foreach (Room room in floor.rooms)
                {
                    string resolvedId = room.id;
                    if (string.IsNullOrEmpty(resolvedId))
                    {
                        resolvedId = $"Room_{roomIndex}";
                        Debug.LogWarning($"AI Level Generator: Room at index {roomIndex} is missing an id; using fallback '{resolvedId}'.");
                        roomIssues.Add($"Missing id -> {resolvedId}");
                    }
                    else if (roomLookup.ContainsKey(resolvedId))
                    {
                        string renamedId = $"{resolvedId}_{roomIndex}";
                        Debug.LogWarning($"AI Level Generator: Duplicate room id '{resolvedId}' detected; renaming to '{renamedId}'.");
                        roomIssues.Add($"Duplicate '{resolvedId}' renamed to '{renamedId}'");
                        resolvedId = renamedId;
                    }

                    Vector3 roomSize = ToSize(room.size, defaultRoomHeight);
                    Vector3 roomPos = SnapVector(ToRoomPosition(room.position));
                    GameObject roomRoot = new GameObject(resolvedId);
                    roomRoot.transform.position = roomPos;

                    bool hasCustom = (room.floor != null) || (room.roof != null) ||
                                     (room.walls != null && room.walls.Length > 0);

                    if (!hasCustom)
                    {
                        BuildAutoRoomWithOpenings(room, roomRoot, roomSize);
                        roomLookup[resolvedId] = roomRoot;
                    }
                    else
                    {
                        // Floor
                        if (room.floor != null)
                        {
                            Vector3 size = SanitizeSize(room.floor.size, defaultY: 0.1f, defaultX: roomSize.x, defaultZ: roomSize.z);
                            Vector3 pos = SnapVector(ToPosition(room.floor.position, new Vector3(0f, -roomSize.y * 0.5f + size.y * 0.5f, 0f)));
                            Quaternion rot = ToRotation(room.floor.rotation);
                            PBHelper.CreateElement(string.IsNullOrEmpty(room.floor.id) ? "Floor" : room.floor.id, pos, size, rot, roomRoot.transform);
                        }
                        // Roof
                        if (room.roof != null)
                        {
                            Vector3 size = SanitizeSize(room.roof.size, defaultY: 0.1f, defaultX: roomSize.x, defaultZ: roomSize.z);
                            Vector3 pos = SnapVector(ToPosition(room.roof.position, new Vector3(0f, roomSize.y * 0.5f - size.y * 0.5f, 0f)));
                            Quaternion rot = ToRotation(room.roof.rotation);
                            PBHelper.CreateElement(string.IsNullOrEmpty(room.roof.id) ? "Roof" : room.roof.id, pos, size, rot, roomRoot.transform);
                        }
                        // Custom walls
                        if (room.walls != null)
                        {
                            for (int w = 0; w < room.walls.Length; w++)
                            {
                                Element wall = room.walls[w];
                                Vector3 size = SanitizeSize(wall.size, defaultY: defaultRoomHeight, defaultX: 0.1f, defaultZ: 0.1f);
                                Vector3 pos = SnapVector(ToPosition(wall.position));
                                Quaternion rot = ToRotation(wall.rotation);
                                PBHelper.CreateElement(string.IsNullOrEmpty(wall.id) ? $"Wall_{w}" : wall.id, pos, size, rot, roomRoot.transform);
                            }
                        }

                        roomLookup[resolvedId] = roomRoot;
                    }

                    roomIndex++;
                }

                // Floor-level props and free doors (rare; you mostly use room doors)
                if (floor.doors != null)
                {
                    foreach (Door door in floor.doors)
                    {
                        Vector3 size = SanitizeSize(door.size, defaultY: 2.0f, defaultX: 1.0f, defaultZ: 0.3f);
                        Vector3 pos = SnapVector(ToPosition(door.position, new Vector3(0f, size.y * 0.5f, 0f)));
                        Quaternion rot = ToRotation(door.rotation);
                        PBHelper.CreateElement(string.IsNullOrEmpty(door.id) ? "Door" : door.id, pos, size, rot, null);
                    }
                }

                if (floor.props != null)
                {
                    foreach (Prop prop in floor.props)
                    {
                        Vector3 size = SanitizeSize(prop.size, defaultY: defaultRoomHeight);
                        Vector3 pos = SnapVector(ToPosition(prop.position));
                        Quaternion rot = ToRotation(prop.rotation);
                        PBHelper.CreateElement(string.IsNullOrEmpty(prop.id) ? prop.type : prop.id, pos, size, rot, null);
                    }
                }
            }
        }

        if (data.stairs != null)
        {
            foreach (Stair stair in data.stairs)
            {
                Vector3 size = ToSize(stair.size, defaultStairHeight, 1f, 1f);
                Vector3 pos = ToPosition(stair.position);
                PBHelper.CreateStairs(string.IsNullOrEmpty(stair.description) ? "Stairs" : stair.description, pos, size);
            }
        }

        string issueSummary = roomIssues.Count > 0 ? $" Issues: {string.Join(", ", roomIssues)}" : string.Empty;
        Debug.Log($"AI Level Generator: Created {roomLookup.Count} rooms and {(data.stairs?.Length ?? 0)} stairs.{issueSummary}");
    }

    // ------------------------------------------------------------------------
    // Auto room & door openings
    // ------------------------------------------------------------------------

    private void BuildAutoRoomWithOpenings(Room room, GameObject roomRoot, Vector3 roomSize)
    {
        float halfX = roomSize.x * 0.5f;
        float halfY = roomSize.y * 0.5f;
        float halfZ = roomSize.z * 0.5f;

        // Floor
        Vector3 floorSize = new Vector3(roomSize.x, defaultSlabThickness, roomSize.z);
        Vector3 floorPos = new Vector3(0f, -halfY + floorSize.y * 0.5f, 0f);
        PBHelper.CreateElement("Floor", floorPos, floorSize, Quaternion.identity, roomRoot.transform);

        // Openings per wall
        List<PBHelper.Opening> frontOpenings = new List<PBHelper.Opening>();
        List<PBHelper.Opening> backOpenings = new List<PBHelper.Opening>();
        List<PBHelper.Opening> rightOpenings = new List<PBHelper.Opening>();
        List<PBHelper.Opening> leftOpenings = new List<PBHelper.Opening>();

        if (room.doors != null)
        {
            foreach (var door in room.doors)
            {
                Vector3 doorSize = SanitizeSize(door.size, defaultY: 2.0f, defaultX: 1.0f, defaultZ: 0.3f);

                // Interpret JSON door.position as:
                // [xLocalFromCenter, zLocalFromCenter, yFromFloorCenter]
                float dx = 0f, dz = 0f;
                if (door.position != null && door.position.Length > 0) dx = door.position[0];
                if (door.position != null && door.position.Length > 1) dz = door.position[1];

                // door.position[2] is door bottom (not center)
                float bottomY = 0f;
                if (door.position != null && door.position.Length > 2)
                    bottomY = door.position[2];

                float centerFromFloor = bottomY + doorSize.y * 0.5f;

                // Local center position in room coordinates
                Vector3 localPos = new Vector3(dx, -halfY + centerFromFloor, dz);

                // Compute baseY (distance from floor to bottom of opening)
                float baseY = Mathf.Clamp(centerFromFloor - doorSize.y * 0.5f, 0f, roomSize.y - doorSize.y);

                // Determine nearest wall
                float distFront = Mathf.Abs(localPos.z - (halfZ - defaultWallThickness * 0.5f));
                float distBack = Mathf.Abs(localPos.z + (halfZ - defaultWallThickness * 0.5f));
                float distRight = Mathf.Abs(localPos.x - (halfX - defaultWallThickness * 0.5f));
                float distLeft = Mathf.Abs(localPos.x + (halfX - defaultWallThickness * 0.5f));

                float min = Mathf.Min(distFront, distBack, distRight, distLeft);

                PBHelper.Opening opening = new PBHelper.Opening
                {
                    width = doorSize.x,
                    height = doorSize.y,
                    baseY = baseY
                };

                if (min == distFront)
                {
                    // Front wall (z = +halfZ)
                    opening.center = Mathf.Clamp(localPos.x, -halfX + opening.width * 0.5f, halfX - opening.width * 0.5f);
                    frontOpenings.Add(opening);
                }
                else if (min == distBack)
                {
                    // Back wall (z = -halfZ)
                    opening.center = Mathf.Clamp(localPos.x, -halfX + opening.width * 0.5f, halfX - opening.width * 0.5f);
                    backOpenings.Add(opening);
                }
                else if (min == distRight)
                {
                    // Right wall (x = +halfX)
                    opening.center = Mathf.Clamp(localPos.z, -halfZ + opening.width * 0.5f, halfZ - opening.width * 0.5f);
                    rightOpenings.Add(opening);
                }
                else
                {
                    // Left wall (x = -halfX)
                    opening.center = Mathf.Clamp(localPos.z, -halfZ + opening.width * 0.5f, halfZ - opening.width * 0.5f);
                    leftOpenings.Add(opening);
                }
            }
        }

        // Build walls with openings using zero-rotation helpers
        Vector3 frontBackSize = new Vector3(roomSize.x, roomSize.y, defaultWallThickness);
        Vector3 leftRightSize = new Vector3(defaultWallThickness, roomSize.y, roomSize.z);

        // Front
        var front = PBHelper.CreateWallWithOpenings(
            "Wall_Front",
            frontBackSize,
            defaultWallThickness,
            PBHelper.WallAxis.X,
            roomRoot.transform,
            frontOpenings.ToArray());
        front.transform.localPosition = new Vector3(0f, 0f, halfZ - defaultWallThickness * 0.5f);

        // Back
        var back = PBHelper.CreateWallWithOpenings(
            "Wall_Back",
            frontBackSize,
            defaultWallThickness,
            PBHelper.WallAxis.X,
            roomRoot.transform,
            backOpenings.ToArray());
        back.transform.localPosition = new Vector3(0f, 0f, -halfZ + defaultWallThickness * 0.5f);

        // Right
        var right = PBHelper.CreateWallWithOpenings(
            "Wall_Right",
            leftRightSize,
            defaultWallThickness,
            PBHelper.WallAxis.Z,
            roomRoot.transform,
            rightOpenings.ToArray());
        right.transform.localPosition = new Vector3(halfX - defaultWallThickness * 0.5f, 0f, 0f);

        // Left
        var left = PBHelper.CreateWallWithOpenings(
            "Wall_Left",
            leftRightSize,
            defaultWallThickness,
            PBHelper.WallAxis.Z,
            roomRoot.transform,
            leftOpenings.ToArray());
        left.transform.localPosition = new Vector3(-halfX + defaultWallThickness * 0.5f, 0f, 0f);
    }

    // ------------------------------------------------------------------------
    // Utility conversions
    // ------------------------------------------------------------------------

    // For rooms: position is [x, z, y] but we only care about x,z and set y=0 (center).
    private Vector3 ToRoomPosition(float[] pos)
    {
        float x = (pos != null && pos.Length > 0) ? pos[0] : 0f;
        float z = (pos != null && pos.Length > 1) ? pos[1] : 0f;
        return new Vector3(x, 0f, z);
    }

    private Vector3 ToPosition(float[] pos)
    {
        float x = (pos != null && pos.Length > 0) ? pos[0] : 0f;
        float z = (pos != null && pos.Length > 1) ? pos[1] : 0f;
        float y = (pos != null && pos.Length > 2) ? pos[2] : 0f;
        return new Vector3(x, y, z);
    }

    private Vector3 ToPosition(float[] pos, Vector3 fallback)
    {
        if (pos == null || pos.Length == 0)
            return fallback;
        float x = pos.Length > 0 ? pos[0] : fallback.x;
        float z = pos.Length > 1 ? pos[1] : fallback.z;
        float y = pos.Length > 2 ? pos[2] : fallback.y;
        return new Vector3(x, y, z);
    }

    private Quaternion ToRotation(float[] rot)
    {
        if (rot == null || rot.Length == 0) return Quaternion.identity;
        float x = rot.Length > 0 ? rot[0] : 0f;
        float y = rot.Length > 1 ? rot[1] : 0f;
        float z = rot.Length > 2 ? rot[2] : 0f;
        return Quaternion.Euler(x, y, z);
    }

    private Vector3 ToSize(float[] size, float defaultY, float defaultX = 1f, float defaultZ = 1f)
    {
        float x = (size != null && size.Length > 0) ? size[0] : defaultX;
        float z = (size != null && size.Length > 1) ? size[1] : defaultZ;
        float y = (size != null && size.Length > 2) ? size[2] : defaultY;
        return new Vector3(x, y, z);
    }

    private Vector3 SanitizeSize(float[] size, float defaultY, float defaultX = 1f, float defaultZ = 1f)
    {
        Vector3 v = ToSize(size, defaultY, defaultX, defaultZ);
        v.x = Mathf.Max(v.x, 0.01f);
        v.y = Mathf.Max(v.y, 0.01f);
        v.z = Mathf.Max(v.z, 0.01f);
        return v;
    }

    private Vector3 SnapVector(Vector3 v, float snap = 0.001f)
    {
        return new Vector3(
            Mathf.Round(v.x / snap) * snap,
            Mathf.Round(v.y / snap) * snap,
            Mathf.Round(v.z / snap) * snap
        );
    }

    // ------------------------------------------------------------------------
    // Data classes for JSON
    // ------------------------------------------------------------------------

    [Serializable]
    private class LevelData
    {
        public Floor[] floors;
        public Stair[] stairs;
    }

    [Serializable]
    private class Floor
    {
        public string id;
        public Room[] rooms;
        public Connection[] connections; // not used in this version but kept for future
        public Door[] doors;
        public Prop[] props;
    }

    [Serializable]
    private class Room
    {
        public string id;
        public string type;
        public float[] size;
        public float[] position;
        public Element floor;
        public Element roof;
        public Element[] walls;
        public Door[] doors;
    }

    [Serializable]
    private class Stair
    {
        public string floor_from;
        public string floor_to;
        public string description;
        public float[] size;
        public float[] position;
    }

    [Serializable]
    private class Connection
    {
        public string from_room;
        public string to_room;
        public string from_floor;
        public string to_floor;
        public float[] location;
    }

    [Serializable]
    private class Door
    {
        public string id;
        public string from_room;
        public string to_room;
        public float[] size;      // [width, height, thickness]
        public float[] position;  // [xLocal, zLocal, yFromFloorCenter]
        public float[] rotation;
    }

    [Serializable]
    private class Prop
    {   
        public string id;
        public string type;
        public float[] size;
        public float[] position;
        public float[] rotation;
    }

    [Serializable]
    private class Element
    {
        public string id;
        public float[] size;
        public float[] position;
        public float[] rotation;
    }
}
