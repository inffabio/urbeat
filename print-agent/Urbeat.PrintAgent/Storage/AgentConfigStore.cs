using System.Text.Json;
using Urbeat.PrintAgent.Models;

namespace Urbeat.PrintAgent.Storage;

public sealed class AgentConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public AgentConfigStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task SaveAsync(AgentPrinterConfig config, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, SerializerOptions);
        await File.WriteAllTextAsync(_filePath, json, cancellationToken);
    }

    public async Task<AgentPrinterConfig?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(_filePath, cancellationToken);
        return JsonSerializer.Deserialize<AgentPrinterConfig>(json);
    }
}
