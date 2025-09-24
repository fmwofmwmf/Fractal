using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Profiling;


public class ChunkTree
{
    public static ChunkTree instance = new ChunkTree();
    public ChunkMeshRenderer renderer;
    
    public const int Size = 1048576 * 16 * 2;
    public NativeParallelHashMap <long, int> ActiveChunks = new (Size, Allocator.Persistent);
    public NativePool<byte> BlockChanges = new (Size, Allocator.Persistent);
    private NativePool<Chunk> _chunks = new (Size, Allocator.Persistent);
    private NativePool<BlockPath> _blockPaths = new (Size, Allocator.Persistent);
    private NativePool<NativeArray<byte>> _chunkLeaves = new (Size, Allocator.Persistent);

    public UnsafeList<NativeArray<byte>> Leaves => _chunkLeaves.List;
    public void Add(ref Chunk chunk)
    {
        
        var hash = chunk.Hash;
        if (chunk.Id != 0)
        {
            Debug.Log($"huh: {hash}, {chunk} / {hash} {ActiveChunks[hash]}");
            return;
        }
        
        chunk.Id = _chunks.Allocate();
        if (_blockPaths.Allocate() != chunk.Id) Debug.LogError("uh oh");
        if (_chunkLeaves.Allocate() != chunk.Id) Debug.LogError("uh oh");
        if (BlockChanges.Allocate() != chunk.Id) Debug.LogError("uh oh");
        _chunks[chunk.Id] = chunk;
        if (_chunkLeaves[chunk.Id].IsCreated) Debug.LogError("already generated?");
        _chunkLeaves[chunk.Id] = chunk.GenerateChildren();

        ActiveChunks[hash] = chunk.Id;
    }
    
    public void Add(Chunk chunk)
    {
        var hash = chunk.Hash;
        if (chunk.Id != 0)
        {
            Debug.Log($"huh: {hash}, {chunk} / {hash} {ActiveChunks[hash]}");
            return;
        }
        
        chunk.Id = _chunks.Allocate();
        if (_blockPaths.Allocate() != chunk.Id) Debug.LogError("uh oh");
        if (_chunkLeaves.Allocate() != chunk.Id) Debug.LogError("uh oh");
        if (BlockChanges.Allocate() != chunk.Id) Debug.LogError("uh oh");
        if (!_chunks[chunk.Id].IsEmpty)
        {
            Debug.Log($"{_chunks[chunk.Id]} lives here new:{chunk}");
        }
        _chunks[chunk.Id] = chunk;
        if (_chunkLeaves[chunk.Id].IsCreated)
        {
            
            Debug.LogError($"already generated? {BlockChanges[chunk.Id]} {_blockPaths[chunk.Id].ToHexString()}");
        }
        _chunkLeaves[chunk.Id] = chunk.GenerateChildren();

        ActiveChunks[hash] = chunk.Id;
    }

    public void AddBatch(NativeList<Chunk> source)
    {
        for (int i = 0; i < source.Length; i++)
        {
            Add(source[i]);
        }
        // int b = _chunks.FreeCount;
        // var childJob = new AddBatchJob
        // {
        //     Source = source,
        //     ChunkIds = ActiveChunks.AsParallelWriter(),
        //     Target = _chunks
        // };
        // childJob.Schedule(source.Length, 16*16*16).Complete();
        //
        // for (int i = 0; i < b - _chunks.FreeCount; i++)
        // {
        //     _blockPaths.Allocate();
        //     _chunkLeaves.Allocate();
        //     BlockChanges.Allocate();
        // }
        //
        // for (int i = 0; i < source.Length; i++)
        // {
        //     var c = source[i];
        //     if (_chunkLeaves[c.Id].IsCreated) Debug.Log($"already generated? {_chunkLeaves[c.Id].Length},{c.Id}"); // true
        //     _chunkLeaves[c.Id] = c.GenerateChildren();
        // }

        source.Dispose();
    }

    public void UnRenderChunk(Chunk chunk)
    {
        renderer.RemoveChunk(chunk);
        if (chunk.Populated)
        {
            foreach (var c in chunk.Children)
            {
                UnRenderChunk(c);
            }
        }
        BlockChanges[chunk.Id] &= 0b11111001;
    }

    private void RemoveChunk(Chunk chunk)
    {
        _blockPaths[chunk.Id].Dispose();
        _chunkLeaves[chunk.Id].Dispose();
        renderer.RemoveChunk(chunk);
        
        ActiveChunks.Remove(chunk.Hash);
        _blockPaths.Free(chunk.Id);
        _chunks.Free(chunk.Id);
        _chunkLeaves.Free(chunk.Id);
        BlockChanges.Free(chunk.Id);
        Debug.Assert(_chunks[chunk.Id].IsEmpty);
    }
    
    private void Delete(Chunk chunk)
    {
        Depopulate(chunk);
        RemoveChunk(chunk);
    }

    public void Depopulate(Chunk chunk)
    {
        if (chunk.Populated)
        {
            foreach (var c in chunk.Children)
            {
                Delete(c);
            }
        }

        BlockChanges[chunk.Id] &= 0b11111110;
    }

    public BlockPath GetPath(Chunk chunk)
    {
        var id = chunk.Id;
        var bp = _blockPaths[id];
         if (bp.Path.IsCreated)
         {
             return bp;
         }

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

        _blockPaths[id] = p;
        return p;
    }
    
    [BurstCompile]
    public struct AddBatchJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction] public NativeList<Chunk> Source;
        [NativeDisableParallelForRestriction][WriteOnly] public NativeParallelHashMap <long, int>.ParallelWriter ChunkIds;
        [NativeDisableParallelForRestriction] public NativePool<Chunk> Target;

        public void Execute(int i)
        {
            Chunk c = Source[i];
            var hash = Source[i].Hash;
            
            c.Id = Target.Allocate();
            Target[c.Id] = c;
            Source[i] = c;
            if (!ChunkIds.TryAdd(hash, c.Id)) Debug.Log("???");
        }
    }

    public Chunk GetById(int id)
    {
        if (id < 0) return new();
        return _chunks[id];
    }
    
    public bool TryGetChunk(BlockPath path, out Chunk chunk)
    {
        return TryGetChunk(BlockHasher.RectifyPath(path), out chunk);
    }
    
    public bool TryGetChunk(long hash, out Chunk chunk)
    {
        bool b = ActiveChunks.TryGetValue(hash, out int id);
        if (b)
        {
            chunk = _chunks[id];
            if (chunk.Hash != hash) Debug.Log($"hash collision? hash:{hash}->{id}, chunk:{chunk.Hash},{chunk.Id}");
        }
        else chunk = new Chunk();
        return b;
    }
    
    public void Dispose()
    {
        foreach (var path in _blockPaths.List)
        {
            path.Dispose();
        }
        foreach (var l in _chunkLeaves.List)
        {
            l.Dispose();
        }
        ActiveChunks.Dispose();
        BlockChanges.Dispose();
        _chunks.Dispose();
        _blockPaths.Dispose();
        _chunkLeaves.Dispose();
    }
}
