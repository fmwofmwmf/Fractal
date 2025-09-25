using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public static class PathOps
{
    /// <summary>
    /// Computes the "shell distance" between two paths.
    /// Each step is a difference in the corresponding local block position.
    /// </summary>
    public static int StellDistance(this ChunkPath a, ChunkPath b)
    {
        int minDepth = Mathf.Min(a.Depth, b.Depth);

        // Check if one path is ancestor of the other
        bool aIsAncestor = true;
        bool bIsAncestor = true;
        for (int i = 0; i < minDepth; i++)
        {
            if (!a.Path[i].Equals(b.Path[i])) aIsAncestor = false;
            if (!a.Path[i].Equals(b.Path[i])) bIsAncestor = false;
        }
        if (aIsAncestor || bIsAncestor) return 0;

        // Compute distance at common depth
        int dist = 0;
        for (int i = 0; i < minDepth; i++)
        {
            dist += Mathf.Abs(a.Path[i].x - b.Path[i].x)
                    + Mathf.Abs(a.Path[i].y - b.Path[i].y)
                    + Mathf.Abs(a.Path[i].z - b.Path[i].z);
        }

        // // Optional: include extra positions for deeper levels
        // if (a.Depth > minDepth)
        // {
        //     for (int i = minDepth; i < a.Depth; i++)
        //         dist += a.Path[i].x + a.Path[i].y + a.Path[i].z;
        // }
        // if (b.Depth > minDepth)
        // {
        //     for (int i = minDepth; i < b.Depth; i++)
        //         dist += b.Path[i].x + b.Path[i].y + b.Path[i].z;
        // }

        return dist;
    }
    
    public static int ShellDista(this ChunkPath pathA, ChunkPath pathB)
    {
        int commonDepth = math.min(pathA.Depth-1, pathB.Depth-1);

        int diff = 0;
        int scale = 1;

        // Walk upward from common depth to root
        for (int d = commonDepth; d >= 0; d--)
        {
            diff += LocalBlockPos.MagDiffWrapped(pathA.Path[d], pathB.Path[d]) * scale;
            scale *= 16;
        }
        
        return diff;
    }
    
    [BurstCompile]
    public static int ShellDistance(this ChunkPath pathA, ChunkPath pathB)
    {
        int depth = math.min(pathA.Depth, pathB.Depth);

        // 1. find divergence depth
        int divDepth = -1;
        for (int d = 0; d < depth; d++)
        {
            if (!pathA.Path[d].Equals(pathB.Path[d]))
            {
                divDepth = d;
                break;
            }
        }

        // if completely equal up to min depth
        if (divDepth == -1)
            return 0;

        int suffixDepthA = pathA.Depth - divDepth;
        int suffixDepthB = pathB.Depth - divDepth;
        int suffixDepth = math.min(suffixDepthA, suffixDepthB);

        // 2. flatten suffix to coordinates
        int3 coordA = FlattenSuffix(pathA, divDepth, suffixDepth);
        int3 coordB = FlattenSuffix(pathB, divDepth, suffixDepth);

        // 3. compute wrapped difference
        int dx = math.abs(coordA.x - coordB.x);
        int dy = math.abs(coordA.y - coordB.y);
        int dz = math.abs(coordA.z - coordB.z);

        return dx + dy + dz;
    }

    private static int3 FlattenSuffix(ChunkPath path, int divDepth, int suffixDepth)
    {
        int3 result = int3.zero;
        for (int i = 0; i < suffixDepth; i++)
        {
            result *= 16;
            result += path.Path[divDepth + i];
        }
        return result;
    }
    
    /// <summary>
    /// Creates a BlockPath from a sequence of LocalBlockPos.
    /// The first element should be the root, last the deepest child.
    /// </summary>
    public static ChunkPath FromPositions(params LocalBlockPos[] positions)
    {
        var path = new ChunkPath();
        path.Path = new NativeArray<LocalBlockPos>(positions.Length, Allocator.Persistent);
        for (int i = 0; i < positions.Length; i++)
        {
            var p = path.Path;
            p[i] = positions[i];
        }
            
        return path;
    }
}