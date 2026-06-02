namespace Dumb.Engine.Wgsl;

public sealed class ImportDirective
{
    public string Path { get; }
    public string[]? Items { get; }
    public string? Alias { get; }
    public int Line { get; }

    public ImportDirective(string path, string[]? items, string? alias, int line)
    {
        Path = path;
        Items = items;
        Alias = alias;
        Line = line;
    }
}

public enum ConditionalKind
{
    Ifdef,
    Ifndef,
    If,
    Else,
    Endif
}

public sealed class ConditionalDirective
{
    public ConditionalKind Kind { get; }
    public string? Name { get; }
    public string? Op { get; }
    public string? Value { get; }
    public int Line { get; }

    public ConditionalDirective(ConditionalKind kind, string? name, string? op, string? value, int line)
    {
        Kind = kind;
        Name = name;
        Op = op;
        Value = value;
        Line = line;
    }
}

public sealed class DirectiveInfo
{
    public string? ImportPath { get; }
    public List<ImportDirective> Imports { get; }
    public Dictionary<string, string> Defines { get; }
    public List<ConditionalDirective> Conditionals { get; }

    public DirectiveInfo(
        string? importPath,
        List<ImportDirective> imports,
        Dictionary<string, string> defines,
        List<ConditionalDirective> conditionals)
    {
        ImportPath = importPath;
        Imports = imports;
        Defines = defines;
        Conditionals = conditionals;
    }
}

public static class DirectiveParser
{
    public static DirectiveInfo Parse(string source)
    {
        var imports = new List<ImportDirective>();
        string? importPath = null;
        var defines = new Dictionary<string, string>();
        var conditionals = new List<ConditionalDirective>();

        var lineNo = 0;
        var inBlockComment = false;
        var span = source.AsSpan();

        foreach (var rawLine in span.EnumerateLines())
        {
            lineNo++;
            var trimmed = rawLine.TrimStart();
            if (trimmed.IsEmpty)
                continue;

            if (inBlockComment)
            {
                if (trimmed.IndexOf("*/") >= 0)
                    inBlockComment = false;
                continue;
            }

            if (trimmed.StartsWith("//"))
                continue;

            if (trimmed.StartsWith("/*"))
            {
                var end = IndexOfAfter(trimmed, "*/", 2);
                if (end < 0) { inBlockComment = true; continue; }
                trimmed = trimmed[end..].TrimStart();
                if (trimmed.IsEmpty || trimmed.StartsWith("//"))
                    continue;
            }

            if (trimmed[0] != '#')
                continue;

            ParseLine(trimmed, lineNo, imports, defines, conditionals, ref importPath);
        }

        return new DirectiveInfo(importPath, imports, defines, conditionals);
    }

    private static void ParseLine(
        ReadOnlySpan<char> line,
        int lineNo,
        List<ImportDirective> imports,
        Dictionary<string, string> defines,
        List<ConditionalDirective> conditionals,
        ref string? importPath)
    {
        if (line.StartsWith("#import "))
        {
            var arg = line["#import ".Length..].TrimStart();
            var (path, items, alias) = ParseImportArg(arg);
            imports.Add(new ImportDirective(path, items, alias, lineNo));
        }
        else if (line.StartsWith("#define_import_path "))
        {
            importPath = line["#define_import_path ".Length..].Trim().ToString();
        }
        else if (line.StartsWith("#define "))
        {
            var def = line["#define ".Length..].Trim();
            var spaceIdx = def.IndexOf(' ');
            if (spaceIdx > 0)
            {
                var name = def[..spaceIdx].ToString();
                var value = def[(spaceIdx + 1)..].Trim().ToString();
                defines[name] = value;
            }
            else
            {
                defines[def.ToString()] = "";
            }
        }
        else if (line.StartsWith("#ifdef "))
        {
            var name = line["#ifdef ".Length..].Trim().ToString();
            conditionals.Add(new ConditionalDirective(ConditionalKind.Ifdef, name, null, null, lineNo));
        }
        else if (line.StartsWith("#ifndef "))
        {
            var name = line["#ifndef ".Length..].Trim().ToString();
            conditionals.Add(new ConditionalDirective(ConditionalKind.Ifndef, name, null, null, lineNo));
        }
        else if (line.StartsWith("#if "))
        {
            var cond = line["#if ".Length..].Trim().ToString();
            var parts = ParseIfCondition(cond);
            conditionals.Add(new ConditionalDirective(
                ConditionalKind.If, parts.name, parts.op, parts.value, lineNo));
        }
        else if (line.StartsWith("#else"))
        {
            conditionals.Add(new ConditionalDirective(ConditionalKind.Else, null, null, null, lineNo));
        }
        else if (line.StartsWith("#endif"))
        {
            conditionals.Add(new ConditionalDirective(ConditionalKind.Endif, null, null, null, lineNo));
        }
    }

    private static (string path, string[]? items, string? alias) ParseImportArg(ReadOnlySpan<char> arg)
    {
        string? alias = null;

        var asIdx = IndexOfKeyword(arg, " as ");
        if (asIdx >= 0)
        {
            alias = arg[(asIdx + 4)..].Trim().ToString();
            arg = arg[..asIdx].TrimEnd();
        }

        var braceIdx = arg.IndexOf('{');
        if (braceIdx < 0)
            return (arg.ToString(), null, alias);

        var closeIdx = arg.LastIndexOf('}');
        if (closeIdx < 0)
            return (arg.ToString(), null, alias);

        var path = arg[..braceIdx].TrimEnd().TrimEnd(':').ToString();
        var itemsStr = arg[(braceIdx + 1)..closeIdx];
        var items = itemsStr.ToString().Split(',')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToArray();

        return (path, items, alias);
    }

    private static (string? name, string? op, string? value) ParseIfCondition(string cond)
    {
        var eqIdx = cond.IndexOf("==", StringComparison.Ordinal);
        if (eqIdx > 0)
        {
            var name = cond[..eqIdx].Trim();
            var value = cond[(eqIdx + 2)..].Trim();
            return (name, "==", value);
        }

        var neIdx = cond.IndexOf("!=", StringComparison.Ordinal);
        if (neIdx > 0)
        {
            var name = cond[..neIdx].Trim();
            var value = cond[(neIdx + 2)..].Trim();
            return (name, "!=", value);
        }

        return (cond.Trim(), null, null);
    }

    private static int IndexOfKeyword(ReadOnlySpan<char> text, string keyword)
    {
        var idx = text.IndexOf(keyword, StringComparison.Ordinal);
        if (idx < 0) return -1;

        var before = idx > 0 ? text[idx - 1] : ' ';
        var after = text[(idx + keyword.Length)..];
        if (char.IsWhiteSpace(before) && (after.IsEmpty || char.IsWhiteSpace(after[0])))
            return idx;

        return -1;
    }

    private static int IndexOfAfter(ReadOnlySpan<char> text, string value, int start)
    {
        var slice = text[start..];
        var idx = slice.IndexOf(value, StringComparison.Ordinal);
        return idx >= 0 ? start + idx + value.Length : -1;
    }
}
