using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sound2.LevelEditing;
using UnityEngine;
using Object = UnityEngine.Object;

public static class VerifyPlayerDamage
{
    static readonly List<string> checks = new();
    static void Check(bool ok, string label)
    {
        checks.Add((ok ? "PASS: " : "FAIL: ") + label);
        if (!ok) throw new Exception(label);
    }
    static async Task Until(Func<bool> condition, float seconds = 12)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > end) throw new Exception("Timed out waiting for gameplay state");
            await Task.Delay(20);
        }
    }
    static async Task Capture(string name)
    {
        ScreenCapture.CaptureScreenshot("Logs/PlayerDamage/" + name + ".png");
        await Task.Delay(100);
    }
    public static async Task<object> WorldAndContact()
    {
        checks.Clear();
        var world = Object.FindAnyObjectByType<LevelWorld>();
        var p = world.player;
        var v = p.GetComponent<PlayerVitality>();
        var f = p.GetComponent<PlayerDamageFeedback>();
        var progression = p.GetComponent<PlayerFloorProgression>();
        var monster = Object.FindAnyObjectByType<PatrolMonster>();
        var agent = monster.GetComponent<UnityEngine.AI.NavMeshAgent>();
        bool agentEnabled = agent.enabled, monsterEnabled = monster.enabled;
        Vector3 monsterPosition = monster.transform.position;
        float immunity = v.invulnerabilitySeconds;
        var hazards = Object.FindObjectsByType<BallLightning>();
        var hazardEnabled = hazards.Select(h => h.enabled).ToArray();
        var nextAttack = typeof(PatrolMonster).GetField("nextContactDamage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        GameObject wall = null;
        var connection = world.connections.First(c => c.source.Level == world.CurrentLevel);
        float travelSeconds = connection.travelSeconds;
        try
        {
            foreach (var h in hazards) h.enabled = false;
            monster.enabled = false; agent.enabled = false;
            p.InputSuspended = false;
            v.RestoreHealth();
            var capsule = p.GetComponent<CharacterController>();
            monster.transform.position = capsule.bounds.center - Vector3.up * .9f + p.transform.right * .7f;
            Vector3 origin = monster.transform.position + Vector3.up * .9f;
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Temporary contact occlusion test";
            wall.transform.position = (origin + capsule.bounds.center) * .5f;
            wall.transform.rotation = Quaternion.FromToRotation(Vector3.right, origin - capsule.bounds.center);
            wall.transform.localScale = new Vector3(.08f, 2, 2);
            Physics.SyncTransforms();
            nextAttack.SetValue(monster, 0f);
            monster.SendMessage("TryContactDamage");
            Check(!v.IsWounded, "Red monster contact cannot damage through an intervening wall");
            Object.DestroyImmediate(wall); wall = null; Physics.SyncTransforms();
            monster.SendMessage("TryContactDamage");
            Check(v.IsWounded && !v.IsDead, "Actual red monster contact damage enters wound state");
            nextAttack.SetValue(monster, 0f);
            monster.SendMessage("TryContactDamage");
            Check(!v.IsDead, "Repeated monster contact respects player immunity");
            v.RestoreHealth(); v.invulnerabilitySeconds = 0;
            var source = connection.source; var destination = connection.destination;
            source.Open(true);
            await Until(() => source.DoorOpen > .999f);
            p.Teleport(source.transform.TransformPoint(new Vector3(0, 1.05f, 0)));
            connection.travelSeconds = 4.5f;
            Check(v.TakeDamage(1) && world.Travel(source, p), "Wounded player can enter a real elevator transaction");
            Check(v.TakeDamage(1) && progression.IsRespawning && world.Travelling, "Death can begin while the elevator is travelling");
            await Until(() => f.WhiteAmount >= .999f);
            Check(world.Travelling && progression.IsRespawning && v.IsDead, "Full white safely waits for the ongoing elevator transaction");
            await Until(() => !world.Travelling);
            await Until(() => !progression.IsRespawning);
            Check(world.CurrentLevel == destination.Level && world.ArrivalCheckpoint == destination
                && destination.Contains(p.transform.position), "After travel, awakening uses the destination floor's exact arrival elevator");
            Check(progression.CurrentFloor == world.levels.IndexOf(destination.Level) + 1 && !p.InputSuspended,
                "Post-travel death preserves floor progress and restores control");
            await Capture("06-arrival-elevator");
            var shader = Resources.Load<Shader>("PlayerDamageVines");
            Check(shader != null && shader.isSupported && !UnityEditor.ShaderUtil.ShaderHasError(shader), "Build-included damage shader compiles and is supported");
            return new { passed = true, checks };
        }
        finally
        {
            if (wall != null) Object.DestroyImmediate(wall);
            connection.travelSeconds = travelSeconds;
            v.invulnerabilitySeconds = immunity;
            monster.transform.position = monsterPosition;
            agent.enabled = agentEnabled;
            if (agentEnabled && agent.isOnNavMesh) agent.Warp(monsterPosition);
            monster.enabled = monsterEnabled;
            for (int i = 0; i < hazards.Length; i++) if (hazards[i] != null) hazards[i].enabled = hazardEnabled[i];
            File.WriteAllText("Logs/PlayerDamage/world-contact-report.json", Newtonsoft.Json.JsonConvert.SerializeObject(checks, Newtonsoft.Json.Formatting.Indented));
        }
    }

    public static async Task<object> Main()
    {
        checks.Clear();
        Directory.CreateDirectory("Logs/PlayerDamage");
        var p = Object.FindAnyObjectByType<PlayerController>();
        var v = p.GetComponent<PlayerVitality>();
        var f = p.GetComponent<PlayerDamageFeedback>();
        var progression = p.GetComponent<PlayerFloorProgression>();
        var monsters = Object.FindObjectsByType<PatrolMonster>();
        var lightning = Object.FindObjectsByType<BallLightning>();
        var monsterEnabled = monsters.Select(m => m.enabled).ToArray();
        var lightningEnabled = lightning.Select(m => m.enabled).ToArray();
        var errors = new List<string>();
        Application.LogCallback log = (message, stack, type) => { if (type == LogType.Error || type == LogType.Exception) errors.Add(message); };
        Application.logMessageReceived += log;
        float originalInvulnerability = v.invulnerabilitySeconds;
        try
        {
            foreach (var m in monsters) m.enabled = false;
            foreach (var h in lightning) h.enabled = false;
            var menu = Object.FindAnyObjectByType<SonicSpaceStartMenu>();
            if (menu != null && !menu.HasStarted) { menu.BeginGame(); await Until(() => menu.HasStarted); }
            p.SetInputCaptured(true);
            int frame = Time.frameCount;
            await Task.Delay(150);
            Check(Time.frameCount > frame, "Play mode advances frames");
            Check(v != null && f != null && progression != null, "Shared player prefab provides health, feedback and respawn");
            Check(v.woundSeconds == 10 && v.invulnerabilitySeconds == 1.5f, "Production defaults: ten-second wound, independent 1.5-second immunity");
            v.RestoreHealth();
            Check(!v.TakeDamage(0) && !v.TakeDamage(-1) && !v.TakeDamage(float.NaN) && !v.TakeDamage(float.PositiveInfinity), "Invalid damage is rejected");
            Check(v.TakeDamage(1000) && v.IsWounded && !v.IsDead && v.Health == v.maximumHealth * .5f, "First accepted hit wounds even when amount exceeds health");
            float remaining = v.WoundRemaining;
            Check(!v.TakeDamage(1) && !v.TakeDamage(1) && v.WoundRemaining <= remaining, "Same-frame multiple sources cannot bypass immunity or extend wound");
            await Capture("01-wounded");
            var rectangles = new Vector4[PlayerUiPointMask.Capacity];
            var options = new Vector4[PlayerUiPointMask.Capacity];
            var camera = p.GetComponentInChildren<Camera>();
            int count = PlayerUiPointMask.Collect(camera, rectangles, options);
            Check(!rectangles.Take(count).Any(r => r.z > 600 && r.w > 350), "Damage overlay does not mask the entire sonar scene");
            await Until(() => !v.IsWounded);
            await Task.Delay(50);
            Check(!v.IsDead && v.Health == v.maximumHealth && !f.OverlayCanvas.enabled, "Ten seconds without another accepted hit restores health and removes red edges");
            v.invulnerabilitySeconds = .2f;
            var world = progression.World;
            var level = world != null ? world.CurrentLevel : null;
            int floor = progression.CurrentFloor;
            Check(v.TakeDamage(1), "New first hit accepted after wound recovery");
            await Until(() => !v.IsInvulnerable);
            Check(v.TakeDamage(1) && v.IsDead && progression.IsRespawning, "Second accepted hit inside danger window starts death exactly once");
            p.InputSuspended = false;
            Check(p.InputSuspended && p.DeathSequenceLocked && !v.TakeDamage(1), "Death lock survives ordinary input toggles and rejects further damage");
            await Until(() => f.Growth >= 1.11f);
            await Capture("02-engulfed");
            await Until(() => f.Retreat > 0);
            Check(f.WhiteAmount > 0, "Whitening begins during the dissolve");
            // With the prefab's 0.1s whitening, the screen is already white well before Retreat reaches 0.3.
            await Until(() => f.Retreat > .3f);
            Check(f.Retreat <= 1.12f, "Outer-to-inner retreat progresses independently of whitening");
            await Capture("03-dissolving");
            await Until(() => f.WhiteAmount >= .999f);
            await Capture("04-white");
            await Until(() => !progression.IsRespawning);
            Check(!v.IsDead && !v.IsWounded && v.Health == v.maximumHealth && !p.DeathSequenceLocked, "Awakening restores health and releases death input lock");
            Check(!f.SequenceActive && !f.OverlayCanvas.enabled && f.WhiteAmount == 0, "White veil fully clears after awakening");
            Check(v.IsInvulnerable && !v.TakeDamage(1), "Awakening protection prevents immediate spawn damage");
            Check(progression.CurrentFloor == floor && (world == null || world.CurrentLevel == level), "Death never advances or resets the current floor");
            if (world != null)
            {
                var lift = world.ArrivalCheckpoint;
                if (lift == null || lift.Level != level) lift = level.GetComponentInChildren<LevelElevator>();
                Check(lift != null && lift.Contains(p.transform.position), "Player awakens inside this floor's elevator");
                Check(Vector3.Dot(p.transform.forward, lift.transform.forward) > .99f, "Awakening view faces the elevator exit");
            }
            await Capture("05-awake");
            // Cancellation is a real lifecycle edge: disabling the owner must not strand an input lock.
            v.RestoreHealth(); v.invulnerabilitySeconds = 0;
            Check(v.TakeDamage(1) && v.TakeDamage(1) && progression.IsRespawning, "A later death can start a fresh sequence");
            progression.enabled = false;
            Check(!progression.IsRespawning && !p.DeathSequenceLocked && !v.IsDead && !f.OverlayCanvas.enabled, "Disabling during death cancels and releases owned resources");
            progression.enabled = true;
            Check(errors.Count == 0, "No runtime errors during injury/death/awakening");
            return new { passed = true, checks, scene = p.gameObject.scene.path, floor, monsters = monsters.Length };
        }
        finally
        {
            v.invulnerabilitySeconds = originalInvulnerability;
            for (int i = 0; i < monsters.Length; i++) if (monsters[i] != null) monsters[i].enabled = monsterEnabled[i];
            for (int i = 0; i < lightning.Length; i++) if (lightning[i] != null) lightning[i].enabled = lightningEnabled[i];
            Application.logMessageReceived -= log;
            File.WriteAllText("Logs/PlayerDamage/report.json", Newtonsoft.Json.JsonConvert.SerializeObject(new { checks, errors }, Newtonsoft.Json.Formatting.Indented));
        }
    }
}
