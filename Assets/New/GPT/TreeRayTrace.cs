using UnityEngine;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Profiling;
using UnityEngine.UI;

public class TreeRaytrace : MonoBehaviour
{
    // HLSL kernel names / shader
    public ComputeShader raytraceShader;
    public int kernelIndex = 0;

    // CPU-side storage (fill these from your generator)
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUBlockDataPadded
    {
        public int Type;
        public int RenderState;
        public int ChildrenOffset;
        public int LeavesOffset;
        public int Depth;
        // padding to make size multiple of 16 bytes (32 bytes total)
        public int parentId;
        public int localIndex;
        public int pad3;

        public static GPUBlockDataPadded FromBlock(Block b)
        {
            return new GPUBlockDataPadded()
            {
                Type = b.Type,
                RenderState = b.Rendered ? 1 : 0,
                Depth = b.Depth,
                ChildrenOffset = b.Expanded ? b.Id * Const.ChunkScale : -1,
                LeavesOffset = b.Id * Const.ChunkScale,
                parentId = b.parentId,
                localIndex = BlockPath.ToInt(b.Path[^1])
            };
        }
    }

    ComputeBuffer blocksBuffer;
    ComputeBuffer childrenBuffer;
    ComputeBuffer leavesBuffer;

    public RawImage img;
    RenderTexture target;
    Camera cam;

    // rendering settings
    public int targetWidth = 512;
    public int targetHeight = 512;
    public float near = 0.0f;
    public float far = 2.0f;
    public int maxSteps = 512;
    public float stepSize = 1.0f / 256.0f; // how far we march each step (tweak)
    public bool working;
    [HideInInspector] public int root;

    void Start()
    {
        cam = Camera.main;
        InitRenderTexture();
    }

    void OnDestroy()
    {
        ReleaseBuffers();
    }

    public void InitializeData(NativeArray<GPUBlockDataPadded> blocks, NativeArray<int> children, NativeArray<int> leaves)
    {
        ReleaseBuffers();
        Profiler.BeginSample("AllocateBlocks");
        blocksBuffer = new ComputeBuffer(blocks.Length, Marshal.SizeOf(typeof(GPUBlockDataPadded)), ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        childrenBuffer = new ComputeBuffer(children.Length, sizeof(int), ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        leavesBuffer = new ComputeBuffer(leaves.Length, sizeof(int), ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
        Profiler.EndSample();
        Profiler.BeginSample("SetBlocks");
        if (blocks.Length > 0)
        {
            var blocksPtr = blocksBuffer.BeginWrite<GPUBlockDataPadded>(0, blocks.Length);
            var blocksJob = new CopyNativeArrayJob<GPUBlockDataPadded>
            {
                Source = blocks,
                Target = blocksPtr
            };
            blocksJob.Schedule(blocks.Length, 64).Complete();
            blocksBuffer.EndWrite<GPUBlockDataPadded>(blocks.Length);
        }

        if (children.Length > 0)
        {
            var childrenPtr = childrenBuffer.BeginWrite<int>(0, children.Length);
            var childrenJob = new CopyNativeArrayJob<int>
            {
                Source = children,
                Target = childrenPtr
            };
            childrenJob.Schedule(children.Length, 256).Complete();
            childrenBuffer.EndWrite<int>(children.Length);
        }

        if (leaves.Length > 0)
        {
            var leavesPtr = leavesBuffer.BeginWrite<int>(0, leaves.Length);
            var leavesJob = new CopyNativeArrayJob<int>
            {
                Source = leaves,
                Target = leavesPtr
            };
            leavesJob.Schedule(leaves.Length, 256).Complete();
            leavesBuffer.EndWrite<int>(leaves.Length);
        }
        Profiler.EndSample();
    }
    
    [BurstCompile]
    struct CopyNativeArrayJob<T> : IJobParallelFor where T : struct
    {
        [ReadOnly] public NativeArray<T> Source;
        public NativeArray<T> Target;

        public void Execute(int index)
        {
            Target[index] = Source[index];
        }
    }
    
    public void UpdateData(
        NativeArray<Block> blocks,
        NativeArray<int> children,
        NativeArray<int> leaves,
        NativeArray<(int, bool, bool)> changedIndices)
    {
        if (changedIndices.Length == 0)
            return;

        // --- Update blocks (only changed indices) ---
        var blocksPtr = blocksBuffer.BeginWrite<GPUBlockDataPadded>(0, blocks.Length);
        var childrenPtr = childrenBuffer.BeginWrite<int>(0, children.Length);
        var leavesPtr = leavesBuffer.BeginWrite<int>(0, leaves.Length);
        
        var job = new UpdateChangedJob
        {
            SourceBlocks = blocks,
            SourceChildren = children,
            SourceLeaves = leaves,
            TargetBlocks = blocksPtr,
            TargetChildren = childrenPtr,
            TargetLeaves = leavesPtr,
            ChangedIndices = changedIndices
        };
        Debug.Log(changedIndices.Length);
        job.Schedule(changedIndices.Length, 64).Complete();
        blocksBuffer.EndWrite<GPUBlockDataPadded>(blocks.Length);
        childrenBuffer.EndWrite<int>(children.Length);
        leavesBuffer.EndWrite<int>(leaves.Length);

        // Children & leaves typically don’t need partial updates
        // unless you’re also tracking which children/leaves changed.
        // If you are, you can do the exact same pattern as above.
    }

    [BurstCompile]
    struct UpdateChangedJob : IJobParallelFor
    {
        [NativeDisableContainerSafetyRestriction] [ReadOnly] public NativeArray<Block> SourceBlocks;
        [ReadOnly] public NativeArray<int> SourceChildren;
        [ReadOnly] public NativeArray<int> SourceLeaves;
        [NativeDisableParallelForRestriction] public NativeArray<GPUBlockDataPadded> TargetBlocks;
        [NativeDisableParallelForRestriction] public NativeArray<int> TargetChildren;
        [NativeDisableParallelForRestriction] public NativeArray<int> TargetLeaves;
        [ReadOnly] public NativeArray<(int, bool, bool)> ChangedIndices;

        public void Execute(int index)
        {
            var (srcIndex, children, leaves) = ChangedIndices[index];
            Block b = SourceBlocks[srcIndex];
            GPUBlockDataPadded data = GPUBlockDataPadded.FromBlock(b);
            TargetBlocks[srcIndex] = data;
            if (children)
            {
                for (int i = 0; i < Const.ChunkScale; i++)
                {
                    TargetChildren[srcIndex * Const.ChunkScale + i] = SourceChildren[srcIndex * Const.ChunkScale + i];
                }
            }

            if (leaves)
            {
                for (int i = 0; i < Const.ChunkScale; i++)
                {
                    TargetLeaves[srcIndex * Const.ChunkScale + i] = SourceLeaves[srcIndex * Const.ChunkScale + i];
                }
            }
            
        }
    }

    void InitRenderTexture()
    {
        if (target != null && (target.width != targetWidth || target.height != targetHeight))
        {
            target.Release();
            target = null;
        }

        if (target == null)
        {
            target = new RenderTexture(targetWidth, targetHeight, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            target.enableRandomWrite = true;
            target.Create();
            img.texture = target;
        }
    }

    void ReleaseBuffers()
    {
        if (blocksBuffer != null) { blocksBuffer.Release(); blocksBuffer = null; }
        if (childrenBuffer != null) { childrenBuffer.Release(); childrenBuffer = null; }
        if (leavesBuffer != null) { leavesBuffer.Release(); leavesBuffer = null; }
    }

    void Update()
    {
        if (raytraceShader == null || !working) return;

        InitRenderTexture();

        // set buffers & parameters
        raytraceShader.SetTexture(kernelIndex, "Result", target);
        raytraceShader.SetBuffer(kernelIndex, "blocks_int4", blocksBuffer);
        raytraceShader.SetBuffer(kernelIndex, "children", childrenBuffer);
        raytraceShader.SetBuffer(kernelIndex, "leaves", leavesBuffer);

        // camera matrices: we need inverse projection and camera to world to compute rays
        Matrix4x4 camToWorld = cam.cameraToWorldMatrix;
        Matrix4x4 camInverseProj = cam.projectionMatrix.inverse;
        
        raytraceShader.SetMatrix("camToWorld", camToWorld);
        raytraceShader.SetMatrix("camInverseProjection", camInverseProj);

        raytraceShader.SetInt("screenWidth", targetWidth);
        raytraceShader.SetInt("screenHeight", targetHeight);
        raytraceShader.SetInt("root", root);
        raytraceShader.SetFloat("nearPlane", near);
        raytraceShader.SetFloat("farPlane", far);
        raytraceShader.SetInt("maxSteps", maxSteps);
        raytraceShader.SetFloat("stepSize", stepSize);

        // dispatch (8x8 threads)
        int threadGroupsX = Mathf.CeilToInt(targetWidth / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(targetHeight / 8.0f);
        raytraceShader.Dispatch(kernelIndex, threadGroupsX, threadGroupsY, 1);

        // blit to screen
        Graphics.Blit(target, (RenderTexture)null);
    }
}
