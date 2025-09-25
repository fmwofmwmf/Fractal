using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct ChunkMeshJob : IJob
{
    [ReadOnly] public NativeArray<byte> Blocks; // (size+2)^3 padded
    [ReadOnly] public int BlockOffset; // offset into blocks for this chunk
    [ReadOnly] public bool LowRes;
    [ReadOnly] public int Size;        // is 16
    [ReadOnly] public float CubeScale; // scale everything by this amount

    public NativeList<ChunkMesher.Vertex> Vertices; // a vertex requires position and normal
    public NativeList<int> Triangles;

    public void Execute()
    {
        Vertices.Clear();
        Triangles.Clear();

        if (LowRes)
        {
            AddCubeLowRes(CubeScale, Size);
            return;
        }
        
        int paddedSize = Size + 2; // 18 for padding
        
        // Process each axis for greedy meshing
        for (int axis = 0; axis < 3; axis++)
        {
            ProcessAxis(axis, paddedSize);
        }
    }
    
    private void ProcessAxis(int axis, int paddedSize)
    {
        // Dimensions for the current axis
        int u = (axis + 1) % 3; // u and v are the two dimensions perpendicular to the axis
        int v = (axis + 2) % 3;
        
        // Direction vectors
        var axisDir = new int3(0);
        var uDir = new int3(0);
        var vDir = new int3(0);
        
        axisDir[axis] = 1;
        uDir[u] = 1;
        vDir[v] = 1;
        
        // We need to check from 0 to size+1 (17 slices) to capture all boundary faces
        // Each slice compares blocks at d and d+1 in padded coordinates
        for (int d = 0; d <= Size; d++)
        {
            // Create mask for this slice
            var mask = new NativeArray<int>(Size * Size, Allocator.Temp);
            
            // Fill mask by comparing adjacent blocks along the axis
            for (int j = 0; j < Size; j++)
            {
                for (int i = 0; i < Size; i++)
                {
                    // Convert to padded coordinates (add 1 to account for padding offset)
                    var pos1 = new int3();
                    var pos2 = new int3();
                    
                    pos1[axis] = d;         // Current slice in padded coords (0 to 16)
                    pos1[u] = i + 1;        // Padded coords (1 to 16) 
                    pos1[v] = j + 1;        // Padded coords (1 to 16)
                    
                    pos2[axis] = d + 1;     // Next slice in padded coords (1 to 17)
                    pos2[u] = i + 1;
                    pos2[v] = j + 1;
                    
                    byte block1 = GetBlock(pos1, paddedSize);
                    byte block2 = GetBlock(pos2, paddedSize);
                    
                    bool solid1 = block1 != 0;
                    bool solid2 = block2 != 0;
                    
                    // Generate face if there's a solid/empty transition
                    if (solid1 != solid2)
                    {
                        // Face normal points toward the empty space
                        // If block1 is solid and block2 is empty, face points in positive axis direction
                        // If block1 is empty and block2 is solid, face points in negative axis direction
                        mask[j * Size + i] = solid1 ? 1 : -1;
                    }
                    else
                    {
                        mask[j * Size + i] = 0;
                    }
                }
            }
            
            // Greedy mesh the mask
            GreedyMesh(mask, d, axis, u, v, axisDir, uDir, vDir);
            
            mask.Dispose();
        }
    }
    
    private void GreedyMesh(NativeArray<int> mask, int d, int axis, int u, int v, 
                           int3 axisDir, int3 uDir, int3 vDir)
    {
        for (int j = 0; j < Size; j++)
        {
            for (int i = 0; i < Size;)
            {
                if (mask[j * Size + i] == 0)
                {
                    i++;
                    continue;
                }
                
                int currentMask = mask[j * Size + i];
                
                // Measure width (along i/u direction)
                int width = 1;
                while (i + width < Size && mask[j * Size + i + width] == currentMask)
                {
                    width++;
                }
                
                // Measure height (along j/v direction)
                int height = 1;
                bool canExtend = true;
                
                while (j + height < Size && canExtend)
                {
                    for (int k = i; k < i + width; k++)
                    {
                        if (mask[(j + height) * Size + k] != currentMask)
                        {
                            canExtend = false;
                            break;
                        }
                    }
                    if (canExtend) height++;
                }
                
                // Generate quad
                GenerateQuad(d, i, j, width, height, currentMask > 0, 
                           axis, u, v, axisDir, uDir, vDir);
                
                // Clear the processed area in mask
                for (int h = 0; h < height; h++)
                {
                    for (int w = 0; w < width; w++)
                    {
                        mask[(j + h) * Size + (i + w)] = 0;
                    }
                }
                
                i += width;
            }
        }
    }
    
    private void GenerateQuad(int d, int i, int j, int width, int height, bool isPositiveFace,
                             int axis, int u, int v, int3 axisDir, int3 uDir, int3 vDir)
    {
        // Face position: the face is between blocks at d and d+1 in padded coordinates
        // Convert to world coordinates where target chunk starts at (0,0,0)
        // Padded coord 0 = world coord -1, padded coord 1 = world coord 0, etc.
        
        var basePos = new float3();
        basePos[axis] = (d - 1) * CubeScale;  // Convert padded coord to world coord

        basePos[axis] += CubeScale;  // Positive faces are offset by one cube
        
        basePos[u] = i * CubeScale;  // i,j are already in target chunk coordinates (0-15)
        basePos[v] = j * CubeScale;
        
        var normal = new float3();
        normal[axis] = isPositiveFace ? 1 : -1;
        
        // Calculate quad corners (convert int3 to float3 for math)
        var uDirFloat = new float3(uDir.x, uDir.y, uDir.z);
        var vDirFloat = new float3(vDir.x, vDir.y, vDir.z);
        
        var corner1 = basePos;
        var corner2 = basePos + uDirFloat * width * CubeScale;
        var corner3 = basePos + uDirFloat * width * CubeScale + vDirFloat * height * CubeScale;
        var corner4 = basePos + vDirFloat * height * CubeScale;
        
        int vertexStart = Vertices.Length;
        
        // Add vertices
        Vertices.Add(new ChunkMesher.Vertex { Position = corner1, Normal = normal });
        Vertices.Add(new ChunkMesher.Vertex { Position = corner2, Normal = normal });
        Vertices.Add(new ChunkMesher.Vertex { Position = corner3, Normal = normal });
        Vertices.Add(new ChunkMesher.Vertex { Position = corner4, Normal = normal });
        
        // Add triangles (quad = 2 triangles)
        if (isPositiveFace)
        {
            // Counter-clockwise for positive face
            Triangles.Add(vertexStart + 0);
            Triangles.Add(vertexStart + 1);
            Triangles.Add(vertexStart + 2);
            
            Triangles.Add(vertexStart + 0);
            Triangles.Add(vertexStart + 2);
            Triangles.Add(vertexStart + 3);
        }
        else
        {
            // Clockwise for negative face (reverse winding)
            Triangles.Add(vertexStart + 0);
            Triangles.Add(vertexStart + 3);
            Triangles.Add(vertexStart + 2);
            
            Triangles.Add(vertexStart + 0);
            Triangles.Add(vertexStart + 2);
            Triangles.Add(vertexStart + 1);
        }
    }
    
    private void AddCubeLowRes(float scale, int size)
    {
        // cube from (0,0,0) to (size, size, size) scaled
        float3 min = new float3(0f, 0f, 0f) * scale;
        float3 max = new float3(size, size, size) * scale;

        int baseIndex = Vertices.Length;

        // Each face needs its own set of 4 vertices (because normals differ per face)
        // Bottom (y = min.y)
        AddFace(
            new float3(min.x, min.y, min.z),
            new float3(max.x, min.y, min.z),
            new float3(max.x, min.y, max.z),
            new float3(min.x, min.y, max.z),
            new float3(0, -1, 0)
        );

        // Top (y = max.y)
        AddFace(
            new float3(min.x, max.y, max.z),
            new float3(max.x, max.y, max.z),
            new float3(max.x, max.y, min.z),
            new float3(min.x, max.y, min.z),
            new float3(0, 1, 0)
        );

        // Front (z = max.z)
        AddFace(
            new float3(min.x, min.y, max.z),
            new float3(max.x, min.y, max.z),
            new float3(max.x, max.y, max.z),
            new float3(min.x, max.y, max.z),
            new float3(0, 0, 1)
        );

        // Back (z = min.z)
        AddFace(
            new float3(max.x, min.y, min.z),
            new float3(min.x, min.y, min.z),
            new float3(min.x, max.y, min.z),
            new float3(max.x, max.y, min.z),
            new float3(0, 0, -1)
        );

        // Left (x = min.x)
        AddFace(
            new float3(min.x, min.y, min.z),
            new float3(min.x, min.y, max.z),
            new float3(min.x, max.y, max.z),
            new float3(min.x, max.y, min.z),
            new float3(-1, 0, 0)
        );

        // Right (x = max.x)
        AddFace(
            new float3(max.x, min.y, max.z),
            new float3(max.x, min.y, min.z),
            new float3(max.x, max.y, min.z),
            new float3(max.x, max.y, max.z),
            new float3(1, 0, 0)
        );
    }

    private void AddFace(float3 v0, float3 v1, float3 v2, float3 v3, float3 normal)
    {
        int start = Vertices.Length;

        Vertices.Add(new ChunkMesher.Vertex { Position = v0, Normal = normal });
        Vertices.Add(new ChunkMesher.Vertex { Position = v1, Normal = normal });
        Vertices.Add(new ChunkMesher.Vertex { Position = v2, Normal = normal });
        Vertices.Add(new ChunkMesher.Vertex { Position = v3, Normal = normal });

        AddTwoTriangles(start + 0, start + 1, start + 2, start + 3);
    }

    
    private void AddTwoTriangles(int a, int b, int c, int d)
    {
        Triangles.Add(a);
        Triangles.Add(b);
        Triangles.Add(c);

        Triangles.Add(a);
        Triangles.Add(c);
        Triangles.Add(d);
    }
    
    private byte GetBlock(int3 pos, int paddedSize)
    {
        // Bounds check
        if (pos.x < 0 || pos.x >= paddedSize ||
            pos.y < 0 || pos.y >= paddedSize ||
            pos.z < 0 || pos.z >= paddedSize)
        {
            return 0; // Return empty for out of bounds
        }
        
        int index = BlockOffset + pos.z * paddedSize * paddedSize + pos.y * paddedSize + pos.x;
        return Blocks[index];
    }
}