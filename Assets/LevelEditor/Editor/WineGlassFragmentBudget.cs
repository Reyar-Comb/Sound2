using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sound2.LevelEditing.Editor
{
    /// <summary>Keep the complete glass silhouette while pairing adjacent shards into 25 bodies.</summary>
    public static class WineGlassFragmentBudget
    {
        public static void Apply(GameObject root)
        {
            int count = root.GetComponentsInChildren<Rigidbody>(true).Length;
            if (count == 25) return;
            if (count != 49) throw new InvalidOperationException("Expected 49 wine-glass bodies, got " + count);
            void Pair(string first, string second)
            {
                var a = root.transform.Find(first); var b = root.transform.Find(second);
                if (a == null || b == null) throw new InvalidOperationException("Missing adjacent glass shard: " + first + ", " + second);
                var body = a.GetComponent<Rigidbody>(); var other = b.GetComponent<Rigidbody>();
                body.mass += other.mass;
                Object.DestroyImmediate(other);
                b.SetParent(a, true);
            }
            for (int band = 0; band < 3; band++)
                for (int slice = 0; slice < 12; slice += 2) Pair("Wall " + band + " " + slice, "Wall " + band + " " + (slice + 1));
            for (int i = 0; i < 6; i += 2) { Pair("Base " + i, "Base " + (i + 1)); Pair("Foot " + i, "Foot " + (i + 1)); }
        }
    }
}
