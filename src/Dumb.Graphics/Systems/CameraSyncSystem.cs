using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Sia;
using Silk.NET.WebGPU;
using Dumb.Graphics.Resources;

namespace Dumb.Graphics;

public sealed class CameraSyncSystem : ExtractSystemBase
{
    private readonly GraphicsContext _ctx;
    private readonly Dictionary<int, Entity> _buffers = [];
    private readonly HashSet<int> _seenIds = [];
    private readonly List<int> _removedIds = [];
    private int _lastExtractVersion = -1;

    public CameraSyncSystem(GraphicsContext ctx)
        : base(Matchers.Any, extractMatcher: Matchers.Of<Engine.Cameras.Camera>())
    {
        _ctx = ctx;
    }

    public Entity GetUniformBuffer(Entity cameraEntity)
    {
        var id = cameraEntity.Id.Value;
        if (_buffers.TryGetValue(id, out var buffer))
            return buffer;
        return _buffers[id] = Buffers.Create(_ctx, (ulong)Unsafe.SizeOf<CameraUniforms>(),
            BufferUsage.Uniform | BufferUsage.CopyDst);
    }

    public bool TryGetUniformBuffer(Entity cameraEntity, [MaybeNullWhen(false)] out Entity buffer)
    {
        return _buffers.TryGetValue(cameraEntity.Id.Value, out buffer);
    }

    public IEnumerable<Entity> AllBuffers => _buffers.Values;

    public Entity? FirstBuffer
    {
        get
        {
            foreach (var buffer in _buffers.Values)
                return buffer;
            return null;
        }
    }

    public override void Execute(World world, IEntityQuery query, IEntityQuery extract)
    {
        // Skip extraction if no camera entities were added/removed
        if (extract.Version == _lastExtractVersion)
            return;
        _lastExtractVersion = extract.Version;

        _seenIds.Clear();

        extract.ForSlice((Entity entity, ref Engine.Cameras.Camera camera) =>
        {
            var id = entity.Id.Value;
            _seenIds.Add(id);

            if (!_buffers.TryGetValue(id, out var buffer))
            {
                buffer = Buffers.Create(_ctx, (ulong)Unsafe.SizeOf<CameraUniforms>(),
                    BufferUsage.Uniform | BufferUsage.CopyDst);
                _buffers[id] = buffer;
            }

            Buffers.Write(_ctx, buffer, CameraUniforms.From(camera));
        });

        _removedIds.Clear();
        foreach (var (id, buffer) in _buffers)
        {
            if (!_seenIds.Contains(id))
            {
                Buffers.Release(_ctx, buffer);
                _removedIds.Add(id);
            }
        }
        foreach (var id in _removedIds)
            _buffers.Remove(id);
    }
}
