using Unity.Mathematics;
using Unity.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

public static class GpuTreeBuilder
{

    // public static void BuildGpuArrays(
    //     NativeArray<Block> blocks,
    //     int rootId,
    //     out List<TreeRaytrace.GPUBlockDataPadded> gpuBlocks,
    //     out List<int> gpuChildren,
    //     out List<int> gpuLeaves)
    // {
    //     gpuBlocks  = new List<TreeRaytrace.GPUBlockDataPadded>();
    //     gpuChildren = new List<int>();
    //     gpuLeaves  = new List<int>();
    //     
    //     // Map sparse Block.Id -> dense gpu index
    //     var idToGpuIndex = new Dictionary<int, int>();
    //
    //     // For quick lookup: sparse Id -> index into blocks array
    //     var idToArrayIndex = new Dictionary<int, int>(blocks.Length);
    //     for (int i = 0; i < blocks.Length; i++)
    //     {
    //         idToArrayIndex[blocks[i].Id] = i;
    //     }
    //
    //     var stack = new Stack<(int blockId, int depth)>();
    //     stack.Push((rootId, 0));
    //
    //     while (stack.Count > 0)
    //     {
    //         var (blockId, depth) = stack.Pop();
    //
    //         if (!idToArrayIndex.TryGetValue(blockId, out int arrayIndex))
    //             continue; // invalid Id
    //
    //         Block b = blocks[arrayIndex];
    //
    //         if (idToGpuIndex.ContainsKey(blockId))
    //             continue; // already processed
    //
    //         int myIndex = gpuBlocks.Count;
    //         idToGpuIndex[blockId] = myIndex;
    //
    //         TreeRaytrace.GPUBlockDataPadded gpu = new TreeRaytrace.GPUBlockDataPadded
    //         {
    //             Type         = b.Type,
    //             RenderState  = b.Rendered ? 1 : 0,
    //             Depth        = depth,
    //             ChildrenOffset = -1,
    //             LeavesOffset   = -1
    //         };
    //
    //         // Children
    //         if (b.Children.IsCreated && b.Children.Length > 0)
    //         {
    //             gpu.ChildrenOffset = gpuChildren.Count;
    //
    //             for (int i = 0; i < b.Children.Length; i++)
    //             {
    //                 int childId = b.Children[i];
    //                 gpuChildren.Add(childId); // placeholder
    //                 stack.Push((childId, depth + 1));
    //             }
    //         }
    //
    //         // Leaves
    //         if (b.Leaves.Length > 0)
    //         {
    //             gpu.LeavesOffset = gpuLeaves.Count;
    //             for (int i = 0; i < b.Leaves.Length; i++)
    //                 gpuLeaves.Add(b.Leaves[i]);
    //         }
    //
    //         gpuBlocks.Add(gpu);
    //     }
    //
    //     // Fix children to dense indices
    //     for (int i = 0; i < gpuChildren.Count; i++)
    //     {
    //         int originalId = gpuChildren[i];
    //         if (idToGpuIndex.TryGetValue(originalId, out int mapped))
    //             gpuChildren[i] = mapped;
    //         else
    //             gpuChildren[i] = -1; // missing/unreachable
    //     }
    // }
    
    public static void BuildGpuArraysFat(
        NativeArray<Block> blocks, out NativeArray<TreeRaytrace.GPUBlockDataPadded> gpuBlocks)
    {
        gpuBlocks  = new NativeArray<TreeRaytrace.GPUBlockDataPadded>(blocks.Length, Allocator.Persistent);
        
        for (int i = 0; i < blocks.Length; i++)
        {
            var b = blocks[i];
            if (!b.IsValid)
            {
                gpuBlocks[i] = new();
                continue;
            }
            TreeRaytrace.GPUBlockDataPadded data = TreeRaytrace.GPUBlockDataPadded.FromBlock(b);
            gpuBlocks[i] = data;
        }
    }
}
