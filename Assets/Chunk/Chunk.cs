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
public struct Chunk
{
    public int Id;
    public int ParentId;
    public byte Type;
    public Chunk Parent
    {
        get
        {
            if (ParentId == 0) return new();
            return ChunkTree.instance.GetById(ParentId);
        }
    }

    public byte RenderState => (byte)((ChunkTree.instance.BlockChanges[Id] >> 1) & 0b11); // 0: none, 1: low res 2: high res 3: subdivided
    
    public bool Populated => (ChunkTree.instance.BlockChanges[Id] & 1) != 0;

    public LocalBlockPos LocalPos;
    public BlockPath Path => ChunkTree.instance.GetPath(this);

    public long Hash {get; private set;}
    
    public int Depth => Path.Depth;
    public bool IsEmpty => Id == 0;
    
    public Chunk(Chunk parent, int x, int y, int z, byte blockType)
    {
        ParentId = parent.Id;
        LocalPos = new LocalBlockPos(x, y, z);
        Hash = 0;
        Id = 0;
        Type = blockType;
        var p = parent;
        Hash = BlockHasher.ExtendHash(p.Hash, x, y, z);
    }

    public NativeArray<byte> GenerateChildren()
    {
        NativeArray<byte> leaves = new NativeArray<byte>(16*16*16, Allocator.Persistent);
        Profiler.BeginSample("Create Leaf");
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        {
            byte b = Generation.GenerateChunk(this, x, y, z);
            leaves[x + 16 * (y + 16 * z)] = b;
        }
        Profiler.EndSample();
        return leaves;
    }
    
   //  public void EnsureChildren()
   //  {
   //      if (Id == 0)
   //      {
   //          Debug.Log("Not in tree!");
   //      }
   //      if (Populated) return;
   //      ChunkTree.instance.BlockChanges[Id] = 0;
   //      
   //      for (int x = 0; x < 16; x++)
   //      for (int y = 0; y < 16; y++)
   //      for (int z = 0; z < 16; z++)
   //      {
   //          if (!Generation.GenerateChunk(this, x, y, z)) continue;
   //          Profiler.BeginSample("Create Chunk");
			// var c = new Chunk(this, x, y, z);
   //          Profiler.EndSample();
   //          Profiler.BeginSample("Add Chunk");
   //          ChunkTree.instance.Add(ref c);
   //          Profiler.EndSample();
   //      }
   //  }
    
    public static NativeList<Chunk> GenerateChildrenParallel(NativeArray<Chunk> list)
    {
        NativeList<Chunk> child = new NativeList<Chunk>(Allocator.TempJob);
        child.SetCapacity(list.Length * 16 * 16 * 16);
        var childJob = new ChunkJobs.GenerateChildrenParallelJob()
        {
            Leaves = ChunkTree.instance.Leaves,
            NewChunks = child.AsParallelWriter(),
            Parents = list,
            Tree = ChunkTree.instance.BlockChanges.List,
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
    
    public Vector3 GetRelativeWorldPosition(BlockPath origin)
    {
        Vector3 pos = Vector3.zero;
        float scale = math.pow(16, origin.Depth-1);

        for (int i = 0; i < math.max(Path.Depth, origin.Depth); i++)
        {
            if (i < Path.Depth) pos += new Vector3(Path.Path[i].x, Path.Path[i].y, Path.Path[i].z) * scale;
            if (i < origin.Depth) pos -= new Vector3(origin.Path[i].x, origin.Path[i].y, origin.Path[i].z) * scale;
            scale /= 16f; // Each child is 1/16 the size of its parent
        }

        return pos;
    }

    // Helper: convert path to coordinates in [0, 16^depth)
    private static Vector3 FlattenToDepth(int depth, BlockPath path)
    {
        Vector3 pos = Vector3.zero;
        for (int i = 0; i < path.Depth; i++)
        {
            float factor = Mathf.Pow(16, depth - i - 1);
            pos += new Vector3(path.Path[i].x, path.Path[i].y, path.Path[i].z) * factor;
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
                else Debug.LogError("chunks arent supposed to be missing elements!");
            }
        }
    }
}
