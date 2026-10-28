using System.IO;

namespace KubaToolKit.Modules.TableViewer.Decoding;

// Big-endian (network byte/bit order) reader over a proprietary table
// file -- confirmed against a real QTable decode: multi-byte integers and
// Unicode string characters are both stored most-significant-byte first.
public class BitReader
{
    private readonly byte[] _data;

    public BitReader(byte[] data)
    {
        _data = data;
    }

    public long BitPosition { get; private set; }

    public long ByteOffset => BitPosition / 8;

    public long TotalBits => (long)_data.Length * 8;

    public bool AtEnd => BitPosition >= TotalBits;

    public long RemainingBits => TotalBits - BitPosition;

    public ulong
    ReadBits(
        int count)
    {
        if (count < 0 || count > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (BitPosition + count > TotalBits)
        {
            throw new EndOfStreamException("ran past the end of the file");
        }

        if (count == 0)
        {
            return 0;
        }

        // Fast path: byte-aligned reads (the overwhelming majority of
        // fields) copy whole bytes instead of looping bit by bit.
        if (BitPosition % 8 == 0 && count % 8 == 0)
        {
            ulong value = 0;
            var startByte = (int)(BitPosition / 8);
            var byteCount = count / 8;

            for (var i = 0; i < byteCount; i++)
            {
                value = (value << 8) | _data[startByte + i];
            }

            BitPosition += count;

            return value;
        }

        ulong bitValue = 0;

        for (var i = 0; i < count; i++)
        {
            var bitIndex = BitPosition + i;
            var byteIndex = (int)(bitIndex / 8);
            var bitInByte = 7 - (int)(bitIndex % 8);
            var bit = (byte)((_data[byteIndex] >> bitInByte) & 1);

            bitValue = (bitValue << 1) | bit;
        }

        BitPosition += count;

        return bitValue;
    }

    public byte[]
    ReadRawBytes(
        int byteCount)
    {
        var bytes = new byte[byteCount];

        if (BitPosition % 8 == 0)
        {
            var startByte = (int)(BitPosition / 8);

            if (startByte + byteCount > _data.Length)
            {
                throw new EndOfStreamException("ran past the end of the file");
            }

            Array.Copy(_data, startByte, bytes, 0, byteCount);
            BitPosition += (long)byteCount * 8;

            return bytes;
        }

        for (var i = 0; i < byteCount; i++)
        {
            bytes[i] = (byte)ReadBits(8);
        }

        return bytes;
    }

    public void
    Skip(
        long bits)
    {
        var target = BitPosition + bits;

        if (target > TotalBits || target < 0)
        {
            throw new EndOfStreamException("seek target is outside the file");
        }

        BitPosition = target;
    }
}
