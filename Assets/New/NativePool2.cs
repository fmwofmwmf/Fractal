
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

public unsafe class NativePool2
{
    private NativeArray<Block> _stuff;
    private NativeArray<int> _leaves;
    private NativeArray<int> _children;
    private NativeList<int> _indices;
    private int _pos;
    private int _capacity, _freeCapacity;
    public int Capacity => _capacity;
    public int FreeCount => _freeCapacity;
    public NativeArray<int> Leaves => _leaves;
    public NativeArray<int> Children => _children;
    public NativeArray<Block> List => _stuff;
    
    public NativePool2(int initialCapacity, Allocator allocator)
    {
        _stuff = new NativeArray<Block>(initialCapacity, allocator);
        _children = new NativeArray<int>(initialCapacity * Const.ChunkScale, allocator);
        _leaves = new NativeArray<int>(initialCapacity * Const.ChunkScale, allocator);
        _indices = new NativeList<int>(initialCapacity, allocator);
        for (int i = initialCapacity-1; i >=0; i--)
        {
            _indices.Add(i);
        }
        _capacity = initialCapacity;
        _freeCapacity =  initialCapacity;
        _pos = initialCapacity - 1;
    }

    public int Allocate()
    {
        _freeCapacity--;
        if (_pos <= 0) Debug.LogError("bruh");
        return _indices[_pos--];
    }

    public void Free(int index)
    {
        _pos++;
        _freeCapacity++;
        _indices[_pos] = index;
        _stuff[index] = default;
    }

    
    
    public ref Block this[int index]
    {
        get
        {
            if (index < 0 || index >= _stuff.Length)
            {
                throw new System.ArgumentOutOfRangeException(nameof(index), "Index out of range");
            }

            Block* ptr = (Block*)NativeArrayUnsafeUtility.GetUnsafePtr(_stuff);
            return ref ptr[index];
        }
    }
    
    public Block* GetPtr(int index)
    {
        if (index < 0 || index >= _stuff.Length)
        {
            throw new System.ArgumentOutOfRangeException(nameof(index), "Index out of range");
        }
        return (Block*)_stuff.GetUnsafePtr() + index;
    }
    
    public void Dispose()
    {
        if (_stuff.IsCreated)
            _stuff.Dispose();
        if (_children.IsCreated)
            _children.Dispose();
        if (_leaves.IsCreated)
            _leaves.Dispose();
            
        if (_indices.IsCreated)
            _indices.Dispose();
    }
}
