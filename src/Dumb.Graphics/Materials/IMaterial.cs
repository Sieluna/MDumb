using Sia;
using Silk.NET.WebGPU;

namespace Dumb.Graphics;

public interface IMaterial
{
    public static abstract string Name { get; }
    public static abstract Engine.Mesh.MeshDescriptor VertexDescriptor { get; }
    public static abstract BindingLayout[][] BindGroupLayouts { get; }
    public static virtual BlendState? Blend => null;
    public static virtual DepthStencilState? DepthStencil => null;
    public static virtual TextureFormat[] ColorFormats => [TextureFormat.Rgba8Unorm];

    public Entity GetShader(GraphicsContext ctx);
    public Entity?[] CreateBindGroups(GraphicsContext ctx, Entity pipelineLayout);
}
