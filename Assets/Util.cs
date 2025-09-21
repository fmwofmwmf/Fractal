using System;
using Unity.Collections;

public class ArraySlice<T>
{
    private readonly T[] _array;
    public int Min { get; private set; }
    public int Max { get; private set; } // exclusive

    public int Length => Max - Min;

    public ArraySlice(T[] array, int min, int max)
    {
        if (array == null) throw new ArgumentNullException(nameof(array));
        if (min < 0 || max > array.Length || min > max)
            throw new ArgumentOutOfRangeException($"Invalid slice range [{min}, {max})");

        _array = array;
        Min = min;
        Max = max;
    }

    // Indexer: accesses _array[Min + index]
    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= Length)
                throw new IndexOutOfRangeException();
            return _array[Min + index];
        }
        set
        {
            if (index < 0 || index >= Length)
                throw new IndexOutOfRangeException();
            _array[Min + index] = value;
        }
    }

    // Create a new slice of this slice
    public ArraySlice<T> Slice(int start, int length)
    {
        if (start < 0 || length < 0 || start + length > Length)
            throw new ArgumentOutOfRangeException();
        return new ArraySlice<T>(_array, Min + start, Min + start + length);
    }

    // Enumerate elements
    public System.Collections.Generic.IEnumerator<T> GetEnumerator()
    {
        for (int i = Min; i < Max; i++)
            yield return _array[i];
    }

    // Optionally, allow implicit conversion back to array segment (T[])
    public T[] ToArray()
    {
        int len = Length;
        T[] result = new T[len];
        Array.Copy(_array, Min, result, 0, len);
        return result;
    }
}

public class Util
{
    public static string SliceToString(NativeArray<byte> blocks, int size, int z)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int idx = x + size * (y + size * z); // no padding
                sb.Append(blocks[idx] == 0 ? '.' : '#'); // empty=., block=#
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}