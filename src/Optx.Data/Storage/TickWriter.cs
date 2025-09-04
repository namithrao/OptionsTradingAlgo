using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Optx.Core.Types;
using Optx.Core.Utils;

namespace Optx.Data.Storage;

/// <summary>
/// High-performance binary tick log writer
/// Format: {timestamp_ns:u64, type:u8, symbol:6 bytes, price_scaled:i64, quantity:i32, extra...}
/// </summary>
public sealed class TickWriter : IDisposable
{
    private const int TickRecordSize = 8 + 1 + 6 + 8 + 4; // 27 bytes base
    private const int BufferSize = 64 * 1024; // 64KB buffer
    
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly byte[] _buffer;
    private int _bufferPosition;
    private bool _disposed;

    /// <summary>
    /// Create tick writer for file path
    /// </summary>
    public TickWriter(string filePath)
    {
        _stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read, BufferSize);
        _ownsStream = true;
        _buffer = new byte[BufferSize];
        _bufferPosition = 0;
    }

    /// <summary>
    /// Create tick writer for existing stream
    /// </summary>
    public TickWriter(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _ownsStream = false;
        _buffer = new byte[BufferSize];
        _bufferPosition = 0;
    }

    /// <summary>
    /// Write market tick to log
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void WriteTick(in MarketTick tick)
    {
        const int recordSize = TickRecordSize;
        
        if (_bufferPosition + recordSize > BufferSize)
        {
            FlushBuffer();
        }

        var span = _buffer.AsSpan(_bufferPosition, recordSize);
        
        fixed (byte* ptr = span)
        {
            var writer = new BinaryWriter(ptr);
            
            // Timestamp (8 bytes)
            writer.WriteUInt64(tick.TimestampNs);
            
            // Type (1 byte)
            writer.WriteByte((byte)tick.Type);
            
            // Symbol (6 bytes, truncated/padded)
            var symbolBytes = new byte[6];
            var symbolSpan = tick.Symbol.Span;
            var bytesToCopy = Math.Min(symbolSpan.Length, 6);
            
            for (int i = 0; i < bytesToCopy; i++)
            {
                symbolBytes[i] = (byte)symbolSpan[i];
            }
            
            writer.WriteBytes(symbolBytes, 6);
            
            // Price scaled (8 bytes)
            writer.WriteInt64(tick.GetPriceScaled());
            
            // Quantity (4 bytes)
            writer.WriteInt32(tick.Quantity);
        }
        
        _bufferPosition += recordSize;
    }

    /// <summary>
    /// Write quote update to log
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void WriteQuote(in QuoteUpdate quote)
    {
        const int recordSize = 8 + 1 + 6 + 8 + 4 + 8 + 4; // 39 bytes
        
        if (_bufferPosition + recordSize > BufferSize)
        {
            FlushBuffer();
        }

        var span = _buffer.AsSpan(_bufferPosition, recordSize);
        
        fixed (byte* ptr = span)
        {
            var writer = new BinaryWriter(ptr);
            
            writer.WriteUInt64(quote.TimestampNs);
            writer.WriteByte((byte)MarketDataType.Quote);
            
            // Symbol (6 bytes)
            var symbolBytes = new byte[6];
            var symbolSpan = quote.Symbol.Span;
            var bytesToCopy = Math.Min(symbolSpan.Length, 6);
            
            for (int i = 0; i < bytesToCopy; i++)
            {
                symbolBytes[i] = (byte)symbolSpan[i];
            }
            
            writer.WriteBytes(symbolBytes, 6);
            
            // Bid price/size
            writer.WriteInt64((long)(quote.BidPrice * 10000m));
            writer.WriteInt32(quote.BidSize);
            
            // Ask price/size
            writer.WriteInt64((long)(quote.AskPrice * 10000m));
            writer.WriteInt32(quote.AskSize);
        }
        
        _bufferPosition += recordSize;
    }

    /// <summary>
    /// Write batch of ticks efficiently
    /// </summary>
    public void WriteBatch(ReadOnlySpan<MarketTick> ticks)
    {
        foreach (ref readonly var tick in ticks)
        {
            WriteTick(in tick);
        }
    }

    /// <summary>
    /// Flush buffer to underlying stream
    /// </summary>
    public void FlushBuffer()
    {
        if (_bufferPosition > 0)
        {
            _stream.Write(_buffer, 0, _bufferPosition);
            _bufferPosition = 0;
        }
    }

    /// <summary>
    /// Flush and sync to disk
    /// </summary>
    public async Task FlushAsync()
    {
        FlushBuffer();
        await _stream.FlushAsync();
    }

    /// <summary>
    /// Get current file position
    /// </summary>
    public long Position => _stream.Position + _bufferPosition;

    /// <summary>
    /// Write file header with metadata
    /// </summary>
    public unsafe void WriteHeader(string version = "1.0", string description = "")
    {
        const int headerSize = 64;
        var headerBytes = new byte[headerSize];
        
        fixed (byte* ptr = headerBytes)
        {
            var writer = new BinaryWriter(ptr);
            
            // Magic number
            writer.WriteUInt32(0x54494B58); // "TIKX"
            
            // Version (8 bytes)
            var versionBytes = System.Text.Encoding.UTF8.GetBytes(version.PadRight(8).Substring(0, 8));
            writer.WriteBytes(versionBytes, 8);
            
            // Timestamp
            writer.WriteUInt64(TimeUtils.GetCurrentNanoseconds());
            
            // Description (32 bytes)
            var descBytes = System.Text.Encoding.UTF8.GetBytes(description.PadRight(32).Substring(0, 32));
            writer.WriteBytes(descBytes, 32);
            
            // Reserved
            writer.WriteBytes(new byte[8], 8);
        }
        
        _stream.Write(headerBytes);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            FlushBuffer();
            
            if (_ownsStream)
            {
                _stream?.Dispose();
            }
            
            _disposed = true;
        }
    }
}

/// <summary>
/// Unsafe binary writer for high-performance serialization
/// </summary>
internal unsafe ref struct BinaryWriter
{
    private byte* _ptr;
    private int _position;

    public BinaryWriter(byte* ptr)
    {
        _ptr = ptr;
        _position = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt64(ulong value)
    {
        *(ulong*)(_ptr + _position) = value;
        _position += 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt64(long value)
    {
        *(long*)(_ptr + _position) = value;
        _position += 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt32(int value)
    {
        *(int*)(_ptr + _position) = value;
        _position += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt32(uint value)
    {
        *(uint*)(_ptr + _position) = value;
        _position += 4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteByte(byte value)
    {
        *(_ptr + _position) = value;
        _position += 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBytes(byte[] bytes, int length)
    {
        fixed (byte* src = bytes)
        {
            Unsafe.CopyBlock(_ptr + _position, src, (uint)length);
        }
        _position += length;
    }
}

/// <summary>
/// Tick log reader for reading binary tick files
/// </summary>
public sealed class TickReader : IDisposable
{
    private const int TickRecordSize = 8 + 1 + 6 + 8 + 4; // 27 bytes
    private const int BufferSize = 64 * 1024;
    
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly byte[] _buffer;
    private int _bufferPosition;
    private int _bufferLength;
    private bool _disposed;

    public TickReader(string filePath)
    {
        _stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
        _ownsStream = true;
        _buffer = new byte[BufferSize];
        ReadHeader();
        FillBuffer();
    }

    public TickReader(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _ownsStream = false;
        _buffer = new byte[BufferSize];
        ReadHeader();
        FillBuffer();
    }

    /// <summary>
    /// Read next tick from log
    /// </summary>
    public unsafe bool ReadTick(out MarketTick tick)
    {
        const int recordSize = TickRecordSize;
        
        if (_bufferPosition + recordSize > _bufferLength)
        {
            if (!RefillBuffer())
            {
                tick = default;
                return false;
            }
        }

        var span = _buffer.AsSpan(_bufferPosition, recordSize);
        
        fixed (byte* ptr = span)
        {
            var reader = new BinaryReader(ptr);
            
            var timestamp = reader.ReadUInt64();
            var type = (MarketDataType)reader.ReadByte();
            
            // Read symbol (6 bytes)
            var symbolBytes = new byte[6];
            reader.ReadBytes(symbolBytes, 6);
            var symbolLength = Array.IndexOf(symbolBytes, (byte)0);
            if (symbolLength == -1) symbolLength = 6;
            
            var symbol = System.Text.Encoding.UTF8.GetString(symbolBytes, 0, symbolLength);
            
            var priceScaled = reader.ReadInt64();
            var quantity = reader.ReadInt32();
            
            tick = new MarketTick(
                timestamp,
                symbol.AsMemory(),
                (decimal)priceScaled / 10000m,
                quantity,
                type);
        }
        
        _bufferPosition += recordSize;
        return true;
    }

    /// <summary>
    /// Read all ticks into memory
    /// </summary>
    public List<MarketTick> ReadAllTicks()
    {
        var ticks = new List<MarketTick>();
        
        while (ReadTick(out var tick))
        {
            ticks.Add(tick);
        }
        
        return ticks;
    }

    private void ReadHeader()
    {
        // Skip 64-byte header
        _stream.Seek(64, SeekOrigin.Begin);
    }

    private void FillBuffer()
    {
        _bufferPosition = 0;
        _bufferLength = _stream.Read(_buffer, 0, BufferSize);
    }

    private bool RefillBuffer()
    {
        var remainingBytes = _bufferLength - _bufferPosition;
        
        if (remainingBytes > 0)
        {
            Array.Copy(_buffer, _bufferPosition, _buffer, 0, remainingBytes);
        }
        
        var bytesRead = _stream.Read(_buffer, remainingBytes, BufferSize - remainingBytes);
        _bufferLength = remainingBytes + bytesRead;
        _bufferPosition = 0;
        
        return _bufferLength > 0;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_ownsStream)
            {
                _stream?.Dispose();
            }
            
            _disposed = true;
        }
    }
}

/// <summary>
/// Unsafe binary reader
/// </summary>
internal unsafe ref struct BinaryReader
{
    private byte* _ptr;
    private int _position;

    public BinaryReader(byte* ptr)
    {
        _ptr = ptr;
        _position = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong ReadUInt64()
    {
        var value = *(ulong*)(_ptr + _position);
        _position += 8;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ReadInt64()
    {
        var value = *(long*)(_ptr + _position);
        _position += 8;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadInt32()
    {
        var value = *(int*)(_ptr + _position);
        _position += 4;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadByte()
    {
        var value = *(_ptr + _position);
        _position += 1;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadBytes(byte[] buffer, int length)
    {
        fixed (byte* dest = buffer)
        {
            Unsafe.CopyBlock(dest, _ptr + _position, (uint)length);
        }
        _position += length;
    }
}