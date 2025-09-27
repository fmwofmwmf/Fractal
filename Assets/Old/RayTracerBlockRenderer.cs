// using System.Collections.Generic;
// using TMPro;
// using Unity.Collections;
// using Unity.Mathematics;
// using UnityEngine;
//
// public unsafe class RaytracerBlockRenderer : MonoBehaviour
// {
//     [Header("World")]
//     public BlockTree World;
//     private Block _root;
//     private readonly Queue<int> _processQueue = new();
//     
//     [Header("UI")]
//     public TextMeshProUGUI text;
//     
//     [Header("Camera Control")]
//     public List<int3> center;
//     private NativeArray<int3> _centerPath;
//     
//     [Header("Raytracer")]
//     public VoxelRaytracer raytracer;
//     
//     [Header("Performance")]
//     public int processesPerFrame = 10;
//     public bool autoUpdateTree = true;
//     public float updateInterval = 0.1f;
//     private float lastUpdateTime;
//     
//     private void Start()
//     {
//         World = new BlockTree();
//         _root = World.GenerateRoot();
//         _processQueue.Enqueue(_root.Id);
//         
//         // Initialize raytracer
//         if (raytracer == null)
//             raytracer = GetComponent<VoxelRaytracer>();
//             
//         if (raytracer != null)
//         {
//             raytracer.World = World;
//         }
//     }
//     
//     public void ReEvaluateTree()
//     {
//         Block* root = World.Blocks.GetPtr(_root.Id);
//         UnRender(root);
//         World.DePopulate(root);
//         
//         if (World.Blocks.FreeCount != World.Blocks.Capacity - 1)
//         {
//             int j = 0;
//             foreach (var i in World.Blocks.List)
//             {
//                 if (i.IsValid)
//                 {
//                     j++;
//                 }
//             }
//             Debug.Log($"Failed cleanup: {j} blocks remaining, expected 1");
//             
//             // Force cleanup of remaining blocks
//             CleanupInvalidBlocks();
//         }
//         
//         _processQueue.Enqueue(_root.Id);
//         
//         // Reset raytracer accumulation
//         if (raytracer != null)
//             raytracer.ResetAccumulation();
//     }
//     
//     private void CleanupInvalidBlocks()
//     {
//         // This is a safety net to clean up any remaining invalid blocks
//         var blocksToFree = new List<int>();
//         
//         foreach (var block in World.Blocks.List)
//         {
//             if (block.IsValid && block.Id != _root.Id)
//             {
//                 blocksToFree.Add(block.Id);
//             }
//         }
//         
//         foreach (int id in blocksToFree)
//         {
//             Block* block = World.Blocks.GetPtr(id);
//             block->Dispose();
//             World.Blocks.Free(id);
//         }
//     }
//
//     private void Update()
//     {
//         // Update center path
//         _centerPath.Dispose();
//         _centerPath = new NativeArray<int3>(center.ToArray(), Allocator.Persistent);
//         
//         // Update UI
//         if (text != null)
//         {
//             text.text = $"Process Queue: {_processQueue.Count}\n" +
//                        $"Blocks: {World.Blocks.Capacity - World.Blocks.FreeCount}\n" +
//                        $"Center Path: {_centerPath.Length}";
//         }
//         
//         // Process blocks
//         for (int i = 0; i < processesPerFrame && _processQueue.Count > 0; i++)
//         {
//             Process();
//         }
//         
//         // Auto-update tree periodically
//         if (autoUpdateTree && Time.time - lastUpdateTime > updateInterval)
//         {
//             if (_processQueue.Count == 0)
//             {
//                 lastUpdateTime = Time.time;
//                 // Optionally trigger re-evaluation based on camera movement
//                 // ReEvaluateTree();
//             }
//         }
//     }
//
//     private void UnRender(Block* block)
//     {
//         if (!block->Expanded) return;
//
//         foreach (int blockChild in block->Children)
//         {
//             Block* child = World.Blocks.GetPtr(blockChild);
//             UnRender(child);
//         }
//     }
//
//     public void Process()
//     {
//         if (_processQueue.Count == 0) return;
//         
//         int i = _processQueue.Dequeue();
//         Block* c = World.Blocks.GetPtr(i);
//         
//         if (!c->IsValid)
//         {
//             Debug.Log($"Invalid block encountered: id={i}");
//             return;
//         }
//         
//         int dist = BlockUtils.ShellDistance(_centerPath, c->Path);
//         
//         // Determine if we should expand this block
//         if (c->Depth < _centerPath.Length)
//         {
//             if (dist < 2) // Close to center path - expand
//             {
//                 if (!c->Expanded) 
//                 {
//                     c->GenerateChildren(World);
//                     
//                     // Notify raytracer that data has changed
//                     if (raytracer != null)
//                         raytracer.ResetAccumulation();
//                 }
//                 
//                 Debug.Assert(c->Expanded);
//                 Debug.Assert(World.Blocks[c->Children[16*16*16 - 1]].Type == 1 || c->Type == 0);
//                 
//                 // Add children to process queue
//                 foreach (var childId in c->Children)
//                 {
//                     Debug.Assert(World.Blocks[childId].IsValid);
//                     _processQueue.Enqueue(childId);
//                 }
//             }
//             else // Far from center path - collapse if needed
//             {
//                 if (c->Expanded)
//                 {
//                     UnRender(c);
//                     World.DePopulate(c);
//                     
//                     // Notify raytracer that data has changed
//                     if (raytracer != null)
//                         raytracer.ResetAccumulation();
//                 }
//             }
//         }
//         // At maximum depth - ensure not expanded
//         else
//         {
//             if (c->Expanded)
//             {
//                 UnRender(c);
//                 World.DePopulate(c);
//                 
//                 // Notify raytracer that data has changed
//                 if (raytracer != null)
//                     raytracer.ResetAccumulation();
//             }
//         }
//     }
//     
//     // Method to get current world statistics
//     public void GetWorldStats(out int totalBlocks, out int expandedBlocks, out int leafBlocks)
//     {
//         totalBlocks = 0;
//         expandedBlocks = 0;
//         leafBlocks = 0;
//         
//         foreach (var block in World.Blocks.List)
//         {
//             if (block.IsValid)
//             {
//                 totalBlocks++;
//                 if (block.Expanded)
//                     expandedBlocks++;
//                 else
//                     leafBlocks++;
//             }
//         }
//     }
//     
//     // Method to force update of a specific region
//     public void ForceUpdateRegion(int3 worldPosition, int radius = 2)
//     {
//         // Add blocks near the specified position to process queue
//         foreach (var block in World.Blocks.List)
//         {
//             if (!block.IsValid) continue;
//             
//             Vector3 blockWorldPos = BlockUtils.GetWorldPosition(block);
//             float distance = math.distance(blockWorldPos, new float3(worldPosition));
//             
//             if (distance <= radius)
//             {
//                 _processQueue.Enqueue(block.Id);
//             }
//         }
//         
//         // Reset raytracer accumulation
//         if (raytracer != null)
//             raytracer.ResetAccumulation();
//     }
//     
//     // Method to set center path programmatically
//     public void SetCenterPath(List<int3> newCenter)
//     {
//         center.Clear();
//         center.AddRange(newCenter);
//         
//         // Force re-evaluation of the tree
//         ReEvaluateTree();
//     }
//     
//     // Method to set up subtree rendering at a specific depth
//     public void SetSubtreeRendering(int3 focusPoint, int targetDepth)
//     {
//         if (raytracer != null)
//         {
//             // Calculate subtree origin based on focus point and depth
//             float blockSize = Mathf.Pow(16, targetDepth);
//             Vector3 subtreeOrigin = new Vector3(focusPoint.x, focusPoint.y, focusPoint.z) * blockSize;
//             
//             raytracer.SetSubtreeForRendering(subtreeOrigin, targetDepth);
//         }
//     }
//     
//     // Method to return to full tree rendering
//     public void RenderFullTree()
//     {
//         if (raytracer != null)
//         {
//             raytracer.RenderFullTree();
//         }
//         //center.AddRange(newCenter);
//         
//         // Force re-evaluation of the tree
//         ReEvaluateTree();
//     }
//     
//     // Method to add a single step to center path
//     public void ExtendCenterPath(int3 newStep)
//     {
//         center.Add(newStep);
//         
//         // Add root to process queue to re-evaluate tree structure
//         if (_processQueue.Count == 0)
//             _processQueue.Enqueue(_root.Id);
//     }
//     
//     private void OnDestroy()
//     {
//         _centerPath.Dispose();
//         World?.Dispose();
//     }
//     
//     // Gizmos for debugging
//     private void OnDrawGizmos()
//     {
//         if (World == null || !Application.isPlaying) return;
//         
//         // Draw expanded blocks in green
//         Gizmos.color = Color.green;
//         foreach (var block in World.Blocks.List)
//         {
//             if (block.IsValid && block.Expanded)
//             {
//                 Vector3 worldPos = BlockUtils.GetWorldPosition(block);
//                 float blockSize = Mathf.Pow(16, _centerPath.Length - block.Depth);
//                 Gizmos.DrawWireCube(worldPos, Vector3.one * blockSize);
//             }
//         }
//         
//         // Draw leaf blocks in blue
//         Gizmos.color = Color.blue;
//         foreach (var block in World.Blocks.List)
//         {
//             if (block.IsValid && !block.Expanded && block.Type > 0)
//             {
//                 Vector3 worldPos = BlockUtils.GetWorldPosition(block);
//                 float blockSize = Mathf.Pow(16, _centerPath.Length - block.Depth);
//                 Gizmos.DrawWireCube(worldPos, Vector3.one * blockSize * 0.9f);
//             }
//         }
//         
//         // Draw center path in red
//         if (_centerPath.IsCreated)
//         {
//             Gizmos.color = Color.red;
//             Vector3 currentPos = Vector3.zero;
//             
//             for (int i = 0; i < _centerPath.Length; i++)
//             {
//                 Vector3 step = new Vector3(_centerPath[i].x, _centerPath[i].y, _centerPath[i].z);
//                 step *= Mathf.Pow(16, _centerPath.Length - i - 1);
//                 currentPos += step;
//                 
//                 Gizmos.DrawSphere(currentPos, 2f);
//                 
//                 if (i > 0)
//                 {
//                     Vector3 prevPos = currentPos - step;
//                     Gizmos.DrawLine(prevPos, currentPos);
//                 }
//             }
//         }
//     }
// }