using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

public static class BlockHasher
{
    static readonly long[] PrimeTable = new long[81]
    {
        1000003, 1000033, 1000037, 1000039, 1000081, 1000099, 1000117, 1000121, 1000133,
        1000151, 1000159, 1000171, 1000183, 1000187, 1000193, 1000199, 1000211, 1000213,
        1000231, 1000249, 1000253, 1000273, 1000289, 1000291, 1000303, 1000313, 1000333,
        1000357, 1000367, 1000381, 1000393, 1000397, 1000403, 1000409, 1000423, 1000427,
        1000429, 1000453, 1000457, 1000507, 1000537, 1000541, 1000547, 1000577, 1000579,
        1000589, 1000609, 1000619, 1000621, 1000639, 1000651, 1000667, 1000669, 1000679,
        1000691, 1000697, 1000721, 1000723, 1000763, 1000777, 1000793, 1000829, 1000847,
        1000849, 1000859, 1000861, 1000889, 1000907, 1000919, 1000921, 1000931, 1000969,
        1000973, 1000981, 1000999, 1001003, 1001017, 1001023, 1001027, 1001041, 1001069
    };
    
    [BurstCompile]
    private static long GetPrime(int n)
    {
        if (n < 0) n = 0;
        return PrimeTable[n % 80];
    }
    
    const int CoordBits = 12;        // each coord uses 12 bits
    const int CoordMask = (1 << 12) - 1; // 0xFFF
    const int MaxCoordsPacked = 4;   // we only pack last 4

    private const int DepthScale = 48;

    [BurstCompile]
    public static long RectifyPath(ChunkPath path)
    {
        int depth = path.Depth;
        if (depth == 0) return 0;

        // Use stack-allocated arrays instead of heap allocation
        // This eliminates GC pressure while preserving exact behavior
        unsafe
        {
            int* xs = stackalloc int[depth];
            int* ys = stackalloc int[depth];
            int* zs = stackalloc int[depth];
        
            // Copy coordinates
            for (int i = 0; i < depth; i++)
            {
                xs[i] = path.Path[i].x;
                ys[i] = path.Path[i].y;
                zs[i] = path.Path[i].z;
            }

            // Propagate carries upward (leaf → root) - identical to original
            for (int i = depth - 1; i >= 0; i--)
            {
                int cx = xs[i] >> 4;
                int cy = ys[i] >> 4;
                int cz = zs[i] >> 4;

                xs[i] &= 15;
                ys[i] &= 15;
                zs[i] &= 15;

                if (i > 0)
                {
                    xs[i - 1] += cx;
                    ys[i - 1] += cy;
                    zs[i - 1] += cz;
                }
                else
                {
                    // bubbled to root
                    if (cx != 0 || cy != 0 || cz != 0)
                        return 0; // root is not zero → invalid
                }
            }

            // Root must be zero - identical to original
            if (xs[0] != 0 || ys[0] != 0 || zs[0] != 0)
                return 0;

            // Compute hash with bounded coords - identical to original
            long total = 0;
            
            total |= (long)depth << DepthScale;

            int count = math.min(depth, 4);

            for (int i = 0; i < count; i++)
            {
                long index = xs[i] + ys[i] * 16 + zs[i] * 256;
                total |= index << (i * 12);
            }

            return total;
        }
    }
    
    [BurstCompile]
    public static long Hash(ChunkPath path)
    {
        return Hash(path.Path);
    }
    
    public static long Hash(NativeArray<LocalBlockPos> path)
    {
        long total = 0;
        int depth = path.Length;

        // Include depth in high bits like before
        total |= (long)depth << DepthScale;

        // Only pack up to the last 4 coords
        int count = math.min(depth, MaxCoordsPacked);

        for (int i = 0; i < count; i++)
        {
            // Get coordinate, from the END
            var p = path[depth - 1 - i];

            // Each Index must fit in 12 bits (0..4095)
            long index = (long)p.Index & CoordMask;

            // Pack into 12-bit slots
            total |= index << (i * CoordBits);
        }

        return total;
    }
    
    [BurstCompile]
    public static long ExtendHash(long hash, LocalBlockPos next)
    {
        return ExtendHash(hash, next.x, next.y, next.z);
    }
    
    

    [BurstCompile]
    public static long ExtendHash(long hash, int x, int y, int z)
    {
        // Unpack depth
        int depth = (int)(hash >> DepthScale);
        depth++;

        // Pack new coord (12-bit index)
        int index = x + (y << 4) + (z << 8);

        // Extract the packed part (last 4 slots)
        long packed = hash & ((1L << (MaxCoordsPacked * CoordBits)) - 1);

        // Shift packed coords back by one slot (12 bits)
        packed <<= CoordBits;

        // Insert new coord into lowest slot
        packed |= (long)index & CoordMask;

        // Mask off anything beyond 4 slots
        packed &= (1L << (MaxCoordsPacked * CoordBits)) - 1;

        // Rebuild final hash: new depth in high bits + packed coords
        hash = ((long)depth << DepthScale) | packed;

        return hash;
    }


    [BurstCompile]
    public static long DisplaceHash(long hash, int dx, int dy, int dz)
    {
        int depth = (int)(hash >> DepthScale);
        if (depth == 0) return hash;

        // Work on the deepest slot
        int slot = math.min(depth - 1, MaxCoordsPacked - 1);

        // Extract old index
        int oldIndex = (int)((hash >> (slot * CoordBits)) & CoordMask);

        // Decode to x,y,z
        int x = oldIndex & 0xF;
        int y = (oldIndex >> 4) & 0xF;
        int z = (oldIndex >> 8) & 0xF;

        // Apply displacement
        x += dx;
        y += dy;
        z += dz;

        // (Optional: handle carry here if you want rectification)

        int newIndex = (x & 0xF) + ((y & 0xF) << 4) + ((z & 0xF) << 8);

        // Clear and write back
        long clearMask = ~((long)CoordMask << (slot * CoordBits));
        hash &= clearMask;
        hash |= ((long)newIndex & CoordMask) << (slot * CoordBits);

        // Depth stays same, but we need to increment DepthScale counter
        hash += 1L << DepthScale;

        return hash;
    }
}