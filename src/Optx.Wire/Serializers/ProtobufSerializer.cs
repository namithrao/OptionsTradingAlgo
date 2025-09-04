using System.Buffers;
using System.Runtime.CompilerServices;
using Google.Protobuf;
using Optx.Core.Utils;

namespace Optx.Wire.Serializers;

/// <summary>
/// Zero-allocation protobuf serializer using pooled buffers
/// </summary>
public static class ProtobufSerializer
{
    private static readonly ArrayPool<byte> BufferPool = ArrayPool<byte>.Shared;

    /// <summary>
    /// Serialize message to pooled buffer
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PooledBuffer Serialize<T>(T message) where T : IMessage
    {
        var size = message.CalculateSize();
        var buffer = BufferPool.Rent(size + 4); // +4 for length prefix
        
        try
        {
            // Write length prefix (big-endian)
            buffer[0] = (byte)(size >> 24);
            buffer[1] = (byte)(size >> 16);
            buffer[2] = (byte)(size >> 8);
            buffer[3] = (byte)size;

            // Write message
            var span = buffer.AsSpan(4, size);
            message.WriteTo(span);

            return new PooledBuffer(buffer, size + 4);
        }
        catch
        {
            BufferPool.Return(buffer);
            throw;
        }
    }

    /// <summary>
    /// Deserialize message from span
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Deserialize<T>(ReadOnlySpan<byte> data) where T : IMessage<T>, new()
    {
        if (data.Length < 4)
            throw new ArgumentException("Data too short for length prefix");

        // Read length prefix (big-endian)
        var length = (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3];
        
        if (data.Length < length + 4)
            throw new ArgumentException("Data shorter than indicated length");

        var messageData = data.Slice(4, length);
        var message = new T();
        message.MergeFrom(messageData);
        return message;
    }

    /// <summary>
    /// Serialize directly to stream
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async Task SerializeToStreamAsync<T>(T message, Stream stream, CancellationToken cancellationToken = default) 
        where T : IMessage
    {
        var size = message.CalculateSize();
        
        // Write length prefix
        var lengthBytes = new byte[4];
        lengthBytes[0] = (byte)(size >> 24);
        lengthBytes[1] = (byte)(size >> 16);
        lengthBytes[2] = (byte)(size >> 8);
        lengthBytes[3] = (byte)size;
        
        await stream.WriteAsync(lengthBytes, cancellationToken);
        
        // Write message
        var buffer = BufferPool.Rent(size);
        try
        {
            var span = buffer.AsSpan(0, size);
            message.WriteTo(span);
            await stream.WriteAsync(buffer.AsMemory(0, size), cancellationToken);
        }
        finally
        {
            BufferPool.Return(buffer);
        }
    }

    /// <summary>
    /// Deserialize from stream
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async Task<T> DeserializeFromStreamAsync<T>(Stream stream, CancellationToken cancellationToken = default) 
        where T : IMessage<T>, new()
    {
        // Read length prefix
        var lengthBytes = new byte[4];
        await stream.ReadExactlyAsync(lengthBytes, cancellationToken);
        
        var length = (lengthBytes[0] << 24) | (lengthBytes[1] << 16) | (lengthBytes[2] << 8) | lengthBytes[3];
        
        if (length <= 0 || length > 100 * 1024 * 1024) // 100MB max
            throw new ArgumentException($"Invalid message length: {length}");

        // Read message data
        var buffer = BufferPool.Rent(length);
        try
        {
            await stream.ReadExactlyAsync(buffer.AsMemory(0, length), cancellationToken);
            
            var message = new T();
            message.MergeFrom(buffer.AsSpan(0, length));
            return message;
        }
        finally
        {
            BufferPool.Return(buffer);
        }
    }

    /// <summary>
    /// Get serialized size without actually serializing
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetSerializedSize<T>(T message) where T : IMessage
    {
        return message.CalculateSize() + 4; // +4 for length prefix
    }
}

/// <summary>
/// RAII wrapper for pooled serialization buffer
/// </summary>
public readonly ref struct PooledBuffer
{
    private readonly byte[] _buffer;
    private readonly int _length;

    public PooledBuffer(byte[] buffer, int length)
    {
        _buffer = buffer;
        _length = length;
    }

    public ReadOnlySpan<byte> Data => _buffer.AsSpan(0, _length);
    public int Length => _length;

    public void Dispose()
    {
        if (_buffer != null)
            ArrayPool<byte>.Shared.Return(_buffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CopyTo(Span<byte> destination)
    {
        if (destination.Length < _length)
            throw new ArgumentException("Destination too small");
        
        Data.CopyTo(destination);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] ToArray()
    {
        return Data.ToArray();
    }
}

/// <summary>
/// Extension methods for stream operations
/// </summary>
public static class StreamExtensions
{
    /// <summary>
    /// Read exactly the specified number of bytes
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async Task<int> ReadExactlyAsync(this Stream stream, byte[] buffer, CancellationToken cancellationToken = default)
    {
        return await ReadExactlyAsync(stream, buffer.AsMemory(), cancellationToken);
    }

    /// <summary>
    /// Read exactly the specified number of bytes
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async Task<int> ReadExactlyAsync(this Stream stream, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int totalBytesRead = 0;
        
        while (totalBytesRead < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer.Slice(totalBytesRead), cancellationToken);
            if (bytesRead == 0)
                throw new EndOfStreamException("Unexpected end of stream");
            
            totalBytesRead += bytesRead;
        }
        
        return totalBytesRead;
    }
}