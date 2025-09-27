using System;
using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;

public unsafe class BlockRenderer : MonoBehaviour
{
    public BlockTree World;
    private Block _root;
    private readonly Queue<int> _processQueue = new ();
    public TextMeshProUGUI text;
    
    public List<int3> center;
    private NativeArray<int3> _centerPath;
    
    public float maxFrameTime;
    public TreeRaytrace raytrace;
    public Transform player;
    private bool _init;
    public bool dirty;
    private Dictionary<int, (Mesh, GameObject)> MeshDict = new();
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
    
    public void InitializeTree()
    {
        var temp = Time.realtimeSinceStartup;
        GpuTreeBuilder.BuildGpuArraysFat(World.Blocks.List, out var list);
        raytrace.InitializeData(list, World.Blocks.Children, World.Blocks.Leaves);
        print ("Init: "+ (Time.realtimeSinceStartup - temp).ToString("f6"));
        raytrace.working = true;
    }
    
    public void ReEvaluateTree()
    {
        var temp = Time.realtimeSinceStartup;
        Profiler.BeginSample("Allocate Changes");
        var changes = World.Changes.ToNativeArray(Allocator.TempJob);
        Profiler.EndSample();
        Profiler.BeginSample("Build Buffer");
        raytrace.UpdateData(World.Blocks.List, World.Blocks.Children, World.Blocks.Leaves, changes);
        Profiler.EndSample();
        changes.Dispose();
        World.Changes.Clear();
        //print ("Update: "+ (Time.realtimeSinceStartup - temp).ToString("f6"));
    }

    private void Update()
    {
        _centerPath.Dispose();
        _centerPath = new NativeArray<int3>(center.ToArray(), Allocator.Persistent);
        _centerPath[^1] += (int3)math.floor(player.transform.position*math.pow(16, _centerPath.Length-1));
        _centerPath = BlockPath.RectifyPathToCoords(_centerPath);
        text.text = $"{_processQueue.Count}\n{World.Blocks.Capacity - World.Blocks.FreeCount}";
        
        float startTime = Time.realtimeSinceStartup;
        while (_processQueue.Count > 0) {
            Process();
            if (Time.realtimeSinceStartup - startTime > maxFrameTime)
            {
                break; // stop for this frame, continue next frame
            }
        }

        raytrace.root = 0;
        
        if (dirty && _processQueue.Count == 0)
        {
            dirty = false;
            if (!_init)
            {
                _init = true;
                InitializeTree();
            }
            else
            {
                ReEvaluateTree();
            }
        }

        if (_processQueue.Count == 0)
        {
            dirty = true;
            _processQueue.Enqueue(_root.Id);
        }
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
            if (c->Depth == _centerPath.Length - 1 && dist < 3)
            {
                //MeshBlock(c);
            }
            else if (c->Rendered)
            {
                UnMeshBlock(c);
            }

            if (dist < 2)
            {
                if (!c->Expanded) c->GenerateChildren(World);

                foreach (var j in c->Children(World))
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

    private void UnMeshBlock(Block* block)
    {
        if (MeshDict.TryGetValue(block->Id, out var contents))
        {
            Destroy(contents.Item2);
            MeshDict.Remove(block->Id);
        }
    }

    private void MeshBlock(Block* block)
    {
        if (block -> Rendered) return;
        block->Rendered = true;
        UnMeshBlock(block);

        Mesh mesh = BlockMesher.BuildBlockMesh(World, block, false, _centerPath.Length-1);
            
        GameObject go = new GameObject(mesh.name);
        go.transform.position = BlockPath.GetRelativeWorldPosition(_centerPath, block->Path);
        var mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
        mc.convex = false;

        MeshDict[block->Id] = (mesh, go);
    }

    private void OnDestroy()
    {
        World.Dispose();
    }
}
