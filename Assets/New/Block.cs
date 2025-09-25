
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public struct Block
{
    public int Id;
    public bool Rendered;
    public byte Type;
    public NativeArray<byte> Leaves;
    public NativeArray<int3> Path;
    public NativeArray<int> Children;
    
    public bool Expanded => Children.IsCreated;
    public bool IsValid => Leaves.IsCreated;
    public int Depth => Path.Length;

    public Block(int id, byte type, NativeArray<int3> parent, int3 pos)
    {
        Id = id;
        Type = type;
        Rendered = false;
        Leaves = new NativeArray<byte>(16 * 16 * 16, Allocator.Persistent);
        Path = BlockPath.Extend(parent, pos);
        Children = new();
        GenerateLeaves();
    }

    public void GenerateChildren(BlockTree tree)
    {
        Children = new NativeArray<int>(16 * 16 * 16, Allocator.Persistent);
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        {
            int index = x + 16 * (y + 16 * z);
            byte b = Leaves[index];
            int cId = tree.Blocks.Allocate();
            Children[index] = cId;
            tree.Blocks[cId] = new(cId, b, Path, new(x,y,z));
        }
    }
    
    public void GenerateLeaves()
    {
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        {
            byte b = Generation.GenerateBlock(Type, x, y, z);
            Leaves[x + 16 * (y + 16 * z)] = b;
        }
        //Debug.Assert(Leaves[16*16*16-1] == 1 || Type == 0, "Leav killed");
    }
    
    public void Dispose()
    {
        Leaves.Dispose();
        Path.Dispose();
        if (Children.IsCreated) Children.Dispose();
    }
}
