using System;
using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;

public unsafe class BlockRendererNew : MonoBehaviour
{
    public BlockTree World;
    private Block _root;
    private readonly Queue<int> _processQueue = new ();
    public TextMeshProUGUI text;
    
    public List<int3> center;
    private NativeArray<int3> _centerPath;
    
    public int batch;
    public TreeRaytrace raytrace;
    public bool dirty;
    private void Start()
    {
        World = new BlockTree();
        _root = World.GenerateRoot();
        _processQueue.Enqueue(_root.Id);
    }

    public void ReDraw()
    {
        _processQueue.Enqueue(_root.Id);
        dirty = true;
    }
    
    public void ReEvaluateTree()
    {
        var temp = Time.realtimeSinceStartup;
        Profiler.BeginSample("Build Buffer");
        GpuTreeBuilder.BuildGpuArraysFat(World.Blocks.List, _root.Id, out var list, out var listC, out var listL);
        raytrace.UpdateData(list, listC, listL);
        Profiler.EndSample();
        print ("Part 1: "+ (Time.realtimeSinceStartup - temp).ToString("f6"));
        temp = Time.realtimeSinceStartup;
        Profiler.BeginSample("Set Buffer");
        raytrace.UpdateGPUBuffers();
        raytrace.working = true;
        Profiler.EndSample();
        print ("Part 2: "+(Time.realtimeSinceStartup - temp).ToString("f6"));
    }

    private void Update()
    {
        _centerPath.Dispose();
        _centerPath = new NativeArray<int3>(center.ToArray(), Allocator.Persistent);
        
        text.text = $"{_processQueue.Count}\n{World.Blocks.Capacity - World.Blocks.FreeCount}";
        for (int i = 0; i < batch; i++)
        {
            if (_processQueue.Count > 0) Process();
        }

        if (dirty && _processQueue.Count == 0)
        {
            dirty = false;
            ReEvaluateTree();
        }
        //if (_processQueue.Count == 0) _processQueue.Enqueue(_root.Id);
    }
    
    public void Process()
    {
        int i = _processQueue.Dequeue();
        Block* c = World.Blocks.GetPtr(i);
        int dist = BlockUtils.ShellDistance(_centerPath, c->Path);
        if (!c->IsValid)
        {
            Debug.Log($"hmmmm id:{i}");
            return;
        }
        if (c->Depth < _centerPath.Length)
        {
            if (dist < 2)
            {
                if (!c->Expanded) c->GenerateChildren(World);

                foreach (var j in c->Children)
                {
                    Debug.Assert(World.Blocks[j].IsValid);
                    _processQueue.Enqueue(j);
                }
            }
            else
            {
                if (c->Expanded)
                {
                    World.DePopulate(c);
                }
            }
        }
    }

    private void OnDestroy()
    {
        World.Dispose();
    }
}
