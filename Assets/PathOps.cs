using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

public static class PathOps
{
    /// <summary>
    /// Computes the "shell distance" between two paths.
    /// Each step is a difference in the corresponding local block position.
    /// </summary>
    public static int ShellDistance(this BlockPath a, BlockPath b)
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
    
    /// <summary>
    /// Creates a BlockPath from a sequence of LocalBlockPos.
    /// The first element should be the root, last the deepest child.
    /// </summary>
    public static BlockPath FromPositions(params LocalBlockPos[] positions)
    {
        var path = new BlockPath();
        path.Path = new NativeArray<LocalBlockPos>(positions.Length, Allocator.Persistent);
        for (int i = 0; i < positions.Length; i++)
        {
            var p = path.Path;
            p[i] = positions[i];
        }
            
        return path;
    }
}