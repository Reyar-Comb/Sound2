using UnityEngine;

/// <summary>Legacy scene component retained for serialized references. Gameplay has no status-text HUD.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(PlayerVitality))]
public sealed class PlayerStatusHUD : MonoBehaviour
{
    [HideInInspector] public bool visible;

    void Awake() => enabled = false;
}