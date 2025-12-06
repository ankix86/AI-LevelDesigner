using System;
using System.Collections.Generic;
using System.Linq;
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
    private Vector2 scroll;

    [Header("Default Materials (optional)")]
    public Material wallMaterial;
    public Material floorMaterial;
    public Material roofMaterial;
    public Material stairMaterial;

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
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Materials (optional)", EditorStyles.boldLabel);
        wallMaterial = (Material)EditorGUILayout.ObjectField("Wall Material", wallMaterial, typeof(Material), false);
        floorMaterial = (Material)EditorGUILayout.ObjectField("Floor Material", floorMaterial, typeof(Material), false);
        roofMaterial = (Material)EditorGUILayout.ObjectField("Roof Material", roofMaterial, typeof(Material), false);
        stairMaterial = (Material)EditorGUILayout.ObjectField("Stair Material", stairMaterial, typeof(Material), false);

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

            data = FixGeometry(data);
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

        // Determine per-floor base elevations
        Dictionary<string, float> floorElevations = new Dictionary<string, float>();
        if (data.floors != null)
        {
            for (int i = 0; i < data.floors.Length; i++)
            {
                var f = data.floors[i];
                if (f == null) continue;
                float baseY = i * defaultRoomHeight;
                if (!string.IsNullOrEmpty(f.id))
                    floorElevations[f.id] = baseY;
            }
        }

        // Precompute stair placements (adjust height/position) for later use and openings
        List<StairPlacement> stairPlacements = new List<StairPlacement>();
        if (data.stairs != null)
        {
            foreach (var stair in data.stairs)
            {
                if (stair == null) continue;

                float fromY = (stair.floor_from != null && floorElevations.TryGetValue(stair.floor_from, out var fy)) ? fy : 0f;
                float toY = (stair.floor_to != null && floorElevations.TryGetValue(stair.floor_to, out var ty)) ? ty : 0f;

                Vector3 size = ToSize(stair.size, defaultStairHeight, 1f, 1f);
                float verticalSpan = Mathf.Abs(toY - fromY);
                if (verticalSpan > 0.01f)
                {
                    size.y = verticalSpan;
                }
                else if (size.y <= 0.01f)
                {
                    size.y = defaultStairHeight;
                }

                Vector3 pos = ToPosition(stair.position);
                // Place ramp base on top of the source floor slab
                pos.y = fromY - defaultRoomHeight * 0.5f + defaultSlabThickness * 0.5f;

                stairPlacements.Add(new StairPlacement
                {
                    stair = stair,
                    position = pos,
                    size = size,
                    fromY = fromY,
                    toY = toY
                });
            }
        }

        if (data.floors != null)
        {
            for (int floorIndex = 0; floorIndex < data.floors.Length; floorIndex++)
            {
                Floor floor = data.floors[floorIndex];
                if (floor == null || floor.rooms == null) continue;

                float floorBaseY = (!string.IsNullOrEmpty(floor.id) && floorElevations.TryGetValue(floor.id, out var fyBase))
                    ? fyBase
                    : floorIndex * defaultRoomHeight;

                foreach (Room room in floor.rooms)
                {
                    Vector3 roomSize = ToSize(room.size, defaultRoomHeight);
                    Vector3 roomPosLocal = ToRoomPosition(room.position);
                    Vector3 roomPos = SnapVector(new Vector3(roomPosLocal.x, floorBaseY + roomPosLocal.y, roomPosLocal.z));
                    GameObject roomRoot = new GameObject(string.IsNullOrEmpty(room.id) ? "Room" : room.id);
                    roomRoot.transform.position = roomPos;

                    bool hasCustomFloor = room.floor != null;
                    bool hasCustomWalls = room.walls != null && room.walls.Length > 0;
                    bool hasCustomRoof = room.roof != null;

                    HoleSpec? hole = FindHoleForRoom(roomRoot.transform.position, roomSize, stairPlacements, floor);

                    if (!hasCustomFloor && !hasCustomWalls)
                    {
                        BuildAutoRoomWithOpenings(room, roomRoot, roomSize, hole);
                        if (hasCustomRoof)
                        {
                            AddRoofElement(room, roomRoot, roomSize);
                        }
                        roomLookup[room.id] = roomRoot;
                    }
                    else
                    {
                        // Floor
                        if (hasCustomFloor)
                        {
                            Vector3 size = SanitizeSize(room.floor.size, defaultY: 0.1f, defaultX: roomSize.x, defaultZ: roomSize.z);
                            Vector3 pos = SnapVector(ToPosition(room.floor.position, new Vector3(0f, -roomSize.y * 0.5f + size.y * 0.5f, 0f)));
                            Quaternion rot = ToRotation(room.floor.rotation);
                            PBHelper.CreateElement(string.IsNullOrEmpty(room.floor.id) ? "Floor" : room.floor.id, pos, size, rot, roomRoot.transform, floorMaterial);
                        }
                        // Roof
                        if (hasCustomRoof)
                        {
                            AddRoofElement(room, roomRoot, roomSize);
                        }
                        // Custom walls
                        if (hasCustomWalls)
                        {
                            for (int w = 0; w < room.walls.Length; w++)
                            {
                                Element wall = room.walls[w];
                                Vector3 size = SanitizeSize(wall.size, defaultY: defaultRoomHeight, defaultX: 0.1f, defaultZ: 0.1f);
                                Vector3 pos = SnapVector(ToPosition(wall.position));
                                Quaternion rot = ToRotation(wall.rotation);
                                PBHelper.CreateElement(string.IsNullOrEmpty(wall.id) ? $"Wall_{w}" : wall.id, pos, size, rot, roomRoot.transform, wallMaterial);
                            }
                        }

                        roomLookup[room.id] = roomRoot;
                    }
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

        if (stairPlacements.Count > 0)
        {
            foreach (var sp in stairPlacements)
            {
                float yaw = (sp.stair.rotation != null && sp.stair.rotation.Length > 1) ? sp.stair.rotation[1] : 0f;
                PBHelper.CreateStairs(string.IsNullOrEmpty(sp.stair.description) ? "Stairs" : sp.stair.description, sp.position, sp.size, yaw, stairMaterial);
            }
        }

        int connectorCount = AutoCreateInvisibleDoorConnectors(data, roomLookup);
        RoomAutoAligner.AlignRooms(roomLookup, defaultWallThickness);

        Debug.Log($"AI Level Generator: Created {roomLookup.Count} rooms, {(data.stairs?.Length ?? 0)} stairs, and auto-aligned {connectorCount} door connectors.");
    }

    // ------------------------------------------------------------------------
    // Auto room & door openings
    // ------------------------------------------------------------------------

    private void BuildAutoRoomWithOpenings(Room room, GameObject roomRoot, Vector3 roomSize, HoleSpec? floorHole)
    {
        float halfX = roomSize.x * 0.5f;
        float halfY = roomSize.y * 0.5f;
        float halfZ = roomSize.z * 0.5f;

        // Floor (with optional hole for stairs)
        Vector3 floorSize = new Vector3(roomSize.x, defaultSlabThickness, roomSize.z);
        float floorY = -halfY + floorSize.y * 0.5f;
        if (floorHole.HasValue)
        {
            BuildFloorWithOpening(roomRoot.transform, roomSize, floorY, floorHole.Value);
        }
        else
        {
            Vector3 floorPos = new Vector3(0f, floorY, 0f);
            PBHelper.CreateElement("Floor", floorPos, floorSize, Quaternion.identity, roomRoot.transform, floorMaterial);
        }

        // Openings per wall
        List<PBHelper.Opening> frontOpenings = new List<PBHelper.Opening>();
        List<PBHelper.Opening> backOpenings = new List<PBHelper.Opening>();
        List<PBHelper.Opening> rightOpenings = new List<PBHelper.Opening>();
        List<PBHelper.Opening> leftOpenings = new List<PBHelper.Opening>();

        // Doors -> openings
        if (room.doors != null)
        {
            foreach (var door in room.doors)
            {
                AddOpeningFromRect(roomSize, door.size, door.position, halfX, halfZ, defaultWallThickness, frontOpenings, backOpenings, rightOpenings, leftOpenings, false);
            }
        }
        // Windows -> openings
        if (room.windows != null)
        {
            foreach (var window in room.windows)
            {
                AddOpeningFromRect(roomSize, window.size, window.position, halfX, halfZ, defaultWallThickness, frontOpenings, backOpenings, rightOpenings, leftOpenings, true, defaultHeight: 1.2f, defaultWidth: 1.5f);
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
            frontOpenings.ToArray(),
            wallMaterial);
        front.transform.localPosition = new Vector3(0f, 0f, halfZ - defaultWallThickness * 0.5f);

        // Back
        var back = PBHelper.CreateWallWithOpenings(
            "Wall_Back",
            frontBackSize,
            defaultWallThickness,
            PBHelper.WallAxis.X,
            roomRoot.transform,
            backOpenings.ToArray(),
            wallMaterial);
        back.transform.localPosition = new Vector3(0f, 0f, -halfZ + defaultWallThickness * 0.5f);

        // Right
        var right = PBHelper.CreateWallWithOpenings(
            "Wall_Right",
            leftRightSize,
            defaultWallThickness,
            PBHelper.WallAxis.Z,
            roomRoot.transform,
            rightOpenings.ToArray(),
            wallMaterial);
        right.transform.localPosition = new Vector3(halfX - defaultWallThickness * 0.5f, 0f, 0f);

        // Left
        var left = PBHelper.CreateWallWithOpenings(
            "Wall_Left",
            leftRightSize,
            defaultWallThickness,
            PBHelper.WallAxis.Z,
            roomRoot.transform,
            leftOpenings.ToArray(),
            wallMaterial);
        left.transform.localPosition = new Vector3(-halfX + defaultWallThickness * 0.5f, 0f, 0f);
    }

    // ------------------------------------------------------------------------
    // Floor helpers (stairs openings)
    // ------------------------------------------------------------------------

    private HoleSpec? FindHoleForRoom(Vector3 roomWorldPos, Vector3 roomSize, List<StairPlacement> stairPlacements, Floor floor)
    {
        if (stairPlacements == null || stairPlacements.Count == 0 || floor == null || string.IsNullOrEmpty(floor.id))
            return null;

        float roomHalfX = roomSize.x * 0.5f;
        float roomHalfZ = roomSize.z * 0.5f;

        foreach (var sp in stairPlacements)
        {
            if (sp.stair == null || sp.stair.floor_to != floor.id)
                continue;

            Vector2 centerTop = new Vector2(sp.position.x, sp.position.z);
            float yaw = (sp.stair.rotation != null && sp.stair.rotation.Length > 1) ? sp.stair.rotation[1] : 0f;
            float rad = yaw * Mathf.Deg2Rad;
            float sx = sp.size.x * 0.5f;
            float sz = sp.size.z * 0.5f;
            float cos = Mathf.Abs(Mathf.Cos(rad));
            float sin = Mathf.Abs(Mathf.Sin(rad));
            float holeHalfX = cos * sx + sin * sz;
            float holeHalfZ = sin * sx + cos * sz;
            Vector2 holeSize = new Vector2(holeHalfX * 2f, holeHalfZ * 2f);

            Vector2 roomMin = new Vector2(roomWorldPos.x - roomHalfX, roomWorldPos.z - roomHalfZ);
            Vector2 roomMax = new Vector2(roomWorldPos.x + roomHalfX, roomWorldPos.z + roomHalfZ);

            if (centerTop.x < roomMin.x || centerTop.x > roomMax.x || centerTop.y < roomMin.y || centerTop.y > roomMax.y)
                continue;

            Vector2 localCenter = new Vector2(centerTop.x - roomWorldPos.x, centerTop.y - roomWorldPos.z);
            return new HoleSpec { center = localCenter, size = holeSize };
        }

        return null;
    }

    private void BuildFloorWithOpening(Transform parent, Vector3 roomSize, float floorY, HoleSpec hole)
    {
        float halfX = roomSize.x * 0.5f;
        float halfZ = roomSize.z * 0.5f;
        float thickness = defaultSlabThickness;
        float minX = -halfX;
        float maxX = halfX;
        float minZ = -halfZ;
        float maxZ = halfZ;

        float hx = Mathf.Clamp(hole.size.x, 0.01f, roomSize.x - 0.01f);
        float hz = Mathf.Clamp(hole.size.y, 0.01f, roomSize.z - 0.01f);

        float holeMinX = Mathf.Clamp(hole.center.x - hx * 0.5f, minX, maxX);
        float holeMaxX = Mathf.Clamp(hole.center.x + hx * 0.5f, minX, maxX);
        float holeMinZ = Mathf.Clamp(hole.center.y - hz * 0.5f, minZ, maxZ);
        float holeMaxZ = Mathf.Clamp(hole.center.y + hz * 0.5f, minZ, maxZ);

        float eps = 0.001f;
        float holeWidth = holeMaxX - holeMinX;
        float holeDepth = holeMaxZ - holeMinZ;

        if (holeWidth <= eps || holeDepth <= eps)
        {
            Vector3 floorSize = new Vector3(roomSize.x, thickness, roomSize.z);
            Vector3 floorPos = new Vector3(0f, floorY, 0f);
            PBHelper.CreateElement("Floor", floorPos, floorSize, Quaternion.identity, parent, floorMaterial);
            return;
        }

        float centerX = (holeMinX + holeMaxX) * 0.5f;

        // Left strip
        float leftWidth = holeMinX - minX;
        if (leftWidth > eps)
        {
            Vector3 size = new Vector3(leftWidth, thickness, roomSize.z);
            Vector3 pos = new Vector3(minX + leftWidth * 0.5f, floorY, 0f);
            PBHelper.CreateElement("Floor_Left", pos, size, Quaternion.identity, parent, floorMaterial);
        }

        // Right strip
        float rightWidth = maxX - holeMaxX;
        if (rightWidth > eps)
        {
            Vector3 size = new Vector3(rightWidth, thickness, roomSize.z);
            Vector3 pos = new Vector3(maxX - rightWidth * 0.5f, floorY, 0f);
            PBHelper.CreateElement("Floor_Right", pos, size, Quaternion.identity, parent, floorMaterial);
        }

        // Front strip (central span)
        float frontDepth = maxZ - holeMaxZ;
        if (frontDepth > eps)
        {
            Vector3 size = new Vector3(holeWidth, thickness, frontDepth);
            Vector3 pos = new Vector3(centerX, floorY, holeMaxZ + frontDepth * 0.5f);
            PBHelper.CreateElement("Floor_Front", pos, size, Quaternion.identity, parent, floorMaterial);
        }

        // Back strip (central span)
        float backDepth = holeMinZ - minZ;
        if (backDepth > eps)
        {
            Vector3 size = new Vector3(holeWidth, thickness, backDepth);
            Vector3 pos = new Vector3(centerX, floorY, holeMinZ - backDepth * 0.5f);
            PBHelper.CreateElement("Floor_Back", pos, size, Quaternion.identity, parent, floorMaterial);
        }
    }

    // ------------------------------------------------------------------------
    // Utility conversions
    // ------------------------------------------------------------------------

    private void AddOpeningFromRect(
        Vector3 roomSize,
        float[] sizeArr,
        float[] posArr,
        float halfX,
        float halfZ,
        float wallThickness,
        List<PBHelper.Opening> frontOpenings,
        List<PBHelper.Opening> backOpenings,
        List<PBHelper.Opening> rightOpenings,
        List<PBHelper.Opening> leftOpenings,
        bool isWindow,
        float defaultHeight = 2.0f,
        float defaultWidth = 1.0f)
    {
        // For windows, scale default width relative to room span (larger rooms get wider windows)
        float dynamicDefaultWidth = defaultWidth;
        if (isWindow)
        {
            float maxSpan = Mathf.Max(roomSize.x, roomSize.z);
            dynamicDefaultWidth = Mathf.Clamp(maxSpan * 0.25f, 1.2f, 2.5f);
        }

        // Interpret size as [width, height, thickness] for doors/windows
        float width = (sizeArr != null && sizeArr.Length > 0) ? sizeArr[0] : dynamicDefaultWidth;
        float height = (sizeArr != null && sizeArr.Length > 1) ? sizeArr[1] : defaultHeight;
        float thickness = (sizeArr != null && sizeArr.Length > 2) ? sizeArr[2] : 0.3f;
        Vector3 rectSize = SanitizeSize(new float[] { width, thickness, height }, defaultY: height, defaultX: width, defaultZ: thickness);

        float dx = (posArr != null && posArr.Length > 0) ? posArr[0] : 0f;
        float dz = (posArr != null && posArr.Length > 1) ? posArr[1] : 0f;
        float bottomY = (posArr != null && posArr.Length > 2) ? posArr[2] : 0f;

        float centerFromFloor = bottomY + rectSize.y * 0.5f;

        float halfY = roomSize.y * 0.5f;
        Vector3 localPos = new Vector3(dx, -halfY + centerFromFloor, dz);

        float baseY = Mathf.Clamp(centerFromFloor - rectSize.y * 0.5f, 0f, roomSize.y - rectSize.y);

        float distFront = Mathf.Abs(localPos.z - (halfZ - wallThickness * 0.5f));
        float distBack = Mathf.Abs(localPos.z + (halfZ - wallThickness * 0.5f));
        float distRight = Mathf.Abs(localPos.x - (halfX - wallThickness * 0.5f));
        float distLeft = Mathf.Abs(localPos.x + (halfX - wallThickness * 0.5f));

        float min = Mathf.Min(distFront, distBack, distRight, distLeft);

        PBHelper.Opening opening = new PBHelper.Opening
        {
            width = rectSize.x,
            height = rectSize.y,
            baseY = baseY
        };

        List<PBHelper.Opening> targetList = null;
        float spanHalf = halfX;

        if (min == distFront)
        {
            opening.center = Mathf.Clamp(localPos.x, -halfX + opening.width * 0.5f, halfX - opening.width * 0.5f);
            targetList = frontOpenings;
            spanHalf = halfX;
        }
        else if (min == distBack)
        {
            opening.center = Mathf.Clamp(localPos.x, -halfX + opening.width * 0.5f, halfX - opening.width * 0.5f);
            targetList = backOpenings;
            spanHalf = halfX;
        }
        else if (min == distRight)
        {
            opening.center = Mathf.Clamp(localPos.z, -halfZ + opening.width * 0.5f, halfZ - opening.width * 0.5f);
            targetList = rightOpenings;
            spanHalf = halfZ;
        }
        else
        {
            opening.center = Mathf.Clamp(localPos.z, -halfZ + opening.width * 0.5f, halfZ - opening.width * 0.5f);
            targetList = leftOpenings;
            spanHalf = halfZ;
        }

        if (targetList == null)
            return;

        // Skip if overlapping an existing opening on the same wall (door/window conflict)
        float newMin = opening.center - opening.width * 0.5f;
        float newMax = opening.center + opening.width * 0.5f;
        foreach (var op in targetList)
        {
            float existingMin = op.center - op.width * 0.5f;
            float existingMax = op.center + op.width * 0.5f;
            if (!(newMax <= existingMin || newMin >= existingMax))
            {
                return; // overlap -> skip this opening
            }
        }

        targetList.Add(opening);
    }

    private Vector3 ToRoomPosition(float[] pos)
    {
        float x = (pos != null && pos.Length > 0) ? pos[0] : 0f;
        float z = (pos != null && pos.Length > 1) ? pos[1] : 0f;
        float y = (pos != null && pos.Length > 2) ? pos[2] : 0f;
        return new Vector3(x, y, z);
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
    // Invisible connector creation & alignment helpers
    // ------------------------------------------------------------------------

    private int AutoCreateInvisibleDoorConnectors(LevelData data, Dictionary<string, GameObject> roomLookup)
    {
        if (data?.floors == null || roomLookup == null || roomLookup.Count == 0)
            return 0;

        int created = 0;
        HashSet<string> anchorAssigned = new HashSet<string>();

        foreach (var floor in data.floors)
        {
            if (floor?.rooms == null) continue;

            foreach (var room in floor.rooms)
            {
                if (room == null || string.IsNullOrEmpty(room.id) || room.doors == null || room.doors.Length == 0)
                    continue;

                if (!roomLookup.TryGetValue(room.id, out GameObject roomRoot) || roomRoot == null)
                    continue;

                Vector3 roomSize = ToSize(room.size, defaultRoomHeight);

                foreach (var door in room.doors.Where(d => d != null))
                {
                    if (string.IsNullOrEmpty(door.from_room) || string.IsNullOrEmpty(door.to_room))
                        continue;

                    string connectionId = BuildConnectionKey(door.from_room, door.to_room);
                    if (string.IsNullOrEmpty(connectionId))
                        continue;

                    if (!TryComputeDoorConnectorLocal(roomSize, door, out Vector3 localPos, out Vector3 localNormal))
                        continue;

                    bool isAnchor = !anchorAssigned.Contains(connectionId);
                    if (isAnchor) anchorAssigned.Add(connectionId);

                    CreateInvisibleConnector(roomRoot.transform, connectionId, localPos, localNormal, isAnchor);
                    created++;
                }
            }
        }

        return created;
    }

    private string BuildConnectionKey(string a, string b)
    {
        if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b))
            return null;

        string first = a ?? string.Empty;
        string second = b ?? string.Empty;
        return string.CompareOrdinal(first, second) <= 0
            ? $"{first}__{second}"
            : $"{second}__{first}";
    }

    private void CreateInvisibleConnector(Transform parent, string connectorId, Vector3 localPos, Vector3 localNormal, bool isAnchor)
    {
        GameObject go = new GameObject($"InvisibleObjectConnector ({connectorId})");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        if (localNormal.sqrMagnitude < 1e-6f)
            localNormal = Vector3.forward;
        go.transform.localRotation = Quaternion.LookRotation(localNormal, Vector3.up);

        InvisibleConnector connector = go.AddComponent<InvisibleConnector>();
        connector.connectorId = connectorId;
        connector.connectorType = ConnectorType.Door;
        connector.isAnchor = isAnchor;
    }

    private bool TryComputeDoorConnectorLocal(Vector3 roomSize, Door door, out Vector3 localPos, out Vector3 localNormal)
    {
        localPos = Vector3.zero;
        localNormal = Vector3.forward;
        if (door == null)
            return false;

        Vector3 doorSize = SanitizeSize(door.size, defaultY: 2.0f, defaultX: 1.0f, defaultZ: 0.3f);

        float dx = (door.position != null && door.position.Length > 0) ? door.position[0] : 0f;
        float dz = (door.position != null && door.position.Length > 1) ? door.position[1] : 0f;
        float bottomY = (door.position != null && door.position.Length > 2) ? door.position[2] : 0f;

        float centerFromFloor = bottomY + doorSize.y * 0.5f;

        float halfX = roomSize.x * 0.5f;
        float halfY = roomSize.y * 0.5f;
        float halfZ = roomSize.z * 0.5f;

        Vector3 localCenter = new Vector3(dx, -halfY + centerFromFloor, dz);

        float distFront = Mathf.Abs(localCenter.z - (halfZ - defaultWallThickness * 0.5f));
        float distBack = Mathf.Abs(localCenter.z + (halfZ - defaultWallThickness * 0.5f));
        float distRight = Mathf.Abs(localCenter.x - (halfX - defaultWallThickness * 0.5f));
        float distLeft = Mathf.Abs(localCenter.x + (halfX - defaultWallThickness * 0.5f));

        float min = Mathf.Min(distFront, distBack, distRight, distLeft);

        if (min == distFront)
        {
            float clampedX = Mathf.Clamp(localCenter.x, -halfX + doorSize.x * 0.5f, halfX - doorSize.x * 0.5f);
            localPos = new Vector3(clampedX, localCenter.y, halfZ);
            localNormal = Vector3.forward;
            return true;
        }

        if (min == distBack)
        {
            float clampedX = Mathf.Clamp(localCenter.x, -halfX + doorSize.x * 0.5f, halfX - doorSize.x * 0.5f);
            localPos = new Vector3(clampedX, localCenter.y, -halfZ);
            localNormal = Vector3.back;
            return true;
        }

        if (min == distRight)
        {
            float clampedZ = Mathf.Clamp(localCenter.z, -halfZ + doorSize.x * 0.5f, halfZ - doorSize.x * 0.5f);
            localPos = new Vector3(halfX, localCenter.y, clampedZ);
            localNormal = Vector3.right;
            return true;
        }

        float clampedZLeft = Mathf.Clamp(localCenter.z, -halfZ + doorSize.x * 0.5f, halfZ - doorSize.x * 0.5f);
        localPos = new Vector3(-halfX, localCenter.y, clampedZLeft);
        localNormal = Vector3.left;
        return true;
    }

    private void AddRoofElement(Room room, GameObject roomRoot, Vector3 roomSize)
    {
        if (room?.roof == null)
            return;

        Vector3 size = SanitizeSize(room.roof.size, defaultY: 0.1f, defaultX: roomSize.x, defaultZ: roomSize.z);
        Vector3 pos = SnapVector(ToPosition(room.roof.position, new Vector3(0f, roomSize.y * 0.5f - size.y * 0.5f, 0f)));
        Quaternion rot = ToRotation(room.roof.rotation);
        PBHelper.CreateElement(string.IsNullOrEmpty(room.roof.id) ? "Roof" : room.roof.id, pos, size, rot, roomRoot.transform, roofMaterial);
    }

    // ------------------------------------------------------------------------
    // Geometry auto-fix (JSON pre-processing)
    // ------------------------------------------------------------------------
    private LevelData FixGeometry(LevelData data)
    {
        if (data == null) return data;

        // Defaults
        if (data.floors != null)
        {
            foreach (var floor in data.floors)
            {
                if (floor?.rooms == null) continue;
                foreach (var room in floor.rooms)
                {
                    if (room == null) continue;
                    DefaultArray(ref room.size, new float[] { 4f, 4f, defaultRoomHeight });
                    DefaultArray(ref room.position, new float[] { 0f, 0f, 0f });
                    DefaultElements(room);
                    DefaultOpenings(room.doors, isWindow: false);
                    DefaultOpenings(room.windows, isWindow: true);
                }
            }
        }
        if (data.stairs != null)
        {
            foreach (var stair in data.stairs)
            {
                if (stair == null) continue;
                DefaultArray(ref stair.size, new float[] { 2f, defaultStairHeight, 3f });
                DefaultArray(ref stair.position, new float[] { 0f, 0f, 0f });
                DefaultArray(ref stair.rotation, new float[] { 0f, 0f, 0f });
            }
        }

        // Room separation (XZ) to avoid overlaps
        SeparateRooms(data, 0.5f);

        // Auto-rotate doors/windows if missing/blocked
        AutoRotateOpenings(data);

        // Stair clearance (simple inward clamp, rotation if needed)
        FixStairs(data);

        return data;
    }

    private void DefaultArray(ref float[] arr, float[] def)
    {
        if (arr == null) arr = (float[])def.Clone();
        if (arr.Length < 3) Array.Resize(ref arr, 3);
        for (int i = 0; i < 3; i++)
        {
            if (float.IsNaN(arr[i]) || arr[i] == 0f)
            {
                arr[i] = def[Mathf.Min(i, def.Length - 1)];
            }
        }
    }

    private void DefaultElements(Room room)
    {
        if (room.floor != null)
        {
            DefaultArray(ref room.floor.size, new float[] { room.size?[0] ?? 4f, 0.1f, room.size?[1] ?? 4f });
            DefaultArray(ref room.floor.position, new float[] { 0f, 0f, 0f });
            DefaultArray(ref room.floor.rotation, new float[] { 0f, 0f, 0f });
        }
        if (room.roof != null)
        {
            DefaultArray(ref room.roof.size, new float[] { room.size?[0] ?? 4f, 0.1f, room.size?[1] ?? 4f });
            DefaultArray(ref room.roof.position, new float[] { 0f, 0f, room.size?[2] ?? defaultRoomHeight });
            DefaultArray(ref room.roof.rotation, new float[] { 0f, 0f, 0f });
        }
        if (room.walls != null)
        {
            foreach (var w in room.walls)
            {
                if (w == null) continue;
                DefaultArray(ref w.size, new float[] { 0.1f, room.size?[2] ?? defaultRoomHeight, 0.1f });
                DefaultArray(ref w.position, new float[] { 0f, 0f, 0f });
                DefaultArray(ref w.rotation, new float[] { 0f, 0f, 0f });
            }
        }
    }

    private void DefaultOpenings(Door[] list, bool isWindow)
    {
        if (list == null) return;
        foreach (var d in list)
        {
            if (d == null) continue;
            DefaultArray(ref d.size, isWindow ? new float[] { 2f, 1.2f, 0.2f } : new float[] { 1f, 2f, 0.2f });
            DefaultArray(ref d.position, isWindow ? new float[] { 0f, 0f, 1f } : new float[] { 0f, 0f, 0f });
            DefaultArray(ref d.rotation, new float[] { 0f, 0f, 0f });
        }
    }
    private void DefaultOpenings(Window[] list, bool isWindow)
    {
        if (list == null) return;
        foreach (var d in list)
        {
            if (d == null) continue;
            DefaultArray(ref d.size, isWindow ? new float[] { 2f, 1.2f, 0.2f } : new float[] { 1f, 2f, 0.2f });
            DefaultArray(ref d.position, isWindow ? new float[] { 0f, 0f, 1f } : new float[] { 0f, 0f, 0f });
            DefaultArray(ref d.rotation, new float[] { 0f, 0f, 0f });
        }
    }

    private void SeparateRooms(LevelData data, float gap)
    {
        List<(Vector3 pos, Vector3 size)> placed = new List<(Vector3 pos, Vector3 size)>();
        foreach (var floor in data.floors ?? Array.Empty<Floor>())
        {
            if (floor?.rooms == null) continue;
            foreach (var room in floor.rooms)
            {
                if (room == null) continue;
                Vector3 pos = ToVec3Room(room.position);
                Vector3 size = ToSize(room.size, defaultRoomHeight);
                foreach (var p in placed)
                {
                    if (AABBOverlap(pos, size, p.pos, p.size))
                    {
                        Vector2 dir = new Vector2(pos.x - p.pos.x, pos.z - p.pos.z);
                        if (dir.sqrMagnitude < 1e-4f) dir = Vector2.right;
                        dir.Normalize();
                        pos.x += dir.x * (gap + size.x * 0.5f + p.size.x * 0.5f);
                        pos.z += dir.y * (gap + size.z * 0.5f + p.size.z * 0.5f);
                    }
                }
                room.position = new float[] { pos.x, pos.z, pos.y };
                placed.Add((pos, size));
            }
        }
    }

    private bool AABBOverlap(Vector3 aPos, Vector3 aSize, Vector3 bPos, Vector3 bSize)
    {
        Vector3 aMin = aPos - aSize * 0.5f;
        Vector3 aMax = aPos + aSize * 0.5f;
        Vector3 bMin = bPos - bSize * 0.5f;
        Vector3 bMax = bPos + bSize * 0.5f;
        return (aMin.x <= bMax.x && aMax.x >= bMin.x) &&
               (aMin.y <= bMax.y && aMax.y >= bMin.y) &&
               (aMin.z <= bMax.z && aMax.z >= bMin.z);
    }

    private void AutoRotateOpenings(LevelData data)
    {
        foreach (var floor in data.floors ?? Array.Empty<Floor>())
        {
            if (floor?.rooms == null) continue;
            foreach (var room in floor.rooms)
            {
                if (room == null) continue;
                Vector3 size = ToSize(room.size, defaultRoomHeight);
                AutoRotateList(size, room.doors);
                AutoRotateList(size, room.windows);
            }
        }
    }

    private void AutoRotateList(Vector3 roomSize, Door[] list)
    {
        if (list == null) return;
        foreach (var d in list)
        {
            if (d == null) continue;
            if (d.rotation != null && d.rotation.Length >= 3) continue;
            float bestYaw = 0f;
            float bestScore = float.NegativeInfinity;
            for (int k = 0; k < 4; k++)
            {
                float yaw = 90f * k;
                float score = ClearanceScore(roomSize, d.position, yaw);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestYaw = yaw;
                }
            }
            d.rotation = new float[] { 0f, bestYaw, 0f };
        }
    }
    private void AutoRotateList(Vector3 roomSize, Window[] list)
    {
        if (list == null) return;
        foreach (var d in list)
        {
            if (d == null) continue;
            if (d.rotation != null && d.rotation.Length >= 3) continue;
            float bestYaw = 0f;
            float bestScore = float.NegativeInfinity;
            for (int k = 0; k < 4; k++)
            {
                float yaw = 90f * k;
                float score = ClearanceScore(roomSize, d.position, yaw);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestYaw = yaw;
                }
            }
            d.rotation = new float[] { 0f, bestYaw, 0f };
        }
    }

    private float ClearanceScore(Vector3 roomSize, float[] posArr, float yaw)
    {
        Vector3 p = ToVec3Room(posArr);
        Vector3 dir = YawDir(yaw);
        float halfX = roomSize.x * 0.5f;
        float halfZ = roomSize.z * 0.5f;
        float score;
        if (Mathf.Abs(dir.x) > Mathf.Abs(dir.z))
        {
            score = dir.x > 0 ? (halfX - p.x) : (p.x + halfX);
        }
        else
        {
            score = dir.z > 0 ? (halfZ - p.z) : (p.z + halfZ);
        }
        return score;
    }

    private Vector3 YawDir(float yaw)
    {
        float rad = yaw * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
    }

    private void FixStairs(LevelData data)
    {
        if (data.stairs == null) return;

        // Build simple hall bounds per floor (pick largest room on the floor)
        Dictionary<string, Vector2> floorBounds = new Dictionary<string, Vector2>();
        if (data.floors != null)
        {
            foreach (var f in data.floors)
            {
                if (f == null || f.rooms == null || string.IsNullOrEmpty(f.id)) continue;
                float bestX = 7f, bestZ = 5f;
                foreach (var r in f.rooms)
                {
                    if (r == null || r.size == null || r.size.Length < 2) continue;
                    bestX = Mathf.Max(bestX, r.size[0] * 0.5f);
                    bestZ = Mathf.Max(bestZ, r.size[1] * 0.5f);
                }
                floorBounds[f.id] = new Vector2(bestX, bestZ);
            }
        }

        foreach (var stair in data.stairs)
        {
            if (stair == null) continue;
            Vector3 pos = ToVec3Room(stair.position);
            if (stair.rotation == null || stair.rotation.Length < 3)
                stair.rotation = new float[] { 0f, 0f, 0f };

            // Clamp inward (assumes typical hall span if no context)
            Vector2 bounds = new Vector2(7f, 5f);
            if (!string.IsNullOrEmpty(stair.floor_from) && floorBounds.TryGetValue(stair.floor_from, out var b))
                bounds = b;
            float margin = Mathf.Max(1.0f, Mathf.Max((stair.size?[0] ?? 2f), (stair.size?[2] ?? 3f)) * 0.5f + 0.5f);
            pos.x = Mathf.Clamp(pos.x, -bounds.x + margin, bounds.x - margin);
            pos.z = Mathf.Clamp(pos.z, -bounds.y + margin, bounds.y - margin);

            // Ensure entry face has clearance from walls (push inward along facing dir if needed)
            float yaw = stair.rotation.Length > 1 ? stair.rotation[1] : 0f;
            Vector3 dir = YawDir(yaw);
            float halfZ = (stair.size != null && stair.size.Length > 2) ? stair.size[2] * 0.5f : 1.5f;
            float entryMargin = 1.0f + halfZ;
            // Distance to wall in facing axis
            float forwardDist = (Mathf.Abs(dir.z) >= Mathf.Abs(dir.x))
                ? (dir.z > 0 ? (bounds.y - pos.z) : (bounds.y + pos.z))
                : (dir.x > 0 ? (bounds.x - pos.x) : (bounds.x + pos.x));
            if (forwardDist < entryMargin)
            {
                float push = entryMargin - forwardDist;
                pos.x -= dir.x * push;
                pos.z -= dir.z * push;
            }

            stair.position = new float[] { pos.x, pos.z, pos.y };
        }
    }

    private Vector3 ToVec3Room(float[] pos)
    {
        float x = (pos != null && pos.Length > 0) ? pos[0] : 0f;
        float z = (pos != null && pos.Length > 1) ? pos[1] : 0f;
        float y = (pos != null && pos.Length > 2) ? pos[2] : 0f;
        return new Vector3(x, y, z);
    }

    private struct HoleSpec
    {
        public Vector2 center;
        public Vector2 size;
    }

    private class StairPlacement
    {
        public Stair stair;
        public Vector3 position;
        public Vector3 size;
        public float fromY;
        public float toY;
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
        public Window[] windows;
    }

    [Serializable]
    private class Stair
    {
        public string floor_from;
        public string floor_to;
        public string description;
        public float[] size;
        public float[] position;
        public float[] rotation;
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
    private class Window
    {
        public string id;
        public float[] size;      // [width, height, thickness]
        public float[] position;  // [xLocal, zLocal, yFromFloorCenter]
        public float[] rotation;  // unused for auto walls, kept for schema symmetry
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

