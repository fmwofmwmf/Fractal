// using System.Collections.Generic;
// using Unity.Collections;
// using Unity.Mathematics;
// using UnityEngine;
// using UnityEngine.Rendering;
//
// public unsafe class VoxelRaytracer : MonoBehaviour
// {
//     [Header("Raytracing Settings")]
//     public ComputeShader raytracingShader;
//     public Camera renderCamera;
//     public int maxRayBounces = 4;
//     public int maxTraversalDepth = 10;
//     public float voxelSize = 1.0f;
//     
//     [Header("Performance")]
//     [Range(0.1f, 2.0f)]
//     public float renderScale = 1.0f;
//     public bool useAdaptiveQuality = true;
//     
//     [Header("Visual Settings")]
//     public Color skyColor = Color.blue;
//     public Vector3 sunDirection = new Vector3(-0.5f, -1f, -0.3f);
//     public Color sunColor = Color.white;
//     public float sunIntensity = 2.0f;
//     
//     // Compute shader kernels
//     private int raytraceKernel;
//     private int clearKernel;
//     
//     // Render targets
//     private RenderTexture raytraceTarget;
//     private RenderTexture accumulationTarget;
//     private int frameCount = 0;
//     
//     // GPU buffers for voxel data
//     private ComputeBuffer blockDataBuffer;
//     private ComputeBuffer blockChildrenBuffer;
//     private ComputeBuffer blockLeavesBuffer;
//     private ComputeBuffer pathDataBuffer;
//     
//     // Block tree reference
//     public BlockTree World;
//     
//     // Data structures for GPU upload
//     private struct GPUBlockData
//     {
//         public int id;
//         public int type; // Changed from byte to int for proper 4-byte alignment
//         public int rendered;
//         public int expanded;
//         public int isValid;
//         public int childrenOffset;
//         public int leavesOffset;
//         public int pathOffset;
//         public int pathLength;
//         public int depth;
//         public float3 worldPosition; // Add world position for direct lookup
//     }
//     
//     private List<GPUBlockData> gpuBlocks = new List<GPUBlockData>();
//     private List<int> gpuChildren = new List<int>();
//     private List<int> gpuLeaves = new List<int>(); // Changed from byte to int for proper stride
//     private List<int3> gpuPaths = new List<int3>();
//     
//     // Subtree rendering support
//     [Header("Subtree Rendering")]
//     public bool useSubtreeRendering = true;
//     public int subtreeDepthOffset = 0;
//     public float3 subtreeOrigin = float3.zero;
//     
//     // Shader property IDs
//     private static readonly int CameraToWorldId = Shader.PropertyToID("_CameraToWorld");
//     private static readonly int CameraInverseProjectionId = Shader.PropertyToID("_CameraInverseProjection");
//     private static readonly int ResultTextureId = Shader.PropertyToID("_ResultTexture");
//     private static readonly int AccumulationTextureId = Shader.PropertyToID("_AccumulationTexture");
//     private static readonly int FrameCountId = Shader.PropertyToID("_FrameCount");
//     private static readonly int MaxBouncesId = Shader.PropertyToID("_MaxBounces");
//     private static readonly int MaxDepthId = Shader.PropertyToID("_MaxTraversalDepth");
//     private static readonly int VoxelSizeId = Shader.PropertyToID("_VoxelSize");
//     private static readonly int SkyColorId = Shader.PropertyToID("_SkyColor");
//     private static readonly int SunDirectionId = Shader.PropertyToID("_SunDirection");
//     private static readonly int SunColorId = Shader.PropertyToID("_SunColor");
//     private static readonly int SunIntensityId = Shader.PropertyToID("_SunIntensity");
//     private static readonly int BlockDataId = Shader.PropertyToID("_BlockData");
//     private static readonly int BlockChildrenId = Shader.PropertyToID("_BlockChildren");
//     private static readonly int BlockLeavesId = Shader.PropertyToID("_BlockLeaves");
//     private static readonly int PathDataId = Shader.PropertyToID("_PathData");
//     private static readonly int SubtreeOriginId = Shader.PropertyToID("_SubtreeOrigin");
//     private static readonly int SubtreeDepthOffsetId = Shader.PropertyToID("_SubtreeDepthOffset");
//     private static readonly int UseSubtreeRenderingId = Shader.PropertyToID("_UseSubtreeRendering");
//     private static readonly int RootBlockIdId = Shader.PropertyToID("_RootBlockId");
//     
//     private void Start()
//     {
//         if (raytracingShader == null)
//         {
//             Debug.LogError("Raytracing compute shader not assigned!");
//             enabled = false;
//             return;
//         }
//         
//         if (renderCamera == null)
//             renderCamera = Camera.main;
//             
//         // Find kernel indices
//         raytraceKernel = raytracingShader.FindKernel("CSRaytrace");
//         clearKernel = raytracingShader.FindKernel("CSClear");
//         
//         // Initialize render targets
//         InitializeRenderTargets();
//     }
//     
//     private void InitializeRenderTargets()
//     {
//         int width = Mathf.RoundToInt(Screen.width * renderScale);
//         int height = Mathf.RoundToInt(Screen.height * renderScale);
//         
//         // Release existing targets
//         if (raytraceTarget != null)
//         {
//             raytraceTarget.Release();
//             accumulationTarget.Release();
//         }
//         
//         // Create new render targets
//         raytraceTarget = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat)
//         {
//             enableRandomWrite = true,
//             filterMode = FilterMode.Bilinear
//         };
//         raytraceTarget.Create();
//         
//         accumulationTarget = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat)
//         {
//             enableRandomWrite = true,
//             filterMode = FilterMode.Bilinear
//         };
//         accumulationTarget.Create();
//         
//         frameCount = 0;
//     }
//     
//     private void Update()
//     {
//         // Check if screen size changed
//         int targetWidth = Mathf.RoundToInt(Screen.width * renderScale);
//         int targetHeight = Mathf.RoundToInt(Screen.height * renderScale);
//         
//         if (raytraceTarget == null || 
//             raytraceTarget.width != targetWidth || 
//             raytraceTarget.height != targetHeight)
//         {
//             InitializeRenderTargets();
//         }
//         
//         // Reset accumulation if camera moved
//         if (renderCamera.transform.hasChanged)
//         {
//             frameCount = 0;
//             renderCamera.transform.hasChanged = false;
//         }
//         
//         // Upload voxel data to GPU
//         if (World != null)
//         {
//             UpdateGPUBuffers();
//         }
//         
//         // Perform raytracing
//         RaytraceFrame();
//     }
//     
//     private void UpdateGPUBuffers()
//     {
//         // Clear previous frame data
//         gpuBlocks.Clear();
//         gpuChildren.Clear();
//         gpuLeaves.Clear();
//         gpuPaths.Clear();
//         
//         if (World == null) return;
//         
//         // Build GPU data structures
//         foreach (var block in World.Blocks.List)
//         {
//             if (!block.IsValid) continue;
//             
//             var gpuBlock = new GPUBlockData
//             {
//                 id = block.Id,
//                 type = block.Type,
//                 rendered = block.Rendered ? 1 : 0,
//                 expanded = block.Expanded ? 1 : 0,
//                 isValid = 1,
//                 pathOffset = gpuPaths.Count,
//                 pathLength = block.Path.Length,
//                 depth = block.Depth,
//                 worldPosition = new float3(BlockUtils.GetWorldPosition(block))
//             };
//             
//             // Add path data
//             for (int i = 0; i < block.Path.Length; i++)
//             {
//                 gpuPaths.Add(block.Path[i]);
//             }
//             
//             // Add children data if expanded
//             if (block.Expanded)
//             {
//                 gpuBlock.childrenOffset = gpuChildren.Count;
//                 for (int i = 0; i < block.Children.Length; i++)
//                 {
//                     gpuChildren.Add(block.Children[i]);
//                 }
//             }
//             else
//             {
//                 gpuBlock.childrenOffset = -1;
//             }
//             
//             // Add leaves data - convert bytes to ints for proper stride
//             gpuBlock.leavesOffset = gpuLeaves.Count;
//             for (int i = 0; i < block.Leaves.Length; i++)
//             {
//                 gpuLeaves.Add((int)block.Leaves[i]); // Convert byte to int
//             }
//             
//             gpuBlocks.Add(gpuBlock);
//         }
//         
//         // Update GPU buffers - only if we have data
//         if (gpuBlocks.Count > 0)
//             UpdateBuffer(ref blockDataBuffer, gpuBlocks);
//         if (gpuChildren.Count > 0)
//             UpdateBuffer(ref blockChildrenBuffer, gpuChildren);
//         if (gpuLeaves.Count > 0)
//             UpdateBuffer(ref blockLeavesBuffer, gpuLeaves);
//         if (gpuPaths.Count > 0)
//             UpdateBuffer(ref pathDataBuffer, gpuPaths);
//     }
//     
//     private void UpdateBuffer<T>(ref ComputeBuffer buffer, List<T> data) where T : struct
//     {
//         if (data.Count == 0) return;
//         
//         if (buffer == null || buffer.count != data.Count)
//         {
//             buffer?.Release();
//             buffer = new ComputeBuffer(data.Count, System.Runtime.InteropServices.Marshal.SizeOf<T>());
//         }
//         
//         buffer.SetData(data);
//     }
//     
//     private void RaytraceFrame()
//     {
//         if (World == null || gpuBlocks.Count == 0) return;
//         
//         // Set camera matrices
//         Matrix4x4 cameraToWorld = renderCamera.cameraToWorldMatrix;
//         Matrix4x4 cameraInverseProjection = renderCamera.projectionMatrix.inverse;
//         
//         raytracingShader.SetMatrix(CameraToWorldId, cameraToWorld);
//         raytracingShader.SetMatrix(CameraInverseProjectionId, cameraInverseProjection);
//         
//         // Set render parameters
//         raytracingShader.SetTexture(raytraceKernel, ResultTextureId, raytraceTarget);
//         raytracingShader.SetTexture(raytraceKernel, AccumulationTextureId, accumulationTarget);
//         raytracingShader.SetInt(FrameCountId, frameCount);
//         raytracingShader.SetInt(MaxBouncesId, maxRayBounces);
//         raytracingShader.SetInt(MaxDepthId, maxTraversalDepth);
//         raytracingShader.SetFloat(VoxelSizeId, voxelSize);
//         
//         // Set lighting parameters
//         raytracingShader.SetVector(SkyColorId, skyColor);
//         raytracingShader.SetVector(SunDirectionId, sunDirection.normalized);
//         raytracingShader.SetVector(SunColorId, sunColor);
//         raytracingShader.SetFloat(SunIntensityId, sunIntensity);
//         
//         // Set voxel data buffers - only if they exist and have data
//         if (blockDataBuffer != null && blockDataBuffer.count > 0) 
//             raytracingShader.SetBuffer(raytraceKernel, BlockDataId, blockDataBuffer);
//         if (blockChildrenBuffer != null && blockChildrenBuffer.count > 0) 
//             raytracingShader.SetBuffer(raytraceKernel, BlockChildrenId, blockChildrenBuffer);
//         if (blockLeavesBuffer != null && blockLeavesBuffer.count > 0) 
//             raytracingShader.SetBuffer(raytraceKernel, BlockLeavesId, blockLeavesBuffer);
//         if (pathDataBuffer != null && pathDataBuffer.count > 0) 
//             raytracingShader.SetBuffer(raytraceKernel, PathDataId, pathDataBuffer);
//         
//         // Set subtree rendering parameters
//         raytracingShader.SetVector(SubtreeOriginId, new Vector4(subtreeOrigin.x, subtreeOrigin.y, subtreeOrigin.z, 0));
//         raytracingShader.SetInt(SubtreeDepthOffsetId, subtreeDepthOffset);
//         raytracingShader.SetBool(UseSubtreeRenderingId, useSubtreeRendering);
//         
//         // Find root block ID
//         int rootId = -1;
//         if (gpuBlocks.Count > 0)
//         {
//             // Find block with shortest path (closest to root)
//             int minPathLength = int.MaxValue;
//             for (int i = 0; i < gpuBlocks.Count; i++)
//             {
//                 if (gpuBlocks[i].pathLength < minPathLength)
//                 {
//                     minPathLength = gpuBlocks[i].pathLength;
//                     rootId = gpuBlocks[i].id;
//                 }
//             }
//         }
//         raytracingShader.SetInt(RootBlockIdId, rootId);
//         
//         // Dispatch compute shader
//         int threadGroupsX = Mathf.CeilToInt(raytraceTarget.width / 8.0f);
//         int threadGroupsY = Mathf.CeilToInt(raytraceTarget.height / 8.0f);
//         raytracingShader.Dispatch(raytraceKernel, threadGroupsX, threadGroupsY, 1);
//         
//         frameCount++;
//         
//         // Copy result to screen
//         Graphics.Blit(raytraceTarget, (RenderTexture)null);
//     }
//     
//     private void OnRenderImage(RenderTexture src, RenderTexture dest)
//     {
//         if (raytraceTarget != null)
//         {
//             Graphics.Blit(raytraceTarget, dest);
//         }
//         else
//         {
//             Graphics.Blit(src, dest);
//         }
//     }
//     
//     public void ResetAccumulation()
//     {
//         frameCount = 0;
//         
//         // Clear accumulation buffer
//         if (clearKernel >= 0 && accumulationTarget != null)
//         {
//             raytracingShader.SetTexture(clearKernel, AccumulationTextureId, accumulationTarget);
//             int threadGroupsX = Mathf.CeilToInt(accumulationTarget.width / 8.0f);
//             int threadGroupsY = Mathf.CeilToInt(accumulationTarget.height / 8.0f);
//             raytracingShader.Dispatch(clearKernel, threadGroupsX, threadGroupsY, 1);
//         }
//     }
//     
//     private void OnDestroy()
//     {
//         // Release render targets
//         if (raytraceTarget != null)
//         {
//             raytraceTarget.Release();
//             accumulationTarget.Release();
//         }
//         
//         // Release compute buffers
//         blockDataBuffer?.Release();
//         blockChildrenBuffer?.Release();
//         blockLeavesBuffer?.Release();
//         pathDataBuffer?.Release();
//     }
//     
//     
//     // Method to set subtree for rendering
//     public void SetSubtreeForRendering(float3 origin, int depthOffset)
//     {
//         subtreeOrigin = origin;
//         subtreeDepthOffset = depthOffset;
//         useSubtreeRendering = true;
//         ResetAccumulation();
//     }
//     
//     // Method to disable subtree rendering and render full tree
//     public void RenderFullTree()
//     {
//         useSubtreeRendering = false;
//         subtreeDepthOffset = 0;
//         subtreeOrigin = float3.zero;
//         ResetAccumulation();
//     }
//
//     private void OnValidate()
//     {
//         // Reset accumulation when settings change
//         if (Application.isPlaying)
//         {
//             ResetAccumulation();
//         }
//     }
// }