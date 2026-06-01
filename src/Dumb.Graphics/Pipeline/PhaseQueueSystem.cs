using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sia;
using Silk.NET.WebGPU;

namespace Dumb.Graphics.Pipeline;

public sealed class PhaseQueueSystem : ExtractSystemBase
{
    private readonly GraphicsContext _ctx;
    private readonly CameraSyncSystem _cameraSync;
    private readonly TransformSyncSystem _transformSync;
    private readonly GpuMeshRegistry _meshRegistry;

    private readonly HashSet<int> _seenIds = [];

    private Entity? _frameBindGroup;
    private int _frameBindGroupKey;
    private Entity? _frameBindGroupLayout;

    public BinnedRenderPhase OpaquePhase { get; } = new();
    public BinnedRenderPhase ForwardPhase { get; } = new();

    public Entity? FrameBindGroupLayout
    {
        get => _frameBindGroupLayout;
        set => _frameBindGroupLayout = value;
    }

    public GpuMeshRegistry MeshRegistry => _meshRegistry;

    public PhaseQueueSystem(
        GraphicsContext ctx,
        CameraSyncSystem cameraSync,
        TransformSyncSystem transformSync,
        GpuMeshRegistry meshRegistry)
        : base(Matchers.Any, extractMatcher: Matchers.Of<Engine.Mesh.VisibleEntity>())
    {
        _ctx = ctx;
        _cameraSync = cameraSync;
        _transformSync = transformSync;
        _meshRegistry = meshRegistry;
    }

    public override void Execute(World world, IEntityQuery query, IEntityQuery extract)
    {
        ReturnPooledBindGroups();
        OpaquePhase.Clear();
        ForwardPhase.Clear();
        _seenIds.Clear();

        extract.ForSlice((Entity entity, ref Engine.Mesh.VisibleEntity visible) =>
        {
            _seenIds.Add(entity.Id.Value);

            if (!_meshRegistry.TryGetOrCreateMesh(entity, out var gpuMesh))
                return;

            if (!_meshRegistry.TryGetMaterial(entity, out var gpuMaterial))
                return;

            if (!_transformSync.TryGetOffset(entity, out var modelOffset))
                return;

            ref var matData = ref gpuMaterial.Get<MaterialResourceData>();

            var frameBg = GetOrCreateFrameBindGroup();
            if (frameBg is null) return;

            var len = matData.BindGroups.Length;
            var bindGroups = ArrayPool<Entity?>.Shared.Rent(len);
            bindGroups[0] = frameBg;
            for (var i = 1; i < len; i++)
                bindGroups[i] = matData.BindGroups[i];

            var binKey = ((ulong)matData.Pipeline.Id.Value << 32)
                       | ((ulong)gpuMaterial.Id.Value & 0xFFFFFFFF);

            var item = new PhaseItem(
                DrawEntity: gpuMaterial,
                Pipeline: matData.Pipeline,
                PipelineLayout: matData.PipelineLayout,
                BindGroups: bindGroups,
                Mesh: gpuMesh,
                SubMeshIndex: 0,
                ModelOffset: modelOffset
            );

            if (matData.IsForward)
            {
                ForwardPhase.Add(item, binKey);
            }
            else
            {
                OpaquePhase.Add(item, binKey);
            }
        });

        _meshRegistry.CleanupRemoved(_seenIds);
    }

    private void ReturnPooledBindGroups()
    {
        foreach (var binItems in OpaquePhase.Bins)
            foreach (var item in binItems)
                if (item.BindGroups is { } arr)
                    ArrayPool<Entity?>.Shared.Return(arr);
        foreach (var binItems in ForwardPhase.Bins)
            foreach (var item in binItems)
                if (item.BindGroups is { } arr)
                    ArrayPool<Entity?>.Shared.Return(arr);
    }

    private Entity? GetOrCreateFrameBindGroup()
    {
        if (_frameBindGroupLayout is not { } frameBgl)
            return null;

        var cameraBuffer = _cameraSync.FirstBuffer;
        if (cameraBuffer is not { } camBuf)
            return null;

        var bindGroupKey = HashCode.Combine(
            camBuf.Id.Value,
            _transformSync.ModelBuffer.Id.Value);

        if (_frameBindGroup is not null && _frameBindGroupKey == bindGroupKey)
            return _frameBindGroup;

        var modelBuffer = _transformSync.ModelBuffer;

        Binding[] bindings =
        [
            Binding.Uniform<CameraUniforms>(0, camBuf),
            Binding.Buffer(2, modelBuffer, (nuint)Unsafe.SizeOf<Matrix4x4>()),
        ];

        if (_frameBindGroup is { } oldBg)
            Pipelines.ReleaseBindGroup(_ctx, oldBg);

        _frameBindGroup = Pipelines.BindGroup(_ctx, frameBgl, bindings);
        _frameBindGroupKey = bindGroupKey;

        return _frameBindGroup;
    }
}
