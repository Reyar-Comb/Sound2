using System;
using UnityEngine;

/// <summary>Reusable health and damage events; presentation and failure rules can subscribe later.</summary>
[DisallowMultipleComponent]
public sealed class PlayerVitality : MonoBehaviour
{
    [Min(1)] public float maximumHealth=100;
    public float Health { get; private set; }
    public bool IsDead => Health<=0;
    public event Action<float> Changed;
    public event Action Died;
    void Awake() => Health=Mathf.Max(1,maximumHealth);
    public bool TakeDamage(float amount)
    {
        if(amount<=0 || float.IsNaN(amount) || float.IsInfinity(amount) || IsDead) return false;
        Health=Mathf.Max(0,Health-amount);Changed?.Invoke(Health);
        if(IsDead) Died?.Invoke(); return true;
    }
    public void RestoreHealth() { Health=Mathf.Max(1,maximumHealth);Changed?.Invoke(Health); }
}
