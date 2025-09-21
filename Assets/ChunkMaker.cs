// using UnityEngine;
// using System.Collections.Generic;
// using Unity.Collections;
//
// public class ChunkMaker : MonoBehaviour
// {
//     [Header("Settings")]
//     public int renderDepth = 2;
//     public int chunksPerFrame = 10;
//     public ChunkMeshRenderer manager;
//     private Queue<Chunk> queue = new Queue<Chunk>();
//     private Chunk root;
//     private int lastRenderDepth = -1;
//     public List<Vector3Int> centers;
//     public List<int> fallbackDepths;
//
//     void Start()
//     {
//         root = new Chunk(null, LocalBlockPos.Origin);
//         ResetBuild();
//     }
//
//     void Update()
//     {
//         if (renderDepth != lastRenderDepth)
//         {
//             ResetBuild();
//         }
//         
//         ProcessChunk(centers, fallbackDepths);
//     }
//
//     private void ResetBuild()
//     {
//         // clear old objects
//         manager.Clear();
//         
//         // reset queue
//         queue.Clear();
//         queue.Enqueue(root);
//
//         lastRenderDepth = renderDepth;
//     }
//
//     private void ProcessChunk(List<Vector3Int> centers, List<int> fallbackDepths)
//     {
//         if (centers.Count == 0) return;
//
//         int batchSize = chunksPerFrame;
//         Queue<Chunk> processingQueue = new Queue<Chunk>();
//         processingQueue.Enqueue(root);
//
//         while (processingQueue.Count > 0)
//         {
//             int count = Mathf.Min(batchSize, processingQueue.Count);
//             List<Chunk> batchChunks = new List<Chunk>(count);
//             NativeArray<bool> mask = new NativeArray<bool>(count, Allocator.Persistent);
//
//             for (int i = 0; i < count; i++)
//             {
//                 Chunk chunk = processingQueue.Dequeue();
//                 batchChunks.Add(chunk);
//
//                 // Compute Manhattan distance from nearest center
//                 int minDistance = int.MaxValue;
//                 foreach (var center in centers)
//                 {
//                     Vector3 worldPos = chunk.GetWorldPosition();
//                     Vector3Int chunkPos = new Vector3Int(
//                         Mathf.FloorToInt(worldPos.x),
//                         Mathf.FloorToInt(worldPos.y),
//                         Mathf.FloorToInt(worldPos.z)
//                     );
//                     int dist = Mathf.Abs(chunkPos.x - center.x)
//                              + Mathf.Abs(chunkPos.y - center.y)
//                              + Mathf.Abs(chunkPos.z - center.z);
//                     if (dist < minDistance) minDistance = dist;
//                 }
//
//                 // Determine target depth based on distance/fallbackDepths
//                 int targetDepth = renderDepth;
//                 for (int j = 0; j < fallbackDepths.Count; j++)
//                 {
//                     if (minDistance > j) targetDepth = fallbackDepths[j];
//                     else break;
//                 }
//
//                 // Should we mesh this chunk now?
//                 if (chunk.Depth == targetDepth)
//                 {
//                     mask[i] = true;
//                 }
//                 else
//                 {
//                     mask[i] = false;
//                     // enqueue children for further processing
//                     chunk.EnsureChildren();
//
//                     foreach (var child in chunk.Children)
//                         if (child.IsEmpty)
//                             processingQueue.Enqueue(child);
//                     
//                 }
//             }
//             
//             // Build meshes in parallel for this batch
//             var meshes = ChunkMesher.BuildChunkMeshes(batchChunks.ToArray(), mask);
//
//             for (int i = 0; i < meshes.Length; i++)
//             {
//                 if (!mask[i] || meshes[i] == null) continue;
//                 manager.AddChunk(meshes[i], batchChunks[i]);
//             }
//             mask.Dispose();
//         }
//     }
//
// }
