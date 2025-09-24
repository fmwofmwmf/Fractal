using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public struct ChunkMeshJobBad : IJob
{
    [ReadOnly] public NativeArray<byte> Blocks;
    [ReadOnly] public int BlockOffset;
    [ReadOnly] public int Size;
    [ReadOnly] public float CubeScale;

    public NativeList<ChunkMesher.Vertex> Vertices;
    public NativeList<int> Triangles;
    
    public static readonly int3[] FaceDirections = new int3[]
    {
        new int3(1, 0, 0),
        new int3(-1, 0, 0),
        new int3(0, 1, 0),
        new int3(0, -1, 0),
        new int3(0, 0, 1),
        new int3(0, 0, -1)
    };
    
    public static readonly float3[] FaceVertices = new float3[]
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
        int strideY = Size + 2;
        int strideZ = strideY * strideY;
        
        for (int z = 1; z <= Size; z++)
        {
            for (int y = 1; y <= Size; y++)
            {
                for (int x = 1; x <= Size; x++)
                {
                    int index = x + y * strideY + z * strideZ;
                    if (Blocks[BlockOffset + index] == 0) continue;

                    for (int face = 0; face < 6; face++)
                    {
                        int3 dir = FaceDirections[face];
                        int nx = x + dir.x;
                        int ny = y + dir.y;
                        int nz = z + dir.z;
                        int neighborIndex = nx + ny * strideY + nz * strideZ;

                        if (Blocks[BlockOffset + neighborIndex] != 0) continue;

                        float3 basePos = new float3(x - 1, y - 1, z - 1) * CubeScale;
                        int startVertexIndex = Vertices.Length;
                        int faceBaseIndex = face * 4;

                        for (int i = 0; i < 4; i++)
                        {
                            float3 vertexPos = basePos + FaceVertices[faceBaseIndex + i] * CubeScale;
                            Vertices.Add(new ChunkMesher.Vertex
                            {
                                Position = vertexPos,
                                Normal = (float3)dir
                            });
                        }

                        Triangles.Add(startVertexIndex);
                        Triangles.Add(startVertexIndex + 1);
                        Triangles.Add(startVertexIndex + 2);
                        Triangles.Add(startVertexIndex);
                        Triangles.Add(startVertexIndex + 2);
                        Triangles.Add(startVertexIndex + 3);
                    }
                }
            }
        }
    }
}