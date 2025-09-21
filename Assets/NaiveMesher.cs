using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public struct ChunkMeshJobBad : IJob
{
    [ReadOnly] public NativeArray<byte> blocks;
    [ReadOnly] public int blockOffset;
    [ReadOnly] public int size;
    [ReadOnly] public float cubeScale;

    public NativeList<ChunkMesher.Vertex> vertices;
    public NativeList<int> triangles;
    
    public static readonly int3[] faceDirections = new int3[]
    {
        new int3(1, 0, 0),
        new int3(-1, 0, 0),
        new int3(0, 1, 0),
        new int3(0, -1, 0),
        new int3(0, 0, 1),
        new int3(0, 0, -1)
    };
    
    public static readonly float3[] faceVertices = new float3[]
    {
        new float3(1, 0, 0),
        new float3(1, 1, 0),
        new float3(1, 1, 1),
        new float3(1, 0, 1),
        new float3(0, 0, 0),
        new float3(0, 0, 1),
        new float3(0, 1, 1),
        new float3(0, 1, 0),
        new float3(0, 1, 0),
        new float3(0, 1, 1),
        new float3(1, 1, 1),
        new float3(1, 1, 0),
        new float3(0, 0, 0),
        new float3(1, 0, 0),
        new float3(1, 0, 1),
        new float3(0, 0, 1),
        new float3(0, 0, 1),
        new float3(1, 0, 1),
        new float3(1, 1, 1),
        new float3(0, 1, 1),
        new float3(0, 0, 0),
        new float3(0, 1, 0),
        new float3(1, 1, 0),
        new float3(1, 0, 0),
    };
    
    public void Execute()
    {
        int strideY = size + 2;
        int strideZ = strideY * strideY;
        
        for (int z = 1; z <= size; z++)
        {
            for (int y = 1; y <= size; y++)
            {
                for (int x = 1; x <= size; x++)
                {
                    int index = x + y * strideY + z * strideZ;
                    if (blocks[blockOffset + index] == 0) continue;

                    for (int face = 0; face < 6; face++)
                    {
                        int3 dir = faceDirections[face];
                        int nx = x + dir.x;
                        int ny = y + dir.y;
                        int nz = z + dir.z;
                        int neighborIndex = nx + ny * strideY + nz * strideZ;

                        if (blocks[blockOffset + neighborIndex] != 0) continue;

                        float3 basePos = new float3(x - 1, y - 1, z - 1) * cubeScale;
                        int startVertexIndex = vertices.Length;
                        int faceBaseIndex = face * 4;

                        for (int i = 0; i < 4; i++)
                        {
                            float3 vertexPos = basePos + faceVertices[faceBaseIndex + i] * cubeScale;
                            vertices.Add(new ChunkMesher.Vertex
                            {
                                position = vertexPos,
                                normal = (float3)dir
                            });
                        }

                        triangles.Add(startVertexIndex);
                        triangles.Add(startVertexIndex + 1);
                        triangles.Add(startVertexIndex + 2);
                        triangles.Add(startVertexIndex);
                        triangles.Add(startVertexIndex + 2);
                        triangles.Add(startVertexIndex + 3);
                    }
                }
            }
        }
    }
}