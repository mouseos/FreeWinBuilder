using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Text;

namespace FreeWin.Core;

/// <summary>Prepares a private shell directory without replacing Windows system DLLs.</summary>
public static class ReactOsShellBuilder
{
    // Files taken from the ReactOS x64 boot CD. Tested with 0.4.17-dev-1268-gbba112f (x64 MSVC Debug).
    private static readonly string[] Sources =
    [
        "explorer.exe", "system32/shell32.dll", "system32/shlwapi.dll", "system32/browseui.dll",
        "system32/shdocvw.dll", "system32/devmgmt.exe", "system32/devmgr.dll", "system32/newdev.dll",
        "system32/sndvol32.exe", "system32/control.exe", "system32/rundll32.exe", "system32/stobject.dll",
        "system32/mmsys.cpl", "system32/main.cpl", "system32/sysdm.cpl", "system32/appwiz.cpl",
        "system32/intl.cpl", "system32/access.cpl", "system32/powercfg.cpl", "system32/usrmgr.cpl",
        "system32/timedate.cpl", "system32/w32time.dll", "system32/desk.cpl", "system32/uxtheme.dll"
    ];
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["shell32.dll"] = "roshell.dll", ["shlwapi.dll"] = "roshapi.dll",
        ["browseui.dll"] = "robrowse.dll", ["shdocvw.dll"] = "rodocvw.dll",
        ["comctl32.dll"] = "roctrl32.dll", ["psapi.dll"] = "rosps.dll"
    };

    public static void Prepare(string reactOsRoot, string windowsRoot, string output)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Shell output already exists.");
        using var adapter = typeof(ReactOsShellBuilder).Assembly.GetManifestResourceStream("FreeWin.Core.Resources.roscrt.dll")
            ?? throw new InvalidOperationException("Build the native adapter with scripts/Build-Native.ps1 before publishing.");
        using var adapterBuffer = new MemoryStream();
        adapter.CopyTo(adapterBuffer);
        var adapterBytes = adapterBuffer.ToArray();
        RequireX64(adapterBytes);
        using var launcher = typeof(ReactOsShellBuilder).Assembly.GetManifestResourceStream("FreeWin.Core.Resources.FreeWinShell.exe")
            ?? throw new InvalidOperationException("Build the native shell launcher before publishing.");
        using var launcherBuffer = new MemoryStream(); launcher.CopyTo(launcherBuffer);
        var launcherBytes = launcherBuffer.ToArray(); RequireX64(launcherBytes);
        using var psapi = typeof(ReactOsShellBuilder).Assembly.GetManifestResourceStream("FreeWin.Core.Resources.rosps.dll")
            ?? throw new InvalidOperationException("Build the native PSAPI forwarder before publishing.");
        using var psapiBuffer = new MemoryStream(); psapi.CopyTo(psapiBuffer);
        var psapiBytes = psapiBuffer.ToArray(); RequireX64(psapiBytes);
        using var mixer = typeof(ReactOsShellBuilder).Assembly.GetManifestResourceStream("FreeWin.Core.Resources.rosmx.dll")
            ?? throw new InvalidOperationException("Build the native audio endpoint bridge before publishing.");
        using var mixerBuffer = new MemoryStream(); mixer.CopyTo(mixerBuffer);
        var mixerBytes = mixerBuffer.ToArray(); RequireX64(mixerBytes);
        using var ui = typeof(ReactOsShellBuilder).Assembly.GetManifestResourceStream("FreeWin.Core.Resources.rosusr.dll")
            ?? throw new InvalidOperationException("Build the native notification adapter before publishing.");
        using var uiBuffer = new MemoryStream(); ui.CopyTo(uiBuffer);
        var uiBytes = uiBuffer.ToArray(); RequireX64(uiBytes);
        using var timers = typeof(ReactOsShellBuilder).Assembly.GetManifestResourceStream("FreeWin.Core.Resources.rostmr.dll")
            ?? throw new InvalidOperationException("Build the private theme API forwarder before publishing.");
        using var timerBuffer = new MemoryStream(); timers.CopyTo(timerBuffer);
        var timerBytes = timerBuffer.ToArray(); RequireX64(timerBytes);
        var inputs = ReadSources(reactOsRoot);
        var controls = Path.Combine(windowsRoot, "System32", "comctl32.dll");
        if (FileVersionInfo.GetVersionInfo(controls).FileMajorPart != 5)
            throw new InvalidDataException("The shell requires Validation OS Common Controls v5.");
        var controlBytes = File.ReadAllBytes(controls);
        RequireX64(controlBytes);
        Directory.CreateDirectory(output);
        foreach (var (name, bytes) in inputs)
        {
            if (name.EndsWith(".cpl", StringComparison.OrdinalIgnoreCase))
            {
                if (name == "mmsys.cpl") Replace(bytes, "winmm.dll", "rosmx.dll", Encoding.ASCII);
                if (name == "timedate.cpl") RedirectImport(bytes, "kernel32_vista.dll", "kernel32.dll");
                if (name == "desk.cpl")
                {
                    RedirectImport(bytes, "advapi32_vista.dll", "advapi32.dll");
                    RedirectImport(bytes, "uxtheme.dll", "rotheme.dll");
                }
                File.WriteAllBytes(Path.Combine(output, name), bytes);
                continue;
            }
            if (name == "uxtheme.dll")
            {
                // VOS forwards the display applet's theme APIs to absent SHUNIMPL.
                // Keep this compatibility engine private to the ReactOS applet.
                RedirectImport(bytes, "user32.dll", "rostmr.dll");
                File.WriteAllBytes(Path.Combine(output, "rotheme.dll"), bytes);
                continue;
            }
            if (name == "stobject.dll")
            {
                // Native Shell_NotifyIcon marshals icons differently from ReactOS Explorer.
                Replace(bytes, "shell32.dll", "roshell.dll", Encoding.ASCII);
                Replace(bytes, "winmm.dll", "rosmx.dll", Encoding.ASCII);
                File.WriteAllBytes(Path.Combine(output, name), bytes);
                continue;
            }
            if (name == "rundll32.exe")
            {
                File.WriteAllBytes(Path.Combine(output, name), bytes);
                continue;
            }
            if (name == "control.exe")
            {
                // VOS has no native Explorer to resolve the legacy My Computer parent.
                // Keep the PE layout intact while opening the private shell's root folder.
                const string original = "/n,::{20D04FE0-3AEA-1069-A2D8-08002B30309D}\\::{21EC2020-3AEA-1069-A2DD-08002B30309D}";
                const string root = "/n,::{21EC2020-3AEA-1069-A2DD-08002B30309D}";
                Replace(bytes, original, root.PadRight(original.Length, '\0'), Encoding.Unicode);
                Replace(bytes, "shell32.dll", "roshell.dll", Encoding.Unicode);
                File.WriteAllBytes(Path.Combine(output, name), bytes);
                continue;
            }
            if (name == "sndvol32.exe")
            {
                Replace(bytes, "winmm.dll", "rosmx.dll", Encoding.ASCII);
                File.WriteAllBytes(Path.Combine(output, name), bytes);
                continue;
            }
            // Device Manager uses the native VOS shell/controls APIs, with only its
            // old device-install wizard supplied privately by the matching ReactOS build.
            if (name is "devmgmt.exe" or "devmgr.dll" or "newdev.dll")
            {
                if (name == "devmgr.dll") Replace(bytes, "newdev.dll", "rosdev.dll", Encoding.ASCII);
                File.WriteAllBytes(Path.Combine(output, name == "newdev.dll" ? "rosdev.dll" : name), bytes);
                continue;
            }
            foreach (var (original, alias) in Aliases)
            {
                Replace(bytes, original, alias, Encoding.ASCII);
                Replace(bytes, original.Replace(".dll", "."), alias.Replace(".dll", "."), Encoding.ASCII);
            }
            if (name == "shlwapi.dll")
            {
                RedirectKernelBase(bytes);
                Replace(bytes, "msvcrt.dll", "roscrt.dll", Encoding.ASCII);
            }
            if (name == "explorer.exe")
            {
                foreach (var original in new[] { "shell32.dll", "shdocvw.dll" })
                    Replace(bytes, original, Aliases[original], Encoding.Unicode);
                Replace(bytes, "user32.dll", "rosusr.dll", Encoding.ASCII);
                Replace(bytes, "USER32.dll", "rosusr.dll", Encoding.ASCII);
            }
            if (name == "shell32.dll")
                Replace(bytes, "shell32.dll", "roshell.dll", Encoding.Unicode);
            File.WriteAllBytes(Path.Combine(output, Aliases.GetValueOrDefault(name, name)), bytes);
        }
        File.WriteAllBytes(Path.Combine(output, "roctrl32.dll"), controlBytes);
        // The native v5 DLL keeps property-sheet dialogs in external MUI files.
        // Renaming the DLL also changes the resource loader's expected MUI name.
        foreach (var languageDirectory in Directory.EnumerateDirectories(Path.Combine(windowsRoot, "System32")))
        {
            var resource = Path.Combine(languageDirectory, "comctl32.dll.mui");
            if (!File.Exists(resource)) continue;
            var destination = Path.Combine(output, Path.GetFileName(languageDirectory));
            Directory.CreateDirectory(destination);
            File.Copy(resource, Path.Combine(destination, "roctrl32.dll.mui"));
        }
        File.WriteAllBytes(Path.Combine(output, "roscrt.dll"), adapterBytes);
        File.WriteAllBytes(Path.Combine(output, "FreeWinShell.exe"), launcherBytes);
        File.WriteAllBytes(Path.Combine(output, "rosps.dll"), psapiBytes);
        File.WriteAllBytes(Path.Combine(output, "rosmx.dll"), mixerBytes);
        File.WriteAllBytes(Path.Combine(output, "rosusr.dll"), uiBytes);
        File.WriteAllBytes(Path.Combine(output, "rostmr.dll"), timerBytes);
    }

    internal static IEnumerable<string> RequiredFiles => Sources.Append("system32/taskmgr.exe");

    public static void ValidateSource(string reactOsRoot) => _ = ReadSources(reactOsRoot);

    private static Dictionary<string, byte[]> ReadSources(string reactOsRoot)
    {
        var inputs = new Dictionary<string, byte[]>();
        foreach (var relative in Sources)
        {
            var path = Path.Combine(reactOsRoot, relative);
            if (!File.Exists(path)) throw new FileNotFoundException("The ReactOS ISO does not contain a required file: " + relative, path);
            var bytes = File.ReadAllBytes(path);
            RequireX64(bytes);
            inputs.Add(Path.GetFileName(relative), bytes);
        }
        return inputs;
    }

    private static void RequireX64(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, false);
        using var pe = new PEReader(stream);
        if (pe.PEHeaders.CoffHeader.Machine != System.Reflection.PortableExecutable.Machine.Amd64)
            throw new InvalidDataException("The shell requires x64 PE files.");
    }

    private static void RedirectImport(byte[] bytes, string original, string replacement)
    {
        using var stream = new MemoryStream(bytes, false);
        using var pe = new PEReader(stream);
        var directory = pe.PEHeaders.PEHeader?.ImportTableDirectory
            ?? throw new InvalidDataException("Missing PE import directory.");
        int Offset(int rva)
        {
            foreach (var section in pe.PEHeaders.SectionHeaders)
                if (rva >= section.VirtualAddress && rva - section.VirtualAddress < section.SizeOfRawData)
                    return checked(section.PointerToRawData + rva - section.VirtualAddress);
            throw new InvalidDataException("Import RVA is outside file-backed sections.");
        }
        var descriptor = Offset(directory.RelativeVirtualAddress);
        var limit = checked(descriptor + directory.Size);
        var after = Encoding.ASCII.GetBytes(replacement);
        var changed = 0;
        for (var p = descriptor; p + 20 <= Math.Min(limit, bytes.Length); p += 20)
        {
            var nameRva = BitConverter.ToInt32(bytes, p + 12);
            if (nameRva == 0) break;
            var at = Offset(nameRva);
            var end = Array.IndexOf(bytes, (byte)0, at);
            if (end < at) throw new InvalidDataException("Unterminated import name.");
            if (!Encoding.ASCII.GetString(bytes, at, end - at).Equals(original, StringComparison.OrdinalIgnoreCase)) continue;
            if (after.Length > end - at) throw new InvalidDataException("Import alias would change the PE layout.");
            bytes.AsSpan(at, end - at).Clear(); after.CopyTo(bytes, at); changed++;
        }
        if (changed != 1) throw new InvalidDataException("Unexpected import descriptor count for " + original);
    }

    private static void Replace(byte[] bytes, string original, string replacement, Encoding encoding)
    {
        var before = encoding.GetBytes(original); var after = encoding.GetBytes(replacement);
        if (before.Length != after.Length) throw new InvalidDataException("Alias size mismatch.");
        for (var i = 0; i <= bytes.Length - before.Length; i++)
            if (bytes.AsSpan(i, before.Length).SequenceEqual(before))
            {
                after.CopyTo(bytes, i); i += before.Length - 1;
            }
    }

    private static void RedirectKernelBase(byte[] bytes)
    {
        var token = Encoding.ASCII.GetBytes("kernelbase_ros.");
        for (var i = 0; i <= bytes.Length - token.Length; i++)
        {
            if (!bytes.AsSpan(i, token.Length).SequenceEqual(token)) continue;
            var end = Array.IndexOf(bytes, (byte)0, i);
            if (end < 0) throw new InvalidDataException("Unterminated import or forwarder.");
            var original = Encoding.ASCII.GetString(bytes, i, end - i);
            var replacement = Encoding.ASCII.GetBytes("KERNELBASE." + original[token.Length..]);
            bytes.AsSpan(i, end - i).Clear(); replacement.CopyTo(bytes, i); i = end;
        }
    }
}
