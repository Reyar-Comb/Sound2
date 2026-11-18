using Sound2.LevelEditing;
using UnityEngine;
using UnityEngine.AI;

internal static class BossArenaEntrance
{
    internal static GameObject Build(LevelLayout level, LevelRoom room)
    {
        var seal = new GameObject("Boss arena entrance seal");
        seal.SetActive(false);
        seal.transform.SetParent(level.transform, false);
        if (level.generatedRoot != null)
            seal.transform.SetParent(level.generatedRoot.transform, true);

        var walls = level.generatedRoot != null ? level.generatedRoot.transform.Find("Walls") : null;
        int layer = walls != null ? walls.gameObject.layer : level.gameObject.layer;
        foreach (var corridor in level.corridors)
        {
            if (corridor.start.roomId == room.id) AddOpening(corridor.start.edge, corridor.width);
            if (corridor.end.roomId == room.id) AddOpening(corridor.end.edge, corridor.width);
        }
        return seal;

        void AddOpening(float edge, float width)
        {
            Vector2 opening = room.Edge(edge, out var normal);
            Vector2 tangent = new(-normal.y, normal.x);
            float halfWidth = width * .5f;
            float height = room.Height(level);
            // Clip the original room facets against the two sides of the corridor.
            // A single panel across the doorway would cut a chord through the room.
            for (int i = 0; i < level.circleSegments; i++)
            {
                Vector2 a = room.Edge((float)i / level.circleSegments, out _);
                Vector2 b = room.Edge((float)(i + 1) / level.circleSegments, out _);
                float left = Vector2.Dot(a - opening, tangent);
                float right = Vector2.Dot(b - opening, tangent);
                if (right <= left || right <= -halfWidth || left >= halfWidth) continue;
                Vector2 from = Vector2.Lerp(a, b, Mathf.InverseLerp(left, right, -halfWidth));
                Vector2 to = Vector2.Lerp(a, b, Mathf.InverseLerp(left, right, halfWidth));
                Vector2 span = to - from;
                Vector2 outward = new Vector2(span.y, -span.x).normalized;
                Vector2 center = (from + to + outward * level.wallThickness) * .5f;

                var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                panel.name = "Walls"; // Uses the same gameplay and developer materials as the shell.
                panel.layer = layer;
                panel.transform.SetParent(seal.transform, false);
                panel.transform.localPosition = new Vector3(center.x, height * .5f, center.y);
                panel.transform.localRotation = Quaternion.LookRotation(new Vector3(outward.x, 0, outward.y));
                panel.transform.localScale = new Vector3(span.magnitude, height, level.wallThickness);
                panel.GetComponent<MeshRenderer>().sharedMaterial = DeveloperMode.Enabled ? level.wallMaterial : level.gameplayMaterial;
                var obstacle = panel.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = Vector3.zero;
                obstacle.size = Vector3.one;
                obstacle.carving = true;
            }
        }
    }
}
