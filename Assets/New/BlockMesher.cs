using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine.Profiling;

public unsafe static class BlockMesher
{
    public static Mesh BuildChunkMeshes(Block* block, bool lod, int scale)
    {
        NativeArray<byte> neighborhood = new NativeArray<byte>(18 * 18 * 18, Allocator.TempJob);
        
        for (int x = -1; x <= 16; x++)
        for (int y = -1; y <= 16; y++)
        for (int z = -1; z <= 16; z++)
        {
            //TODO fix
                
            int flatIndex = (x+1) + 18 * ((y+1) + 18 * (z+1));
            neighborhood[flatIndex] = 0;
            if (x >= 0 && y >= 0 && z >= 0 && x < 16 && y < 16 && z < 16)
            {
                int flatIndex1 = x + 16 * (y + 16 * z);
                    
                if (block->Leaves[flatIndex1] != 0)
                {
                    neighborhood[flatIndex] = 1;
                }
            }
        }
        
        float cubeSize = Mathf.Pow(16, scale-block->Depth);
        NativeList<ChunkMesher.Vertex> verts = new (Allocator.Persistent);
        NativeList<int> tris = new (Allocator.Persistent);

        verts.Clear();
        tris.Clear();
        
        new ChunkMeshJob
        {
            Blocks = neighborhood,
            BlockOffset = 0,
            LowRes = lod,
            Size = 16,
            CubeScale = cubeSize,
            Vertices = verts,
            Triangles = tris
        }.Schedule().Complete();
        
        neighborhood.Dispose(); 
        
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
        
        verts.Dispose();
        tris.Dispose();
        return mesh;
    }
}
