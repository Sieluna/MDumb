using Sia;
using Silk.NET.WebGPU;

namespace Dumb.Graphics;

public static class Materials
{
    public static Entity Create<T>(GraphicsContext ctx, T material)
        where T : IMaterial
    {
        var shader = material.GetShader(ctx);

        var bglDescs = T.BindGroupLayouts;
        var bglEntities = new Entity[bglDescs.Length];
        for (var i = 0; i < bglDescs.Length; i++)
            bglEntities[i] = Pipelines.BindGroupLayout(ctx, bglDescs[i]);

        var pipelineLayout = Pipelines.Layout(ctx, bglEntities);

        var bufferLayouts = Mesh.ToVertexBufferLayouts(T.VertexDescriptor.Streams);

        var colorFormats = T.ColorFormats;
        var depthStencil = T.DepthStencil;
        Entity pipeline;
        if (colorFormats.Length == 1)
        {
            pipeline = Pipelines.Render(
                ctx, shader, pipelineLayout,
                colorFormats[0],
                TextureFormat.Depth32float,
                bufferLayouts,
                T.Blend,
                depthStencil);
        }
        else
        {
            pipeline = Pipelines.RenderMRT(
                ctx, shader, pipelineLayout,
                colorFormats,
                TextureFormat.Depth32float,
                bufferLayouts,
                T.Blend);
        }

        var bindGroups = material.CreateBindGroups(ctx, pipelineLayout);

        return ctx._materials.Create(HList.From(new MaterialResourceData
        {
            Pipeline = pipeline,
            PipelineLayout = pipelineLayout,
            BindGroups = bindGroups,
            IsForward = T.Blend != null,
            RefCount = 1
        }));
    }

    public static void Retain(GraphicsContext ctx, Entity material)
    {
        ref var data = ref material.Get<MaterialResourceData>();
        Interlocked.Increment(ref data.RefCount);
    }

    public static void Release(GraphicsContext ctx, Entity material)
    {
        ref var data = ref material.Get<MaterialResourceData>();
        if (Interlocked.Decrement(ref data.RefCount) == 0)
        {
            Pipelines.ReleaseRenderPipeline(ctx, data.Pipeline);
            Pipelines.ReleasePipelineLayout(ctx, data.PipelineLayout);
            if (data.BindGroups != null)
            {
                foreach (var bg in data.BindGroups)
                {
                    if (bg?.Host != null)
                        Pipelines.ReleaseBindGroup(ctx, bg);
                }
            }
            material.Destroy();
        }
    }
}
