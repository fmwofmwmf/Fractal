using System;
using UnityEngine;
using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
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
    private Queue<Chunk> processQueue = new Queue<Chunk>();
    private Queue<Chunk> renderQueue = new Queue<Chunk>();
    private Chunk root;

    public TextMeshProUGUI text;

    void Start()
    {
        root = new Chunk(new(), LocalBlockPos.Origin);
        //var boot = new Chunk(root, new LocalBlockPos(1,0,0));
        //var boot1 = new Chunk(boot, new LocalBlockPos(0,0,0));
        ChunkTree.instance.Add(ref root);
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
        
        var p = PathOps.FromPositions(new LocalBlockPos(1,2,3));
        Debug.Log(p.Hash());
        Debug.Log(BlockHasher.ExtendHash(0, 1, 2, 3));
    }

    void Update()
    {
        text.text = $"{processQueue.Count} {renderQueue.Count}\n{ChunkTree.instance.ActiveChunks.Count()}";
        //ChunkTree.instance.RunningJobs.Complete();

        if (processQueue.Count > 0)
        {
            int count = Mathf.Min(processPerFrame, processQueue.Count);
            NativeArray<Chunk> batch = new NativeArray<Chunk>(count, Allocator.TempJob);
        
            Profiler.BeginSample("Generation");
            for (int i = 0; i < count; i++)
            {
                var c = processQueue.Dequeue();
                batch[i] = c;
            }
            Profiler.EndSample();
            ProcessBatch(batch);
            batch.Dispose();
        }
        else if (renderQueue.Count > 0)
        {
            int count = Mathf.Min(renderPerFrame, renderQueue.Count);
            
            NativeArray<Chunk> batch = new NativeArray<Chunk>(count, Allocator.TempJob);
        
            Profiler.BeginSample("Rendering-GenerateChildren");
            for (int i = 0; i < count; i++)
            {
                var c = renderQueue.Dequeue();
                c.EnsureChildren();
                batch[i] = c;
            }
            Profiler.EndSample();
            RenderBatch(batch);
        
            batch.Dispose();
        }
        
        
    }

    /// <summary>
    /// Reset the queue to start rendering from scratch.
    /// </summary>
    public void ResetQueue()
    {
        processQueue.Clear();
        manager.Clear();
        
        processQueue.Enqueue(root);
    }
    
    
    /// <summary>
    /// Process a batch of chunks, generate meshes at the correct resolution.
    /// </summary>
    private void ProcessBatch(NativeArray<Chunk> batch)
    {
        if (batch.Length == 0) return;
        
        Profiler.BeginSample("Generate Children");
        NativeList<Chunk> children = Chunk.GenerateChildrenParallel(batch);

        ChunkTree.instance.AddBatch(children);
        //Debug.Log(ChunkTree.instance.ActiveChunks.Count());
        // UnsafeList<BlockPath> allPaths = new (batch.Count, Allocator.Temp);
        // Debug.Log(allPaths.Length);
        // for (int i = 0; i < batch.Count; i++)
        // {
        //     allPaths.Add(batch[i].Path);
        // }
        // Debug.Log(batch.Count);
        // UnsafeList<Chunk> newChunks = new UnsafeList<Chunk>(batch.Count, Allocator.TempJob);
        // var job = new Chunk.ComputeChunkChildrenJob
        // {
        //     paths = allPaths,
        //     output = newChunks.AsParallelWriter(),
        // };
        //
        // JobHandle handle = job.Schedule(batch.Count, 1);
        // handle.Complete();
        //
        // foreach (Chunk chunk in newChunks)
        // {
        //     if (!chunk.IsEmpty) ChunkTree.instance.Add(chunk);
        // }
        //
        // allPaths.Dispose();
        // newChunks.Dispose();
        Profiler.EndSample();
        Profiler.BeginSample("Precompute");
        
        for (int i = 0; i < batch.Length; i++)
        {
            Chunk chunk = batch[i];
            
            //chunk.SetPopulated();
            
            // Compute min shell distance from all centers
            int shellDistance = int.MaxValue;

            shellDistance = Mathf.Min(shellDistance, chunk.Path.ShellDistance(_center));
            
            int targetDepth = chunk.Depth; // default: render at chunk depth
            if (fallbackDepths != null && fallbackDepths.Count > 0)
            {
                // fallbackDepths[i] = max distance to stay at center resolution
                for (int shell = 0; shell < fallbackDepths.Count; shell++)
                {
                    if (shellDistance <= fallbackDepths[shell])
                    {
                        targetDepth = _center.Depth; // render at center resolution
                        break;
                    }
                }
            }
            
            // Decide whether to mesh this chunk now
            chunk.EnsureChildren();
            if (chunk.Depth == targetDepth)
            {
                renderQueue.Enqueue(chunk);
            }
            else
            {
                foreach (var child in chunk.Children)
                {
                    processQueue.Enqueue(child);
                }
            }
        }
        Profiler.EndSample();
    }

    private void RenderBatch(NativeArray<Chunk> batch)
    {
        Profiler.BeginSample("Build");
        var meshes = ChunkMesher.BuildChunkMeshes(batch.ToArray(), quality);
        Profiler.EndSample();
        for (int i = 0; i < meshes.Length; i++)
        {
            manager.AddChunk(meshes[i], batch[i]);
        }
    }

    private void OnDestroy()
    {
        ChunkMesher.DisposePersistentBuffers();
        ChunkTree.instance.Dispose();
    }
}
