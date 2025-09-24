
using UnityEngine;

public static class ChunkOps
{
    public static int DistanceTo(this Chunk chunk, Chunk other)
    {
        Vector3 a = chunk.GetWorldPosition();
        Vector3 b = other.GetWorldPosition();

        // treat each "superchunk" as a unit
        return Mathf.Abs(Mathf.RoundToInt(a.x) - Mathf.RoundToInt(b.x)) / 16
               + Mathf.Abs(Mathf.RoundToInt(a.y) - Mathf.RoundToInt(b.y)) / 16
               + Mathf.Abs(Mathf.RoundToInt(a.z) - Mathf.RoundToInt(b.z)) / 16;
    }
}
