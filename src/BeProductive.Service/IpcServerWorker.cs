using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using BeProductive.Core.Ipc;
using BeProductive.Core.Sessions;

namespace BeProductive.Service;

/// <summary>Answers the UI and the CLI over a local named pipe: one request line, one response line.</summary>
public sealed class IpcServerWorker(SessionService sessions, ILogger<IpcServerWorker> logger) : BackgroundService
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var pipe = CreatePipe();
            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync();
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Waiting for a pipe connection failed");
                await pipe.DisposeAsync();
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                continue;
            }

            // Served in the background, so a slow command (a recovery code that sends an email) doesn't block others.
            _ = ServeAsync(pipe, stoppingToken);
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        await using (pipe)
        {
            try
            {
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                readTimeout.CancelAfter(ReadTimeout);
                var line = await reader.ReadLineAsync(readTimeout.Token);

                var request = line is null ? null : TryParse(line);
                var response = request is null
                    ? new IpcResponse(false, "The request couldn't be understood.", null)
                    : await sessions.HandleAsync(request, stoppingToken);
                await writer.WriteLineAsync(IpcProtocol.Serialize(response));
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "A pipe client request failed");
            }
        }
    }

    private IpcRequest? TryParse(string line)
    {
        try
        {
            return IpcProtocol.Deserialize<IpcRequest>(line);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Ignoring a malformed pipe request");
            return null;
        }
    }

    private static NamedPipeServerStream CreatePipe() =>
        OperatingSystem.IsWindows()
            ? CreateWindowsPipe()
            : new NamedPipeServerStream(
                IpcProtocol.PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    /// <summary>The service runs as SYSTEM, so the pipe has to let signed-in users connect explicitly.</summary>
    [SupportedOSPlatform("windows")]
    private static NamedPipeServerStream CreateWindowsPipe()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            IpcProtocol.PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, inBufferSize: 0, outBufferSize: 0, security);
    }
}
