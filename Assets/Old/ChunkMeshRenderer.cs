using System;
using System.Collections.Generic;
using UnityEngine;

public class ChunkMeshRenderer : MonoBehaviour
{
    public Dictionary<int, (Mesh, Vector3)> Chunks = new ();
    public Material material;

    void Update()
    {
        RenderParams rp = new RenderParams(material);
        foreach (var (mesh, pos) in Chunks.Values)
        {
            Graphics.RenderMesh(rp, mesh, 0, transform.localToWorldMatrix * Matrix4x4.Translate(pos));
        }
    }

    public void RemoveChunk(Chunk chunk)
    {
        Chunks.Remove(chunk.Id);
    }

    public void AddChunk(Mesh m, Chunk chunk, ChunkPath origin)
    {
        Chunks[chunk.Id] = (m, chunk.GetRelativeWorldPosition(origin));
    }
    
    public void Clear()
    {
        Chunks.Clear();
    }
}