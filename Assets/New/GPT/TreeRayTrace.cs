using UnityEngine;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
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
        public int pad1;
        public int pad2;
        public int pad3;
    }

    // These are the data your generator should fill
    private NativeArray<GPUBlockDataPadded> gpuBlocks = new();
    private NativeArray<int> gpuChildren = new(); // child node indices (-1 = none)
    private NativeArray<int> gpuLeaves = new();   // leaf occupancy / type (0 empty)

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

    void Start()
    {
        cam = Camera.main;
        InitRenderTexture();
    }

    void OnDestroy()
    {
        ReleaseBuffers();
        if (gpuBlocks.IsCreated) gpuBlocks.Dispose();
        if (gpuChildren.IsCreated) gpuChildren.Dispose();
        if (gpuLeaves.IsCreated) gpuLeaves.Dispose();
    }

    public void UpdateData(NativeArray<GPUBlockDataPadded> blocks, NativeArray<int> children, NativeArray<int> leaves)
    {
        if (gpuBlocks.IsCreated) gpuBlocks.Dispose();
        if (gpuChildren.IsCreated) gpuChildren.Dispose();
        if (gpuLeaves.IsCreated) gpuLeaves.Dispose();
        gpuBlocks = blocks;
        gpuChildren = children;
        gpuLeaves = leaves;
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

    // Call when gpuBlocks/gpuChildren/gpuLeaves changed
    public void UpdateGPUBuffers()
    {
        ReleaseBuffers();

        if (gpuBlocks.Length > 0)
        {
            blocksBuffer = new ComputeBuffer(gpuBlocks.Length, Marshal.SizeOf(typeof(GPUBlockDataPadded)), ComputeBufferType.Structured);
            blocksBuffer.SetData(gpuBlocks);
        }
        else
        {
            Debug.Log("killed");
        }

        if (gpuChildren.Length > 0)
        {
            childrenBuffer = new ComputeBuffer(gpuChildren.Length, sizeof(int), ComputeBufferType.Structured);
            childrenBuffer.SetData(gpuChildren);
        }
        else
        {
            childrenBuffer = new ComputeBuffer(1, sizeof(int));
            childrenBuffer.SetData(new int[] { -1 });
        }

        if (gpuLeaves.Length > 0)
        {
            leavesBuffer = new ComputeBuffer(gpuLeaves.Length, sizeof(int), ComputeBufferType.Structured);
            leavesBuffer.SetData(gpuLeaves);
        }
        else
        {
            Debug.Log("killed");
        }
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
        raytraceShader.SetFloat("nearPlane", near);
        raytraceShader.SetFloat("farPlane", far);
        raytraceShader.SetInt("maxSteps", maxSteps);
        raytraceShader.SetFloat("stepSize", stepSize);

        // tree metadata
        raytraceShader.SetInt("numBlocks", gpuBlocks.Length);
        raytraceShader.SetInt("numChildren", gpuChildren.Length);
        raytraceShader.SetInt("numLeaves", gpuLeaves.Length);

        // dispatch (8x8 threads)
        int threadGroupsX = Mathf.CeilToInt(targetWidth / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(targetHeight / 8.0f);
        raytraceShader.Dispatch(kernelIndex, threadGroupsX, threadGroupsY, 1);

        // blit to screen
        Graphics.Blit(target, (RenderTexture)null);
    }
}
