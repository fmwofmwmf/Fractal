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
        public Vector3 Position;
        public Vector3 Normal;
    }

    /// <summary>
    /// Build meshes for multiple chunks in parallel, using a bool mask to select which chunks to process.
    /// Returns a list of Mesh objects (null for skipped chunks).
    /// </summary>
    /// 
    private static NativeList<Vertex>[] _persistentVerts;
    private static NativeList<int>[] _persistentTris;
    private static NativeArray<byte> _persistentBlocks;
    private static int _persistentCapacity = 0; // track how many chunks allocated for

    public static Mesh[] BuildChunkMeshes(NativeArray<Chunk> chunks, bool[] lodMask, int scale, bool good = true)
    {
        int count = chunks.Length;
        Mesh[] results = new Mesh[count];
        NativeArray<JobHandle> handles = new NativeArray<JobHandle>(count, Allocator.Temp);
        
        // Ensure persistent arrays are allocated and large enough
        if (_persistentVerts == null || _persistentCapacity < count)
        {
            // Dispose old if exists
            if (_persistentVerts != null)
            {
                for (int k = 0; k < _persistentCapacity; k++)
                {
                    if (_persistentVerts[k].IsCreated) _persistentVerts[k].Dispose();
                    if (_persistentTris[k].IsCreated) _persistentTris[k].Dispose();
                }
                _persistentBlocks.Dispose();
            }

            _persistentVerts = new NativeList<Vertex>[count];
            _persistentTris = new NativeList<int>[count];
            _persistentBlocks = new NativeArray<byte>(count * 18 * 18 * 18, Allocator.Persistent);
            for (int k = 0; k < count; k++)
            {
                _persistentVerts[k] = new NativeList<Vertex>(Allocator.Persistent);
                _persistentTris[k] = new NativeList<int>(Allocator.Persistent);
            }
            _persistentCapacity = count;
        }
        
        Profiler.BeginSample("Setup");

        Profiler.BeginSample("GetChild");
        var parentHashes = new UnsafeList<ChunkPath>(count, Allocator.TempJob);
        
        for (int i = 0; i < count; i++)
        {
            if (!lodMask[i])
            {
                parentHashes.Add(chunks[i].Path);
            }
            else parentHashes.Add(new ChunkPath());
        }
        
        // for (int j = 0; j < _persistentBlocks.Length; j++)
        // {
        //     _persistentBlocks[j] = 0;
        // }
        
        var childJob = new ChunkJobs.FetchNeighborsJob
        {
            ParentHashes = parentHashes,
            Tree = ChunkTree.instance.ActiveChunks,
            Leaves = ChunkTree.instance.Leaves,
            AllChunks = _persistentBlocks
        };
        
        
        JobHandle childHandle = childJob.Schedule(count, 1);
        childHandle.Complete();
        parentHashes.Dispose();
        Profiler.EndSample();
        
        for (int i = 0; i < count; i++)
        {
            Chunk chunk = chunks[i];
            
            float cubeSize = Mathf.Pow(16, scale-chunk.Depth);
            var verts = _persistentVerts[i];
            var tris = _persistentTris[i];

            verts.Clear();
            tris.Clear();

            JobHandle handle;

            if (good)
            {
                var job = new ChunkMeshJob
                {
                    Blocks = _persistentBlocks,
                    BlockOffset = i * 18 * 18 * 18,
                    LowRes = lodMask[i],
                    Size = 16,
                    CubeScale = cubeSize,
                    Vertices = verts,
                    Triangles = tris
                };
                handle = job.Schedule();
            }
            else
            {
                var job = new ChunkMeshJobBad
                {
                    Blocks = _persistentBlocks,
                    BlockOffset = i * 18 * 18 * 18,
                    Size = 16,
                    CubeScale = cubeSize,
                    Vertices = verts,
                    Triangles = tris
                };
                handle = job.Schedule();
            }
            
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

            var verts = _persistentVerts[i];
            var tris = _persistentTris[i];

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
        if (_persistentVerts != null)
        {
            for (int k = 0; k < _persistentCapacity; k++)
            {
                if (_persistentVerts[k].IsCreated) _persistentVerts[k].Dispose();
                if (_persistentTris[k].IsCreated) _persistentTris[k].Dispose();
            }
            _persistentVerts = null;
            _persistentTris = null;
            _persistentBlocks.Dispose();
            _persistentCapacity = 0;
        }
    }
}
