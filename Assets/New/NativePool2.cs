
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

public unsafe class NativePool2
{
    private NativeArray<Block> stuff;
    private NativeList<int> indices;
    private int pos;
    private int _capacity, _freeCapacity;
    public int Capacity => _capacity;
    public int FreeCount => _freeCapacity;
    public NativeArray<Block> List => stuff;
    public NativePool2(int initialCapacity, Allocator allocator)
    {
        stuff = new NativeArray<Block>(initialCapacity, allocator);
        indices = new NativeList<int>(initialCapacity, allocator);
        for (int i = initialCapacity-1; i >=0; i--)
        {
            indices.Add(i);
        }
        _capacity = initialCapacity;
        _freeCapacity =  initialCapacity;
        pos = initialCapacity - 1;
    }

    public int Allocate()
    {
        _freeCapacity--;
        if (pos <= 0) Debug.LogError("bruh");
        return indices[pos--];
    }

    public void Free(int index)
    {
        pos++;
        _freeCapacity++;
        indices[pos] = index;
        stuff[index] = default;
    }
    
    public ref Block this[int index]
    {
        get
        {
            if (index < 0 || index >= stuff.Length)
            {
                throw new System.ArgumentOutOfRangeException(nameof(index), "Index out of range");
            }

            Block* ptr = (Block*)NativeArrayUnsafeUtility.GetUnsafePtr(stuff);
            return ref ptr[index];
        }
    }
    
    public Block* GetPtr(int index)
    {
        if (index < 0 || index >= stuff.Length)
        {
            throw new System.ArgumentOutOfRangeException(nameof(index), "Index out of range");
        }
        return (Block*)stuff.GetUnsafePtr() + index;
    }
    
    public void Dispose()
    {
        if (stuff.IsCreated)
            stuff.Dispose();
        if (indices.IsCreated)
            indices.Dispose();
    }
}
