using BeProductive.Core.Ipc;

namespace BeProductive.App.Services;

/// <summary>How the app talks to the background service.</summary>
public interface IServiceClient
{
    /// <summary>Throws <see cref="ServiceUnavailableException"/> when the service isn't running.</summary>
    Task<IpcResponse> SendAsync(IpcRequest request, CancellationToken cancellationToken = default);
}

public sealed class PipeServiceClient : IServiceClient
{
    public Task<IpcResponse> SendAsync(IpcRequest request, CancellationToken cancellationToken = default) =>
        IpcProtocol.SendAsync(request, cancellationToken);
}
