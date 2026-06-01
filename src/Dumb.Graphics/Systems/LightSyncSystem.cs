using System.Numerics;
using System.Runtime.InteropServices;
using Sia;
using Silk.NET.WebGPU;

namespace Dumb.Graphics;

[StructLayout(LayoutKind.Sequential)]
public readonly struct GPULight
{
    public readonly Vector3 Color;
    public readonly float Intensity;
    public readonly Vector3 Direction;
    public readonly float Range;
    public readonly Vector3 Position;
    public readonly uint Type;
    public readonly float InnerConeAngle;
    public readonly float OuterConeAngle;
    public readonly uint CastsShadows;
    public readonly float _pad1;

    public const uint MaxLights = 64;
    public const int Size = 64;

    public GPULight(Vector3 color, float intensity, Vector3 direction, float range,
        Vector3 position, uint type, float innerConeAngle, float outerConeAngle,
        bool castsShadows = false)
    {
        Color = color;
        Intensity = intensity;
        Direction = direction;
        Range = range;
        Position = position;
        Type = type;
        InnerConeAngle = innerConeAngle;
        OuterConeAngle = outerConeAngle;
        CastsShadows = castsShadows ? 1u : 0u;
        _pad1 = 0;
    }

    public static GPULight From(in Engine.Lighting.Light light, Vector3 position)
    {
        return new GPULight(light.Color, light.Intensity, light.Direction, light.Range,
            position, (uint)light.Type, light.InnerConeAngle, light.OuterConeAngle,
            light.CastsShadows);
    }
}

public sealed class LightSyncSystem : ExtractSystemBase
{
    private readonly GraphicsContext _ctx;
    private readonly Dictionary<int, int> _entityToSlot = [];
    private readonly HashSet<int> _seenIds = [];
    private readonly List<int> _removedIds = [];
    private readonly byte[] _lightData = new byte[GPULight.MaxLights * GPULight.Size];
    private Entity _lightBuffer = null!;
    private int _lightCount;
    private int _nextSlot;

    public Entity LightBuffer => _lightBuffer;
    public int LightCount => _lightCount;

    public Vector3 GetLightDirection(int slot)
    {
        if (slot < 0 || (slot + 1) * GPULight.Size > _lightData.Length)
            return -Vector3.UnitY;
        var span = _lightData.AsSpan(slot * GPULight.Size, GPULight.Size);
        return MemoryMarshal.Read<Vector3>(span[16..]);
    }

    public uint GetLightType(int slot)
    {
        if (slot < 0 || (slot + 1) * GPULight.Size > _lightData.Length)
            return uint.MaxValue;
        var span = _lightData.AsSpan(slot * GPULight.Size, GPULight.Size);
        return MemoryMarshal.Read<uint>(span[44..]);
    }

    public bool GetLightCastsShadows(int slot)
    {
        if (slot < 0 || (slot + 1) * GPULight.Size > _lightData.Length)
            return false;
        var span = _lightData.AsSpan(slot * GPULight.Size, GPULight.Size);
        return MemoryMarshal.Read<uint>(span[56..]) != 0;
    }

    public LightSyncSystem(GraphicsContext ctx)
        : base(Matchers.Any, extractMatcher: Matchers.Of<Engine.Lighting.Light>())
    {
        _ctx = ctx;
    }

    public override void Execute(World world, IEntityQuery query, IEntityQuery extract)
    {
        _seenIds.Clear();
        _lightCount = 0;
        Array.Clear(_lightData);

        extract.ForSlice((Entity entity, ref Engine.Lighting.Light light) =>
        {
            var id = entity.Id.Value;
            _seenIds.Add(id);

            if (!_entityToSlot.TryGetValue(id, out var slot))
            {
                slot = _nextSlot++;
                _entityToSlot[id] = slot;
            }
            if (slot >= GPULight.MaxLights)
                return;

            var position = entity.Contains<Engine.Transform.GlobalTransform>()
                ? entity.Get<Engine.Transform.GlobalTransform>().Value.Translation
                : Vector3.Zero;

            var gpuLight = GPULight.From(light, position);
            MemoryMarshal.Write(_lightData.AsSpan(slot * GPULight.Size), gpuLight);
            _lightCount = Math.Max(_lightCount, slot + 1);
        });

        // Resize buffer if needed
        if (_lightBuffer?.Host == null)
        {
            _lightBuffer = Buffers.Create(_ctx,
                (ulong)(GPULight.MaxLights * GPULight.Size),
                BufferUsage.Uniform | BufferUsage.CopyDst | BufferUsage.CopySrc);
        }

        Buffers.Write(_ctx, _lightBuffer!, 0, _lightData);

        // Clean up removed entities
        _removedIds.Clear();
        foreach (var id in _entityToSlot.Keys)
        {
            if (!_seenIds.Contains(id))
                _removedIds.Add(id);
        }
        foreach (var id in _removedIds)
            _entityToSlot.Remove(id);
    }
}
