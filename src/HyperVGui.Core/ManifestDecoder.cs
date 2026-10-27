using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HyperVGui.Core;

/// <summary>Decodes Windows component manifests with the dictionary from the matching WCP.</summary>
public static class ManifestDecoder
{
    private const uint LoadLibraryAsDataFile = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct DeltaInput { public IntPtr Data; public nuint Size; public int Editable; }
    [StructLayout(LayoutKind.Sequential)]
    private struct DeltaOutput { public IntPtr Data; public nuint Size; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LockResource(IntPtr resource);
    [DllImport("msdelta.dll", SetLastError = true)]
    private static extern bool ApplyDeltaB(ulong flags, DeltaInput source, DeltaInput delta, out DeltaOutput target);
    [DllImport("msdelta.dll")]
    private static extern bool DeltaFree(IntPtr memory);

    public static byte[] ReadDictionary(string wcpPath)
    {
        var module = LoadLibraryEx(wcpPath, IntPtr.Zero, LoadLibraryAsDataFile);
        if (module == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            // WCP uses resource type 614. Locate the first entry without executing the DLL.
            IntPtr found = IntPtr.Zero;
            EnumResourceNamesProc callback = (_, _, name, _) => { found = FindResource(module, name, (IntPtr)614); return false; };
            EnumResourceNames(module, (IntPtr)614, callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            if (found == IntPtr.Zero) throw new InvalidDataException("WCP dictionary resource was not found.");
            var length = checked((int)SizeofResource(module, found));
            var data = LockResource(LoadResource(module, found));
            if (length == 0 || data == IntPtr.Zero) throw new InvalidDataException("WCP dictionary resource is empty.");
            var dictionary = new byte[length]; Marshal.Copy(data, dictionary, 0, length); return dictionary;
        }
        finally { FreeLibrary(module); }
    }

    private delegate bool EnumResourceNamesProc(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);
    [DllImport("kernel32.dll", EntryPoint = "EnumResourceNamesW", SetLastError = true)]
    private static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumResourceNamesProc callback, IntPtr parameter);

    public static byte[] Decode(byte[] dictionary, byte[] input)
    {
        var offset = 0;
        if (input.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) offset = 3;
        while (offset < input.Length && (input[offset] == 32 || input[offset] == 13 || input[offset] == 10 || input[offset] == 9)) offset++;
        if (input.AsSpan(offset).StartsWith("<?xml"u8)) return input;
        if (!input.AsSpan().StartsWith(new byte[] { 0x44, 0x43, 0x4d, 0x01, 0x50, 0x41, 0x33, 0x30 }))
            throw new InvalidDataException("Unsupported component manifest compression.");
        var sourcePin = GCHandle.Alloc(dictionary, GCHandleType.Pinned);
        var deltaPin = GCHandle.Alloc(input, GCHandleType.Pinned);
        try
        {
            var source = new DeltaInput { Data = sourcePin.AddrOfPinnedObject(), Size = (nuint)dictionary.Length };
            var delta = new DeltaInput { Data = IntPtr.Add(deltaPin.AddrOfPinnedObject(), 4), Size = (nuint)(input.Length - 4) };
            if (!ApplyDeltaB(0, source, delta, out var output)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try { var result = new byte[checked((int)output.Size)]; Marshal.Copy(output.Data, result, 0, result.Length); return result; }
            finally { DeltaFree(output.Data); }
        }
        finally { deltaPin.Free(); sourcePin.Free(); }
    }
}
