using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Optx.Core.Utils;

/// <summary>
/// Zero-allocation memory utilities
/// </summary>
public static class MemoryUtils
{
    private static readonly ArrayPool<byte> BytePool = ArrayPool<byte>.Shared;
    private static readonly ArrayPool<char> CharPool = ArrayPool<char>.Shared;
    private static readonly ArrayPool<decimal> DecimalPool = ArrayPool<decimal>.Shared;

    /// <summary>
    /// Rent a byte array from the shared pool
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] RentBytes(int minimumLength)
    {
        return BytePool.Rent(minimumLength);
    }

    /// <summary>
    /// Return a byte array to the shared pool
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReturnBytes(byte[] array)
    {
        BytePool.Return(array, clearArray: true);
    }

    /// <summary>
    /// Rent a char array from the shared pool
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static char[] RentChars(int minimumLength)
    {
        return CharPool.Rent(minimumLength);
    }

    /// <summary>
    /// Return a char array to the shared pool
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReturnChars(char[] array)
    {
        CharPool.Return(array, clearArray: true);
    }

    /// <summary>
    /// Rent a decimal array from the shared pool
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal[] RentDecimals(int minimumLength)
    {
        return DecimalPool.Rent(minimumLength);
    }

    /// <summary>
    /// Return a decimal array to the shared pool
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReturnDecimals(decimal[] array)
    {
        DecimalPool.Return(array, clearArray: true);
    }

    /// <summary>
    /// Copy memory efficiently using unsafe operations
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void CopyMemory(void* destination, void* source, int byteCount)
    {
        Unsafe.CopyBlock(destination, source, (uint)byteCount);
    }

    /// <summary>
    /// Zero memory efficiently
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void ZeroMemory(void* ptr, int byteCount)
    {
        Unsafe.InitBlock(ptr, 0, (uint)byteCount);
    }

    /// <summary>
    /// Convert span to array without allocation if possible
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T[] ToArrayEfficient<T>(ReadOnlySpan<T> span)
    {
        if (span.IsEmpty) return Array.Empty<T>();
        return span.ToArray();
    }

    /// <summary>
    /// Get reference to first element of span
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref T GetReference<T>(Span<T> span)
    {
        return ref MemoryMarshal.GetReference(span);
    }

    /// <summary>
    /// Get readonly reference to first element of readonly span
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref readonly T GetReference<T>(ReadOnlySpan<T> span)
    {
        return ref MemoryMarshal.GetReference(span);
    }

    /// <summary>
    /// Cast span of bytes to span of structs
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<T> CastSpan<T>(ReadOnlySpan<byte> bytes) where T : struct
    {
        return MemoryMarshal.Cast<byte, T>(bytes);
    }

    /// <summary>
    /// Cast span of structs to span of bytes
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<byte> AsBytes<T>(ReadOnlySpan<T> structs) where T : struct
    {
        return MemoryMarshal.AsBytes(structs);
    }
}

/// <summary>
/// RAII wrapper for pooled arrays
/// </summary>
public readonly ref struct PooledArray<T>
{
    private readonly T[] _array;
    private readonly ArrayPool<T> _pool;
    private readonly int _length;

    public PooledArray(ArrayPool<T> pool, int length)
    {
        _pool = pool;
        _length = length;
        _array = pool.Rent(length);
    }

    public Span<T> Span => _array.AsSpan(0, _length);
    public T[] Array => _array;
    public int Length => _length;

    public void Dispose()
    {
        if (_array != null)
            _pool.Return(_array, clearArray: true);
    }

    public T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _array[index];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _array[index] = value;
    }
}