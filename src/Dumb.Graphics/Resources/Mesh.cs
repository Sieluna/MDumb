using Sia;
using Silk.NET.WebGPU;

namespace Dumb.Graphics.Resources;

public static class Mesh
{
    public static Entity Create(GraphicsContext ctx, Engine.Mesh.MeshData data)
    {
        if (!data.TryValidate(out var validationError))
            throw new InvalidOperationException($"Mesh validation failed: {validationError}");

        var desc = data.Descriptor;
        var vertexBuffers = new Entity[data.Streams.Length];
        for (var i = 0; i < data.Streams.Length; i++)
        {
            var stream = data.Streams[i] ?? throw new InvalidOperationException("Mesh stream is null");
            var buffer = Buffers.Create(ctx, (ulong)stream.Length,
                BufferUsage.Vertex | BufferUsage.CopyDst);
            Buffers.Write(ctx, buffer, 0, stream);
            vertexBuffers[i] = buffer;
        }

        Entity indexBuffer = null!;
        if (!data.Indices.IsEmpty)
        {
            var indicesSpan = data.Indices.GetSpan();
            indexBuffer = Buffers.Create(ctx, (ulong)indicesSpan.Length,
                BufferUsage.Index | BufferUsage.CopyDst);
            Buffers.Write(ctx, indexBuffer, 0, indicesSpan);
        }

        var subMeshes = data.SubMeshes;
        if (subMeshes.Length == 0)
        {
            subMeshes =
            [
                new Engine.Mesh.SubMesh(
                    0, (uint)data.IndexCount, 0, (uint)data.VertexCount,
                    PrimitiveTopology.TriangleList)
            ];
        }

        var vertexCounts = desc.Streams.Select((s, i) =>
            s.StepMode == VertexStepMode.Instance
                ? data.Streams[i].Length / (int)Engine.Mesh.MeshDescriptor.StreamStride(s.Elements)
                : data.VertexCount
        ).ToArray();

        return ctx._meshes.Create(HList.From(new MeshResourceData
        {
            VertexBuffers = vertexBuffers,
            VertexCounts = vertexCounts,
            IndexBuffer = indexBuffer,
            IndexFormat = data.IndexFormat,
            IndexCount = (uint)data.IndexCount,
            SubMeshes = subMeshes,
            RefCount = 1
        }));
    }

    public static void Retain(GraphicsContext ctx, Entity mesh)
    {
        ref var data = ref mesh.Get<MeshResourceData>();
        Interlocked.Increment(ref data.RefCount);
    }

    public static void Release(GraphicsContext ctx, Entity mesh)
    {
        ref var data = ref mesh.Get<MeshResourceData>();
        if (Interlocked.Decrement(ref data.RefCount) == 0)
        {
            foreach (var vb in data.VertexBuffers)
            {
                if (vb.Host != null)
                    Buffers.Release(ctx, vb);
            }
            if (data.IndexBuffer.Host != null)
                Buffers.Release(ctx, data.IndexBuffer);
            mesh.Destroy();
        }
    }

    public static void Draw(RenderPass pass, Entity mesh, uint subMeshIndex = 0)
    {
        ref var data = ref mesh.Get<MeshResourceData>();
        var sm = data.SubMeshes[subMeshIndex];

        for (var i = 0; i < data.VertexBuffers.Length; i++)
            pass.SetVertexBuffer((uint)i, data.VertexBuffers[i]);

        if (data.IndexBuffer.Host != null)
        {
            pass.SetIndexBuffer(data.IndexBuffer, data.IndexFormat);
            pass.DrawIndexed(sm.IndexCount, 1, sm.IndexStart, (int)sm.VertexStart);
        }
        else
        {
            pass.Draw(sm.VertexCount, 1, sm.VertexStart);
        }
    }

    public static void DrawInstanced(RenderPass pass, Entity mesh, uint instanceCount, uint subMeshIndex = 0)
    {
        ref var data = ref mesh.Get<MeshResourceData>();
        var sm = data.SubMeshes[subMeshIndex];

        for (var i = 0; i < data.VertexBuffers.Length; i++)
            pass.SetVertexBuffer((uint)i, data.VertexBuffers[i]);

        if (data.IndexBuffer.Host != null)
        {
            pass.SetIndexBuffer(data.IndexBuffer, data.IndexFormat);
            pass.DrawIndexed(sm.IndexCount, instanceCount, sm.IndexStart, (int)sm.VertexStart);
        }
        else
        {
            pass.Draw(sm.VertexCount, instanceCount, sm.VertexStart);
        }
    }

    public static VertexBufferLayoutDescriptor[] ToVertexBufferLayouts(
        Engine.Mesh.VertexStreamDescriptor[] streams)
    {
        return
        [
            ..streams.Select(static s =>
            {
                var layouts = new VertexAttributeLayout[s.Elements.Length];
                ulong offset = 0;
                for (var i = 0; i < s.Elements.Length; i++)
                {
                    var e = s.Elements[i];
                    var format = e.Attribute.Format;
                    var size = e.Attribute.Size;
                    layouts[i] = new VertexAttributeLayout(e.Location, format, offset);
                    offset += size;
                }
                return new VertexBufferLayoutDescriptor(layouts, offset, s.StepMode);
            })
        ];
    }
}
