// using System;
// using System.Collections.Generic;
// using TMPro;
// using Unity.Collections;
// using Unity.Mathematics;
// using UnityEngine;
//
// public unsafe class BlockRenderer : MonoBehaviour
// {
//     public BlockTree World;
//     private Block _root;
//     private readonly Queue<int> _processQueue = new ();
//     private readonly Queue<int> _renderQueue = new ();
//     public TextMeshProUGUI text;
//     
//     public List<int3> center;
//     private NativeArray<int3> _centerPath;
//     
//     public Dictionary<int, (Mesh, Vector3)> Rendered = new ();
//     public Material material;
//     public int batch;
//     private void Start()
//     {
//         World = new BlockTree();
//         _root = World.GenerateRoot();
//         _processQueue.Enqueue(_root.Id);
//     }
//     
//     public void ReEvaluateTree()
//     {
//         if (_processQueue.Count == 0) _processQueue.Enqueue(_root.Id);
//         // Block* root = World.Blocks.GetPtr(_root.Id);
//         // UnRender(root);
//         // World.DePopulate(root);
//         // if (World.Blocks.FreeCount != World.Blocks.Capacity - 1)
//         // {
//         //     int j = 0;
//         //     foreach (var i in World.Blocks.List)
//         //     {
//         //         if (i.IsValid)
//         //         {
//         //             _renderQueue.Enqueue(i.Id);
//         //             j++;
//         //         }
//         //     }
//         //     Debug.Log($"Failed {j}, failed_check {World.Blocks.Capacity - 1 - World.Blocks.FreeCount}");
//         // }
//         // else
//         // {
//         //     _processQueue.Enqueue(_root.Id);
//         // }
//     }
//
//     private void Update()
//     {
//         RenderParams rp = new RenderParams(material);
//         foreach (var (mesh, pos) in Rendered.Values)
//         {
//             Graphics.RenderMesh(rp, mesh, 0, transform.localToWorldMatrix * Matrix4x4.Translate(pos));
//         }
//
//         _centerPath.Dispose();
//         _centerPath = new NativeArray<int3>(center.ToArray(), Allocator.Persistent);
//         
//         text.text = $"{_processQueue.Count} {_renderQueue.Count}\n{World.Blocks.Capacity - World.Blocks.FreeCount}";
//         for (int i = 0; i < batch; i++)
//         {
//             if (_processQueue.Count > 0) Process();
//             if (_processQueue.Count == 0 && _renderQueue.Count > 0) Render();
//         }
//         //if (_processQueue.Count == 0) _processQueue.Enqueue(_root.Id);
//     }
//
//     private void UnRender(Block* block)
//     {
//         Rendered.Remove(block->Id);
//         block->Rendered = false;
//         if (!block->Expanded) return;
//
//         foreach (int blockChild in block->Children)
//         {
//             UnRender(World.Blocks.GetPtr(blockChild));
//         }
//     }
//
//     public void Process()
//     {
//         int i = _processQueue.Dequeue();
//         Block* c = World.Blocks.GetPtr(i);
//         int dist = BlockUtils.ShellDistance(_centerPath, c->Path);
//         if (!c->IsValid)
//         {
//             Debug.Log($"hmmmm id:{i}");
//             return;
//         }
//         if (c->Depth < _centerPath.Length)
//         {
//             if (dist < 2)
//             {
//                 if (!c->Expanded) c->GenerateChildren(World);
//                 if (c->Rendered)
//                 {
//                     UnRender(c);
//                 }
//                 Debug.Assert(c->Expanded);
//                 //Debug.Assert(World.Blocks[c->Children(World)[16*16*16 -1]].Type == 1 || c->Type==0);
//                 foreach (var j in c->Children)
//                 {
//                     Debug.Assert(World.Blocks[j].IsValid);
//                     _processQueue.Enqueue(j);
//                 }
//             }
//             else
//             {
//                 if (!c->Rendered) _renderQueue.Enqueue(i);
//                 if (c->Expanded)
//                 {
//                     UnRender(c);
//                     World.DePopulate(c);
//                     _renderQueue.Enqueue(i);
//                 }
//             }
//         }
//         else
//         {
//             if (!c->Rendered) _renderQueue.Enqueue(i);
//         }
//     }
//
//     public void Render()
//     {
//         int i = _renderQueue.Dequeue();
//         Block* c = World.Blocks.GetPtr(i);
//         if (c->Type == 0) return;
//         c->Rendered = true;
//
//         var mesh = BlockMesher.BuildChunkMeshes(c, false, 0);
//         
//         Rendered[i] = (mesh, BlockUtils.GetWorldPosition(*c));
//     }
//
//     private void OnDestroy()
//     {
//         World.Dispose();
//     }
// }
