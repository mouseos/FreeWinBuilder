using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HyperVGui.Core;

/// <summary>Reads component payloads stored as DCS LZMS blocks.</summary>
public static class ComponentPayload
{
    [DllImport("cabinet.dll", SetLastError = true)]
    private static extern bool CreateDecompressor(uint algorithm, IntPtr allocation, out IntPtr decompressor);
    [DllImport("cabinet.dll", SetLastError = true)]
    private static extern bool Decompress(IntPtr decompressor, byte[] input, nuint inputSize,
        byte[] output, nuint outputSize, out nuint written);
    [DllImport("cabinet.dll", SetLastError = true)]
    private static extern bool CloseDecompressor(IntPtr decompressor);

    public static byte[] Unpack(byte[] input)
    {
        if (input.Length < 4 || !input.AsSpan(0, 4).SequenceEqual("DCS\u0001"u8))
        {
            if (input.Length >= 3 && (input.AsSpan(0, 3).SequenceEqual("DCN"u8) ||
                input.AsSpan(0, 3).SequenceEqual("DCM"u8) || input.AsSpan(0, 3).SequenceEqual("DCD"u8) ||
                input.AsSpan(0, 3).SequenceEqual("DCH"u8) || input.AsSpan(0, 3).SequenceEqual("DCX"u8)))
                throw new InvalidDataException("Unsupported component payload encoding.");
            return input;
        }
        if (input.Length < 12) throw new InvalidDataException("Truncated DCS header.");
        var blocks = BinaryPrimitives.ReadUInt32LittleEndian(input.AsSpan(4));
        var expected = BinaryPrimitives.ReadUInt32LittleEndian(input.AsSpan(8));
        if (blocks is 0 or > 65536 || expected is 0 or > 128 * 1024 * 1024)
            throw new InvalidDataException("Invalid DCS dimensions.");
        if (!CreateDecompressor(5 | 0x20000000, IntPtr.Zero, out var handle))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var result = new byte[expected];
            var position = 12;
            var total = 0;
            for (var block = 0; block < blocks; block++)
            {
                if (input.Length - position < 8) throw new InvalidDataException("Truncated DCS block.");
                var size = BinaryPrimitives.ReadUInt32LittleEndian(input.AsSpan(position));
                var rawSize = BinaryPrimitives.ReadUInt32LittleEndian(input.AsSpan(position + 4));
                if (size < 4 || size > input.Length - position - 4 || rawSize == 0 || rawSize > expected - total)
                    throw new InvalidDataException("Invalid DCS block size.");
                var compressed = input.AsSpan(position + 8, checked((int)size - 4)).ToArray();
                var raw = new byte[rawSize];
                if (!Decompress(handle, compressed, (nuint)compressed.Length, raw, (nuint)raw.Length, out var written))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                if (written != (nuint)raw.Length) throw new InvalidDataException("Unexpected decompressed size.");
                raw.CopyTo(result, total);
                total = checked(total + raw.Length);
                position = checked(position + (int)size + 4);
            }
            if (total != expected || position != input.Length) throw new InvalidDataException("DCS length mismatch.");
            return result;
        }
        finally { CloseDecompressor(handle); }
    }
}
