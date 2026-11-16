using Sound2.LevelEditing;
using UnityEngine;

/// <summary>Hanged Man cue across the frame and open aperture, with no solid barrier or damage.</summary>
[DisallowMultipleComponent]
public sealed class StartingRoomWindowVines : MonoBehaviour
{
    public Vector3 windowCenter;
    public Vector3 windowSize = new(4.3f, 3.65f, .3f);
    GameObject vineRoot;
    public bool IsOvergrown => StoryRunProgress.Current.DiedAfterBossStarted;
    public MonsterVines Vines => vineRoot != null ? vineRoot.GetComponent<MonsterVines>() : null;

    void OnEnable()
    {
        StoryRunProgress.Current.EndingProgressChanged += RefreshPresentation;
        RefreshPresentation();
    }
    void OnDisable() => StoryRunProgress.Current.EndingProgressChanged -= RefreshPresentation;

    void RefreshPresentation()
    {
        if (IsOvergrown && vineRoot == null) CreateVines();
        if (vineRoot != null) vineRoot.SetActive(IsOvergrown);
    }

    void CreateVines()
    {
        vineRoot = new GameObject("Window red vines");
        vineRoot.SetActive(false);
        vineRoot.transform.SetParent(transform, false);
        vineRoot.transform.localPosition = windowCenter - Vector3.up * windowSize.y * .5f;
        var area = vineRoot.AddComponent<MonsterVineArea>();
        area.width = windowSize.x; area.depth = Mathf.Max(.3f, windowSize.z); area.height = windowSize.y;
        area.growthSources = 8; area.contactDamageRadius = 0;
        vineRoot.GetComponent<MonsterAudioEmitter>().enabled = false;
        // A trigger supplies a surface across the aperture only to the scoped vine rays.
        // It adds no rendered pane and cannot obstruct a jump through the window.
        var support = new GameObject("Nonblocking window vine surface", typeof(BoxCollider));
        support.transform.SetParent(vineRoot.transform, false);
        var collider = support.GetComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.center = new Vector3(0, windowSize.y * .5f, .06f);
        collider.size = new Vector3(windowSize.x, windowSize.y, .02f);
        var vines = vineRoot.GetComponent<MonsterVines>();
        vines.surfaceRoot = support.transform; vines.includeTriggerSurfaces = true;
        vines.growthInterval = .15f; vines.lifetime = 6; vines.reach = 2.8f;
    }
}
