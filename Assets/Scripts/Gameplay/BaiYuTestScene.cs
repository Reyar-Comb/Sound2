using System.Collections.Generic;
using Sound2.LevelEditing;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Scene-local integration fixture. Populates only the player's current generated floor.</summary>
[DefaultExecutionOrder(90), DisallowMultipleComponent]
public sealed class BaiYuTestScene : MonoBehaviour
{
    public LevelMap map;
    public PlayerFloorProgression player;
    public GameObject coinPrefab, lightningPrefab, pickupPrefab;
    public Material worldMaterial;
    public GameObject ActiveContent { get; private set; }
    public RuntimePatrolNavigation Navigation { get; private set; }
    public int PopulatedFloor { get; private set; } = -1;
    readonly Dictionary<int, GameObject> contents = new();

    void Start()
    {
        if (map == null || player == null || coinPrefab == null || lightningPrefab == null || pickupPrefab == null)
        { Debug.LogError("BaiYu-Test requires map, player and all three gameplay prefabs.", this); enabled = false; return; }
        player.FloorChanged += EnterFloor;
        EnterFloor(player.CurrentRule);
    }

    void OnDestroy() { if (player != null) player.FloorChanged -= EnterFloor; }

    void EnterFloor(FloorRule rule)
    {
        if (rule == null) return;
        if (ActiveContent != null) ActiveContent.SetActive(false);
        var floor = map.FindFloor(rule.floor);
        if (floor == null) { Debug.LogError("Generate the destination floor before populating it.", this); return; }
        if (!contents.TryGetValue(rule.floor, out var content) || content == null)
        {
            content = new GameObject("Gameplay F" + rule.floor);
            content.SetActive(false);
            content.transform.SetParent(floor.transform, false);
            contents[rule.floor] = content;
            Populate(floor, content, rule);
        }
        ActiveContent = content;
        Navigation = content.GetComponent<RuntimePatrolNavigation>();
        PopulatedFloor = rule.floor;
        Physics.SyncTransforms();
        content.SetActive(true);
        // Old floor objects are retired together with LevelMap's previous/current/next window.
        var retired = new List<int>();
        foreach (var pair in contents) if (pair.Value == null || pair.Key < rule.floor - 1) retired.Add(pair.Key);
        foreach (int index in retired) contents.Remove(index);
    }

    Vector3 InRoom(SeededFloor floor, int room, float x = 0, float z = 0, float y = .05f)
    {
        var cell = floor.Plan.Rooms[room % floor.Plan.Rooms.Count];
        return floor.transform.TransformPoint(new Vector3(cell.x + x, y, cell.y + z));
    }

    void Populate(SeededFloor floor, GameObject root, FloorRule rule)
    {
        var nav = root.AddComponent<RuntimePatrolNavigation>();
        nav.geometryRoot = floor.transform;
        var controller = player.GetComponent<PlayerController>();
        var sonar = player.GetComponentInChildren<SphericalSonar>();
        int growth = Mathf.Min(rule.floor, map.growthLimit);
        // One introductory coin is nearby; further coins occupy deterministic room positions.
        for (int i = 0; i < 6 + growth * 3; i++)
        {
            int room = i == 0 ? 0 : 1 + (i - 1) % (floor.Plan.Rooms.Count - 1);
            float x = i == 0 ? 1.8f : (i % 2 == 0 ? 2.2f : -2.2f);
            float z = i == 0 ? 1.6f : (i % 3 - 1) * 2.2f;
            var coin = Instantiate(coinPrefab, InRoom(floor, room, x, z, .9f), Quaternion.identity, root.transform);
            coin.name = "Coin " + (i + 1);
            coin.GetComponent<CoinPickup>().phaseOffset = i * .17f;
            var emitter = coin.GetComponent<SonarObjectEmitter>();
            emitter.firstDelay = .7f + (i % 8) * .35f;
        }
        for (int i = 0; i <= floor.Plan.Rooms.Count; i++)
        {
            int room = i == 0 ? 0 : i - 1;
            var position = i == 0 ? InRoom(floor, 0, 0, 1.5f, .24f) : InRoom(floor, room, 2, 2, .24f);
            var ball = Instantiate(pickupPrefab, position, Quaternion.identity, root.transform);
            ball.name = "可投掷球 " + (i + 1);
            ball.GetComponent<SonarObjectEmitter>().automatic = false;
        }

        // First physical crystal is placed near the spawn for the reticle interaction test.
        var resources = floor.GetComponentsInChildren<MapResource>();
        if (resources.Length > 0) resources[0].transform.position = InRoom(floor, 0, -1.5f, 1.5f, .6f);

        var route = new GameObject("Monster patrol route");
        route.transform.SetParent(root.transform, false);
        var points = new List<Transform>();
        for (int i = 1; i < floor.Plan.Rooms.Count; i++)
        {
            var point = new GameObject("Room " + i);
            point.transform.SetParent(route.transform, false);
            // Offset from the room centre so the final room's open shaft is never a patrol target.
            point.transform.position = InRoom(floor, i, -2, 2);
            points.Add(point.transform);
        }
        var actor = new GameObject("Invisible patrol monster");
        actor.transform.SetParent(root.transform, false);
        actor.transform.position = points[0].position;
        actor.AddComponent<NavMeshAgent>().enabled = false;
        var monster = actor.AddComponent<PatrolMonster>();
        monster.navigation = nav; monster.patrolPoints = points.ToArray();
        monster.patrolSpeed = 1.1f * Mathf.Min(2, rule.difficulty);
        monster.investigateSpeed = 1.8f * Mathf.Min(2, rule.difficulty);
        var scan = actor.AddComponent<MonsterScanEmitter>();
        scan.player = player.transform;
        scan.nearInterval = .65f / Mathf.Min(1.6f, rule.difficulty);

        var hazardObject = Instantiate(lightningPrefab, InRoom(floor, 2, 2, -2, 0), Quaternion.identity, root.transform);
        hazardObject.name = "Ball lightning";
        var hazard = hazardObject.GetComponent<BallLightning>();
        hazard.navigation = nav; hazard.player = player.GetComponent<PlayerVitality>();
        hazard.heightRelativeToSpawn = true;
        hazard.damage = 20 * Mathf.Min(2, rule.difficulty);
        hazard.wanderRadius = 2;
        hazard.autoCycle = true;

        var relay = GameObject.CreatePrimitive(PrimitiveType.Cube);
        relay.name = "可搬运方块";
        relay.transform.SetParent(root.transform, false);
        relay.transform.position = InRoom(floor, 0, 2, -1.5f, .7f);
        relay.transform.localScale = Vector3.one * .6f;
        relay.GetComponent<Renderer>().sharedMaterial = worldMaterial;
        var pickup = relay.AddComponent<PickupItem>();
        var body=relay.GetComponent<Rigidbody>();body.mass=.6f;body.interpolation=RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
        var pulse = relay.GetComponent<SonarObjectEmitter>();
        pulse.toggleKey = UnityEngine.InputSystem.Key.None; pulse.range = 3; pulse.interval = 5;
        pulse.firstDelay = 1.5f; pulse.playSound = true;

        var hum = new GameObject("Shaft spatial audio beacon", typeof(AudioSource));
        hum.transform.SetParent(root.transform, false);
        hum.transform.position = floor.transform.TransformPoint(new Vector3(floor.Plan.Exit.x, 1.5f, floor.Plan.Exit.y));
        hum.AddComponent<HallHumEmitter>().volume = .12f;
        foreach (var emitter in root.GetComponentsInChildren<SonarObjectEmitter>(true)) emitter.sonar = sonar;
    }

    [ContextMenu("Test: move above current floor shaft")]
    public void MoveToShaft()
    {
        if (!Application.isPlaying || player == null || map == null) return;
        var floor = map.FindFloor(player.CurrentFloor);
        if (floor == null) return;
        var controller = player.GetComponent<PlayerController>();
        controller.Teleport(floor.transform.TransformPoint(new Vector3(floor.Plan.Exit.x, 1.1f, floor.Plan.Exit.y)));
        controller.SetInputCaptured(true);
    }

    [ContextMenu("Test: restore player health")]
    public void RestoreHealth()
    {
        if (Application.isPlaying && player != null) player.GetComponent<PlayerVitality>().RestoreHealth();
    }
}
