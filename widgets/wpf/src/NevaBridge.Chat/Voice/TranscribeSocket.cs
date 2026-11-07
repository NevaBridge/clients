using System.Net.WebSockets;

namespace NevaBridge.Chat;

/// <summary>
/// One WebSocket connection to Amazon Transcribe streaming. The default implementation uses
/// <see cref="ClientWebSocket"/>; replace it to route the stream differently or in tests.
/// </summary>
public interface ITranscribeSocket : IDisposable
{
    /// <summary>Connects to the presigned address. Never put the address into a message or a log.</summary>
    Task ConnectAsync(Uri url, CancellationToken cancellationToken);

    /// <summary>Sends one binary message. Calls may overlap; they are sent one after another.</summary>
    Task SendAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken);

    /// <summary>Reads the next binary message, or null once the stream has closed.</summary>
    Task<byte[]?> ReceiveAsync(CancellationToken cancellationToken);

    /// <summary>Why the stream closed, once <see cref="ReceiveAsync"/> returned null.</summary>
    string? CloseReason { get; }

    /// <summary>Tells Transcribe this side is done; the stream closes once Transcribe answers.</summary>
    Task CloseAsync(CancellationToken cancellationToken);
}

/// <summary>An <see cref="ITranscribeSocket"/> over <see cref="ClientWebSocket"/>.</summary>
public sealed class WebSocketTranscribeSocket : ITranscribeSocket
{
    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _sending = new(1, 1);

    public string? CloseReason { get; private set; }

    public Task ConnectAsync(Uri url, CancellationToken cancellationToken) => _socket.ConnectAsync(url, cancellationToken);

    public async Task SendAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken)
    {
        await _sending.WaitAsync(cancellationToken);
        try
        {
            await _socket.SendAsync(message, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);
        }
        finally
        {
            _sending.Release();
        }
    }

    public async Task<byte[]?> ReceiveAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[16 * 1024];
        using MemoryStream message = new();
        while (true)
        {
            ValueWebSocketReceiveResult received = await _socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
            if (received.MessageType == WebSocketMessageType.Close)
            {
                CloseReason = string.IsNullOrEmpty(_socket.CloseStatusDescription)
                    ? $"Amazon Transcribe closed the stream ({(int?)_socket.CloseStatus})."
                    : _socket.CloseStatusDescription;
                return null;
            }

            message.Write(buffer, 0, received.Count);
            if (received.EndOfMessage)
            {
                return message.ToArray();
            }
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        await _sending.WaitAsync(cancellationToken);
        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                // Only the closing frame: a receive loop may be waiting, and it reads Transcribe's answer.
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
            }
        }
        finally
        {
            _sending.Release();
        }
    }

    public void Dispose() => _socket.Dispose();
}
