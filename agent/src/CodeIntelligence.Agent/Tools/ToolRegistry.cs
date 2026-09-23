namespace CodeIntelligence.Agent.Tools;

/// <summary>
/// Looks up a tool by name. Tools are discovered from DI (<c>IEnumerable&lt;ITool&gt;</c>
/// — see DependencyInjection/ServiceCollectionExtensions), so adding one is "implement
/// ITool + register it as ITool"; nothing here changes.
/// </summary>
public sealed class ToolRegistry
{
    private readonly IReadOnlyDictionary<string, ITool> _byName;

    public ToolRegistry(IEnumerable<ITool> tools)
    {
        All = tools.ToList();
        _byName = All.ToDictionary(t => t.Definition.Name, StringComparer.Ordinal);
    }

    public IReadOnlyList<ITool> All { get; }

    public ITool? Find(string name) => _byName.GetValueOrDefault(name);
}
