
using System;
using NUnit.Framework;
using UnityEngine;

public class UnitTests : MonoBehaviour
{
    private void Start()
    {
        var p1 = PathOps.FromPositions(new LocalBlockPos(1,2,3));
        var h1 = p1.Add(new LocalBlockPos(4, 5, 6));
        
        Debug.Assert(p1.HashWithChild(new LocalBlockPos(4, 5, 6)) == h1.Hash());
        
        var p = PathOps.FromPositions(new LocalBlockPos(1,2,3));
        Debug.Assert(p.Hash() == BlockHasher.ExtendHash(0, 1, 2, 3));
        
        var p2 = PathOps.FromPositions(LocalBlockPos.Origin, new LocalBlockPos(1,2,3));
        var p3 = p2.Add(new LocalBlockPos(16, 31, 6));
        var h2 = BlockHasher.RectifyPath(p3);
        var h12 = p2.Hash();
        //Debug.Assert(h2 == BlockHasher.ExtendHash(hash, 0, 15, 6));
        //Debug.Assert(b1);
        
        p1.Dispose();
        p2.Dispose();
        p3.Dispose();
        h1.Dispose();
        p.Dispose();
    }
}
