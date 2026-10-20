using System.Text.Json;
using Urbeat.PrintAgent.Models;

namespace Urbeat.PrintAgent.Storage;

/// <summary>
/// Thread-safe print job store. When a file path is supplied the jobs and their operation keys are
/// persisted atomically so idempotency survives an agent/service restart. Without a path it behaves
/// as an in-memory store for tests and constrained environments.
///
/// A job persisted as <c>sending</c> before the spooler call is considered <c>ambiguous</c> after a
/// restart: the agent cannot know whether Windows/CUPS accepted the bytes before the crash. The
/// safe policy is to never auto-resend an ambiguous job; an explicit retry must be requested.
/// </summary>
public sealed class PrintJobStore : IPrintJobStore
{
    private const int MaxJobs = 500;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string? _filePath;
    private readonly List<PrintJobRecord> _jobs = [];

    public PrintJobStore()
        : this(null)
    {
    }

    public PrintJobStore(string? filePath)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath;
        Load();
        RecoverAmbiguousJobs();
    }

    public string? FilePath => _filePath;

    public string? LastLoadError { get; private set; }

    public void Add(PrintJobRecord job)
    {
        ArgumentNullException.ThrowIfNull(job);

        lock (_gate)
        {
            var existingIndex = _jobs.FindIndex(x => string.Equals(x.Id, job.Id, StringComparison.Ordinal));
            if (existingIndex >= 0)
            {
                _jobs[existingIndex] = job;
            }
            else
            {
                _jobs.Add(job);
            }

            if (_jobs.Count > MaxJobs)
            {
                _jobs.RemoveRange(0, _jobs.Count - MaxJobs);
            }

            Persist();
        }
    }

    public void Update(PrintJobRecord job) => Add(job);

    public IReadOnlyList<PrintJobRecord> List()
    {
        lock (_gate)
        {
            return _jobs.ToArray();
        }
    }

    public PrintJobRecord? FindByOperationKey(string operationKey)
    {
        if (string.IsNullOrWhiteSpace(operationKey)) return null;

        lock (_gate)
        {
            return _jobs
                .LastOrDefault(job => string.Equals(job.OperationKey, operationKey, StringComparison.Ordinal));
        }
    }

    public PrintJobRecord? Get(string id)
    {
        lock (_gate)
        {
            return _jobs.FirstOrDefault(job => string.Equals(job.Id, id, StringComparison.Ordinal));
        }
    }

    private void Load()
    {
        if (_filePath is null || !File.Exists(_filePath)) return;

        try
        {
            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<List<PrintJobRecord>>(json);
            if (loaded is null)
            {
                LastLoadError = "O arquivo de jobs está vazio ou inválido.";
                return;
            }

            _jobs.AddRange(loaded
                .Where(job => !string.IsNullOrWhiteSpace(job.Id))
                .TakeLast(MaxJobs));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt/locked store must not prevent the agent from starting. Keep the in-memory
            // store empty and surface the warning in /health instead of silently pretending the
            // persisted idempotency state is intact.
            LastLoadError = $"Não foi possível carregar os jobs persistidos: {exception.Message}";
            TryPreserveCorruptFile();
        }
    }

    private void TryPreserveCorruptFile()
    {
        if (_filePath is null) return;

        try
        {
            File.Move(_filePath, $"{_filePath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}", overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LastLoadError += $" Também não foi possível preservar o arquivo corrompido: {exception.Message}";
        }
    }

    private void RecoverAmbiguousJobs()
    {
        lock (_gate)
        {
            var changed = false;
            foreach (var job in _jobs.Where(job => string.Equals(job.Status, "sending", StringComparison.OrdinalIgnoreCase)))
            {
                job.Status = "ambiguous";
                job.Message = "O agente caiu durante o envio; o resultado físico é desconhecido. Use retry explícito se necessário.";
                changed = true;
            }

            if (!changed) return;

            try
            {
                Persist();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LastLoadError = $"Jobs ambíguos encontrados, mas não foi possível persistir a recuperação: {exception.Message}";
            }
        }
    }

    private void Persist()
    {
        if (_filePath is null) return;

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        var json = JsonSerializer.Serialize(_jobs, SerializerOptions);
        using (var stream = new FileStream(
            tempPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(json);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }
}
