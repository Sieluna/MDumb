using Sia;

namespace Dumb.Graphics.Resources;

public sealed class GpuMeshRegistry
{
    private readonly GraphicsContext _ctx;
    private readonly Dictionary<int, Engine.Mesh.MeshData> _meshDataMap = [];
    private readonly SparseSet<Entity> _gpuMeshMap = new();
    private readonly SparseSet<Entity> _gpuMaterialMap = new();
    private readonly List<int> _removedIds = [];

    public GpuMeshRegistry(GraphicsContext ctx)
    {
        _ctx = ctx;
    }

    public void RegisterMesh(Entity engineEntity, Engine.Mesh.MeshData meshData)
    {
        _meshDataMap[engineEntity.Id.Value] = meshData;
    }

    public void RegisterMaterial(Entity engineEntity, Entity gpuMaterial)
    {
        _gpuMaterialMap[engineEntity.Id.Value] = gpuMaterial;
    }

    public void Unregister(Entity engineEntity)
    {
        var id = engineEntity.Id.Value;
        _meshDataMap.Remove(id);
        if (_gpuMeshMap.Remove(id, out var gpuMesh))
            Mesh.Release(_ctx, gpuMesh);
        if (_gpuMaterialMap.Remove(id, out var gpuMat))
            Materials.Release(_ctx, gpuMat);
    }

    public bool TryGetMaterial(Entity engineEntity, out Entity gpuMaterial)
    {
        return _gpuMaterialMap.TryGetValue(engineEntity.Id.Value, out gpuMaterial);
    }

    public bool TryGetOrCreateMesh(Entity engineEntity, out Entity gpuMesh)
    {
        var id = engineEntity.Id.Value;
        if (_gpuMeshMap.TryGetValue(id, out gpuMesh!))
            return true;

        if (!_meshDataMap.TryGetValue(id, out var meshData))
        {
            gpuMesh = default;
            return false;
        }

        try
        {
            gpuMesh = Mesh.Create(_ctx, meshData);
            _gpuMeshMap[id] = gpuMesh;
            return true;
        }
        catch
        {
            gpuMesh = default;
            return false;
        }
    }

    public void CleanupRemoved(HashSet<int> activeIds)
    {
        _removedIds.Clear();
        foreach (var kv in _gpuMeshMap)
            if (!activeIds.Contains(kv.Key)) _removedIds.Add(kv.Key);
        foreach (var kv in _gpuMaterialMap)
            if (!activeIds.Contains(kv.Key) && !_removedIds.Contains(kv.Key)) _removedIds.Add(kv.Key);

        foreach (var id in _removedIds)
        {
            if (_gpuMeshMap.Remove(id, out var gpuMesh))
                Mesh.Release(_ctx, gpuMesh);
            if (_gpuMaterialMap.Remove(id, out var gpuMat))
                Materials.Release(_ctx, gpuMat);
            _meshDataMap.Remove(id);
        }
    }
}
