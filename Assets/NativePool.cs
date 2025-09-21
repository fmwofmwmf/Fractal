using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

public struct NativePool<T> where T : unmanaged
{
    private NativeList<T> _data;
    private NativeList<int> _free;

    public NativePool(int initialCapacity, Allocator allocator)
    {
        _data = new NativeList<T>(initialCapacity, allocator);
        _free = new NativeList<int>(allocator);
    }

    public int Alloc()
    {
        if (_free.Length > 0)
        {
            int last = _free.Length - 1;
            int idx = _free[last];
            _free.RemoveAtSwapBack(last);
            return idx;
        }
        else
        {
            int idx = _data.Length;
            _data.Add(default); // can resize on main thread
            return idx;
        }
    }

    public void Free(int index)
    {
        _free.Add(index);
    }

    public ref T this[int index] => ref _data.ElementAt(index);

    public bool IsCreated => _data.IsCreated;

    // Ensure sufficient capacity before parallel operations
    public void EnsureCapacity(int minCapacity)
    {
        if (_data.Capacity < minCapacity)
            _data.Capacity = minCapacity;
    }

    public unsafe ParallelWriter AsParallelWriter(int maxExpectedAllocs, Allocator allocator)
    {
        // Pre-allocate maximum expected capacity
        int targetLength = _data.Length + maxExpectedAllocs;
        EnsureCapacity(targetLength);
        
        // IMPORTANT: Set the length to the target size so ElementAt works
        _data.Length = targetLength;
        
        // Allocate counter pointer
        int* counterPtr = (int*)UnsafeUtility.Malloc(sizeof(int), sizeof(int), allocator);
        *counterPtr = _data.Length - maxExpectedAllocs; // Start from original length
        
        return new ParallelWriter
        {
            _data = _data,
            _counterPtr = counterPtr,
            _maxIndex = targetLength - 1
        };
    }

    // Call this after parallel operations to update the list length and clean up
    // Returns the number of items that were allocated during the parallel job
    public unsafe int CommitParallelLength(ParallelWriter writer)
    {
        int oldLength = _data.Length;
        int newLength = *writer._counterPtr;
        int allocatedCount = newLength - oldLength;
        
        _data.Length = newLength;
        UnsafeUtility.Free(writer._counterPtr, Allocator.TempJob);
        
        return allocatedCount;
    }

    public void Dispose()
    {
        if (_data.IsCreated) _data.Dispose();
        if (_free.IsCreated) _free.Dispose();
    }

    public struct ParallelWriter
    {
        [NativeDisableParallelForRestriction]
        internal NativeList<T> _data;
        
        [NativeDisableUnsafePtrRestriction]
        internal unsafe int* _counterPtr;
        
        internal int _maxIndex;

        // Allocate and return index
        public unsafe int Alloc(T value = default)
        {
            // Atomic increment to get next index
            int index = System.Threading.Interlocked.Increment(ref *_counterPtr) - 1;
            
            // Check bounds against actual allocated length
            if (index > _maxIndex)
            {
                // Capacity exceeded - return invalid index
                return -1;
            }
            
            _data[index] = value;
            return index;
        }

        // Allocate and return reference for complex assignment
        public unsafe ref T AllocRef(out int index)
        {
            index = System.Threading.Interlocked.Increment(ref *_counterPtr) - 1;
            
            if (index > _maxIndex)
            {
                // Capacity exceeded - return invalid index and dummy ref
                index = -1;
                return ref _data.ElementAt(0);
            }
            
            return ref _data.ElementAt(index);
        }

        // Assign to specific index (for pre-allocated indices)
        public void Set(int index, T value)
        {
            _data[index] = value;
        }

        // Get reference to specific index
        public ref T GetRef(int index)
        {
            return ref _data.ElementAt(index);
        }
        
        // Get current allocation count
        public unsafe int CurrentCount => *_counterPtr;
    }
}