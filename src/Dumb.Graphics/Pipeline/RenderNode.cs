using Sia;

namespace Dumb.Graphics.Pipeline;

public abstract class RenderNode
{
    public string Name { get; set; }

    public List<ResourceHandle> Inputs { get; } = [];
    public List<ResourceHandle> Outputs { get; } = [];

    protected RenderNode()
    {
        Name = GetType().Name;
    }

    public virtual void DeclareResources() { }
    public virtual void Update(World world) { }
    public abstract void Execute(World world, RenderContext ctx);
    public virtual void Dispose() { }
}
