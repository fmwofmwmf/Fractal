using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

public static class ChunkJobs
{
    [BurstCompile]
    public struct GenerateChildrenParallelJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Chunk> Parents;
        [ReadOnly] public UnsafeList<NativeArray<byte>> Leaves;
        [WriteOnly] public UnsafeList<byte> Tree;
        public NativeList<Chunk>.ParallelWriter NewChunks;

        public void Execute(int i)
        {
            Chunk p = Parents[i];
            if (p.Id == 0)
            {
                return;
            }
            
            Tree[p.Id] |= 1;
            var leaves = Leaves[p.Id];
            for (int x = 0; x < 16; x++)
            for (int y = 0; y < 16; y++)
            for (int z = 0; z < 16; z++)
            {
                var c = new Chunk(p, x, y, z, leaves[x + 16 * (y + 16 * z)]);
                NewChunks.AddNoResize(c);
            }
        }
    }
    
    [BurstCompile]
    public struct FetchChildrenJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<long> ParentHashes;       // parent chunk hashes
        [ReadOnly] public NativeArray<bool> Mask;
        [ReadOnly] public NativeParallelHashMap <long, bool> Tree;       // parent chunk hashes
        [NativeDisableParallelForRestriction]
        public NativeArray<byte> AllChunks;                  // flattened storage for results

        public void Execute(int i)
        {
            if (!Mask[i]) return;
            long parentHash = ParentHashes[i];
            int offset = i * 16 * 16 * 16;
            
            for (int x = 0; x < 16; x++)
            for (int y = 0; y < 16; y++)
            for (int z = 0; z < 16; z++)
            {
                var pos = new LocalBlockPos(x, y, z);
                var newH = BlockHasher.ExtendHash(parentHash, pos);
                if (Tree.TryGetValue(newH, out bool _))
                {
                    AllChunks[offset + pos.Index] = 1;
                }
            }
        }
    }

    [BurstCompile]
    public struct FetchNeighborhoodJob : IJobParallelFor
    {
        [ReadOnly] public UnsafeList<BlockPath> ParentHashes; // hash of the central chunk
        [ReadOnly] public NativeParallelHashMap <long, int> Tree; // all existing chunks
        [NativeDisableParallelForRestriction] public NativeArray<byte> AllChunks; // flattened 18^3 array

        public void Execute(int i)
        {
            if (!ParentHashes[i].Path.IsCreated) return;
            int size = 18; // from -1..16

            int resultOffset = i * 18 * 18 * 18;

            for (int x = -1; x <= 16; x++)
            for (int y = -1; y <= 16; y++)
            for (int z = -1; z <= 16; z++)
            {
                var pos = new LocalBlockPos(x, y, z); // maps to 0..17
                int flatIndex = (x+1) + size * ((y+1) + size * (z+1));
                var arr = ParentHashes[i].Add(pos, Allocator.Temp);
                var hash = BlockHasher.RectifyPath(arr);
                if (hash == 0) continue;
                if (Tree.TryGetValue(hash, out int _))
                {
                    AllChunks[resultOffset + flatIndex] = 1;
                }
            }
        }
    }
    
    [BurstCompile]
    public struct FetchNeighborsJob : IJobParallelFor
    {
        [ReadOnly] public UnsafeList<BlockPath> ParentHashes; // hash of the central chunk
        [ReadOnly] public UnsafeList<NativeArray<byte>> Leaves; // hash of the central chunk
        [ReadOnly] public NativeParallelHashMap <long, int> Tree; // all existing chunks
        [NativeDisableParallelForRestriction] public NativeArray<byte> AllChunks; // flattened 18^3 array

        public void Execute(int i)
        {
            if (!ParentHashes[i].Path.IsCreated) return;
            int size = 18; // from -1..16

            int resultOffset = i * 18 * 18 * 18;
            
            if (!Tree.TryGetValue(ParentHashes[i].Hash(), out int leafIndex))
            {
                Debug.Log("nuh");
                return;
            }
            var leaves = Leaves[leafIndex];
            if (!leaves.IsCreated) {
                Debug.Log("nuhuh");
                return;
            }
            
            for (int x = -1; x <= 16; x++)
            for (int y = -1; y <= 16; y++)
            for (int z = -1; z <= 16; z++)
            {
                //TODO fix
                
                int flatIndex = (x+1) + size * ((y+1) + size * (z+1));
                AllChunks[resultOffset + flatIndex] = 0;
                if (x >= 0 && y >= 0 && z >= 0 && x < 16 && y < 16 && z < 16)
                {
                    
                    
                    int flatIndex1 = x + 16 * (y + 16 * z);
                    
                    if (leaves[flatIndex1] != 0)
                    {
                        AllChunks[resultOffset + flatIndex] = 1;
                    }
                }
            }
        }
    }
}
