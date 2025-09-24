using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

/// <summary>
/// A thread-safe pool for unmanaged types that supports parallel allocation from jobs.
/// Uses an internal array for storage and a stack of free indices for efficient allocation.
/// </summary>
/// <typeparam name="T">Unmanaged type to pool</typeparam>
public unsafe struct NativePool<T> : System.IDisposable where T : unmanaged
{
    [NativeDisableUnsafePtrRestriction]
    private UnsafeList<T> _mItems;
    
    [NativeDisableUnsafePtrRestriction] 
    private UnsafeList<int> _mFreeIndices;
    
    [NativeDisableUnsafePtrRestriction]
    private int* _mNextFreeIndex;
    
    private readonly Allocator _mAllocator;
    private bool _mIsCreated;
    
    public UnsafeList<T> List => _mItems;

    /// <summary>
    /// Creates a new unmanaged pool with the specified initial capacity
    /// </summary>
    /// <param name="initialCapacity">Initial capacity of the pool</param>
    /// <param name="allocator">Memory allocator to use</param>
    public NativePool(int initialCapacity, Allocator allocator)
    {
        _mAllocator = allocator;
        _mItems = new UnsafeList<T>(initialCapacity, allocator);
        _mFreeIndices = new UnsafeList<int>(initialCapacity, allocator);
        _mNextFreeIndex = (int*)UnsafeUtility.Malloc(sizeof(int), sizeof(int), allocator);
        *_mNextFreeIndex = -1;
        _mIsCreated = true;

        // Pre-populate free indices stack
        for (int i = initialCapacity - 1; i >= 0; i--)
        {
            _mFreeIndices.Add(i);
            _mItems.Add(default(T));
        }
        *_mNextFreeIndex = initialCapacity - 1;
    }

    /// <summary>
    /// Gets whether this pool has been created and is valid to use
    /// </summary>
    public bool IsCreated => _mIsCreated && _mItems.IsCreated;

    /// <summary>
    /// Gets the current capacity of the pool
    /// </summary>
    public int Capacity => _mItems.Capacity;

    /// <summary>
    /// Gets the number of available free slots
    /// </summary>
    public int FreeCount => *_mNextFreeIndex + 1;

    /// <summary>
    /// Thread-safe allocation of an index from the pool.
    /// This method can be called from jobs in parallel.
    /// </summary>
    /// <param name="index">The allocated index, or -1 if allocation failed</param>
    /// <returns>True if allocation succeeded, false if no free indices available</returns>
    public bool TryAllocate(out int index)
    {
        // Atomically decrement and get the free index
        int freeIndex = System.Threading.Interlocked.Decrement(ref *_mNextFreeIndex);
        
        if (freeIndex >= 0)
        {
            index = _mFreeIndices[freeIndex];
            return true;
        }
        
        // Restore the counter if we went negative
        System.Threading.Interlocked.Increment(ref *_mNextFreeIndex);
        index = -1;
        return false;
    }

    /// <summary>
    /// Allocates an index from the pool. Throws if no free indices available.
    /// This method can be called from jobs in parallel.
    /// </summary>
    /// <returns>The allocated index</returns>
    /// <exception cref="System.InvalidOperationException">Thrown when no free indices are available</exception>
    public int Allocate()
    {
        if (TryAllocate(out int index))
        {
            return index;
        }
        throw new System.InvalidOperationException("No free indices available in pool");
    }

    /// <summary>
    /// Frees an index back to the pool. 
    /// This method is NOT thread-safe and should only be called from the main thread.
    /// </summary>
    /// <param name="index">The index to free</param>
    public void Free(int index)
    {
        if (index < 0 || index >= _mItems.Length)
        {
            throw new System.ArgumentOutOfRangeException(nameof(index), "Index out of range");
        }

        // Clear the item at this index
        _mItems[index] = default(T);
        
        // Add the index back to the free stack
        int currentFreeIndex = *_mNextFreeIndex;
        *_mNextFreeIndex = currentFreeIndex + 1;
        
        if (*_mNextFreeIndex >= _mFreeIndices.Capacity)
        {
            _mFreeIndices.Resize(*_mNextFreeIndex + 1);
        }
        
        _mFreeIndices[*_mNextFreeIndex] = index;
    }

    /// <summary>
    /// Gets a reference to the item at the specified index
    /// </summary>
    /// <param name="index">The index to access</param>
    /// <returns>Reference to the item</returns>
    public ref T this[int index]
    {
        get
        {
            if (index < 0 || index >= _mItems.Length)
            {
                throw new System.ArgumentOutOfRangeException(nameof(index), "Index out of range");
            }
            return ref _mItems.ElementAt(index);
        }
    }

    /// <summary>
    /// Gets a pointer to the item at the specified index for unsafe operations
    /// </summary>
    /// <param name="index">The index to access</param>
    /// <returns>Pointer to the item</returns>
    public T* GetPtr(int index)
    {
        if (index < 0 || index >= _mItems.Length)
        {
            throw new System.ArgumentOutOfRangeException(nameof(index), "Index out of range");
        }
        return (T*)_mItems.Ptr + index;
    }

    /// <summary>
    /// Expands the pool capacity by the specified amount
    /// </summary>
    /// <param name="additionalCapacity">Number of additional slots to add</param>
    public void ExpandCapacity(int additionalCapacity)
    {
        int oldCapacity = _mItems.Length;
        int newCapacity = oldCapacity + additionalCapacity;
        
        _mItems.Resize(newCapacity);
        
        // Add new indices to the free stack
        for (int i = newCapacity - 1; i >= oldCapacity; i--)
        {
            _mItems[i] = default(T);
            *_mNextFreeIndex = *_mNextFreeIndex + 1;
            
            if (*_mNextFreeIndex >= _mFreeIndices.Capacity)
            {
                _mFreeIndices.Resize(*_mNextFreeIndex + 1);
            }
            
            _mFreeIndices[*_mNextFreeIndex] = i;
        }
    }

    /// <summary>
    /// Clears the pool and marks all indices as free
    /// </summary>
    public void Clear()
    {
        // Reset all items to default
        for (int i = 0; i < _mItems.Length; i++)
        {
            _mItems[i] = default(T);
        }
        
        // Rebuild free indices stack
        _mFreeIndices.Clear();
        for (int i = _mItems.Length - 1; i >= 0; i--)
        {
            _mFreeIndices.Add(i);
        }
        *_mNextFreeIndex = _mItems.Length - 1;
    }

    /// <summary>
    /// Disposes the pool and frees all allocated memory
    /// </summary>
    public void Dispose()
    {
        if (!_mIsCreated) return;
        
        if (_mItems.IsCreated)
            _mItems.Dispose();
            
        if (_mFreeIndices.IsCreated)
            _mFreeIndices.Dispose();
            
        if (_mNextFreeIndex != null)
        {
            UnsafeUtility.Free(_mNextFreeIndex, _mAllocator);
            _mNextFreeIndex = null;
        }
        
        _mIsCreated = false;
    }
}

// Example job showing how to use the pool in parallel
public struct ExampleAllocationJob : IJobParallelFor
{
    public NativePool<float> Pool;
    public NativeArray<int> AllocatedIndices;

    public void Execute(int index)
    {
        if (Pool.TryAllocate(out int poolIndex))
        {
            AllocatedIndices[index] = poolIndex;
            Pool[poolIndex] = UnityEngine.Random.value;
        }
        else
        {
            AllocatedIndices[index] = -1;
        }
    }
}