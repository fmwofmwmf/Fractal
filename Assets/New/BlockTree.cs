
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public unsafe class BlockTree
{
    public const int Size = 16 * 16 * 16 * 16;
    public NativePool2 Blocks = new (Size, Allocator.Persistent);

    public Block GenerateRoot()
    {
        var i = Blocks.Allocate();
        Blocks[i] = new Block(i, 2, new(0, Allocator.Persistent), int3.zero);
        return Blocks[i];
    }

    public void DePopulate(Block* block)
    {
        if (!block->Expanded) return;

        foreach (var blockChild in block->Children)
        {
            Delete(blockChild);
        }

        block->Children.Dispose();
    }

    private void Delete(int i)
    {
        Block* b = Blocks.GetPtr(i);
        DePopulate(b);
        b->Dispose();
        Blocks.Free(i);
    }

    public void Dispose()
    {
        foreach (var block in Blocks.List)
        {
            block.Dispose();
        }
        Blocks.Dispose();
    }
}
