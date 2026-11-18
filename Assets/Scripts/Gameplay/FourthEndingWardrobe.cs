using System.Collections;
using Sound2.LevelEditing;
using UnityEngine;

public sealed class FourthEndingWardrobe : InteractableObject
{
    public GameObject ringPrefab;
    public float liftSeconds = .8f, inspectSeconds = 2.5f, storeSeconds = .8f;
    public float ringScale = 1.5f;
    public Vector3 pocketLocalPosition = new(.18f, .42f, -.145f);
    Transform ringPocket;
    public bool RingStored { get; private set; }
    DoorInteraction[] doors;
    bool taking, ownsLock, previousLock;
    PlayerController actor;
    GameObject heldRing;
    internal bool CanTakeRing
    {
        get
        {
            if (taking || RingStored || ringPrefab == null || doors == null) return false;
            foreach (var door in doors) if (Mathf.Abs(door.CurrentAngle) > 50) return true;
            return false;
        }
    }
    public override string InteractionHint => "打开衣柜";
    public override bool AllowWhileHolding => true;
    public override bool CanInteract(PlayerInteraction player) => base.CanInteract(player) && !taking;

    internal void Initialize(GameObject sourcePrefab)
    {
        // Disabling LevelProp restores the authored materials that OnEnable cached.
        var props = GetComponentsInChildren<LevelProp>(true);
        for (int i = props.Length - 1; i >= 0; i--) props[i].enabled = false;
        // Nested LevelProps may have cached a parent's black gameplay material.
        // Restore the source prefab's authored materials explicitly, independent of enable order.
        foreach (var source in sourcePrefab.GetComponentsInChildren<Renderer>(true))
        {
            string path = "";
            for (var part = source.transform; part != sourcePrefab.transform; part = part.parent)
                path = string.IsNullOrEmpty(path) ? part.name : part.name + "/" + path;
            var target = string.IsNullOrEmpty(path) ? transform : transform.Find(path);
            if (target != null && target.TryGetComponent<Renderer>(out var renderer)) renderer.sharedMaterials = source.sharedMaterials;
        }
        doors = GetComponentsInChildren<DoorInteraction>(true);
        foreach (var door in doors)
        {
            if (door.hinge == null || !door.hinge.TryGetComponent<Rigidbody>(out var body)) continue;
            // These leaves rotate about fixed cabinet-local pivots. Interpolation of a
            // child rigidbody can retain the old parent-space pose after room placement.
            body.interpolation = RigidbodyInterpolation.None;
            body.position = door.hinge.position;
            body.rotation = door.hinge.rotation;
        }
        Physics.SyncTransforms();
        foreach (var part in GetComponentsInChildren<Transform>(true))
            if (part.name == "BeltedBlueCoat")
            {
                var coat = part.gameObject.AddComponent<FourthEndingCoat>();
                coat.wardrobe = this;
                var anchor = new GameObject("Ring pocket interaction").transform;
                anchor.SetParent(part, false);
                anchor.localPosition = pocketLocalPosition;
                ringPocket = anchor;
                coat.interactionAnchor = anchor;
            }
        var lightObject = new GameObject("Wardrobe soft light", typeof(Light));
        lightObject.transform.SetParent(transform, false);
        lightObject.transform.localPosition = new Vector3(0, 1.7f, -.8f);
        var light = lightObject.GetComponent<Light>();
        light.type = LightType.Point; light.range = 4; light.intensity = 1.5f;
        light.shadows = LightShadows.None;
        var rootAnchor = new GameObject("Wardrobe interaction").transform;
        rootAnchor.SetParent(transform, false); rootAnchor.localPosition = new Vector3(0, 1.1f, -.4f);
        interactionAnchor = rootAnchor;
    }

    protected override void OnInteract(PlayerInteraction player)
    {
        foreach (var door in doors) if (!door.TargetOpen) door.TryInteract(player);
    }

    internal void TakeRing(PlayerInteraction player)
    {
        if (!CanTakeRing || player == null) return;
        taking = true;
        actor = player.GetComponent<PlayerController>();
        if (actor == null) { taking = false; return; }
        StartCoroutine(InspectRing(player));
    }

    IEnumerator InspectRing(PlayerInteraction player)
    {
        previousLock = actor.EncounterInputLocked; ownsLock = true; actor.EncounterInputLocked = true;
        // Use the same hand-to-bag presentation as the campaign collectibles.
        Transform view = actor.ViewTransform;
        heldRing = Instantiate(ringPrefab, ringPocket != null ? ringPocket.position : interactionAnchor.position, Quaternion.identity);
        foreach (var prop in heldRing.GetComponentsInChildren<LevelProp>(true)) prop.enabled = false;
        foreach (var action in heldRing.GetComponentsInChildren<InteractableObject>(true)) action.interactionEnabled = false;
        foreach (var collider in heldRing.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (var body in heldRing.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.useGravity = false; }
        heldRing.transform.SetParent(view, true);
        Vector3 start = heldRing.transform.localPosition;
        Vector3 scale = Vector3.one * ringScale;
        var hand = new Vector3(.15f, -.12f, .42f);
        Quaternion rotation = Quaternion.Euler(65, -25, 15);
        for (float t = 0; t < liftSeconds; t += Time.deltaTime)
        {
            heldRing.transform.localPosition = Vector3.Lerp(start, hand, Mathf.SmoothStep(0, 1, t / liftSeconds));
            heldRing.transform.localRotation = rotation;
            heldRing.transform.localScale = scale;
            yield return null;
        }
        heldRing.transform.localPosition = hand;
        for (float t = 0; t < inspectSeconds; t += Time.deltaTime)
        {
            heldRing.transform.localRotation = rotation * Quaternion.Euler(0, Mathf.Sin(t * Mathf.PI / Mathf.Max(.01f, inspectSeconds)) * 35, 0);
            yield return null;
        }
        for (float t = 0; t < storeSeconds; t += Time.deltaTime)
        {
            float p = Mathf.SmoothStep(0, 1, t / storeSeconds);
            heldRing.transform.localPosition = Vector3.Lerp(hand, new Vector3(.5f, -.7f, .25f), p);
            heldRing.transform.localScale = scale * Mathf.Lerp(1, .1f, p);
            yield return null;
        }
        Destroy(heldRing); heldRing = null;
        RingStored = true;
        ReleaseInput();
    }

    void ReleaseInput()
    {
        if (ownsLock && actor != null) actor.EncounterInputLocked = previousLock;
        ownsLock = false;
    }
    void OnDisable()
    {
        StopAllCoroutines();
        if (heldRing != null) Destroy(heldRing);
        ReleaseInput();
    }
}
