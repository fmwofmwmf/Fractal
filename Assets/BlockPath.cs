using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// Represents a path from root to a specific chunk.
/// Useful as a dictionary key or unique identifier.
/// </summary>
[Serializable]
public struct BlockPath
{
    [field:SerializeField] public NativeArray<LocalBlockPos> Path { get; set; }
    public LocalBlockPos Local => Path[^1];
    public int Depth => Path.Length;

    public BlockPath(NativeArray<LocalBlockPos> path)
    {
        Path = path;
    }

    public override bool Equals(object obj)
    {
        if (obj is BlockPath other && other.Depth == Depth)
        {
            for (int i = 0; i < Depth; i++)
                if (!Path[i].Equals(other.Path[i])) return false;
            return true;
        }
        return false;
    }
    
    public BlockPath Add(LocalBlockPos pos)
    {
        var p = new NativeArray<LocalBlockPos>(Depth + 1, Allocator.Persistent);
        for (int i = 0; i < Depth; i++)
        {
            p[i] = Path[i];
        }
        p[Depth] = pos;
        return new BlockPath { Path = p };
    }
    
    public BlockPath Add(LocalBlockPos pos, Allocator allocator)
    {
        var p = new NativeArray<LocalBlockPos>(Depth + 1, allocator);
        for (int i = 0; i < Depth; i++)
        {
            p[i] = Path[i];
        }
        p[Depth] = pos;
        return new BlockPath { Path = p };
    }
    
    public string ToHexString()
    {
        if (Depth == 0) return "";

        char[] chars = new char[Depth * 3];
        for (int i = 0; i < Depth; i++)
        {
            var pos = Path[i];
            int offset = i * 3;
            chars[offset] = pos.x.ToString("X")[0];
            chars[offset + 1] = pos.y.ToString("X")[0];
            chars[offset + 2] = pos.z.ToString("X")[0];
        }

        return new string(chars);
    }

    public void Dispose()
    {
        if (Path.IsCreated) Path.Dispose();
    }

    public override int GetHashCode()
    {
        int hash = 17;
        foreach (var pos in Path) hash = hash * 31 + pos.Hash();
        return hash;
    }

    [BurstCompile]
    public long Hash()
    {
        return BlockHasher.Hash(this);
    }
    
    public long HashWithChild(LocalBlockPos child)
    {
        return BlockHasher.ExtendHash(Hash(), child);
    }
}

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

    private const int DepthScale = 48;

    [BurstCompile]
    public static long RectifyPath(BlockPath path)
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
            long total = ((long)depth << DepthScale);
            int n = 1;
            for (int i = 0; i < depth; i++)
            {
                int index = xs[i] + ys[i] * 16 + zs[i] * 256;
                total += GetPrime(n++) * index;
            }

            return total;
        }
    }
    
    [BurstCompile]
    public static long Hash(BlockPath path)
    {
        long total = 0;
        int n = 1;

        int depth = path.Path.Length;
        total += (long)depth << DepthScale;

        for (int i = 0; i < depth; i++)
        {
            var p = path.Path[i];
            total += GetPrime(n++) * p.Index;
        }

        return total;
    }
    
    [BurstCompile]
    public static long ExtendHash(long hash, LocalBlockPos next)
    {
        var depth = hash >> DepthScale;
        return hash + GetPrime((int)depth + 1) * next.Index + (1L << DepthScale);
    }
    
    [BurstCompile]
    public static long ExtendHash(long hash, int x, int y, int z)
    {
        var depth = hash >> DepthScale;
        return hash + GetPrime((int)depth + 1) * (x + y * 16 + z * 16 * 16)+ (1L << DepthScale);
    }
    
    [BurstCompile]
    public static long DisplaceHash(long hash, int dx, int dy, int dz)
    {
        int depth = (int)(hash >> DepthScale);

        // compute how much a displacement changes Index
        int deltaIndex = dx + dy * 16 + dz * 16 * 16;

        // adjust only the last coordinate (deepest level)
        return hash + GetPrime(depth) * deltaIndex + (1L << DepthScale);
    }
}

/// <summary>
/// Local position inside a chunk (0–15 per axis).
/// </summary>
[Serializable]
public struct LocalBlockPos
{
    public int x, y, z;
    public int Index => x + y * 16 + z * 16 * 16;
    public LocalBlockPos(int x, int y, int z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }
    
    public static int MagDiff(LocalBlockPos a, LocalBlockPos b)
    {
        int dx = a.x - b.x;
        int dy = a.y - b.y;
        int dz = a.z - b.z;
        
        return Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz);
    }

    public static LocalBlockPos Origin => new LocalBlockPos(0, 0, 0);
    
    public int Hash() => z + (16 * y) + (256 * x);

    public override bool Equals(object obj)
    {
        if (!(obj is LocalBlockPos other)) return false;
        return x == other.x && y == other.y && z == other.z;
    }
    
    public string ToHexString()
    {

        char[] chars = new char[3];

        chars[0] = x.ToString("X")[0];
        chars[1] = y.ToString("X")[0];
        chars[2] = z.ToString("X")[0];
        
        return new string(chars);
    }

    public override int GetHashCode() => Hash();
}