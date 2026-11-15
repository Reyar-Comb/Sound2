using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Builds one shared walkable map from static room colliders. Owns its runtime NavMesh data.</summary>
[DefaultExecutionOrder(-200), DisallowMultipleComponent]
public sealed class RuntimePatrolNavigation : MonoBehaviour
{
    public Transform geometryRoot;
    public LayerMask geometryLayers = Physics.DefaultRaycastLayers;
    [Min(.1f)] public float agentRadius = .35f;
    [Min(.5f)] public float agentHeight = 1.8f;
    [Min(0), Tooltip("Maximum single step height in metres; not an absolute altitude limit.")]
    public float agentClimb = .3f;
    public bool Ready { get; private set; }
    public bool IsBuilding { get; private set; }
    public int SourceCount { get; private set; }
    NavMeshData data;
    NavMeshDataInstance instance;
    Coroutine build;
    AsyncOperation operation;

    // LevelLayout enables its gameplay ceiling before collecting geometry.
    void OnEnable() => Rebuild();
    public NavMeshBuildSettings BuildSettings()
    {
        var settings = NavMesh.GetSettingsByIndex(0);
        settings.agentRadius = Mathf.Max(.1f, agentRadius);
        settings.agentHeight = Mathf.Max(.5f, agentHeight);
        var level = GetComponent<Sound2.LevelEditing.LevelLayout>();
        settings.agentClimb = Mathf.Clamp(level != null ? level.enemyStepHeight : agentClimb, 0, settings.agentHeight);
        settings.agentSlope = 45;
        settings.overrideVoxelSize = true; settings.voxelSize = .1f;
        return settings;
    }
    [ContextMenu("Rebuild navigation (Play mode)")]
    public void Rebuild()
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        Release();
        IsBuilding = true;
        build = StartCoroutine(BuildAsync());
    }
    IEnumerator BuildAsync()
    {
        // Let all Awake/OnEnable/Start callbacks finish, including staged floor activation.
        yield return null;
        var level = GetComponentInParent<Sound2.LevelEditing.LevelLayout>();
        while (level != null && level.IsPreparing) yield return null;
        yield return null;
        Physics.SyncTransforms();
        var colliders = geometryRoot != null ? geometryRoot.GetComponentsInChildren<Collider>()
            : FindObjectsByType<Collider>();
        var marks = new List<NavMeshBuildMarkup>();
        var bounds = new Bounds(); bool any = false;
        double sliceStart = Time.realtimeSinceStartupAsDouble;
        foreach (var collider in colliders)
        {
            if (Time.realtimeSinceStartupAsDouble - sliceStart >= .002)
            {
                yield return null;
                sliceStart = Time.realtimeSinceStartupAsDouble;
            }
            if (collider == null) continue;
            bool ignored = collider.isTrigger || collider.attachedRigidbody != null
                || collider is CharacterController || collider.GetComponentInParent<NavMeshAgent>() != null;
            if (ignored) { marks.Add(new NavMeshBuildMarkup { root = collider.transform, ignoreFromBuild = true }); continue; }
            if (!collider.enabled || !collider.gameObject.activeInHierarchy
                || (geometryLayers.value & (1 << collider.gameObject.layer)) == 0) continue;
            if (!any) { bounds = collider.bounds; any = true; } else bounds.Encapsulate(collider.bounds);
        }
        if (!any) { Debug.LogError("Monster navigation needs static room colliders.", this); IsBuilding = false; build = null; yield break; }
        bounds.Expand(2);
        var sources = new List<NavMeshBuildSource>();
        if (geometryRoot != null)
            NavMeshBuilder.CollectSources(geometryRoot, geometryLayers, NavMeshCollectGeometry.PhysicsColliders, 0, marks, sources);
        else NavMeshBuilder.CollectSources(bounds, geometryLayers, NavMeshCollectGeometry.PhysicsColliders, 0, marks, sources);
        SourceCount = sources.Count;
        var settings = BuildSettings();
        // Collection uses Unity objects on the main thread; voxelization runs asynchronously.
        yield return null;
        data = new NavMeshData(settings.agentTypeID);
        operation = NavMeshBuilder.UpdateNavMeshDataAsync(data, settings, sources, bounds);
        yield return operation;
        operation = null;
        instance = NavMesh.AddNavMeshData(data); Ready = instance.valid;
        IsBuilding = false; build = null;
    }
    void Release()
    {
        Ready = false;
        IsBuilding = false;
        SourceCount = 0;
        if (build != null) StopCoroutine(build);
        build = null;
        if (operation != null && data != null) NavMeshBuilder.Cancel(data);
        operation = null;
        if (instance.valid) instance.Remove();
        if (data != null) Destroy(data);
        data = null;
    }
    void OnDisable() => Release();
}
