using Unity.Mathematics;

public struct Generation
{
    public static byte GenerateChunk(Chunk c, int x, int y, int z)
    {
        if (c.Type == 0) return 0;
        return (byte)(IsCustomMengerVoxel(x, y, z) ? 1 : 0);
    }
    
    public static bool IsCustomStair(int x, int y, int z)
    {
        // First, central 8x8x8 hole
        if (x < y)
            return false;
        
        return true; // otherwise solid
    }

    public static bool IsCustomMengerVoxel(int x, int y, int z)
    {
        // First, central 8x8x8 hole
        if ((x >= 4 && x < 12 ? 1:0) +
            (y >= 4 && y < 12 ? 1:0) +
            (z >= 4 && z < 12 ? 1:0) >= 2)
            return false;

        int xl = x % 4;
        int yl = y % 4;
        int zl = z % 4;
        
        if ((xl >= 1 && xl <= 2 ? 1:0) +
            (yl >= 1 && yl <= 2 ? 1:0) +
            (zl >= 1 && zl <= 2 ? 1:0) >= 2)
            return false;
        
        return true; // otherwise solid
    }
    
    public static bool IsCustomSpongeVoxel(int x, int y, int z)
    {
        return noise.pnoise(new float3(x+.5f, y+.5f, z+.5f), new float3(16, 16, 16)) < 0f; // otherwise solid
    }
}