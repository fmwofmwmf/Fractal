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
    public List<int> lodRanges;
    public LocalBlockPos[] center;
    public bool quality;
    private ChunkPath _center;
    private readonly Queue<Chunk> _processQueue = new ();
    private readonly Queue<(Chunk, bool)> _renderQueue = new();
    private Chunk _root;
    
    public Transform player;
    private ChunkPath _playerPos;
    private bool IsWorking => _processQueue.Count > 0 || _renderQueue.Count > 0;
    
    public int BaseDepth => _center.Depth - 1;

    public TextMeshProUGUI text;

    void Start()
    {
        _root = new Chunk(new(), 0,0,0, 1);
        ChunkTree.instance.Add(ref _root);
        _center = PathOps.FromPositions(center);
        ResetQueue();
        ChunkTree.instance.renderer = manager;
    }

    void Update()
    {
        // TODO add in place methods
        int3 disp = (int3)math.floor(player.position);
        var currentPPos = ChunkPath.RectifyPathToCoords(_center.Displace(new LocalBlockPos(disp)), Allocator.Persistent);
        if (!currentPPos.Equals(_playerPos))
        {
            ReEvaluateTree();
        }
        _playerPos.Dispose();
        _playerPos = currentPPos;
        _center.Dispose();
        _center = PathOps.FromPositions(center);
        Render();
    }

    private void Render()
    {
        text.text = $"{_processQueue.Count} {_renderQueue.Count}\n{ChunkTree.instance.ActiveChunks.Count()}";

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
        if (_renderQueue.Count > 0)
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

    public void ReEvaluateTree()
    {
        if (IsWorking)
        {
            Debug.LogError("Already Working!");
            return;
        }
        _processQueue.Enqueue(_root);
        
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
        //NativeList<Chunk> cullTargets = new NativeList<Chunk>(batch.Length, Allocator.TempJob);
        for (int i = 0; i < batch.Length; i++)
        {
            Chunk chunk = batch[i];
            
            int shellDistance = chunk.Path.ShellDistance(_playerPos);
            int depthDiff = _playerPos.Depth - chunk.Depth;
            int renderRange = GetRenderRange(chunk, depthDiff);


            if (depthDiff > 0)
            {
                byte targetRes;
                if (shellDistance <= renderRange)
                {
                    targetRes = 0b11;
                }
                else if (shellDistance <= lodRanges[depthDiff])
                {
                    targetRes = 0b10;
                }
                else
                {
                    targetRes = 0b01;
                }
                
                if (chunk.RenderState == targetRes)
                {
                    if (targetRes != 0b11)
                    {
                        batch[i] = new();
                        continue;
                    }
                }
                else
                {
                    if (targetRes == 0b11)
                    {
                        ChunkTree.instance.UnRenderChunk(chunk);
                        if (!chunk.Populated) childTargets.Add(chunk);
                    }
                    else if (targetRes == 0b10)
                    {
                        if (chunk.Populated)
                        {
                            ChunkTree.instance.UnRenderChunk(chunk);
                            ChunkTree.instance.Depopulate(chunk);
                        }
                    }
                    else
                    {
                        if (chunk.Populated)
                        {
                            ChunkTree.instance.UnRenderChunk(chunk);
                            ChunkTree.instance.Depopulate(chunk);
                        }
                    }
                }
            }
            else if (depthDiff < 0) // Too small
            {
                ChunkTree.instance.UnRenderChunk(chunk);
                ChunkTree.instance.Depopulate(chunk);
                batch[i] = new();
            }
        }
        
        //cullTargets.Dispose();
        
        Profiler.BeginSample("Generate Children");
        NativeList<Chunk> children = Chunk.GenerateChildrenParallel(childTargets.AsArray());
        childTargets.Dispose();
        
        ChunkTree.instance.AddBatch(children);
        Profiler.EndSample();
        
        Profiler.BeginSample("Precompute");
        for (int i = 0; i < batch.Length; i++)
        {
            Chunk chunk = batch[i];
            if (batch[i].IsEmpty) continue;
            
            if (!chunk.Path.Path.IsCreated) Debug.LogError($"{chunk.ToString()} error");
            
            int shellDistance = chunk.Path.ShellDistance(_playerPos);
            int depthDiff = _playerPos.Depth - chunk.Depth;
            int renderRange = GetRenderRange(chunk, depthDiff);


            if (shellDistance <= renderRange && depthDiff > 0) 
            {
                ChunkTree.instance.BlockChanges[chunk.Id] |= 0b110; // subdivided tag
                foreach (var child in chunk.Children)
                {
                    _processQueue.Enqueue(child);
                }
            }
            else
            {
                if (chunk.Type != 0)
                { 
                    _renderQueue.Enqueue((chunk, shellDistance >= lodRanges[depthDiff]));
                }
            }
        }
        Profiler.EndSample();
    }

    private int GetRenderRange(Chunk chunk, int depthDiff)
    {
        int shellDistance = chunk.Path.ShellDistance(_playerPos);
        
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
            manager.RemoveChunk(chunk);
            ChunkTree.instance.BlockChanges[chunk.Id] |= lowRes[i] ? (byte)0b010 : (byte)0b100;
        }
        Profiler.EndSample();
        Profiler.BeginSample("Build");
        var meshes = ChunkMesher.BuildChunkMeshes(batch, lowRes, BaseDepth, quality);
        Profiler.EndSample();
        for (int i = 0; i < meshes.Length; i++)
        {
            manager.AddChunk(meshes[i], batch[i], _center);
        }
    }

    private void OnDestroy()
    {
        _center.Dispose();
        _playerPos.Dispose();
        ChunkMesher.DisposePersistentBuffers();
        ChunkTree.instance.Dispose();
    }
}
