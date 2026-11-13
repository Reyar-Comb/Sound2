using System;
using UnityEditor;
using UnityEngine;

public static class ConfigurePickup
{
    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play mode before updating prefabs.");
        const string playerPath = "Assets/Prefabs/Player.prefab";
        var player = PrefabUtility.LoadPrefabContents(playerPath);
        try
        {
            var camera = player.GetComponentInChildren<Camera>();
            var pickup = player.GetComponent<PlayerPickup>();
            if (pickup == null) pickup = player.AddComponent<PlayerPickup>();
            var rangeTransform = player.transform.Find("PickupRange");
            if (rangeTransform == null)
            {
                rangeTransform = new GameObject("PickupRange").transform;
                rangeTransform.SetParent(player.transform, false);
            }
            rangeTransform.gameObject.layer = 2; // Ignore Raycast; queries explicitly ignore triggers too.
            var range = rangeTransform.GetComponent<SphereCollider>();
            if (range == null) range = rangeTransform.gameObject.AddComponent<SphereCollider>();
            range.isTrigger = true;
            range.radius = 2f;
            range.center = new Vector3(0, .35f, 0);
            var anchor = camera.transform.Find("HoldPoint");
            if (anchor == null)
            {
                anchor = new GameObject("HoldPoint").transform;
                anchor.SetParent(camera.transform, false);
            }
            anchor.localPosition = new Vector3(.45f, -.3f, .8f);
            pickup.pickupRange = range;
            pickup.holdPoint = anchor;
            pickup.viewTransform = camera.transform;
            PrefabUtility.SaveAsPrefabAsset(player, playerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }

        const string itemPath = "Assets/Prefabs/PickupItem.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(itemPath) == null)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                item.name = "PickupItem";
                item.transform.localScale = Vector3.one * .3f;
                var body = item.AddComponent<Rigidbody>();
                body.mass = .5f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                item.AddComponent<PickupItem>();
                PrefabUtility.SaveAsPrefabAsset(item, itemPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(item); }
        }
        AssetDatabase.SaveAssets();
        return new { playerPath, itemPath, radius = 2, key = "E", cooldown = 1 };
    }
}
