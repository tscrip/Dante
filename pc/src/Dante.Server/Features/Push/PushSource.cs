using System.Collections.Concurrent;
using Dante.Core.Config;
using Dante.Core.Sources;

namespace Dante.Server.Features.Push;

/// <summary>Values pushed by anything over REST: POST /api/apps/{id}/value.</summary>
public sealed class PushStore
{
    private readonly ConcurrentDictionary<string, SourceReading> _readings = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string>? Pushed;

    public void Set(string appId, SourceReading reading)
    {
        _readings[appId] = reading;
        Pushed?.Invoke(appId);
    }

    public SourceReading? Get(string appId) => _readings.GetValueOrDefault(appId);
}

public sealed class PushSource(PushStore store) : IDataSource
{
    public const string TypeName = "push";
    public string Type => TypeName;
    public string DisplayName => "Pushed via REST API";
    public string Description =>
        "Any script or tool POSTs {\"value\":3,\"text\":\"...\",\"pending\":[{\"key\":\"a\",\"title\":\"...\",\"since\":\"2026-01-01T10:00:00Z\"}]} " +
        "to http://localhost:5280/api/apps/{id}/value.";
    public bool IsPushBased => true;
    public IReadOnlyList<SettingField> Settings { get; } = [];

    public string? Example(AppDefinition app) =>
        $"curl -X POST http://127.0.0.1:5280/api/apps/{(string.IsNullOrWhiteSpace(app.Id) ? ConfigStore.Slug(app.Name) : app.Id)}/value -H \"Content-Type: application/json\" -d '{{\"value\": 3, \"text\": \"three things\"}}'";

    public Task<SourceReading> ReadAsync(AppDefinition app, CancellationToken ct) =>
        Task.FromResult(store.Get(app.Id) ?? new SourceReading { Text = "waiting for data" });
}

