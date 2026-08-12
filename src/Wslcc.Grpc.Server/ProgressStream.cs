using System.Threading.Channels;
using Grpc.Core;
using Wslcc.Abstractions;
using Wslcc.Grpc.Contracts;

namespace Wslcc.Grpc.Server;

/// <summary>
/// Bridges engine <see cref="IProgress{T}"/> reports onto a gRPC server stream of progress/completed events.
/// </summary>
internal static class ProgressStream
{
    public static async Task WriteOperationAsync<TEvent, TResponse>(
        IServerStreamWriter<TEvent> responseStream,
        Func<IProgress<ServiceProgressUpdate>, CancellationToken, Task<TResponse>> run,
        Func<ServiceProgress, TEvent> wrapProgress,
        Func<TResponse, TEvent> wrapCompleted,
        ServerCallContext context)
    {
        var channel = Channel.CreateUnbounded<ServiceProgressUpdate>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        var progress = new ChannelProgress(channel.Writer);

        var runTask = Task.Run(async () =>
        {
            try
            {
                return await run(progress, context.CancellationToken).ConfigureAwait(false);
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, CancellationToken.None);

        try
        {
            await foreach (var update in channel.Reader.ReadAllAsync(context.CancellationToken).ConfigureAwait(false))
            {
                await responseStream.WriteAsync(wrapProgress(ToProto(update))).ConfigureAwait(false);
            }

            var response = await runTask.ConfigureAwait(false);
            await responseStream.WriteAsync(wrapCompleted(response)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            // Client cancelled; drop the remainder.
        }
        finally
        {
            // Surface engine failures that completed after the reader stopped, or ensure faults propagate.
            if (runTask.IsFaulted)
            {
                await runTask.ConfigureAwait(false);
            }
        }
    }

    public static ServiceProgress ToProto(ServiceProgressUpdate update) => new()
    {
        Service = update.Service ?? string.Empty,
        Phase = update.Phase ?? string.Empty,
        Status = update.Status ?? string.Empty,
        Message = update.Message ?? string.Empty,
        ContainerId = update.ContainerId ?? string.Empty,
    };

    private sealed class ChannelProgress(ChannelWriter<ServiceProgressUpdate> writer)
        : IProgress<ServiceProgressUpdate>
    {
        public void Report(ServiceProgressUpdate value) => writer.TryWrite(value);
    }
}
