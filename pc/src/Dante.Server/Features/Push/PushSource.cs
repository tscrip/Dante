using System.Collections.Concurrent;
using Dante.Core.Config;
using Dante.Core.Widgets;

namespace Dante.Server.Features.Push;

/// <summary>Values pushed by anything over REST: POST /api/widgets/{id}/value.</summary>
public sealed class PushStore
{
    private readonly ConcurrentDictionary<string, WidgetReading> _readings = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string>? Pushed;

    public void Set(string appId, WidgetReading reading)
    {
        _readings[appId] = reading;
        Pushed?.Invoke(appId);
    }

    public WidgetReading? Get(string appId) => _readings.GetValueOrDefault(appId);
}

public sealed class PushSource(PushStore store) : IWidgetType
{
    public const string TypeName = "push";
    public string Type => TypeName;
    public string PluginId => PushPlugin.PluginId;
    public string DisplayName => "Pushed value";
    public string Description =>
        "Any script or tool POSTs {\"value\":3,\"text\":\"...\",\"pending\":[{\"key\":\"a\",\"title\":\"...\",\"since\":\"2026-01-01T10:00:00Z\"}]} " +
        "to http://localhost:5280/api/widgets/{id}/value.";
    public bool IsPushBased => true;
    public IReadOnlyList<SettingField> Fields { get; } = [];

    public string? Example(WidgetDefinition app) =>
        $"curl -X POST http://127.0.0.1:5280/api/widgets/{(string.IsNullOrWhiteSpace(app.Id) ? ConfigStore.Slug(app.Name) : app.Id)}/value -H \"Content-Type: application/json\" -d '{{\"value\": 3, \"text\": \"three things\"}}'";

    public Task<WidgetReading> ReadAsync(WidgetDefinition app, CancellationToken ct) =>
        Task.FromResult(store.Get(app.Id) ?? new WidgetReading { Text = "waiting for data" });
}

