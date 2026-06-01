using Silk.NET.WebGPU;
using Sia;

namespace Dumb.Graphics;

public struct BufferData
{
    public nint NativePtr;
    public ulong Size;
    public BufferUsage Usage;
    public int RefCount;
}

public struct TextureData
{
    public nint NativePtr;
    public Extent3D Size;
    public TextureFormat Format;
    public TextureUsage Usage;
    public uint MipLevelCount;
    public uint SampleCount;
    public int RefCount;
}

public struct TextureViewData
{
    public nint NativePtr;
    public Entity Texture;
    public int RefCount;
}

public struct SamplerData
{
    public nint NativePtr;
    public int RefCount;
}

public struct ShaderData
{
    public nint NativePtr;
    public int RefCount;
}

public struct BindGroupLayoutData
{
    public nint NativePtr;
    public int RefCount;
}

public struct BindGroupData
{
    public nint NativePtr;
    public Entity Layout;
    public int RefCount;
}

public struct PipelineLayoutData
{
    public nint NativePtr;
    public uint BindGroupLayoutCount;
    public Entity[]? BindGroupLayouts;
    public int RefCount;
}

public struct RenderPipelineData
{
    public nint NativePtr;
    public Entity VertexShader;
    public Entity FragmentShader;
    public Entity Layout;
    public int RefCount;
}

public struct ComputePipelineData
{
    public nint NativePtr;
    public Entity ComputeShader;
    public Entity Layout;
    public int RefCount;
}

public struct MaterialResourceData
{
    public Entity Pipeline;
    public Entity PipelineLayout;
    public Entity?[] BindGroups;
    public bool IsForward;
    public int RefCount;
}

public struct MeshResourceData
{
    public Entity[] VertexBuffers;
    public int[] VertexCounts;
    public Entity IndexBuffer;
    public IndexFormat IndexFormat;
    public uint IndexCount;
    public Engine.Mesh.SubMesh[] SubMeshes;
    public int RefCount;
}
