using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace FreeWin.Core;

/// <summary>Edits protected offline keys without changing their security descriptors.</summary>
internal sealed class RegistryRestoreScope : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Privilege { public uint Count, Low; public int High; public uint Attributes; }
    private readonly SafeAccessTokenHandle token;
    private readonly List<Privilege> previous = [];
    private bool disposed;

    public RegistryRestoreScope()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x28, out token)) throw new Win32Exception();
        try
        {
            foreach (var name in new[] { "SeBackupPrivilege", "SeRestorePrivilege" })
            {
                if (!LookupPrivilegeValue(null, name, out var luid)) throw new Win32Exception();
                var requested = new Privilege { Count = 1, Low = luid.Low, High = luid.High, Attributes = 2 };
                if (!AdjustTokenPrivileges(token, false, ref requested, (uint)Marshal.SizeOf<Privilege>(), out var old, out _))
                    throw new Win32Exception();
                var error = Marshal.GetLastWin32Error();
                if (error != 0) throw new Win32Exception(error);
                previous.Add(old);
            }
        }
        catch { Dispose(); throw; }
    }

    public RegistryKey Open(RegistryKey root, string path)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var error = RegCreateKeyEx(root.Handle, path, 0, null, 4, 0, IntPtr.Zero, out var handle, out _);
        if (error != 0) { handle?.Dispose(); throw new Win32Exception(error); }
        try { return RegistryKey.FromHandle(handle, RegistryView.Registry64); }
        catch { handle.Dispose(); throw; }
    }

    public void DeleteLeaf(RegistryKey root, string path)
    {
        using (var existing = root.OpenSubKey(path))
            if (existing is null) return;
        using var key = Open(root, path);
        if (key.SubKeyCount != 0) throw new InvalidDataException("Expected a leaf registry reference: " + path);
        var status = NtDeleteKey(key.Handle);
        if (status < 0) throw new Win32Exception((int)RtlNtStatusToDosError(status));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var error = 0;
        for (var i = previous.Count - 1; i >= 0; --i)
        {
            var old = previous[i];
            if (!RestoreTokenPrivileges(token, false, ref old, 0, IntPtr.Zero, IntPtr.Zero))
                error = Marshal.GetLastWin32Error();
        }
        token.Dispose();
        if (error != 0) throw new Win32Exception(error);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { public uint Low; public int High; }
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token, [MarshalAs(UnmanagedType.Bool)] bool disableAll,
        ref Privilege state, uint size, out Privilege previous, out uint returned);
    [DllImport("advapi32.dll", EntryPoint = "AdjustTokenPrivileges", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RestoreTokenPrivileges(SafeAccessTokenHandle token, [MarshalAs(UnmanagedType.Bool)] bool disableAll,
        ref Privilege state, uint size, IntPtr previous, IntPtr returned);
    [DllImport("advapi32.dll", EntryPoint = "RegCreateKeyExW", CharSet = CharSet.Unicode)]
    private static extern int RegCreateKeyEx(SafeRegistryHandle root, string path, uint reserved, string? keyClass,
        uint options, uint access, IntPtr security, out SafeRegistryHandle result, out uint disposition);
    [DllImport("ntdll.dll")] private static extern int NtDeleteKey(SafeRegistryHandle key);
    [DllImport("ntdll.dll")] private static extern uint RtlNtStatusToDosError(int status);
}
