using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Sound2.LevelEditing.Editor
{
    public static class LevelPropPlacement
    {
        public static Transform PropsRoot(LevelLayout layout) => layout != null ? layout.transform.Find("Props") : null;

        public static int EnsureUniqueNames(LevelLayout layout)
        {
            var parent = PropsRoot(layout);
            if (Application.isPlaying || parent == null) return 0;
            var names = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            int renamed = 0;
            foreach (Transform child in parent)
            {
                if (names.Add(child.name)) continue;
                Undo.RecordObject(child.gameObject, "区分同名道具");
                child.name = GameObjectUtility.GetUniqueNameForSibling(parent, child.name);
                PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
                names.Add(child.name); renamed++;
            }
            if (renamed > 0) EditorSceneManager.MarkSceneDirty(layout.gameObject.scene);
            return renamed;
        }

        public static bool TryPose(LevelLayout layout, GameObject prefab, Ray ray, PropSurface surface,
            float offset, float angle, out Pose pose)
        {
            pose = default;
            if (layout == null || layout.generatedRoot == null || prefab == null) return false;
            if (prefab.GetComponent<EmbeddedDoor>() != null)
                return EmbeddedDoorPlacement.TryPose(layout, prefab, ray, out pose);
            RaycastHit nearest = default;
            float distance = float.PositiveInfinity;
            Physics.SyncTransforms();
            var surfaces = new System.Collections.Generic.List<Collider>(layout.generatedRoot.GetComponentsInChildren<Collider>());
            var props = PropsRoot(layout);
            if (props != null) surfaces.AddRange(props.GetComponentsInChildren<Collider>());
            foreach (var collider in surfaces)
                if (!collider.transform.IsChildOf(prefab.transform)
                    && collider.enabled && !collider.isTrigger && collider.Raycast(ray, out var hit, 10000)
                    && hit.distance < distance) { nearest = hit; distance = hit.distance; }
            if (nearest.collider == null) return false;
            var up = layout.transform.up;
            float vertical = Vector3.Dot(nearest.normal, up);
            if (surface == PropSurface.Wall ? Mathf.Abs(vertical) > .25f : vertical < .7f) return false;
            Quaternion rotation = surface == PropSurface.Wall
                ? Quaternion.LookRotation(nearest.normal, up) * Quaternion.AngleAxis(angle, Vector3.forward)
                : Quaternion.FromToRotation(up, nearest.normal) * layout.transform.rotation * Quaternion.Euler(0, angle, 0);
            var metadata = prefab.GetComponent<LevelProp>();
            Vector3 scale = prefab.scene.IsValid() ? prefab.transform.lossyScale : prefab.transform.localScale;
            // Authored anchors describe the default surface only: a wall hanging's centre
            // is not its floor contact, and a floor prop's origin is not its back face.
            Vector3 anchor = metadata != null && metadata.surface == surface
                ? metadata.surfaceAnchor : BoundsAnchor(prefab, surface, scale);
            pose = new Pose(nearest.point + nearest.normal * Mathf.Max(0, offset)
                - rotation * Vector3.Scale(anchor, scale), rotation);
            return true;
        }

        public static void Reposition(LevelLayout layout, GameObject instance, Pose pose)
        {
            if (Application.isPlaying || layout == null || instance == null
                || !instance.scene.IsValid() || instance.transform.parent != PropsRoot(layout))
                throw new InvalidOperationException("请选择当前布局 Props 下的道具实例。");
            var door = instance.GetComponent<EmbeddedDoor>();
            var attachment = door != null ? EmbeddedDoorPlacement.ValidatePose(layout, instance, pose) : null;
            if (LevelPropOverlap.HasOverlap(layout, instance, pose))
                throw new InvalidOperationException("道具碰撞体不能重叠。");
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("重新吸附关卡道具");
            Undo.RecordObject(instance.transform, "重新吸附关卡道具");
            instance.transform.SetPositionAndRotation(pose.position, pose.rotation);
            if (door != null) { EmbeddedDoorPlacement.Bind(door, attachment); LevelBuilder.Rebuild(layout); }
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            EditorSceneManager.MarkSceneDirty(instance.scene);
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(group);
            Undo.IncrementCurrentGroup();
            Physics.SyncTransforms();
        }

        static Vector3 BoundsAnchor(GameObject prefab, PropSurface surface, Vector3 scale)
        {
            bool found = false;
            Bounds bounds = default;
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var local = filter.sharedMesh.bounds;
                var matrix = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var p = matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!found) { bounds = new Bounds(p, Vector3.zero); found = true; } else bounds.Encapsulate(p);
                }
            }
            if (!found) return Vector3.zero;
            return surface == PropSurface.Floor
                ? new Vector3(bounds.center.x, scale.y < 0 ? bounds.max.y : bounds.min.y, bounds.center.z)
                : new Vector3(bounds.center.x, bounds.center.y, scale.z < 0 ? bounds.max.z : bounds.min.z);
        }

        public static GameObject Place(LevelLayout layout, GameObject prefab, Pose pose)
        {
            if (Application.isPlaying || layout == null || prefab == null || !PrefabUtility.IsPartOfPrefabAsset(prefab))
                throw new InvalidOperationException("请在编辑模式选择有效布局和 Prefab 资源。");
            var attachment = prefab.GetComponent<EmbeddedDoor>() != null
                ? EmbeddedDoorPlacement.ValidatePose(layout, prefab, pose) : null;
            if (LevelPropOverlap.HasOverlap(layout, prefab, pose))
                throw new InvalidOperationException("道具碰撞体不能重叠。");
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("放置关卡道具");
            var parent = PropsRoot(layout);
            if (parent == null)
            {
                var root = new GameObject("Props");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, layout.gameObject.scene);
                Undo.RegisterCreatedObjectUndo(root, "创建道具容器");
                Undo.SetTransformParent(root.transform, layout.transform, "创建道具容器");
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;
                parent = root.transform;
            }
            EnsureUniqueNames(layout);
            prefab = LevelPropOptimizer.ForPlacement(prefab);
            string instanceName = GameObjectUtility.GetUniqueNameForSibling(parent, prefab.name);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, layout.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(instance, "放置关卡道具");
            Undo.SetTransformParent(instance.transform, parent, "放置关卡道具");
            Undo.RecordObject(instance.transform, "放置关卡道具");
            instance.name = instanceName;
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
            instance.transform.SetPositionAndRotation(pose.position, pose.rotation);
            if (attachment != null)
            {
                EmbeddedDoorPlacement.Bind(instance.GetComponent<EmbeddedDoor>(), attachment);
                LevelBuilder.Rebuild(layout);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            EditorSceneManager.MarkSceneDirty(layout.gameObject.scene);
            Undo.CollapseUndoOperations(group);
            Physics.SyncTransforms();
            return instance;
        }
    }
}
