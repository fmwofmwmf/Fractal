using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Profiling;


public class ChunkTree
{
    public static ChunkTree instance = new ChunkTree();
    public const int Size = 1048576 * 16 * 2;
    public NativeParallelHashMap <long, int> ActiveChunks = new (Size, Allocator.Persistent);
    public NativeParallelHashMap <long, bool> BlockData = new (Size, Allocator.Persistent);
    public NativeParallelHashMap <long, bool> BlockChanges = new (Size, Allocator.Persistent);
    private NativePool<Chunk> Chunks = new (Size, Allocator.Persistent);
    //private NativePool<BlockPath> BlockPaths = new (Size, Allocator.Persistent);
    //private NativeParallelHashMap<int, Chunk> Chunks = new (Size, Allocator.Persistent);
    private JobHandle RunningJobs;
    public void Add(ref Chunk chunk)
    {
        
        var hash = chunk.Hash;
        if (chunk.Id != 0)
        {
            Debug.Log($"huh: {hash}, {chunk} / {hash} {ActiveChunks[hash]}");
            return;
        }
        
        //Profiler.BeginSample("Add Operation 1");
        //ActivePaths[hash] = chunk.Path;
        BlockData[hash] = true;
        chunk.Id = Chunks.Alloc();
        //if (BlockPaths.Alloc() != chunk.Id) Debug.LogError("uh oh");
        Chunks[chunk.Id] = chunk;
        //Profiler.EndSample();
        
        //Profiler.BeginSample("Add Operation 2");
        ActiveChunks[hash] = chunk.Id;
        //Profiler.EndSample();
    }

    public void AddBatch(NativeList<Chunk> source)
    {
        var writer = Chunks.AsParallelWriter(16*16*16, Allocator.TempJob);
        var childJob = new AddBatchJob
        {
            Source = source,
            ChunkIds = ActiveChunks.AsParallelWriter(),
            BlockData = BlockData.AsParallelWriter(),
            Target = writer
        };
        JobHandle handle = childJob.Schedule(source.Length, 16*16*16);
        JobHandle disposeHandle = source.Dispose(handle);
        disposeHandle.Complete();
        int x = Chunks.CommitParallelLength(writer);
        for (int i = 0; i < x; i++)
        {
            //BlockPaths.Alloc();
        }
        //RunningJobs = JobHandle.CombineDependencies(RunningJobs, handle, disposeHandle);
    }

    public void FinishBatch()
    {
        RunningJobs.Complete();
        RunningJobs = new();
    }

    public BlockPath GetPath(Chunk chunk)
    {
        var id = chunk.Id;
        //var bp = BlockPaths[id];
        // if (bp.Path.IsCreated)
        // {
        //     return bp;
        // }

        BlockPath p;
        if (chunk.Parent.IsEmpty)
        {
            p = PathOps.FromPositions(chunk.LocalPos);
        }
        else
        {
            var parentPath = chunk.Parent.Path;
            var len = parentPath.Depth;
            var arr = new NativeArray<LocalBlockPos>(len + 1, Allocator.Persistent);
            for (int i = 0; i < len; i++)
                arr[i] = parentPath.Path[i];
            arr[len] = chunk.LocalPos;
            p = new BlockPath(arr);
        }
        //BlockPaths[id] = p;
        return p;
    }
    
    [BurstCompile]
    public struct AddBatchJob : IJobParallelFor
    {
        [ReadOnly] public NativeList<Chunk> Source;
        [NativeDisableParallelForRestriction][WriteOnly] public NativeParallelHashMap <long, int>.ParallelWriter ChunkIds;
        [NativeDisableParallelForRestriction][WriteOnly] public NativeParallelHashMap <long, bool>.ParallelWriter BlockData;
        [NativeDisableParallelForRestriction] public NativePool<Chunk>.ParallelWriter Target;

        public void Execute(int i)
        {
            Chunk c = Source[i];
            var hash = Source[i].Hash;
            BlockData.TryAdd(hash, true);
            
            ref Chunk chunk = ref Target.AllocRef(out int id);
            chunk = c;
            chunk.Id = id;
            ChunkIds.TryAdd(hash, id);
        }
    }

    public Chunk GetById(int id)
    {
        if (id < 0) return new();
        return Chunks[id];
    }
    
    public void ForceAdd(Chunk chunk, int id)
    {
        var hash = chunk.Path.Hash();
        ActiveChunks[hash] = id;
        Chunks[id] = chunk;
        //ActivePaths[hash] = chunk.Path;
        BlockData[hash] = true;
    }

    // public static long RectifyPath(BlockPath path)
    // {
    //     if (path.Path.Length == 0) return 0;
    //     
    //     // Start from root hash
    //     long hash = 0;
    //     for (int depth = 0; depth < path.Path.Length; depth++)
    //     {
    //         var pos = path.Path[depth];
    //
    //         int cx = pos.x;
    //         int cy = pos.y;
    //         int cz = pos.z;
    //
    //         // Only allow top-level chunk at 0,0,0
    //         if (depth == 0 && (cx != 0 || cy != 0 || cz != 0)) return 0;
    //
    //         // Compute displacement for coordinates outside 0..15
    //         int dx = Mathf.FloorToInt((float)cx / 16f);
    //         int dy = Mathf.FloorToInt((float)cy / 16f);
    //         int dz = Mathf.FloorToInt((float)cz / 16f);
    //
    //         // Wrap into 0..15
    //         cx = ((cx % 16) + 16) % 16;
    //         cy = ((cy % 16) + 16) % 16;
    //         cz = ((cz % 16) + 16) % 16;
    //
    //         // Displace hash for parent if needed
    //         if (dx != 0 || dy != 0 || dz != 0)
    //         {
    //             hash = BlockHasher.DisplaceHash(hash, dx, dy, dz);
    //         }
    //
    //         hash = BlockHasher.ExtendHash(hash, new LocalBlockPos(cx, cy, cz));
    //     }
    //
    //     return hash;
    // }

    public bool TryGetChunk(BlockPath path, out Chunk chunk)
    {
        return TryGetChunk(BlockHasher.RectifyPath(path), out chunk);
    }
    
    public bool TryGetChunk(long hash, out Chunk chunk)
    {
        bool b = ActiveChunks.TryGetValue(hash, out int id);
        //Debug.Log(id);
        //Debug.Log(Chunks.Count());
        if (b)
        {
            chunk = Chunks[id];
            if (chunk.Hash != hash) Debug.Log("huh");
        }
        else chunk = new Chunk();
        return b;
    }
    
    public void Dispose()
    {
        // foreach (var path in ActivePaths.GetValueArray(Allocator.Temp))
        // {
        //     path.Dispose();
        // }
        //PathCache.Dispose();
        ActiveChunks.Dispose();
        BlockData.Dispose();
        BlockChanges.Dispose();
        Chunks.Dispose();
        //BlockPaths.Dispose();
    }
}
