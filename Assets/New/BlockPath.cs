
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

public static class BlockPath
{
    public static int ToInt(int3 p)
    {
        return p.x + 16 * p.y * (16 * p.z);
    }
    
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
    
    public static float3 LocalPos(NativeArray<int3> path)
    {
        // Compute how many zeros we need to pad on the front
        int pad = math.max(0, 4 - path.Length);

        float3 pos = float3.zero;
        float scale = 1f;

        // Iterate over the "padded" path starting at the 4th ancestor
        for (int i = -pad; i < path.Length; i++)
        {
            int3 step = (i < 0) ? int3.zero : path[i];
            pos += new float3(step) * scale;
            scale /= 16f;
        }

        return pos;
    }
    
    public static float3 GetRelativeWorldPosition(NativeArray<int3> origin, NativeArray<int3> point)
    {
        float3 pos = float3.zero;
        float scale;

        // Pad origin to at least 1 length if needed
        int originLength = origin.Length;

        for (int i = 0; i < point.Length; i++)
        {
            int3 originCoord = i < originLength ? origin[i] : int3.zero;
            int3 relativeCoord = point[i] - originCoord;

            scale = math.pow(1f / 16f, i + 1 - originLength); // scaling per depth
            pos += new float3(relativeCoord.x, relativeCoord.y, relativeCoord.z) * scale;
        }

        return pos;
    }

    
    [BurstCompile]
    public static NativeArray<int3> RectifyPathToCoords(NativeArray<int3> path)
    {
        int depth = path.Length;
        if (depth == 0)
            return path;
        
        unsafe
        {
            int* xs = stackalloc int[depth];
            int* ys = stackalloc int[depth];
            int* zs = stackalloc int[depth];

            // Copy coordinates
            for (int i = 0; i < depth; i++)
            {
                xs[i] = path[i].x;
                ys[i] = path[i].y;
                zs[i] = path[i].z;
            }

            // Propagate carries upward (leaf → root)
            for (int i = depth - 1; i >= 0; i--)
            {
                int cx = xs[i] >> 4;
                int cy = ys[i] >> 4;
                int cz = zs[i] >> 4;

                xs[i] &= 15;
                ys[i] &= 15;
                zs[i] &= 15;

                if (i > 0)
                {
                    xs[i - 1] += cx;
                    ys[i - 1] += cy;
                    zs[i - 1] += cz;
                }
                else
                {
                    // bubbled to root
                    if (cx != 0 || cy != 0 || cz != 0)
                    {
                        // root is not zero → invalid path, return empty array
                        return path;
                    }
                }
            }

            // Copy into the NativeArray
            for (int i = 0; i < depth; i++)
            {
                path[i] = new LocalBlockPos { x = xs[i], y = ys[i], z = zs[i] };
            }
        }

        return path;
    }
}
