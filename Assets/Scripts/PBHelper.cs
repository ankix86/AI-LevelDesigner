using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.Shapes;
using System;

namespace AILevelDesign
{
    /// <summary>
    /// Helper for creating ProBuilder geometry in the editor using the Unity 6 API.
    /// This version keeps ALL walls at zero local rotation.
    /// Front/Back walls run along X, Left/Right walls run along Z.
    /// </summary>
    public static class PBHelper
    {
        public enum WallAxis { X, Z }

        private const float DefaultWallThickness = 0.1f;
        private const float DefaultFloorThickness = 0.1f;

        public struct Opening
        {
            public float center; // Position along wall
            public float width;  // Opening width
            public float height; // Opening height
            public float baseY;  // Distance from floor to bottom of opening
        }

        // --------------------------------------------------------------------
        // Public helpers
        // --------------------------------------------------------------------

        public static GameObject CreateRoom(string name, Vector3 position, Vector3 size)
        {
            return CreateRoomComposite(name, position, size, DefaultWallThickness, DefaultFloorThickness);
        }

        public static GameObject CreateStairs(string name, Vector3 position, Vector3 size, Material material = null)
        {
            return CreateCubeShape(name, position, size, Quaternion.identity, null, material);
        }

        public static GameObject CreateElement(string name, Vector3 position, Vector3 size, Quaternion rotation, Transform parent = null, Material material = null)
        {
            return CreateCubeShape(name, position, size, rotation, parent, material);
        }

        /// <summary>
        /// Create a wall with rectangular openings (doors/windows).
        /// Header FIX applied: header height = 1 unit always.
        /// </summary>
        public static GameObject CreateWallWithOpenings(
            string name,
            Vector3 size,
            float thickness,
            WallAxis axis,
            Transform parent,
            Opening[] openings,
            Material material = null)
        {
            if (openings == null)
                openings = Array.Empty<Opening>();

            GameObject wallParent = new GameObject(string.IsNullOrEmpty(name) ? "Wall" : name);
            wallParent.transform.SetParent(parent, false);
            wallParent.transform.localPosition = Vector3.zero;
            wallParent.transform.localRotation = Quaternion.identity;
            wallParent.transform.localScale = Vector3.one;

            float height = size.y;
            float halfHeight = height * 0.5f;

            float length = (axis == WallAxis.X) ? size.x : size.z;
            float halfLength = length * 0.5f;

            float depth = (axis == WallAxis.X) ? size.z : size.x;

            Array.Sort(openings, (a, b) => a.center.CompareTo(b.center));

            float cursor = -halfLength;
            int segmentIndex = 0;

            foreach (var op in openings)
            {
                float opWidth = Mathf.Clamp(op.width, 0.01f, length * 0.95f);
                float opHeight = Mathf.Clamp(op.height, 0.01f, height * 0.95f);

                float opCenter = Mathf.Clamp(
                    op.center,
                    -halfLength + opWidth * 0.5f,
                    halfLength - opWidth * 0.5f
                );

                float baseY = Mathf.Clamp(op.baseY, 0f, height - opHeight);

                // ------------------------------
                // LEFT SIDE WALL SEGMENT
                // ------------------------------
                float left = Mathf.Max(cursor, opCenter - opWidth * 0.5f);
                if (left > cursor + 0.001f)
                {
                    float segLen = left - cursor;
                    Vector3 segSize, segPos;

                    if (axis == WallAxis.X)
                    {
                        segSize = new Vector3(segLen, height, depth);
                        segPos = new Vector3(cursor + segLen * 0.5f, 0f, 0f);
                    }
                    else
                    {
                        segSize = new Vector3(depth, height, segLen);
                        segPos = new Vector3(0f, 0f, cursor + segLen * 0.5f);
                    }

                    CreateCubeShape($"Seg_{segmentIndex++}", segPos, segSize, Quaternion.identity, wallParent.transform, material);
                }

                // ------------------------------
                // Vertical fills: sill (below opening) and header (above opening)
                // ------------------------------
                float openingBottomLocalY = -halfHeight + baseY;
                float openingTopLocalY = openingBottomLocalY + opHeight;

                float sillHeight = Mathf.Max(baseY, 0f);
                if (sillHeight > 0.001f)
                {
                    float sillCenterY = -halfHeight + sillHeight * 0.5f;
                    Vector3 sillSize, sillPos;

                    if (axis == WallAxis.X)
                    {
                        sillSize = new Vector3(opWidth, sillHeight, depth);
                        sillPos = new Vector3(opCenter, sillCenterY, 0f);
                    }
                    else
                    {
                        sillSize = new Vector3(depth, sillHeight, opWidth);
                        sillPos = new Vector3(0f, sillCenterY, opCenter);
                    }

                    CreateCubeShape($"Sill_{segmentIndex++}", sillPos, sillSize, Quaternion.identity, wallParent.transform, material);
                }

                float headerHeight = Mathf.Max(halfHeight - openingTopLocalY, 0f);
                if (headerHeight > 0.001f)
                {
                    float headerCenterY = openingTopLocalY + headerHeight * 0.5f;
                    Vector3 headerSize, headerPos;

                    if (axis == WallAxis.X)
                    {
                        headerSize = new Vector3(opWidth, headerHeight, depth);
                        headerPos = new Vector3(opCenter, headerCenterY, 0f);
                    }
                    else
                    {
                        headerSize = new Vector3(depth, headerHeight, opWidth);
                        headerPos = new Vector3(0f, headerCenterY, opCenter);
                    }

                    CreateCubeShape($"Header_{segmentIndex++}", headerPos, headerSize, Quaternion.identity, wallParent.transform, material);
                }

                // ------------------------------
                // UPDATE CURSOR AFTER OPENING
                // ------------------------------
                cursor = opCenter + opWidth * 0.5f;
            }

            // ------------------------------
            // RIGHT SIDE WALL SEGMENT
            // ------------------------------
            if (cursor < halfLength - 0.001f)
            {
                float segLen = halfLength - cursor;
                Vector3 segSize, segPos;

                if (axis == WallAxis.X)
                {
                    segSize = new Vector3(segLen, height, depth);
                    segPos = new Vector3(cursor + segLen * 0.5f, 0f, 0f);
                }
                else
                {
                    segSize = new Vector3(depth, height, segLen);
                    segPos = new Vector3(0f, 0f, cursor + segLen * 0.5f);
                }

                CreateCubeShape($"Seg_{segmentIndex++}", segPos, segSize, Quaternion.identity, wallParent.transform, material);
            }

#if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(wallParent, $"Create {wallParent.name}");
            EditorUtility.SetDirty(wallParent);
#endif

            return wallParent;
        }

        // --------------------------------------------------------------------
        // Internal helpers
        // --------------------------------------------------------------------

        private static GameObject CreateRoomComposite(string name, Vector3 worldPosition, Vector3 size, float wallThickness, float floorThickness)
        {
            GameObject parent = new GameObject(string.IsNullOrEmpty(name) ? "Room" : name);
            parent.transform.position = worldPosition;

            float halfX = size.x * 0.5f;
            float halfY = size.y * 0.5f;
            float halfZ = size.z * 0.5f;

            Vector3 floorSize = new Vector3(size.x, floorThickness, size.z);
            Vector3 floorLocalPos = new Vector3(0f, -halfY + floorThickness * 0.5f, 0f);
            CreateCubeShape("Floor", floorLocalPos, floorSize, Quaternion.identity, parent.transform);

            Vector3 ceilSize = new Vector3(size.x, floorThickness, size.z);
            Vector3 ceilLocalPos = new Vector3(0f, halfY - floorThickness * 0.5f, 0f);
            CreateCubeShape("Ceiling", ceilLocalPos, ceilSize, Quaternion.identity, parent.transform);

            Vector3 wallSizeFrontBack = new Vector3(size.x, size.y, wallThickness);
            CreateCubeShape("Wall_Front", new Vector3(0f, 0f, halfZ - wallThickness * 0.5f), wallSizeFrontBack, Quaternion.identity, parent.transform);
            CreateCubeShape("Wall_Back", new Vector3(0f, 0f, -halfZ + wallThickness * 0.5f), wallSizeFrontBack, Quaternion.identity, parent.transform);

            Vector3 wallSizeLeftRight = new Vector3(wallThickness, size.y, size.z);
            CreateCubeShape("Wall_Right", new Vector3(halfX - wallThickness * 0.5f, 0f, 0f), wallSizeLeftRight, Quaternion.identity, parent.transform);
            CreateCubeShape("Wall_Left", new Vector3(-halfX + wallThickness * 0.5f, 0f, 0f), wallSizeLeftRight, Quaternion.identity, parent.transform);

#if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(parent, $"Create {parent.name}");
            EditorUtility.SetDirty(parent);
#endif
            return parent;
        }

        private static GameObject CreateCubeShape(string name, Vector3 position, Vector3 size, Quaternion rotation, Transform parent, Material material = null)
        {
            ProBuilderMesh mesh = ShapeFactory.Instantiate<Cube>();
            if (parent != null)
            {
                mesh.transform.SetParent(parent, false);
                mesh.transform.localPosition = position;
                mesh.transform.localRotation = rotation;
            }
            else
            {
                mesh.transform.SetPositionAndRotation(position, rotation);
            }

            mesh.gameObject.name = string.IsNullOrEmpty(name) ? "ProBuilder Cube" : name;

            var cubeShape = new Cube();
            cubeShape.RebuildMesh(mesh, size, rotation);

            if (!mesh.TryGetComponent(out MeshCollider collider))
            {
                collider = mesh.gameObject.AddComponent<MeshCollider>();
            }

            var mf = mesh.GetComponent<MeshFilter>();
            collider.sharedMesh = mf != null ? mf.sharedMesh : null;

            if (material != null && mesh.TryGetComponent<MeshRenderer>(out var renderer))
            {
                renderer.sharedMaterial = material;
            }

#if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(mesh.gameObject, $"Create {mesh.gameObject.name}");
            EditorUtility.SetDirty(mesh);
#endif
            return mesh.gameObject;
        }
    }
}
