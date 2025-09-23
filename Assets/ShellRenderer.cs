using System;
using UnityEngine;
using System.Collections.Generic;
using JetBrains.Annotations;
using TMPro;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine.Profiling;

/// <summary>
/// Renders a chunk world in "shells" around one or more center chunks,
/// with decreasing resolution for farther-away chunks.
/// </summary>
public class ShellRenderer : MonoBehaviour
{
    [Header("Settings")]
    public int processPerFrame = 100; // how many chunks to process per frame
    public int renderPerFrame = 10; // how many chunks to process per frame
    public ChunkMeshRenderer manager; // handles meshes

    public List<int> fallbackDepths; // resolution for shells (fallbackDepths[i] = depth for chunks i shells away)
    public LocalBlockPos[] center;
    public bool quality;
    private BlockPath _center;
    private readonly Queue<Chunk> _processQueue = new ();
    private readonly Queue<(Chunk, bool)> _renderQueue = new();
    private Chunk _root;

    public TextMeshProUGUI text;

    void Start()
    {
        _root = new Chunk(new(), LocalBlockPos.Origin);
        //var boot = new Chunk(root, new LocalBlockPos(1,0,0));
        //var boot1 = new Chunk(boot, new LocalBlockPos(0,0,0));
        ChunkTree.instance.Add(ref _root);
        // ChunkTree.instance.Add(boot);
        // ChunkTree.instance.Add(boot1);
        // Debug.Log($"Target: {boot1.Path.Hash()}");
        // ChunkTree.instance.TryGetChunk(PathOps.FromPositions(new LocalBlockPos(0, 0, 0), new LocalBlockPos(0, 0, 0), new LocalBlockPos(16, 0, 0)), out var chunk);
        // Debug.Log(chunk);
        
        _center = PathOps.FromPositions(center);
        ResetQueue();
        
        // var p = PathOps.FromPositions(new LocalBlockPos(1,2,3));
        // var h = p.Add(new LocalBlockPos(4, 5, 6));
        // Debug.Log(p.HashWithChild(new LocalBlockPos(4, 5, 6)));
        // Debug.Log(h.Hash());
        
        // var p = PathOps.FromPositions(new LocalBlockPos(1,2,3));
        // Debug.Log(p.Hash());
        // Debug.Log(BlockHasher.ExtendHash(0, 1, 2, 3));

        // for (int i = 0; i < 16; i++)
        // {
        //     Debug.Log(noise.pnoise(new float3(0, i+.5f, 0), new float3(16f, 16f, 16f)));
        // }
        
    }

    void Update()
    {
        text.text = $"{_processQueue.Count} {_renderQueue.Count}\n{ChunkTree.instance.ActiveChunks.Count()}";
        //ChunkTree.instance.RunningJobs.Complete();

        if (_processQueue.Count > 0)
        {
            int count = Mathf.Min(processPerFrame, _processQueue.Count);
            NativeArray<Chunk> batch = new NativeArray<Chunk>(count, Allocator.TempJob);
        
            Profiler.BeginSample("Generation");
            for (int i = 0; i < count; i++)
            {
                var c = _processQueue.Dequeue();
                batch[i] = c;
            }
            Profiler.EndSample();
            ProcessBatch(batch);
            batch.Dispose();
        }
        else if (_renderQueue.Count > 0)
        {
            int count = Mathf.Min(renderPerFrame, _renderQueue.Count);
            
            NativeArray<Chunk> batch = new NativeArray<Chunk>(count, Allocator.TempJob);
            bool[] res = new bool[count];
        
            Profiler.BeginSample("Rendering-GenerateChildren");
            for (int i = 0; i < count; i++)
            {
                var (c, b) = _renderQueue.Dequeue();
                batch[i] = c;
                res[i] = b;
            }
            Profiler.EndSample();
            RenderBatch(batch, res);
        
            batch.Dispose();
        }
        
        
    }

    /// <summary>
    /// Reset the queue to start rendering from scratch.
    /// </summary>
    public void ResetQueue()
    {
        _processQueue.Clear();
        manager.Clear();
        
        _processQueue.Enqueue(_root);
    }
    
    
    /// <summary>
    /// Process a batch of chunks, generate meshes at the correct resolution.
    /// </summary>
    private void ProcessBatch(NativeArray<Chunk> batch)
    {
        if (batch.Length == 0) return;
        NativeList<Chunk> childTargets = new NativeList<Chunk>(batch.Length, Allocator.TempJob);
        for (int i = 0; i < batch.Length; i++)
        {
            Chunk chunk = batch[i];
            
            int shellDistance = chunk.Path.ShellDistance(_center);
            
            int depthDiff = _center.Depth - chunk.Depth;

            int renderRange = GetRenderRange(chunk, depthDiff);
            
            if (shellDistance <= renderRange && depthDiff != 0 && !chunk.Populated)
            {
                childTargets.Add(chunk);
            }
            else
            {
                if (shellDistance < 4 && !chunk.Populated) childTargets.Add(chunk);
            }
        }
        
        Profiler.BeginSample("Generate Children");
        NativeList<Chunk> children = Chunk.GenerateChildrenParallel(childTargets.AsArray());
        childTargets.Dispose();
        
        ChunkTree.instance.AddBatch(children);
        Profiler.EndSample();
        
        Profiler.BeginSample("Precompute");
        for (int i = 0; i < batch.Length; i++)
        {
            Chunk chunk = batch[i];
            
            int shellDistance = chunk.Path.ShellDistance(_center);
            
            int depthDiff = _center.Depth - chunk.Depth;

            int renderRange = GetRenderRange(chunk, depthDiff);
            
            if (shellDistance <= renderRange && depthDiff != 0)
            {
                foreach (var child in chunk.Children)
                {
                    _processQueue.Enqueue(child);
                }
            }
            else
            {
                _renderQueue.Enqueue((chunk, shellDistance >= 4));
            }
        }
        Profiler.EndSample();
    }

    private int GetRenderRange(Chunk chunk, int depthDiff)
    {
        int shellDistance = chunk.Path.ShellDistance(_center);
        
        if (depthDiff < 0) return -1;
            
        int renderRange;
        if (depthDiff >= fallbackDepths.Count)
        {
            if (shellDistance > 0)
            {
                return -1;
            }
            renderRange = 0;
        }
        else
        {
            renderRange = fallbackDepths[depthDiff];
        }
        return renderRange;
    }

    private void RenderBatch(NativeArray<Chunk> batch, bool[] lowRes)
    {
        Profiler.BeginSample("SetState");
        for (int i = 0; i < batch.Length; i++)
        {
            Chunk chunk = batch[i];
            ChunkTree.instance.BlockChanges[chunk.Id] = lowRes[i] ? (byte)1 : (byte)2;
        }
        Profiler.EndSample();
        Profiler.BeginSample("Build");
        var meshes = ChunkMesher.BuildChunkMeshes(batch, lowRes, quality);
        Profiler.EndSample();
        for (int i = 0; i < meshes.Length; i++)
        {
            manager.AddChunk(meshes[i], batch[i]);
        }
    }

    private void OnDestroy()
    {
        _center.Dispose();
        ChunkMesher.DisposePersistentBuffers();
        ChunkTree.instance.Dispose();
    }
}
