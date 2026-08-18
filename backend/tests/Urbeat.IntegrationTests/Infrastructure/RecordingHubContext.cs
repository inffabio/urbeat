using Microsoft.AspNetCore.SignalR;

namespace Urbeat.IntegrationTests.Infrastructure;

public sealed class RecordingHubContext<THub> : IHubContext<THub> where THub : Hub
{
    private readonly RecordingHubClients _clients = new();

    public IHubClients Clients => _clients;

    public IGroupManager Groups { get; } = new RecordingGroupManager();

    public RecordingHubClients Recording => _clients;

    public IReadOnlyList<SignalRMessage> Messages => _clients.Messages;

    public void Clear() => _clients.Clear();
}

public sealed class RecordingHubClients : IHubClients
{
    private readonly List<SignalRMessage> _messages = new();

    public IReadOnlyList<SignalRMessage> Messages => _messages;

    public IClientProxy All => new RecordingClientProxy("all", _messages);

    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => All;

    public ISingleClientProxy Client(string connectionId) => new RecordingClientProxy($"connection:{connectionId}", _messages);

    IClientProxy IHubClients<IClientProxy>.Client(string connectionId) => Client(connectionId);

    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => All;

    public IClientProxy Group(string groupName) => new RecordingClientProxy($"group:{groupName}", _messages);

    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Group(groupName);

    public IClientProxy Groups(IReadOnlyList<string> groupNames) => All;

    public IClientProxy User(string userId) => new RecordingClientProxy($"user:{userId}", _messages);

    public IClientProxy Users(IReadOnlyList<string> userIds) => All;

    public void Clear() => _messages.Clear();
}

public sealed class RecordingClientProxy : ISingleClientProxy
{
    private readonly string _target;
    private readonly List<SignalRMessage> _messages;

    public RecordingClientProxy(string target, List<SignalRMessage> messages)
    {
        _target = target;
        _messages = messages;
    }

    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        _messages.Add(new SignalRMessage(_target, method, args ?? Array.Empty<object?>()));
        return Task.CompletedTask;
    }

    public Task<T> InvokeCoreAsync<T>(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        _messages.Add(new SignalRMessage(_target, method, args ?? Array.Empty<object?>()));
        return Task.FromResult(default(T)!);
    }
}

public sealed class SignalRMessage
{
    public SignalRMessage(string target, string method, object?[] args)
    {
        Target = target;
        Method = method;
        Args = args;
    }

    public string Target { get; }

    public string Method { get; }

    public object?[] Args { get; }
}

public sealed class RecordingGroupManager : IGroupManager
{
    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
