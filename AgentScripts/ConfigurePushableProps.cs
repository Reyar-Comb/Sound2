using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

public static class ConfigurePushableProps
{
    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Configure pushable prefabs in Edit Mode.");
        const string folder = "Assets/Physics";
        Directory.CreateDirectory(folder); AssetDatabase.Refresh();
        Configure("ShoppingCart", 18, 1.8f, 140, 3, .08f, .35f, 12);
        Configure("TrashBin", 7, 1.2f, 75, 7, .25f, .5f, 9);
        AssetDatabase.SaveAssets();
        return new { configured = new[] { "ShoppingCart", "TrashBin" }, activation = "Walk into the prop", audio = "Recording slots remain empty" };
    }
    static void Configure(string name, float mass, float speed, float force, float resistance, float friction, float interval, float hearing)
    {
        string path = "Assets/Prefabs/" + name + ".prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var materialPath = "Assets/Physics/" + name + "Push.physicMaterial";
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(materialPath);
            if (material == null) { material = new PhysicsMaterial(name + "Push"); AssetDatabase.CreateAsset(material, materialPath); }
            material.dynamicFriction = friction; material.staticFriction = friction;
            material.frictionCombine = PhysicsMaterialCombine.Minimum; material.bounciness = 0; material.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(material);
            var prop = root.GetComponent<PushableProp>(); if (prop == null) prop = root.AddComponent<PushableProp>();
            prop.maximumPushSpeed = speed; prop.maximumPushForce = force; prop.stoppingResistance = resistance;
            prop.movementInterval = interval; prop.movementPulseRange = name == "ShoppingCart" ? 5 : 4; prop.movementHearingRange = hearing;
            var body = root.GetComponent<Rigidbody>(); body.mass = mass; body.useGravity = true; body.isKinematic = false;
            body.linearDamping = .05f; body.angularDamping = 5; body.maxAngularVelocity = 2;
            body.interpolation = RigidbodyInterpolation.Interpolate; body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            body.centerOfMass = new Vector3(0, name == "ShoppingCart" ? .32f : .22f, 0);
            foreach (var collider in root.GetComponentsInChildren<Collider>()) collider.sharedMaterial = material;
            foreach (var child in root.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
            var origin = root.transform.Find("Sound Origin");
            if (origin == null) { origin = new GameObject("Sound Origin").transform; origin.SetParent(root.transform, false); }
            origin.localPosition = new Vector3(0, .15f, 0);
            var sonar = root.GetComponent<SonarObjectEmitter>(); sonar.automatic = false; sonar.toggleKey = Key.None;
            sonar.emissionOrigin = origin; sonar.range = 6; sonar.soundRange = 14; sonar.waveSpeed = 7; sonar.afterglow = .6f;
            var cue = root.GetComponent<SonarCueSource>(); if (cue == null) cue = root.AddComponent<SonarCueSource>();
            cue.generateDefaultPing = false; cue.output = root.GetComponent<AudioSource>();
            cue.output.playOnAwake = false; cue.output.spatialBlend = 1;
            var impact = root.GetComponent<CollisionSonarEmitter>(); impact.minimumImpactSpeed = .6f; impact.emissionInterval = .25f;
            impact.rangePerSpeed = 2; impact.maximumRange = 14; impact.hearingRange = 16;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
