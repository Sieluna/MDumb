namespace Dumb.Engine.Wgsl;

public delegate string? ImportResolver(string importPath, string importerName);

public sealed class ModuleNode
{
    public string Name { get; }
    public string Source { get; }
    public DirectiveInfo Directives { get; }
    public List<ModuleNode> Dependencies { get; } = [];

    public ModuleNode(string name, string source, DirectiveInfo directives)
    {
        Name = name;
        Source = source;
        Directives = directives;
    }
}

public static class ModuleGraph
{
    public static List<ModuleNode> BuildAndSort(
        string entryName,
        string entrySource,
        ImportResolver importResolver,
        List<WgslDiagnostic> diagnostics)
    {
        var registry = new Dictionary<string, ModuleNode>();
        var resolved = new HashSet<string>();

        var entry = Build(entryName, entrySource, importResolver, registry, resolved, diagnostics);
        if (HasErrors(diagnostics))
            return [];

        return TopologicalSort(entry, diagnostics);
    }

    private static ModuleNode Build(
        string name,
        string source,
        ImportResolver importResolver,
        Dictionary<string, ModuleNode> registry,
        HashSet<string> resolved,
        List<WgslDiagnostic> diagnostics)
    {
        var directives = DirectiveParser.Parse(source);
        var canonicalName = directives.ImportPath ?? name;

        if (registry.TryGetValue(canonicalName, out var existing))
            return existing;

        if (resolved.Contains(canonicalName))
        {
            diagnostics.Add(new WgslDiagnostic(
                DiagnosticSeverity.Error,
                $"Circular import detected: module '{canonicalName}' imports itself via '{name}'",
                0, name));
            return new ModuleNode(canonicalName, source, directives);
        }

        resolved.Add(canonicalName);
        var node = new ModuleNode(canonicalName, source, directives);
        registry[canonicalName] = node;

        foreach (var imp in directives.Imports)
        {
            var resolvedSource = importResolver(imp.Path, node.Name);
            if (resolvedSource == null)
            {
                diagnostics.Add(new WgslDiagnostic(
                    DiagnosticSeverity.Error,
                    $"Import not found: '{imp.Path}'",
                    imp.Line, name));
                continue;
            }

            var dep = Build(imp.Path, resolvedSource, importResolver, registry, resolved, diagnostics);
            if (!node.Dependencies.Contains(dep))
                node.Dependencies.Add(dep);
        }

        resolved.Remove(canonicalName);
        return node;
    }

    private static List<ModuleNode> TopologicalSort(
        ModuleNode root,
        List<WgslDiagnostic> diagnostics)
    {
        var result = new List<ModuleNode>();
        var visited = new HashSet<ModuleNode>();
        var inStack = new HashSet<ModuleNode>();

        Visit(root, visited, inStack, result, diagnostics);

        return result;
    }

    private static void Visit(
        ModuleNode node,
        HashSet<ModuleNode> visited,
        HashSet<ModuleNode> inStack,
        List<ModuleNode> result,
        List<WgslDiagnostic> diagnostics)
    {
        if (visited.Contains(node))
            return;

        if (inStack.Contains(node))
        {
            diagnostics.Add(new WgslDiagnostic(
                DiagnosticSeverity.Error,
                $"Circular dependency involving module '{node.Name}'",
                0, node.Name));
            return;
        }

        inStack.Add(node);

        foreach (var dep in node.Dependencies)
            Visit(dep, visited, inStack, result, diagnostics);

        inStack.Remove(node);
        visited.Add(node);
        result.Add(node);
    }

    private static bool HasErrors(List<WgslDiagnostic> diagnostics) =>
        diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}
