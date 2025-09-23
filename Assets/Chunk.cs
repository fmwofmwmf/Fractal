using System;
using UnityEngine;
using System.Collections.Generic;
using JetBrains.Annotations;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Profiling;
using UnityEngine.SocialPlatforms;
using Random = UnityEngine.Random;

/// <summary>
/// Represents a chunk in a hierarchical voxel world.
/// Each chunk may have children, recursively subdividing space.
/// </summary>
[BurstCompile]
public partial struct Chunk
{
    public int Id;
    public int ParentId;
    public Chunk Parent
    {
        get
        {
            if (ParentId == 0) return new();
            return ChunkTree.instance.GetById(ParentId);
        }
    }

    public bool Populated => ChunkTree.instance.BlockChanges.TryGetValue(Id, out byte p);

    public LocalBlockPos LocalPos;
    public BlockPath Path => ChunkTree.instance.GetPath(this);

    public long Hash {get; private set;}
    
    public int Depth => Path.Depth;
    public bool IsEmpty => Id == 0;

    public Chunk(Chunk parent, LocalBlockPos localPos)
    {
        ParentId = parent.Id;
        LocalPos = localPos;
        Hash = 0;
        Id = 0;
        var p = parent;
        Hash = BlockHasher.ExtendHash(p.Hash, localPos);
    }
    
    public Chunk(Chunk parent, int x, int y, int z)
    {
        ParentId = parent.Id;
        LocalPos = new LocalBlockPos(x, y, z);
        Hash = 0;
        Id = 0;
        
        var p = parent;
        Hash = BlockHasher.ExtendHash(p.Hash, x, y, z);
    }

    /// <summary>
    /// Ensures children exist. Randomly populates children for demonstration.
    /// </summary>
    public void EnsureChildren()
    {
        if (Id == 0)
        {
            Debug.Log("Not in tree!");
        }
        if (Populated) return;
        ChunkTree.instance.BlockChanges[Id] = 0;
        
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        {
            if (!GenerateChunk(x, y, z)) continue;
            Profiler.BeginSample("Create Chunk");
			var c = new Chunk(this, x, y, z);
            Profiler.EndSample();
            Profiler.BeginSample("Add Chunk");
            ChunkTree.instance.Add(ref c);
            Profiler.EndSample();
        }
    }
    
    public void EnsureChildrenParallel(NativeList<Chunk> list)
    {
        if (Id == 0)
        {
            Debug.Log("Not in tree!");
        }
        if (ChunkTree.instance.BlockChanges.TryGetValue(Id, out byte _)) return;
        ChunkTree.instance.BlockChanges[Id] = 0;
        
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        {
            if (!GenerateChunk(x, y, z)) continue;
            Profiler.BeginSample("Create Chunk");
            var c = new Chunk(this, x, y, z);
            Profiler.EndSample();
            Profiler.BeginSample("Add Chunk");
            list.Add(c);
            Profiler.EndSample();
        }
    }
    
    public static NativeList<Chunk> GenerateChildrenParallel(NativeArray<Chunk> list)
    {
        NativeList<Chunk> child = new NativeList<Chunk>(Allocator.TempJob);
        child.SetCapacity(list.Length * 16 * 16 * 16);
        var childJob = new GenerateChildrenParallelJob()
        {
            NewChunks = child.AsParallelWriter(),
            Parents = list,
            Tree = ChunkTree.instance.BlockChanges.AsParallelWriter(),
        };
        childJob.Schedule(list.Length, 64).Complete();
        return child;
    }

    public override string ToString()
    {
        return Path.ToHexString();
    }

    /// <summary>
    /// Returns the world position of this chunk's pivot (corner).
    /// </summary>
    public Vector3 GetWorldPosition()
    {
        Vector3 pos = Vector3.zero;
        float scale = 1;

        for (int i = 0; i < Path.Depth; i++)
        {
            pos += new Vector3(Path.Path[i].x, Path.Path[i].y, Path.Path[i].z) * scale;
            scale /= 16f; // Each child is 1/16 the size of its parent
        }

        return pos;
    }


    /// <summary>
    /// Returns all children (for iteration, meshing, etc.).
    /// </summary>
    public IEnumerable<Chunk> Children
    {
        get
        {
            for (int x = 0; x < 16; x++)
            for (int y = 0; y < 16; y++)
            for (int z = 0; z < 16; z++)
            {
                var b = ChunkTree.instance.TryGetChunk(Path.HashWithChild(new LocalBlockPos(x, y, z)), out Chunk child);
                if (b) yield return child;
            }
        }
    }
    
    [BurstCompile]
    public struct GenerateChildrenParallelJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Chunk> Parents;
        [WriteOnly] public NativeParallelHashMap <int, byte>.ParallelWriter Tree;
        public NativeList<Chunk>.ParallelWriter NewChunks;

        public void Execute(int i)
        {
            Chunk p = Parents[i];
            if (p.Id == 0)
            {
                return;
            }
            
            //if (Tree.TryGetValue(p.Hash, out bool _)) return;
            Tree.TryAdd(p.Id, 0);
        
            for (int x = 0; x < 16; x++)
            for (int y = 0; y < 16; y++)
            for (int z = 0; z < 16; z++)
            {
                if (!GenerateChunk(x, y, z)) continue;
                var c = new Chunk(p, x, y, z);
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
}
