using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Optional pickup, stow and shatter actions for a story collectible.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(StoryCollectible))]
[DefaultExecutionOrder(60)]
public sealed class CollectionItemSequence : MonoBehaviour
{
    public enum Action { PickUpAndStore, InteractAndStore, ShatterThenStore }

    [SerializeField] private Action action;
    [SerializeField] private PickupItem cup;
    [SerializeField] private bool keepGoldAfterDiscovery;
    [SerializeField] private AudioClip idleLoop;
    [SerializeField] private AudioClip interactionClip;
    [SerializeField, Range(0, 1)] private float volume = .75f;
    [SerializeField] private SpatialAudioSettings spatial = new() { distanceMode = SoundDistanceMode.Linear, maxDistance = 9 };
    [SerializeField, Min(0)] private float interactionGoldRange = 5;
    [SerializeField, Min(.1f)] private float stowSeconds = .7f;

    StoryCollectible collectible;
    AudioSource idleSource;
    Transform view;
    Vector3 initialPosition, initialScale;
    Quaternion initialRotation;
    float stowElapsed;
    bool inspectedCup;
    bool stowing;

    internal bool RequiresShatter => action == Action.ShatterThenStore;
    internal string InteractionHint => action == Action.InteractAndStore ? "调查" : "拾取";
    internal bool KeepGold => (cup != null && cup.IsHeld) || (keepGoldAfterDiscovery && collectible != null && collectible.Discovered);

    void Awake() => collectible = GetComponent<StoryCollectible>();

    void OnEnable()
    {
        if (stowing)
        {
            Destroy(gameObject);
            return;
        }
        if (cup != null)
        {
            cup.PickedUp += OnCupPickedUp;
            cup.RemainsSpawned += OnCupShattered;
        }
        if (idleLoop != null && !collectible.Collected)
        {
            var output = GetComponent<SpatialAudioEmitter>();
            if (output == null) output = gameObject.AddComponent<SpatialAudioEmitter>();
            output.settings = spatial;
            output.volume = volume;
            output.Apply();
            idleSource = output.Source;
            idleSource.clip = idleLoop;
            idleSource.loop = true;
            idleSource.Play();
        }
    }

    void OnDisable()
    {
        if (idleSource != null) idleSource.Stop();
        if (cup == null) return;
        cup.PickedUp -= OnCupPickedUp;
        cup.RemainsSpawned -= OnCupShattered;
    }

    internal bool CanInteract(PlayerInteraction player) => !stowing && player != null && player.Pickup != null
        && player.Pickup.HeldItem == null && (!RequiresShatter || (cup != null && cup.CanInteract(player)));

    internal bool TryHold(PlayerInteraction player) => cup != null && cup.TryInteract(player);

    void OnCupPickedUp()
    {
        if (inspectedCup) return;
        inspectedCup = true;
        collectible.PlayStory();
    }

    void OnCupShattered(GameObject remains)
    {
        var shard = remains.GetComponentInChildren<StoryCollectible>();
        if (shard == null) return;
        shard.ContinueFrom(collectible);
    }

    internal void Store(PlayerInteraction player)
    {
        if (stowing) return;
        stowing = true;
        if (idleSource != null) idleSource.Stop();
        if (interactionClip != null) PlayInteractionSound(player);

        view = player.Pickup.viewTransform;
        if (view == null)
        {
            Finish();
            return;
        }
        foreach (var itemCollider in GetComponentsInChildren<Collider>()) itemCollider.enabled = false;
        if (TryGetComponent<Rigidbody>(out var body))
        {
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.isKinematic = true;
        }
        initialPosition = view.InverseTransformPoint(transform.position);
        initialRotation = Quaternion.Inverse(view.rotation) * transform.rotation;
        initialScale = transform.localScale;
    }

    void PlayInteractionSound(PlayerInteraction player)
    {
        Vector3 origin = collectible.InteractionPosition;
        var voice = new GameObject(interactionClip.name + " Collection Audio");
        SceneManager.MoveGameObjectToScene(voice, gameObject.scene);
        voice.transform.SetParent(transform.parent, false);
        voice.transform.position = origin;
        var output = voice.AddComponent<SpatialAudioEmitter>();
        output.settings = spatial;
        output.volume = volume;
        output.PlayOneShot(interactionClip);
        Destroy(voice, interactionClip.length + .25f);

        var sonar = player.GetComponentInChildren<SphericalSonar>();
        if (interactionGoldRange > 0 && sonar != null && sonar.PulseField != null)
            sonar.PulseField.EmitFrom(origin, interactionGoldRange, 10, .45f, SonarPulseKind.Gold, null, source: transform);
    }

    void LateUpdate()
    {
        if (!stowing) return;
        if (view == null)
        {
            Finish();
            return;
        }

        stowElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(stowElapsed / Mathf.Max(.1f, stowSeconds));
        float lift = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0, .42f, t));
        float tuck = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.42f, 1, t));
        var hand = new Vector3(.23f, -.14f, .76f);
        var bag = new Vector3(.52f, -.70f, .43f);
        Vector3 position = Vector3.Lerp(Vector3.Lerp(initialPosition, hand, lift), bag, tuck);
        Quaternion rotation = Quaternion.Slerp(initialRotation, Quaternion.Euler(34 * tuck, -30 * tuck, 18 * tuck), lift);
        transform.SetPositionAndRotation(view.TransformPoint(position), view.rotation * rotation);
        transform.localScale = initialScale * Mathf.Lerp(1, .12f, tuck);
        if (t >= 1) Finish();
    }

    void Finish()
    {
        gameObject.SetActive(false);
        Destroy(gameObject);
    }
}
