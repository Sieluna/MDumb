using System.Linq;
using System.Text;
using System.Threading;
using Sia;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace Dumb.Graphics;

public readonly struct BindingLayout
{
    public readonly BindGroupLayoutEntry Entry;

    private BindingLayout(BindGroupLayoutEntry entry) => Entry = entry;

    public static BindingLayout UniformBuffer(uint binding, ShaderStage visibility, ulong minBindingSize, bool hasDynamicOffset = false)
        => Buffer(binding, visibility, BufferBindingType.Uniform, minBindingSize, hasDynamicOffset);

    public static BindingLayout StorageBuffer(
        uint binding, ShaderStage visibility, ulong minBindingSize, bool readOnly = false, bool hasDynamicOffset = false)
        => Buffer(binding, visibility, readOnly ? BufferBindingType.ReadOnlyStorage : BufferBindingType.Storage, minBindingSize, hasDynamicOffset);

    public static BindingLayout Sampler(
        uint binding, ShaderStage visibility, SamplerBindingType type = SamplerBindingType.Filtering)
        => new(new BindGroupLayoutEntry
        {
            Binding = binding,
            Visibility = visibility,
            Sampler = new SamplerBindingLayout { Type = type }
        });

    public static BindingLayout Texture(
        uint binding, ShaderStage visibility,
        TextureSampleType sampleType = TextureSampleType.Float,
        TextureViewDimension dimension = TextureViewDimension.Dimension2D,
        bool multisampled = false)
        => new(new BindGroupLayoutEntry
        {
            Binding = binding,
            Visibility = visibility,
            Texture = new TextureBindingLayout
            {
                SampleType = sampleType,
                ViewDimension = dimension,
                Multisampled = multisampled
            }
        });

    private static BindingLayout Buffer(
        uint binding, ShaderStage visibility, BufferBindingType type, ulong minBindingSize, bool hasDynamicOffset = false)
        => new(new BindGroupLayoutEntry
        {
            Binding = binding,
            Visibility = visibility,
            Buffer = new BufferBindingLayout
            {
                Type = type,
                HasDynamicOffset = hasDynamicOffset,
                MinBindingSize = minBindingSize
            }
        });
}

public readonly struct Binding
{
    public readonly uint Slot;
    public readonly BindingKind Kind;
    public readonly Entity Entity;
    public readonly nuint Size;

    private Binding(uint slot, BindingKind kind, Entity entity, nuint size)
    {
        Slot = slot;
        Kind = kind;
        Entity = entity;
        Size = size;
    }

    public static Binding Buffer(uint slot, Entity buffer, nuint size)
    {
        return new Binding(slot, BindingKind.Buffer, buffer, size);
    }

    public static unsafe Binding Uniform<T>(uint slot, Entity buffer) where T : unmanaged
    {
        return Buffer(slot, buffer, (nuint)sizeof(T));
    }

    public static unsafe Binding Storage<T>(uint slot, Entity buffer, int elementCount) where T : unmanaged
    {
        return Buffer(slot, buffer, (nuint)(sizeof(T) * elementCount));
    }

    public static Binding Texture(uint slot, Entity textureView)
    {
        return new Binding(slot, BindingKind.TextureView, textureView, 0);
    }

    public static Binding Sampler(uint slot, Entity sampler)
    {
        return new Binding(slot, BindingKind.Sampler, sampler, 0);
    }
}

public enum BindingKind
{
    Buffer,
    TextureView,
    Sampler
}

public readonly record struct VertexAttributeLayout(
    uint ShaderLocation,
    VertexFormat Format,
    ulong Offset);

public readonly record struct VertexBufferLayoutDescriptor
{
    public VertexAttributeLayout[] Attributes { get; init; }
    public ulong Stride { get; init; }
    public VertexStepMode StepMode { get; init; }

    public VertexBufferLayoutDescriptor(
        VertexAttributeLayout[] attributes,
        ulong stride,
        VertexStepMode stepMode = VertexStepMode.Vertex)
    {
        Attributes = attributes;
        Stride = stride;
        StepMode = stepMode;
    }
}

public static unsafe class Pipelines
{
    private static readonly StencilFaceState DefaultStencilFace = new()
    {
        Compare = CompareFunction.Always,
        FailOp = StencilOperation.Keep,
        DepthFailOp = StencilOperation.Keep,
        PassOp = StencilOperation.Keep
    };

    // --- BindGroupLayout ---

    public static Entity BindGroupLayout(
        GraphicsContext ctx,
        ReadOnlySpan<BindingLayout> entries)
    {
        var nativeEntries = stackalloc BindGroupLayoutEntry[entries.Length];
        for (var i = 0; i < entries.Length; i++)
            nativeEntries[i] = entries[i].Entry;

        BindGroupLayoutDescriptor descriptor = new()
        {
            EntryCount = (nuint)entries.Length,
            Entries = nativeEntries,
            Label = null
        };
        return CreateBindGroupLayout(ctx, descriptor);
    }

    public static Entity CreateBindGroupLayout(GraphicsContext ctx, BindGroupLayoutDescriptor descriptor)
    {
        var native = ctx.Device.CreateBindGroupLayout(ctx.NativeDevice, &descriptor);
        return ctx._bindGroupLayouts.Create(HList.From(new BindGroupLayoutData { NativePtr = native, RefCount = 1 }));
    }

    public static void ReleaseBindGroupLayout(GraphicsContext ctx, Entity bgl)
    {
        ref var data = ref bgl.Get<BindGroupLayoutData>();
        if (Interlocked.Decrement(ref data.RefCount) == 0)
        {
            ctx.Device.ReleaseBindGroupLayout(data.NativePtr);
            bgl.Destroy();
        }
    }

    // --- BindGroup ---

    public static Entity BindGroup(
        GraphicsContext ctx,
        Entity layout,
        ReadOnlySpan<Binding> bindings)
    {
        var entries = stackalloc BindGroupEntry[bindings.Length];
        for (var i = 0; i < bindings.Length; i++)
        {
            var binding = bindings[i];
            entries[i] = new BindGroupEntry
            {
                Binding = binding.Slot,
                Offset = 0,
                Size = binding.Size,
                Buffer = null,
                TextureView = null,
                Sampler = null
            };

            switch (binding.Kind)
            {
                case BindingKind.Buffer:
                    entries[i].Buffer = (WgpuBuffer*)binding.Entity.Get<BufferData>().NativePtr;
                    break;
                case BindingKind.TextureView:
                    entries[i].TextureView = (TextureView*)binding.Entity.Get<TextureViewData>().NativePtr;
                    break;
                case BindingKind.Sampler:
                    entries[i].Sampler = (Sampler*)binding.Entity.Get<SamplerData>().NativePtr;
                    break;
            }
        }

        BindGroupDescriptor descriptor = new()
        {
            Layout = (BindGroupLayout*)layout.Get<BindGroupLayoutData>().NativePtr,
            EntryCount = (nuint)bindings.Length,
            Entries = entries,
            Label = null
        };
        return CreateBindGroup(ctx, descriptor, layout);
    }

    public static Entity CreateBindGroup(
        GraphicsContext ctx, BindGroupDescriptor descriptor, Entity layout)
    {
        if (layout.Host == null)
            throw new InvalidOperationException("BindGroupLayout entity not alive.");

        var native = ctx.Device.CreateBindGroup(ctx.NativeDevice, &descriptor);
        return ctx._bindGroups.Create(HList.From(new BindGroupData
        {
            NativePtr = native,
            Layout = layout,
            RefCount = 1
        }));
    }

    public static void ReleaseBindGroup(GraphicsContext ctx, Entity bindGroup)
    {
        ref var bg = ref bindGroup.Get<BindGroupData>();
        if (Interlocked.Decrement(ref bg.RefCount) == 0)
        {
            ctx.Device.ReleaseBindGroup(bg.NativePtr);
            bindGroup.Destroy();
        }
    }

    // --- PipelineLayout ---

    public static Entity Layout(
        GraphicsContext ctx,
        ReadOnlySpan<Entity> bindGroupLayouts)
    {
        var nativeLayouts = stackalloc BindGroupLayout*[bindGroupLayouts.Length];
        for (var i = 0; i < bindGroupLayouts.Length; i++)
            nativeLayouts[i] = (BindGroupLayout*)bindGroupLayouts[i].Get<BindGroupLayoutData>().NativePtr;

        PipelineLayoutDescriptor descriptor = new()
        {
            BindGroupLayoutCount = (nuint)bindGroupLayouts.Length,
            BindGroupLayouts = nativeLayouts,
            Label = null
        };
        return CreatePipelineLayout(ctx, descriptor, bindGroupLayouts);
    }

    public static Entity CreatePipelineLayout(
        GraphicsContext ctx, PipelineLayoutDescriptor descriptor,
        ReadOnlySpan<Entity> bindGroupLayouts)
    {
        var native = ctx.Device.CreatePipelineLayout(ctx.NativeDevice, &descriptor);

        var handles = bindGroupLayouts.Length > 0
            ? bindGroupLayouts.ToArray()
            : null;

        return ctx._pipelineLayouts.Create(HList.From(new PipelineLayoutData
        {
            NativePtr = native,
            BindGroupLayoutCount = (uint)bindGroupLayouts.Length,
            BindGroupLayouts = handles,
            RefCount = 1
        }));
    }

    public static void ReleasePipelineLayout(GraphicsContext ctx, Entity pipelineLayout)
    {
        ref var pl = ref pipelineLayout.Get<PipelineLayoutData>();
        if (Interlocked.Decrement(ref pl.RefCount) == 0)
        {
            if (pl.BindGroupLayouts is { } bgls)
            {
                foreach (var bgl in bgls)
                {
                    if (bgl.Host != null)
                        ReleaseBindGroupLayout(ctx, bgl);
                }
            }
            ctx.Device.ReleasePipelineLayout(pl.NativePtr);
            pipelineLayout.Destroy();
        }
    }

    // --- RenderPipeline (with dedup) ---

    public static Entity Render(
        GraphicsContext ctx,
        Entity shader,
        Entity layout,
        TextureFormat colorFormat,
        string vertexEntryPoint = "vs_main",
        string fragmentEntryPoint = "fs_main")
    {
        return Render(
            ctx,
            shader,
            layout,
            colorFormat,
            ReadOnlySpan<VertexBufferLayoutDescriptor>.Empty,
            vertexEntryPoint,
            fragmentEntryPoint);
    }

    public static Entity Render(
        GraphicsContext ctx,
        Entity shader,
        Entity layout,
        TextureFormat colorFormat,
        ReadOnlySpan<VertexBufferLayoutDescriptor> vertexBuffers,
        string vertexEntryPoint = "vs_main",
        string fragmentEntryPoint = "fs_main")
    {
        return Render(ctx, shader, layout, colorFormat, null, vertexBuffers, null, null, vertexEntryPoint, fragmentEntryPoint);
    }

    public static Entity Render(
        GraphicsContext ctx,
        Entity shader,
        Entity layout,
        TextureFormat colorFormat,
        TextureFormat? depthFormat,
        ReadOnlySpan<VertexBufferLayoutDescriptor> vertexBuffers,
        BlendState? blend = null,
        DepthStencilState? depthStencil = null,
        string vertexEntryPoint = "vs_main",
        string fragmentEntryPoint = "fs_main")
    {
        DepthStencilState ds = default;
        DepthStencilState* dsPtr = null;
        if (depthStencil is { } dss)
        {
            ds = dss;
            dsPtr = &ds;
        }
        else if (depthFormat is { } df)
        {
            ds = new DepthStencilState
            {
                Format = df,
                DepthWriteEnabled = true,
                DepthCompare = CompareFunction.Less,
                StencilFront = DefaultStencilFace,
                StencilBack = DefaultStencilFace
            };
            dsPtr = &ds;
        }

        BlendState bs = default;
        BlendState* bsPtr = null;
        if (blend is { } b)
        {
            bs = b;
            bsPtr = &bs;
        }

        var vsBytes = Encoding.UTF8.GetBytes(vertexEntryPoint + '\0');
        var fsBytes = Encoding.UTF8.GetBytes(fragmentEntryPoint + '\0');
        fixed (byte* vsPtr = vsBytes)
        fixed (byte* fsPtr = fsBytes)
        {
            if (vertexBuffers.Length > 0)
            {
                var totalAttrs = vertexBuffers.ToArray().Sum(static vb => vb.Attributes.Length);

                Span<VertexAttribute> allNativeAttrs = stackalloc VertexAttribute[totalAttrs];
                Span<VertexBufferLayout> nativeBuffers = stackalloc VertexBufferLayout[vertexBuffers.Length];

                fixed (VertexAttribute* attrPtr = allNativeAttrs)
                fixed (VertexBufferLayout* bufPtr = nativeBuffers)
                {
                    var attrOff = 0;
                    for (var i = 0; i < vertexBuffers.Length; i++)
                    {
                        var desc = vertexBuffers[i];
                        for (var j = 0; j < desc.Attributes.Length; j++)
                        {
                            attrPtr[attrOff + j] = new VertexAttribute
                            {
                                ShaderLocation = desc.Attributes[j].ShaderLocation,
                                Format = desc.Attributes[j].Format,
                                Offset = desc.Attributes[j].Offset
                            };
                        }
                        bufPtr[i] = new VertexBufferLayout
                        {
                            ArrayStride = desc.Stride,
                            StepMode = desc.StepMode,
                            AttributeCount = (nuint)desc.Attributes.Length,
                            Attributes = attrPtr + attrOff
                        };
                        attrOff += desc.Attributes.Length;
                    }
                    return RenderCore(ctx, shader, layout, colorFormat, vsPtr, fsPtr, bufPtr, (uint)vertexBuffers.Length, bsPtr, dsPtr);
                }
            }
            return RenderCore(ctx, shader, layout, colorFormat, vsPtr, fsPtr, null, 0, bsPtr, dsPtr);
        }
    }

    private static Entity RenderCore(
        GraphicsContext ctx,
        Entity shader,
        Entity layout,
        TextureFormat colorFormat,
        byte* vertexEntryPoint,
        byte* fragmentEntryPoint,
        VertexBufferLayout* vertexBuffers,
        uint vertexBufferCount,
        BlendState* blend = null,
        DepthStencilState* depthStencil = null)
    {
        ColorTargetState colorTarget = new()
        {
            Format = colorFormat,
            Blend = blend,
            WriteMask = ColorWriteMask.All
        };

        FragmentState fragment = new()
        {
            Module = (ShaderModule*)shader.Get<ShaderData>().NativePtr,
            EntryPoint = fragmentEntryPoint,
            TargetCount = 1,
            Targets = &colorTarget
        };

        RenderPipelineDescriptor descriptor = new()
        {
            Layout = (PipelineLayout*)layout.Get<PipelineLayoutData>().NativePtr,
            Vertex = new VertexState
            {
                Module = (ShaderModule*)shader.Get<ShaderData>().NativePtr,
                EntryPoint = vertexEntryPoint,
                BufferCount = vertexBufferCount,
                Buffers = vertexBuffers
            },
            Primitive = new PrimitiveState
            {
                Topology = PrimitiveTopology.TriangleList,
                StripIndexFormat = IndexFormat.Undefined,
                FrontFace = FrontFace.Ccw,
                CullMode = CullMode.None
            },
            Multisample = new MultisampleState
            {
                Count = 1,
                Mask = uint.MaxValue,
                AlphaToCoverageEnabled = false
            },
            Fragment = &fragment,
            DepthStencil = depthStencil,
            Label = null
        };

        return CreateRenderPipeline(ctx, &descriptor, shader, shader, layout);
    }

    public static Entity CreateRenderPipeline(
        GraphicsContext ctx,
        RenderPipelineDescriptor* descriptor,
        Entity vertexShader,
        Entity fragmentShader,
        Entity layout)
    {
        if (vertexShader.Host == null ||
            fragmentShader.Host == null ||
            layout.Host == null)
            throw new InvalidOperationException("Pipeline dependency not alive.");

        var vsId = vertexShader.Id.Value;
        var fsId = fragmentShader.Id.Value;
        var loId = layout.Id.Value;

        // Dedup: search existing pipelines for matching shader+layout combination.
        foreach (var entity in ctx._renderPipelines)
        {
            ref var existing = ref entity.Get<RenderPipelineData>();
            if (existing.VertexShader.Id.Value == vsId &&
                existing.FragmentShader.Id.Value == fsId &&
                existing.Layout.Id.Value == loId)
            {
                Interlocked.Increment(ref existing.RefCount);
                return entity;
            }
        }

        var native = ctx.Device.CreateRenderPipeline(ctx.NativeDevice, descriptor);
        return ctx._renderPipelines.Create(HList.From(new RenderPipelineData
        {
            NativePtr = native,
            VertexShader = vertexShader,
            FragmentShader = fragmentShader,
            Layout = layout,
            RefCount = 1
        }));
    }

    // --- RenderPipeline Depth-Only ---

    public static Entity RenderDepthOnly(
        GraphicsContext ctx,
        Entity shader,
        Entity layout,
        TextureFormat depthFormat,
        ReadOnlySpan<VertexBufferLayoutDescriptor> vertexBuffers,
        string vertexEntryPoint = "vs_main",
        string fragmentEntryPoint = "fs_main")
    {
        DepthStencilState ds = new()
        {
            Format = depthFormat,
            DepthWriteEnabled = true,
            DepthCompare = CompareFunction.Less,
            StencilFront = DefaultStencilFace,
            StencilBack = DefaultStencilFace
        };

        var vsBytes = Encoding.UTF8.GetBytes(vertexEntryPoint + '\0');
        var fsBytes = Encoding.UTF8.GetBytes(fragmentEntryPoint + '\0');
        fixed (byte* vsPtr = vsBytes)
        fixed (byte* fsPtr = fsBytes)
        {
            VertexBufferLayout* vbPtr = null;
            uint vbCount = 0;
            if (vertexBuffers.Length > 0)
            {
                var totalAttrs = vertexBuffers.ToArray().Sum(static vb => vb.Attributes.Length);

                Span<VertexAttribute> allNativeAttrs = stackalloc VertexAttribute[totalAttrs];
                Span<VertexBufferLayout> nativeBuffers = stackalloc VertexBufferLayout[vertexBuffers.Length];

                fixed (VertexAttribute* attrPtr = allNativeAttrs)
                fixed (VertexBufferLayout* bufPtr = nativeBuffers)
                {
                    var attrOff = 0;
                    for (var i = 0; i < vertexBuffers.Length; i++)
                    {
                        var desc = vertexBuffers[i];
                        for (var j = 0; j < desc.Attributes.Length; j++)
                        {
                            attrPtr[attrOff + j] = new VertexAttribute
                            {
                                ShaderLocation = desc.Attributes[j].ShaderLocation,
                                Format = desc.Attributes[j].Format,
                                Offset = desc.Attributes[j].Offset
                            };
                        }
                        bufPtr[i] = new VertexBufferLayout
                        {
                            ArrayStride = desc.Stride,
                            StepMode = desc.StepMode,
                            AttributeCount = (nuint)desc.Attributes.Length,
                            Attributes = attrPtr + attrOff
                        };
                        attrOff += desc.Attributes.Length;
                    }
                    vbPtr = bufPtr;
                    vbCount = (uint)vertexBuffers.Length;
                }
            }

            FragmentState fragment = new()
            {
                Module = (ShaderModule*)shader.Get<ShaderData>().NativePtr,
                EntryPoint = fsPtr,
                TargetCount = 0,
                Targets = null
            };

            RenderPipelineDescriptor descriptor = new()
            {
                Layout = (PipelineLayout*)layout.Get<PipelineLayoutData>().NativePtr,
                Vertex = new VertexState
                {
                    Module = (ShaderModule*)shader.Get<ShaderData>().NativePtr,
                    EntryPoint = vsPtr,
                    BufferCount = vbCount,
                    Buffers = vbPtr
                },
                Primitive = new PrimitiveState
                {
                    Topology = PrimitiveTopology.TriangleList,
                    StripIndexFormat = IndexFormat.Undefined,
                    FrontFace = FrontFace.Ccw,
                    CullMode = CullMode.None
                },
                Multisample = new MultisampleState
                {
                    Count = 1,
                    Mask = uint.MaxValue,
                    AlphaToCoverageEnabled = false
                },
                Fragment = &fragment,
                DepthStencil = &ds,
                Label = null
            };

            return CreateRenderPipeline(ctx, &descriptor, shader, shader, layout);
        }
    }

    // --- RenderPipeline MRT (multiple color targets) ---

    public static Entity RenderMRT(
        GraphicsContext ctx,
        Entity shader,
        Entity layout,
        ReadOnlySpan<TextureFormat> colorFormats,
        TextureFormat? depthFormat,
        ReadOnlySpan<VertexBufferLayoutDescriptor> vertexBuffers,
        BlendState? blend = null,
        string vertexEntryPoint = "vs_main",
        string fragmentEntryPoint = "fs_main")
    {
        DepthStencilState ds = default;
        DepthStencilState* dsPtr = null;
        if (depthFormat is { } df)
        {
            ds = new DepthStencilState
            {
                Format = df,
                DepthWriteEnabled = true,
                DepthCompare = CompareFunction.Less,
                StencilFront = DefaultStencilFace,
                StencilBack = DefaultStencilFace
            };
            dsPtr = &ds;
        }

        BlendState bs = default;
        BlendState* bsPtr = null;
        if (blend is { } b)
        {
            bs = b;
            bsPtr = &bs;
        }

        var vsBytes = Encoding.UTF8.GetBytes(vertexEntryPoint + '\0');
        var fsBytes = Encoding.UTF8.GetBytes(fragmentEntryPoint + '\0');
        fixed (byte* vsPtr = vsBytes)
        fixed (byte* fsPtr = fsBytes)
        {
            Span<ColorTargetState> colorTargets = stackalloc ColorTargetState[colorFormats.Length];
            for (var i = 0; i < colorFormats.Length; i++)
            {
                colorTargets[i] = new ColorTargetState
                {
                    Format = colorFormats[i],
                    Blend = bsPtr,
                    WriteMask = ColorWriteMask.All
                };
            }

            VertexBufferLayout* vbPtr = null;
            uint vbCount = 0;
            if (vertexBuffers.Length > 0)
            {
                var totalAttrs = vertexBuffers.ToArray().Sum(static vb => vb.Attributes.Length);

                Span<VertexAttribute> allNativeAttrs = stackalloc VertexAttribute[totalAttrs];
                Span<VertexBufferLayout> nativeBuffers = stackalloc VertexBufferLayout[vertexBuffers.Length];

                fixed (VertexAttribute* attrPtr = allNativeAttrs)
                fixed (VertexBufferLayout* bufPtr = nativeBuffers)
                {
                    var attrOff = 0;
                    for (var i = 0; i < vertexBuffers.Length; i++)
                    {
                        var desc = vertexBuffers[i];
                        for (var j = 0; j < desc.Attributes.Length; j++)
                        {
                            attrPtr[attrOff + j] = new VertexAttribute
                            {
                                ShaderLocation = desc.Attributes[j].ShaderLocation,
                                Format = desc.Attributes[j].Format,
                                Offset = desc.Attributes[j].Offset
                            };
                        }
                        bufPtr[i] = new VertexBufferLayout
                        {
                            ArrayStride = desc.Stride,
                            StepMode = desc.StepMode,
                            AttributeCount = (nuint)desc.Attributes.Length,
                            Attributes = attrPtr + attrOff
                        };
                        attrOff += desc.Attributes.Length;
                    }
                    vbPtr = bufPtr;
                    vbCount = (uint)vertexBuffers.Length;
                }
            }

            fixed (ColorTargetState* ctPtr = colorTargets)
            {
                FragmentState fragment = new()
                {
                    Module = (ShaderModule*)shader.Get<ShaderData>().NativePtr,
                    EntryPoint = fsPtr,
                    TargetCount = (nuint)colorFormats.Length,
                    Targets = ctPtr
                };

                RenderPipelineDescriptor descriptor = new()
                {
                    Layout = (PipelineLayout*)layout.Get<PipelineLayoutData>().NativePtr,
                    Vertex = new VertexState
                    {
                        Module = (ShaderModule*)shader.Get<ShaderData>().NativePtr,
                        EntryPoint = vsPtr,
                        BufferCount = vbCount,
                        Buffers = vbPtr
                    },
                    Primitive = new PrimitiveState
                    {
                        Topology = PrimitiveTopology.TriangleList,
                        StripIndexFormat = IndexFormat.Undefined,
                        FrontFace = FrontFace.Ccw,
                        CullMode = CullMode.None
                    },
                    Multisample = new MultisampleState
                    {
                        Count = 1,
                        Mask = uint.MaxValue,
                        AlphaToCoverageEnabled = false
                    },
                    Fragment = &fragment,
                    DepthStencil = dsPtr,
                    Label = null
                };

                return CreateRenderPipeline(ctx, &descriptor, shader, shader, layout);
            }
        }
    }

    public static void ReleaseRenderPipeline(GraphicsContext ctx, Entity pipeline)
    {
        ref var rp = ref pipeline.Get<RenderPipelineData>();
        if (Interlocked.Decrement(ref rp.RefCount) == 0)
        {
            ctx.Device.ReleaseRenderPipeline(rp.NativePtr);
            pipeline.Destroy();
        }
    }

    // --- ComputePipeline (with dedup) ---

    public static Entity Compute(
        GraphicsContext ctx,
        Entity shader,
        Entity layout,
        string entryPoint = "cs_main")
    {
        var entryBytes = Encoding.UTF8.GetBytes(entryPoint + '\0');
        fixed (byte* entryPtr = entryBytes)
        {
            ComputePipelineDescriptor descriptor = new()
            {
                Layout = (PipelineLayout*)layout.Get<PipelineLayoutData>().NativePtr,
                Compute = new ProgrammableStageDescriptor
                {
                    Module = (ShaderModule*)shader.Get<ShaderData>().NativePtr,
                    EntryPoint = entryPtr,
                    ConstantCount = 0,
                    Constants = null
                },
                Label = null
            };
            return CreateComputePipeline(ctx, &descriptor, shader, layout);
        }
    }

    public static Entity CreateComputePipeline(
        GraphicsContext ctx,
        ComputePipelineDescriptor* descriptor,
        Entity computeShader,
        Entity layout)
    {
        if (computeShader.Host == null || layout.Host == null)
            throw new InvalidOperationException("Pipeline dependency not alive.");

        var csId = computeShader.Id.Value;
        var loId = layout.Id.Value;

        // Dedup.
        foreach (var entity in ctx._computePipelines)
        {
            ref var existing = ref entity.Get<ComputePipelineData>();
            if (existing.ComputeShader.Id.Value == csId && existing.Layout.Id.Value == loId)
            {
                Interlocked.Increment(ref existing.RefCount);
                return entity;
            }
        }

        var native = ctx.Device.CreateComputePipeline(ctx.NativeDevice, descriptor);
        return ctx._computePipelines.Create(HList.From(new ComputePipelineData
        {
            NativePtr = native,
            ComputeShader = computeShader,
            Layout = layout,
            RefCount = 1
        }));
    }

    public static void ReleaseComputePipeline(GraphicsContext ctx, Entity pipeline)
    {
        ref var cp = ref pipeline.Get<ComputePipelineData>();
        if (Interlocked.Decrement(ref cp.RefCount) == 0)
        {
            ctx.Device.ReleaseComputePipeline(cp.NativePtr);
            pipeline.Destroy();
        }
    }
}
