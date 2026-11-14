using System;
using UnityEngine;

/// <summary>Two accepted hits within the wound window kill; rejected hits never extend either timer.</summary>
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
    public bool IsInvulnerable => Time.time < invulnerableUntil;
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
    public bool TakeDamage(float amount)
    {
        if (amount <= 0 || float.IsNaN(amount) || float.IsInfinity(amount) || !isActiveAndEnabled
            || IsDead || IsInvulnerable || SonicSpaceStartMenu.BlocksGameplay
            || (controller != null && (!controller.isActiveAndEnabled || controller.InputSuspended))) return false;
        RecoverExpiredWound();
        bool lethal = IsWounded;
        // The amount is retained for API compatibility: each valid hit costs one wound state.
        Health = lethal ? 0 : Mathf.Max(1, maximumHealth) * .5f;
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
