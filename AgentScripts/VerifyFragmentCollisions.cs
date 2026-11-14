using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class VerifyFragmentCollisions
{
    static readonly List<string> checks = new();
    static void Check(bool value, string label)
    { checks.Add((value ? "PASS: " : "FAIL: ") + label); if (!value) throw new Exception(label); }
    static async Task Wait(int milliseconds)
    { int frame = Time.frameCount; await Task.Delay(milliseconds); Check(Time.frameCount > frame, "Player loop advances"); }
    public static async Task<object> Play()
    {
        checks.Clear(); Check(Application.isPlaying, "Play mode active");
        var fixture = new GameObject("Fragment collision verification");
        var origin = new Vector3(8000, 0, 8000);
        try
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.SetParent(fixture.transform);
            floor.transform.position = origin + Vector3.down * .5f; floor.transform.localScale = new Vector3(30, 1, 30);
            var barrier = GameObject.CreatePrimitive(PrimitiveType.Cube); barrier.transform.SetParent(fixture.transform);
            PropFragments.SetLayer(barrier);
            // Instantiate the player after the fragment exists: no dependency on spawn order.
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"), origin + Vector3.up * 2, Quaternion.identity, fixture.transform);
            var controller = player.GetComponent<PlayerController>(); controller.InputSuspended = true;
            var cc = player.GetComponent<CharacterController>();
            barrier.transform.position = cc.bounds.center + Vector3.forward * 1.2f;
            barrier.transform.localScale = new Vector3(6, 8, .1f);
            Physics.SyncTransforms();
            Check((cc.excludeLayers.value & (1 << PropFragments.Layer)) != 0, "Player excludes Fragments layer");
            Vector3 start = player.transform.position;
            cc.Move(Vector3.forward * 3);
            Check(player.transform.position.z > start.z + 2.8f, "Actual CharacterController movement passes through fragments");
            controller.Teleport(start); barrier.layer = 0; Physics.SyncTransforms();
            cc.Move(Vector3.forward * 3);
            Check(player.transform.position.z < start.z + 1.3f, "The same shape on the environment layer still blocks player");
            barrier.SetActive(false); controller.Teleport(start);
            player.SetActive(false); player.SetActive(true); controller.InputSuspended = true;
            Check((cc.excludeLayers.value & (1 << PropFragments.Layer)) != 0, "Player reactivation retains fragment exclusion");
            foreach (string name in new[] { "AlarmClockBroken", "CoffeeMugBroken", "WineGlassBroken", "CeramicVaseBroken" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + name + ".prefab");
                var piece = prefab.GetComponentsInChildren<Rigidbody>().First();
                var body = Object.Instantiate(piece, origin + new Vector3(5, 2, 0), Quaternion.identity, fixture.transform);
                Check(body.GetComponentsInChildren<Collider>().All(c => c.gameObject.layer == PropFragments.Layer), name + ": real fragment uses correct layer");
                body.useGravity = true;
                await Wait(1200);
                Check(body.position.y > -.2f && body.position.y < 1 && Math.Abs(body.linearVelocity.y) < 1,
                    name + ": fragment falls and rests on floor instead of passing through");
                body.useGravity = false; body.angularVelocity = Vector3.zero;
                body.position = cc.bounds.center - Vector3.forward * 2; body.rotation = Quaternion.identity;
                body.linearVelocity = Vector3.forward * 6; Physics.SyncTransforms();
                await Wait(700);
                Check(body.position.z > cc.bounds.center.z + 1, name + ": moving rigidbody fragment passes through player body");
                Object.Destroy(body.gameObject); await Task.Delay(50);
            }
            return new { passed = true, checks };
        }
        finally
        {
            Object.Destroy(fixture);
            Directory.CreateDirectory("Logs/MeshConsolidation");
            File.WriteAllLines("Logs/MeshConsolidation/collisions.txt", checks);
        }
    }
}
