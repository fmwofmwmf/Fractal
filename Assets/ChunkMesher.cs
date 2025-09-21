using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Profiling;

public static class ChunkMesher
{

    public struct Vertex
    {
        public Vector3 position;
        public Vector3 normal;
    }

    /// <summary>
    /// Build meshes for multiple chunks in parallel, using a bool mask to select which chunks to process.
    /// Returns a list of Mesh objects (null for skipped chunks).
    /// </summary>
    /// 
    private static NativeList<Vertex>[] persistentVerts;
    private static NativeList<int>[] persistentTris;
    private static NativeArray<byte> persistentBlocks;
    private static int persistentCapacity = 0; // track how many chunks allocated for

    public static Mesh[] BuildChunkMeshes(Chunk[] chunks, bool good = true)
    {
        int count = chunks.Length;
        Mesh[] results = new Mesh[count];
        NativeArray<JobHandle> handles = new NativeArray<JobHandle>(count, Allocator.Temp);
        
        // Ensure persistent arrays are allocated and large enough
        if (persistentVerts == null || persistentCapacity < count)
        {
            // Dispose old if exists
            if (persistentVerts != null)
            {
                for (int k = 0; k < persistentCapacity; k++)
                {
                    if (persistentVerts[k].IsCreated) persistentVerts[k].Dispose();
                    if (persistentTris[k].IsCreated) persistentTris[k].Dispose();
                }
                persistentBlocks.Dispose();
            }

            persistentVerts = new NativeList<Vertex>[count];
            persistentTris = new NativeList<int>[count];
            persistentBlocks = new NativeArray<byte>(count * 18 * 18 * 18, Allocator.Persistent);
            for (int k = 0; k < count; k++)
            {
                persistentVerts[k] = new NativeList<Vertex>(Allocator.Persistent);
                persistentTris[k] = new NativeList<int>(Allocator.Persistent);
            }
            persistentCapacity = count;
        }
        
        Profiler.BeginSample("Setup");

        Profiler.BeginSample("GetChild");
        var parentHashes = new UnsafeList<BlockPath>(count, Allocator.TempJob);
        
        for (int i = 0; i < count; i++)
        {
            parentHashes.Add(chunks[i].Path);
        }
        
        for (int j = 0; j < persistentBlocks.Length; j++)
        {
            persistentBlocks[j] = 0;
        }
        
        var childJob = new Chunk.FetchNeighborhoodJob
        {
            ParentHashes = parentHashes,
            Tree = ChunkTree.instance.BlockData,
            AllChunks = persistentBlocks
        };
        
        
        JobHandle childHandle = childJob.Schedule(count, 1);
        childHandle.Complete();
        parentHashes.Dispose();
        Profiler.EndSample();
        
        for (int i = 0; i < count; i++)
        {
            Chunk chunk = chunks[i];
            
            float cubeSize = Mathf.Pow(16, -chunk.Depth);
            var verts = persistentVerts[i];
            var tris = persistentTris[i];

            verts.Clear();
            tris.Clear();
            
            
            
            // int childCount = 0;
            // foreach (var c in chunk.Children)
            // {
            //     blocks[c.Path.Local.Index] = 1;
            //     childCount++;
            // }
            // if (childCount == 0)
            // {
            //     results[i] = null;
            //     continue;
            // }
            JobHandle handle;

            if (good)
            {
                var job = new ChunkMeshJob
                {
                    blocks = persistentBlocks,
                    blockOffset = i * 18 * 18 * 18,
                    size = 16,
                    cubeScale = cubeSize,
                    vertices = verts,
                    triangles = tris
                };
                handle = job.Schedule();
            }
            else
            {
                var job = new ChunkMeshJobBad
                {
                    blocks = persistentBlocks,
                    blockOffset = i * 18 * 18 * 18,
                    size = 16,
                    cubeScale = cubeSize,
                    vertices = verts,
                    triangles = tris
                };
                handle = job.Schedule();
            }

            // Dispose childPositions after job completes
            //handle = JobHandle.CombineDependencies(handle);
            handles[i] = handle;
        }
        Profiler.EndSample();

        // Wait for all jobs to finish
        JobHandle.CompleteAll(handles);
        handles.Dispose();
        // Build Mesh objects on main thread
        int jobIndex = 0;
        for (int i = 0; i < count; i++)
        {
            if (results[i] != null) continue;

            var verts = persistentVerts[i];
            var tris = persistentTris[i];

            Mesh mesh = new Mesh();
            mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertexBufferParams(
                verts.Length,
                new VertexAttributeDescriptor(VertexAttribute.Position),
                new VertexAttributeDescriptor(VertexAttribute.Normal)
            );

            mesh.SetVertexBufferData(verts.AsArray(), 0, 0, verts.Length, 0, 
                MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);

            mesh.SetIndexBufferParams(tris.Length, IndexFormat.UInt32);
            mesh.SetIndexBufferData(tris.AsArray(), 0, 0, tris.Length,
                MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);

            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, tris.Length), 
                MeshUpdateFlags.DontRecalculateBounds);

            mesh.RecalculateBounds();

            results[i] = mesh;

            jobIndex++;
        }

        return results;
    }
    
    public static void DisposePersistentBuffers()
    {
        if (persistentVerts != null)
        {
            for (int k = 0; k < persistentCapacity; k++)
            {
                if (persistentVerts[k].IsCreated) persistentVerts[k].Dispose();
                if (persistentTris[k].IsCreated) persistentTris[k].Dispose();
            }
            persistentVerts = null;
            persistentTris = null;
            persistentBlocks.Dispose();
            persistentCapacity = 0;
        }
    }
}
