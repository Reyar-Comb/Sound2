using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sound2.LevelEditing
{
    /// <summary>Floor number is the seed; generation never touches UnityEngine.Random.</summary>
    public sealed class SeededFloorPlan
    {
        public int Floor { get; private set; }
        public readonly List<Vector2Int> Rooms = new();
        public readonly List<Vector2Int> Cells = new();
        public readonly List<Vector2> Resources = new();
        public readonly List<(int from, int to)> Connections = new();
        public Vector2Int Exit { get; private set; }
        readonly HashSet<Vector2Int> occupied = new();
        public bool Contains(Vector2Int cell) => occupied.Contains(cell);
        public bool IsPit(Vector2Int cell) => cell.x >= Exit.x - 1 && cell.x < Exit.x + 1 && cell.y >= Exit.y - 1 && cell.y < Exit.y + 1;
        struct Random
        {
            uint state;
            public Random(int seed) { state = unchecked((uint)seed + 0x9E3779B9u); if (state == 0) state = 1; }
            public int Next(int count) { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return (int)(state % (uint)count); }
        }
        public static SeededFloorPlan Generate(int floor, int growthLimit = 24)
        {
            if (floor < 0) throw new ArgumentOutOfRangeException(nameof(floor));
            var plan = new SeededFloorPlan { Floor = floor };
            var random = new Random(floor);
            int growth = Mathf.Min(floor, Mathf.Clamp(growthLimit, 0, 100)), count = 5 + growth * 2;
            var nodes = new List<Vector2Int> { Vector2Int.zero };
            var used = new HashSet<Vector2Int> { Vector2Int.zero };
            var directions = new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };
            while (nodes.Count < count)
            {
                int parent = random.Next(nodes.Count), start = random.Next(4);
                for (int d = 0; d < 4; d++)
                {
                    Vector2Int next = nodes[parent] + directions[(start + d) % 4];
                    if (!used.Add(next)) continue;
                    plan.Connections.Add((parent, nodes.Count)); nodes.Add(next); break;
                }
            }
            foreach (var node in nodes) plan.Rooms.Add(node * 14);
            int loops = Mathf.Min(1 + growth, count / 2);
            var possible = new List<(int, int)>();
            for (int a = 0; a < count; a++) for (int b = a + 1; b < count; b++)
            {
                var delta = nodes[a] - nodes[b];
                if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1) continue;
                if (!plan.Connections.Exists(e => (e.from == a && e.to == b) || (e.from == b && e.to == a))) possible.Add((a, b));
            }
            while (loops-- > 0 && possible.Count > 0)
            {
                int index = random.Next(possible.Count); plan.Connections.Add(possible[index]); possible.RemoveAt(index);
            }
            foreach (var room in plan.Rooms)
                for (int z = -4; z < 4; z++) for (int x = -4; x < 4; x++) plan.occupied.Add(room + new Vector2Int(x, z));
            foreach (var edge in plan.Connections)
            {
                Vector2Int a = plan.Rooms[edge.from], b = plan.Rooms[edge.to];
                for (int z = Mathf.Min(a.y, b.y) - 1; z <= Mathf.Max(a.y, b.y) + 1; z++)
                    for (int x = Mathf.Min(a.x, b.x) - 1; x <= Mathf.Max(a.x, b.x) + 1; x++) plan.occupied.Add(new Vector2Int(x, z));
            }
            int farthest = 1;
            for (int i = 2; i < count; i++) if (plan.Rooms[i].sqrMagnitude > plan.Rooms[farthest].sqrMagnitude) farthest = i;
            plan.Exit = plan.Rooms[farthest];
            plan.Cells.AddRange(plan.occupied);
            plan.Cells.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            var resourceCells = new List<Vector2>();
            foreach (var cell in plan.Cells)
            {
                Vector2 p = new Vector2(cell.x + .5f, cell.y + .5f);
                if (p.sqrMagnitude < 9 || (p - plan.Exit).sqrMagnitude < 9) continue;
                if (!plan.Contains(cell + Vector2Int.right) || !plan.Contains(cell + Vector2Int.left)
                    || !plan.Contains(cell + Vector2Int.up) || !plan.Contains(cell + Vector2Int.down)) continue;
                resourceCells.Add(p);
            }
            int resources = Mathf.Min(6 + growth * 3, resourceCells.Count);
            while (plan.Resources.Count < resources)
            {
                int index = random.Next(resourceCells.Count); plan.Resources.Add(resourceCells[index]); resourceCells.RemoveAt(index);
            }
            return plan;
        }
    }
}
