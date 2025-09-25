using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public static class BlockUtils
{
    public static Vector3 GetWorldPosition(Block block)
    {
        return GetWorldPosition(block.Path);
    }
    
    public static Vector3 GetWorldPosition(NativeArray<int3> path)
    {
        Vector3 pos = Vector3.zero;
        float scale = 1;

        for (int i = 0; i < path.Length; i++)
        {
            pos += new Vector3(path[i].x, path[i].y, path[i].z) * scale;
            scale /= 16f; // Each child is 1/16 the size of its parent
        }

        return pos;
    }
    
    [BurstCompile]
    public static int ShellDistance(NativeArray<int3> pathA, NativeArray<int3> pathB)
    {
        int depth = math.min(pathA.Length, pathB.Length);

        int divDepth = -1;
        for (int d = 0; d < depth; d++)
        {
            if (!pathA[d].Equals(pathB[d]))
            {
                divDepth = d;
                break;
            }
        }

        if (divDepth == -1)
            return 0;

        int suffixDepthA = pathA.Length - divDepth;
        int suffixDepthB = pathB.Length - divDepth;
        int suffixDepth = math.min(suffixDepthA, suffixDepthB);

        int3 coordA = FlattenSuffix(pathA, divDepth, suffixDepth);
        int3 coordB = FlattenSuffix(pathB, divDepth, suffixDepth);

        int dx = math.abs(coordA.x - coordB.x);
        int dy = math.abs(coordA.y - coordB.y);
        int dz = math.abs(coordA.z - coordB.z);

        return dx + dy + dz;
    }
    
    private static int3 FlattenSuffix(NativeArray<int3> path, int divDepth, int suffixDepth)
    {
        int3 result = int3.zero;
        for (int i = 0; i < suffixDepth; i++)
        {
            result *= 16;
            result += path[divDepth + i];
        }
        return result;
    }
    
    public static string ToHexString(Block b)
    {
        if (b.Depth == 0) return "";

        char[] chars = new char[b.Depth * 3];
        for (int i = 0; i < b.Depth; i++)
        {
            var pos = b.Path[i];
            int offset = i * 3;
            chars[offset] = pos.x.ToString("X")[0];
            chars[offset + 1] = pos.y.ToString("X")[0];
            chars[offset + 2] = pos.z.ToString("X")[0];
        }

        return new string(chars);
    }
    
    [BurstCompile]
    public struct FetchNeighborsJob : IJobParallelFor
    {
        [ReadOnly] public UnsafeList<NativeArray<int3>> ParentPath;
        [ReadOnly] public UnsafeList<NativeArray<byte>> Leaves;
        [ReadOnly] public NativeParallelHashMap <long, int> Tree;
        [NativeDisableParallelForRestriction] public NativeArray<byte> AllChunks;

        public void Execute(int i)
        {
            // if (!ParentPath[i].Path.IsCreated) return;
            // int size = 18; // from -1..16
            //
            // int resultOffset = i * 18 * 18 * 18;
            //
            // if (!Tree.TryGetValue(ParentPath[i].Hash(), out int leafIndex))
            // {
            //     Debug.Log("nuh");
            //     return;
            // }
            // var leaves = Leaves[leafIndex];
            // if (!leaves.IsCreated) {
            //     Debug.Log("nuhuh");
            //     return;
            // }
            //
            // for (int x = -1; x <= 16; x++)
            // for (int y = -1; y <= 16; y++)
            // for (int z = -1; z <= 16; z++)
            // {
            //     //TODO fix
            //     
            //     int flatIndex = (x+1) + size * ((y+1) + size * (z+1));
            //     AllChunks[resultOffset + flatIndex] = 0;
            //     if (x >= 0 && y >= 0 && z >= 0 && x < 16 && y < 16 && z < 16)
            //     {
            //         
            //         
            //         int flatIndex1 = x + 16 * (y + 16 * z);
            //         
            //         if (leaves[flatIndex1] != 0)
            //         {
            //             AllChunks[resultOffset + flatIndex] = 1;
            //         }
            //     }
            // }
        }
    }
}
