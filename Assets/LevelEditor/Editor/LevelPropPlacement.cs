using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Sound2.LevelEditing.Editor
{
    public static class LevelPropPlacement
    {
        public static Transform PropsRoot(LevelLayout layout) => layout != null ? layout.transform.Find("Props") : null;

        public static bool TryPose(LevelLayout layout, GameObject prefab, Ray ray, PropSurface surface,
            float offset, float angle, out Pose pose)
        {
            pose = default;
            if (layout == null || layout.generatedRoot == null || prefab == null) return false;
            RaycastHit nearest = default;
            float distance = float.PositiveInfinity;
            Physics.SyncTransforms();
            foreach (var collider in layout.generatedRoot.GetComponentsInChildren<Collider>())
                if (collider.enabled && !collider.isTrigger && collider.Raycast(ray, out var hit, 10000)
                    && hit.distance < distance) { nearest = hit; distance = hit.distance; }
            if (nearest.collider == null) return false;
            var up = layout.transform.up;
            float vertical = Vector3.Dot(nearest.normal, up);
            if (surface == PropSurface.Wall ? Mathf.Abs(vertical) > .25f : vertical < .7f) return false;
            Quaternion rotation = surface == PropSurface.Wall
                ? Quaternion.LookRotation(nearest.normal, up) * Quaternion.AngleAxis(angle, Vector3.forward)
                : Quaternion.FromToRotation(up, nearest.normal) * layout.transform.rotation * Quaternion.Euler(0, angle, 0);
            var metadata = prefab.GetComponent<LevelProp>();
            Vector3 anchor = metadata != null ? metadata.surfaceAnchor : BottomAnchor(prefab);
            pose = new Pose(nearest.point + nearest.normal * Mathf.Max(0, offset)
                - rotation * Vector3.Scale(anchor, prefab.transform.localScale), rotation);
            return true;
        }

        static Vector3 BottomAnchor(GameObject prefab)
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
            return found ? new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) : Vector3.zero;
        }

        public static GameObject Place(LevelLayout layout, GameObject prefab, Pose pose)
        {
            if (Application.isPlaying || layout == null || prefab == null || !PrefabUtility.IsPartOfPrefabAsset(prefab))
                throw new InvalidOperationException("请在编辑模式选择有效布局和 Prefab 资源。");
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
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, layout.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(instance, "放置关卡道具");
            Undo.SetTransformParent(instance.transform, parent, "放置关卡道具");
            Undo.RecordObject(instance.transform, "放置关卡道具");
            instance.transform.SetPositionAndRotation(pose.position, pose.rotation);
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            EditorSceneManager.MarkSceneDirty(layout.gameObject.scene);
            Undo.CollapseUndoOperations(group);
            Physics.SyncTransforms();
            return instance;
        }
    }
}
