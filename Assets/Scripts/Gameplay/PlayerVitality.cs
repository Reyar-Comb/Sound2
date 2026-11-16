using System;
using UnityEngine;

/// <summary>Two ordinary hits within the wound window or one lethal hit kill; rejected hits never extend either timer.</summary>
[DisallowMultipleComponent]
public sealed class PlayerVitality : MonoBehaviour
{
    [Min(1)] public float maximumHealth=100;
    [Min(0), InspectorName("受伤无敌时间（秒）")] public float invulnerabilitySeconds = 1.5f;
    [Min(.1f), InspectorName("受伤危险期（秒）")] public float woundSeconds = 10;
    [Min(0), InspectorName("苏醒保护时间（秒）")] public float respawnProtectionSeconds = 2;
    public float Health { get; private set; }
    public bool IsDead => Health<=0;
    public bool IsWounded => !IsDead && woundedUntil > Time.time;
    public bool IsInvulnerable => DeveloperMode.Enabled || Time.time < invulnerableUntil;
    public float WoundRemaining => IsWounded ? woundedUntil - Time.time : 0;
    public event Action<float> Changed;
    public event Action Died;
    public event Action Hurt;
    float woundedUntil, invulnerableUntil;
    PlayerController controller;
    void Awake()
    {
        Health = Mathf.Max(1, maximumHealth);
        controller = GetComponent<PlayerController>();
        if (controller != null && GetComponent<PlayerDamageFeedback>() == null)
            gameObject.AddComponent<PlayerDamageFeedback>();
    }
    void Update() => RecoverExpiredWound();
    void RecoverExpiredWound()
    {
        if (!IsDead && woundedUntil > 0 && Time.time >= woundedUntil)
        {
            woundedUntil = 0;
            Health = Mathf.Max(1, maximumHealth);
            Changed?.Invoke(Health);
        }
    }
    // Shared by monsters and lethal vine areas so scene, capsule and occlusion rules stay identical.
    public bool CanReceiveContactFrom(Transform source, Vector3 origin, float radius)
    {
        if (source == null || radius <= 0 || source.gameObject.scene != gameObject.scene
            || !isActiveAndEnabled || IsDead || !TryGetComponent<CharacterController>(out var capsule)
            || !capsule.enabled) return false;
        if (Vector3.Distance(origin, capsule.ClosestPoint(origin)) > radius) return false;
        Vector3 delta = capsule.bounds.center - origin;
        foreach (var hit in Physics.RaycastAll(origin, delta.normalized, delta.magnitude,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(source) && !hit.transform.IsChildOf(transform)) return false;
        return true;
    }
    public bool TakeDamage(float amount) => ApplyDamage(amount, false);
    public bool TakeLethalDamage() => ApplyDamage(1, true);
    public bool TakeExactDamage(float amount) => ApplyDamage(amount, false, true);
    bool ApplyDamage(float amount, bool instantDeath, bool exact = false)
    {
        if (amount <= 0 || float.IsNaN(amount) || float.IsInfinity(amount) || !isActiveAndEnabled
            || IsDead || IsInvulnerable || SonicSpaceStartMenu.BlocksGameplay
            || (controller != null && (!controller.isActiveAndEnabled || controller.InputSuspended))) return false;
        RecoverExpiredWound();
        bool lethal = instantDeath || (exact ? amount >= Health : IsWounded);
        // Ordinary damage keeps the two-hit rule; lethal hazards skip directly to death.
        Health = lethal ? 0 : exact ? Health - amount : Mathf.Max(1, maximumHealth) * .5f;
        if (lethal && controller != null) StoryRunProgress.Current.RecordPlayerDeath();
        woundedUntil = lethal ? 0 : Time.time + Mathf.Max(.1f, woundSeconds);
        invulnerableUntil = Time.time + Mathf.Max(0, invulnerabilitySeconds);
        Changed?.Invoke(Health);
        if (lethal) Died?.Invoke(); else Hurt?.Invoke();
        return true;
    }
    public void RestoreHealth()
    {
        woundedUntil = invulnerableUntil = 0;
        Health = Mathf.Max(1, maximumHealth);
        Changed?.Invoke(Health);
    }
    public void RestoreAfterRespawn()
    {
        RestoreHealth();
        invulnerableUntil = Time.time + Mathf.Max(0, respawnProtectionSeconds);
    }
}
