
using Unity.Collections;
using Unity.Mathematics;

public static class BlockPath
{
    public static NativeArray<int3> Extend(NativeArray<int3> parent, int3 end)
    {
        if (!parent.IsCreated)
        {
            var b = new NativeArray<int3>(1, Allocator.Persistent);
            b[0] = end;
            return b;
        }
        var p = new NativeArray<int3>(parent.Length + 1, Allocator.Persistent);
        for (int i = 0; i < parent.Length; i++)
        {
            p[i] = parent[i];
        }
        p[parent.Length] = end;
        return p;
    }
}
