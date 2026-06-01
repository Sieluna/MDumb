using Sia;

namespace Dumb.Graphics.Pipeline;

public readonly record struct CompileResult
{
    public bool Success { get; init; }
    public IReadOnlyList<string> Errors { get; init; }
    public IReadOnlyList<string> Warnings { get; init; }

    public static CompileResult Ok() =>
        new() { Success = true, Errors = [], Warnings = [] };

    public static CompileResult Fail(List<string> errors, List<string> warnings) =>
        new() { Success = false, Errors = errors.AsReadOnly(), Warnings = warnings.AsReadOnly() };
}

public sealed class RenderGraph
{
    private readonly List<RenderNode> _nodes = [];
    private readonly RenderContext _renderContext;
    private bool _compiled;

    public RenderGraph(GraphicsContext ctx)
    {
        _renderContext = new RenderContext(ctx);
    }

    public void AddNode(RenderNode node) => _nodes.Add(node);
    public void RemoveNode(RenderNode node) => _nodes.Remove(node);
    public void Clear()
    {
        foreach (var node in _nodes)
            node.Dispose();
        _nodes.Clear();
        _compiled = false;
    }
    public IReadOnlyList<RenderNode> Nodes => _nodes;

    public CompileResult Compile()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        foreach (var node in _nodes)
        {
            node.Inputs.Clear();
            node.Outputs.Clear();
            node.DeclareResources();
        }

        var producerMap = new Dictionary<Entity, RenderNode>();
        foreach (var node in _nodes)
        {
            foreach (var output in node.Outputs)
            {
                if (!output.IsValid)
                {
                    warnings.Add($"Node '{node.Name}' declares output '{output.Name}' with an unbound view.");
                    continue;
                }
                if (producerMap.TryGetValue(output.View, out var existing))
                    errors.Add($"Resource '{output.Name}' is written by both '{existing.Name}' and '{node.Name}'.");
                else
                    producerMap[output.View] = node;
            }
        }

        foreach (var node in _nodes)
        {
            foreach (var input in node.Inputs)
            {
                if (!input.IsValid)
                    continue;
                if (!producerMap.ContainsKey(input.View))
                    warnings.Add($"Input '{input.Name}' of node '{node.Name}' is not produced by any node in the graph.");
            }
        }

        for (int i = 0; i < _nodes.Count; i++)
        {
            var consumer = _nodes[i];
            foreach (var input in consumer.Inputs)
            {
                if (!input.IsValid) continue;
                if (!producerMap.TryGetValue(input.View, out var producer)) continue;
                if (producer == consumer) continue;

                int producerIndex = _nodes.IndexOf(producer);
                if (producerIndex > i)
                    errors.Add($"Node '{consumer.Name}' consumes '{input.Name}' but producer '{producer.Name}' appears after it. Add '{producer.Name}' before '{consumer.Name}'.");
            }
        }

        return errors.Count == 0
            ? CompileResult.Ok()
            : CompileResult.Fail(errors, warnings);
    }

    public void Run(World world)
    {
        if (!_compiled)
        {
            var result = Compile();
            if (!result.Success)
                throw new InvalidOperationException(
                    $"Render graph compilation failed: {string.Join("; ", result.Errors)}");
            _compiled = true;
        }

        foreach (var node in _nodes)
            node.Update(world);

        foreach (var node in _nodes)
            node.Execute(world, _renderContext);

        _renderContext.Submit();
    }
}
