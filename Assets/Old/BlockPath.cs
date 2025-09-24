using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Represents a path from root to a specific chunk.
/// Useful as a dictionary key or unique identifier.
/// </summary>
[Serializable]
public struct BlockPath
{
    // triples of coordinates [0...15], getting finer
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
    
    public BlockPath Displace(LocalBlockPos p)
    {
        return Displace(p.x, p.y, p.z);
    }

    public BlockPath Displace(int x, int y, int z)
    {
        var p = Path;
        p[^1] += new LocalBlockPos(x, y, z);
        return this;
    }
    
    [BurstCompile]
    public static BlockPath RectifyPathToCoords(BlockPath path, Allocator allocator)
    {
        int depth = path.Depth;
        if (depth == 0)
            return new BlockPath();

        NativeArray<LocalBlockPos> rectified = new NativeArray<LocalBlockPos>(depth, allocator);

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

            // Propagate carries upward (leaf → root)
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
                    {
                        // root is not zero → invalid path, return empty array
                        rectified.Dispose();
                        return new BlockPath();
                    }
                }
            }

            // Copy into the NativeArray
            for (int i = 0; i < depth; i++)
            {
                rectified[i] = new LocalBlockPos { x = xs[i], y = ys[i], z = zs[i] };
            }
        }

        return new BlockPath(rectified);
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
    
    public LocalBlockPos(int3 p)
    {
        this.x = p.x;
        this.y = p.y;
        this.z = p.z;
    }

    public static LocalBlockPos operator +(LocalBlockPos t, LocalBlockPos other)
    {
        return new LocalBlockPos(t.x + other.x, t.y + other.y, t.z + other.z);
    }
    
    public static implicit operator int3(LocalBlockPos t)
    {
        return new int3(t.x, t.y, t.z);
    }
    
    public static int MagDiff(LocalBlockPos a, LocalBlockPos b)
    {
        int dx = a.x - b.x;
        int dy = a.y - b.y;
        int dz = a.z - b.z;
        
        return Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz);
    }
    
    public static int MagDiffWrapped(LocalBlockPos a, LocalBlockPos b)
    {
        int dx = math.abs(a.x - b.x);
        int dy = math.abs(a.y - b.y);
        int dz = math.abs(a.z - b.z);
        
        dx = math.min(dx, 16 - dx);
        dy = math.min(dy, 16 - dy);
        dz = math.min(dz, 16 - dz);
        
        return dx + dy + dz;
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