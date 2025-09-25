using Unity.Burst;
using Unity.Mathematics;

public struct Generation
{
    public static byte GenerateChunk(Chunk c, int x, int y, int z)
    {
        return (byte)(IsCustomMengerVoxel(x, y, z) ? 1 : 0);
    }
    
    [BurstCompile]
    public static byte GenerateBlock(byte c, int x, int y, int z)
    {
        switch (c)
        {
            case (byte)BlockType.Air:
                return (byte)BlockType.Air;
            case (byte)BlockType.World:
                return GenerateWorld(x, y, z);
            case (byte)BlockType.Grass:
                return GenerateGrass(x, y, z);
            case (byte)BlockType.Dirt:
                return GenerateDirt(x, y, z);
            default:
                return 0;
        }
    }
    
    public static byte GenerateWorld(int x, int y, int z)
    {
        return y < 8 ? (y == 7 ? (byte)BlockType.Grass : (byte)BlockType.Dirt) : (byte)BlockType.Air; // otherwise solid
    }
    
    public static byte GenerateGrass(int x, int y, int z)
    {
        return IsCustomMengerVoxel(x,y,z) ? (byte)BlockType.Grass : (byte)BlockType.Air; // otherwise solid
    }
    
    public static byte GenerateDirt(int x, int y, int z)
    {
        return IsCustomMengerVoxel(x,y,z) ? (byte)BlockType.Dirt : (byte)BlockType.Grass; // otherwise solid
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

public enum BlockType
{
    Air = 0,
    World = 1,
    Grass = 2,
    Dirt = 3,
    T_Placeholder = 4,
}