using System.Collections.Generic;
using Clipper2Lib;
using UnityEngine;

namespace Sound2.LevelEditing.Editor
{
    public static class LevelPartitions
    {
        public sealed class Host
        {
            public RoomPartition data;
            public LevelRoom room;
            public string key;
            public Vector2 Start => room == null ? data.start : room.PartitionPoint(data.start);
            public Vector2 End => room == null ? data.end : room.PartitionPoint(data.end);
            public Paths64 Strip => LevelGeometry.Offset(new Paths64 { LevelGeometry.Path(new[] { Start, End }) }, data.thickness / 2, EndType.Butt);
        }

        public static IEnumerable<Host> All(LevelLayout layout)
        {
            foreach (var p in layout.partitions)
                yield return new Host { data = p, key = "Partition_Free_" + p.id };
            for (int r = 0; r < layout.rooms.Count; r++)
                for (int p = 0; p < layout.rooms[r].partitions.Count; p++)
                    yield return new Host { data = layout.rooms[r].partitions[p], room = layout.rooms[r], key = "Partition_" + r + "_" + p };
        }
    }
}
