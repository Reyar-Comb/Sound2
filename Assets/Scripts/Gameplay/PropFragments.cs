using System;
using UnityEngine;

/// <summary>Shared collision policy for physical prop remnants.</summary>
public static class PropFragments
{
    public const string LayerName = "Fragments";

    public static int Layer
    {
        get
        {
            int layer = LayerMask.NameToLayer(LayerName);
            if (layer < 0) throw new InvalidOperationException("Configure the Fragments physics layer before spawning prop remnants.");
            return layer;
        }
    }

    public static void SetLayer(GameObject remains)
    {
        int layer = Layer;
        foreach (var child in remains.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = layer;
    }

    public static void ExcludeFromPlayer(PlayerController player)
    {
        int mask = 1 << Layer;
        foreach (var collider in player.GetComponentsInChildren<Collider>(true))
            if (collider.GetComponentInParent<PlayerController>() == player
                && collider.GetComponentInParent<PickupItem>() == null)
                collider.excludeLayers |= mask;
    }
}
