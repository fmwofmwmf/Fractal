
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public struct Block
{
    public int Id;
    public int parentId;
    public bool Rendered;
    public byte Type;
    public bool Expanded;
    public float3 Position;
    public NativeArray<int3> Path;
    
    public bool IsValid => Path.IsCreated;
    public int Depth => Path.Length;

    public Block(BlockTree world, int id, int pid, byte type, NativeArray<int3> parent, int3 pos)
    {
        Id = id;
        Type = type;
        Rendered = false;
        Path = BlockPath.Extend(parent, pos);
        Expanded = false;
        Position = BlockPath.LocalPos(Path);
        parentId = pid;
        GenerateLeaves(world);
        world.MarkDirty(Id, false, true);
    }

    public void GenerateChildren(BlockTree world)
    {
        Expanded = true;
        world.MarkDirty(Id, true, false);
        int offset = Id * Const.ChunkScale;
        var leaves = world.Blocks.Leaves;
        var children = world.Blocks.Children;
        
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        {
            int index = x + 16 * (y + 16 * z);
            byte b = (byte)leaves[offset + index];
            int cId = world.Blocks.Allocate();
            children[offset + index] = cId;
            world.Blocks[cId] = new(world, cId, Id, b, Path, new(x,y,z));
        }
    }
    
    public IEnumerable<int> Children(BlockTree world)
    {
        int offset = Id * Const.ChunkScale;
        var children = world.Blocks.Children;
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        {
            yield return children[offset + x + 16 * (y + 16 * z)];
        }
    }
    
    public void GenerateLeaves(BlockTree world)
    {
        int offset = Id * Const.ChunkScale;
        var leaves = world.Blocks.Leaves;
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        {
            byte b = Generation.GenerateBlock(Position, Type, x, y, z);
            leaves[offset + x + 16 * (y + 16 * z)] = b;
        }
        //Debug.Assert(Leaves[16*16*16-1] == 1 || Type == 0, "Leav killed");
    }
    
    public void Dispose()
    {
        Path.Dispose();
    }
}
