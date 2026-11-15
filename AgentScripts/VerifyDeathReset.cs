using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Sound2.LevelEditing;
using UnityEngine;
using Object = UnityEngine.Object;

public static class VerifyDeathReset
{
    static readonly List<string> checks = new();
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks.Add(message);
    }
    static async Task Until(Func<bool> predicate, int milliseconds = 30000)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!predicate())
        {
            if (watch.ElapsedMilliseconds > milliseconds) throw new Exception("Runtime state timed out");
            await Task.Delay(20);
        }
    }
    public static async Task<object> Main()
    {
        checks.Clear();
        var errors = new List<string>();
        Application.LogCallback log = (message, stack, type) =>
        { if (type == LogType.Error || type == LogType.Exception) errors.Add(message); };
        Application.logMessageReceived += log;
        try
        {
            int frame = Time.frameCount;
            await Until(() => Time.frameCount > frame);
            Check(true, "Player loop advances");
            var menu = Object.FindAnyObjectByType<SonicSpaceStartMenu>();
            if (menu != null && !menu.HasStarted) { menu.BeginGame(); await Until(() => menu.HasStarted); }
            var world = Object.FindAnyObjectByType<LevelWorld>();
            var player = world.player;
            var pickup = player.GetComponent<PlayerPickup>();
            var vitality = player.GetComponent<PlayerVitality>();
            var progression = player.GetComponent<PlayerFloorProgression>();
            var feedback = player.GetComponent<PlayerDamageFeedback>();
            feedback.engulfSeconds = feedback.dissolveSeconds = feedback.wakeSeconds = .1f;
            feedback.deathHoldSeconds = feedback.whiteHoldSeconds = 0;
            var opening = Object.FindAnyObjectByType<SoundShellOpening>();
            var card = opening.GetComponentsInChildren<UnityEngine.UI.RawImage>(true).Single();
            var importer = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(UnityEditor.AssetDatabase.GetAssetPath(card.texture));
            importer.GetSourceTextureWidthAndHeight(out int sourceWidth, out int sourceHeight);
            Check(Mathf.Abs((float)card.texture.width / card.texture.height - (float)sourceWidth / sourceHeight) < .002f, "Imported texture preserves source artwork aspect");
            // Vary the actual parent rect and animation scale, including a narrow portrait view.
            var probe = new GameObject("Aspect test canvas", typeof(RectTransform), typeof(Canvas));
            var probeRect = probe.GetComponent<RectTransform>();
            var parent = card.transform.parent;
            card.transform.SetParent(probe.transform, false);
            foreach (var size in new[] { new Vector2(1280,720), new Vector2(720,1280), new Vector2(2560,1080) })
            foreach (float scale in new[] { .045f, .5f, 1f, 1.06f })
            {
                probeRect.sizeDelta = size;
                card.rectTransform.localScale = Vector3.one * scale;
                Canvas.ForceUpdateCanvases();
                var corners = new Vector3[4]; card.rectTransform.GetWorldCorners(corners);
                float actual = Vector3.Distance(corners[2], corners[1]) / Vector3.Distance(corners[1], corners[0]);
                Check(Mathf.Abs(actual - (float)card.texture.width / card.texture.height) < .0001f,
                    "Card aspect at " + size + " scale " + scale);
            }
            card.transform.SetParent(parent, false); card.rectTransform.localScale = Vector3.one;
            Object.Destroy(probe);
            var beforeFall = world.CurrentLevel;
            player.Teleport(opening.startingRoom.transform.TransformPoint(new Vector3(0,-3,-8)));
            await Until(() => opening.CurrentPhase == SoundShellOpening.Phase.AwaitingReturn);
            ScreenCapture.CaptureScreenshot("Logs/DeathReset-card.png");
            await Task.Delay(150);
            opening.ReturnToRoom(); await Until(() => opening.CurrentPhase == SoundShellOpening.Phase.Room);
            Check(world.CurrentLevel != beforeFall && opening.startingRoom == world.CurrentLevel && pickup.HeldItem == null,
                "Window ending also resets the room and clears hands");

            // First reset the starting room, then die in transit to exercise checkpoint remapping.
            var room = world.CurrentLevel;
            player.SetInputCaptured(true); vitality.RestoreHealth();
            Check(vitality.TakeLethalDamage(), "Starting room death begins");
            await Until(() => !progression.IsRespawning);
            Check(world.CurrentLevel != room && opening.startingRoom == world.CurrentLevel
                && opening.spawn.IsChildOf(world.CurrentLevel.transform), "Starting room and opening references reset");
            var link = world.connections[0];
            link.source.Open(true); await Until(() => link.source.DoorOpen >= .999f);
            player.Teleport(link.source.transform.TransformPoint(new Vector3(0,1.1f,0)));
            player.SetInputCaptured(true); vitality.RestoreHealth(); link.travelSeconds = 1;
            Check(world.Travel(link.source, player) && vitality.TakeLethalDamage(), "Death during elevator travel");
            await Until(() => !world.Travelling && !progression.IsRespawning, 60000);
            Check(world.CurrentLevel == world.levels[1] && world.ArrivalCheckpoint == link.destination && link.destination.Contains(player.transform.position), "Destination reset retains exact arrival checkpoint");

            // Mutate several independent state types, including sibling indices before the lift.
            var initial = world.CurrentLevel;
            var props = initial.transform.Find("Props");
            var items = initial.GetComponentsInChildren<PickupItem>();
            Check(items.Length >= 2, "School has authored pickup props");
            var held = items[0];
            string heldName = held.name, removedName = items[1].name;
            Vector3 heldOrigin = held.transform.position;
            Check(held.TryPickUp(pickup, pickup.holdPoint), "Pick up authored object");
            typeof(PlayerPickup).GetField("heldItem", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(pickup, held);
            Object.Destroy(items[1].gameObject);
            var debris = new GameObject("Test runtime debris"); debris.transform.SetParent(props, false);
            await Task.Delay(30);
            player.SetInputCaptured(true); vitality.RestoreHealth();
            Check(vitality.TakeLethalDamage(), "Death begins");
            Check(pickup.HeldItem == null && !held.gameObject.activeSelf, "Hands clear immediately on death");
            await Until(() => !progression.IsRespawning);
            Check(world.CurrentLevel != initial && initial == null, "Old level replaced and destroyed");
            var restoredItems = world.CurrentLevel.GetComponentsInChildren<PickupItem>();
            Check(restoredItems.Any(x => x.name == removedName), "Destroyed prop restored");
            Check(restoredItems.Any(x => x.name == heldName && Vector3.Distance(x.transform.position, heldOrigin) < .2f), "Held prop restored at authored location");
            Check(debris == null && pickup.HeldItem == null && pickup.holdPoint.GetComponentsInChildren<PickupItem>(true).Length == 0, "No debris or held children remain");
            Check(link.destination.Level == world.CurrentLevel && world.startingPoint != null, "Elevator and spawn references survive sibling deletion");
            Check(world.CurrentLevel.GetComponent<RuntimePatrolNavigation>().Ready, "Rebuilt enemy navigation ready");
            var school = world.CurrentLevel;
            player.SetInputCaptured(true); vitality.RestoreHealth();
            Check(vitality.TakeLethalDamage(), "Repeat death begins");
            await Until(() => !progression.IsRespawning, 60000);
            Check(world.CurrentLevel != school && world.ArrivalCheckpoint.Contains(player.transform.position) && pickup.HeldItem == null, "Repeated death resets floor and leaves hands empty");
            Check(errors.Count == 0, "No runtime errors: " + string.Join("; ", errors));
            return new { passed = true, checks };
        }
        finally
        {
            Application.logMessageReceived -= log;
            System.IO.File.WriteAllText("Logs/DeathReset-report.json", Newtonsoft.Json.JsonConvert.SerializeObject(new { checks, errors }, Newtonsoft.Json.Formatting.Indented));
        }
    }

    public static async Task<object> SeededFloor()
    {
        var root = new GameObject("Seeded reset verification");
        root.SetActive(false);
        var map = root.AddComponent<LevelMap>();
        map.generateOnPlay = false;
        root.transform.position = new Vector3(1500, 0, 1500);
        root.SetActive(true);
        try
        {
            map.EnsureFloor(0); var old = map.EnsureFloor(1); var next = map.EnsureFloor(2);
            var position = old.transform.position; var rotation = old.transform.rotation;
            int count = old.GetComponentsInChildren<PickupItem>().Length;
            Check(count > 0, "Seeded floor has resources");
            Object.Destroy(old.GetComponentsInChildren<PickupItem>()[0].gameObject);
            await Task.Delay(30);
            map.ResetFloor(1);
            var current = map.FindFloor(1);
            Check(current != old && !old.gameObject.activeSelf, "Old seeded floor disabled immediately");
            Check(current.transform.position == position && Quaternion.Angle(current.transform.rotation, rotation) < .001f,
                "Seeded floor placement and orientation preserved");
            Check(current.GetComponentsInChildren<PickupItem>().Length == count, "Seeded resources restored");
            Check(map.FindFloor(0).Exit.destination == current && current.Exit.destination == next, "Both seeded shaft links preserved");
            return new { passed = true, count };
        }
        finally { Object.Destroy(root); }
    }
}
