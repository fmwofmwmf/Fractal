using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class ChunkMeshRenderer : MonoBehaviour
{
    public List<(Mesh, Chunk)> chunks = new List<(Mesh, Chunk)>();
    public Material material;

    void Update()
    {
        RenderParams rp = new RenderParams(material);
        foreach (var (mesh, chunk) in chunks)
        {
            Graphics.RenderMesh(rp, mesh, 0, transform.localToWorldMatrix * Matrix4x4.Translate(chunk.GetWorldPosition()));
        }
    }

    public void AddChunk(Mesh m, Chunk chunk)
    {
        chunks.Add((m, chunk));
    }
    
    public void Clear()
    {
        chunks.Clear();
    }
}