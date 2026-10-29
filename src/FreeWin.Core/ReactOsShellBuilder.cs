using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace FreeWin.Core;

/// <summary>Prepares a private shell directory without replacing Windows system DLLs.</summary>
public static class ReactOsShellBuilder
{
    // ReactOS 0.4.17-dev-1268-gbba112f, x64 MSVC. This CRT profile has been verified on Validation OS.
    private static readonly Dictionary<string, string> Sources = new()
    {
        ["explorer.exe"] = "0CFAEA5B596D16DB1CE3F7660020D17034AD1B3663FD6F61BC3B9391992966A3",
        ["system32/shell32.dll"] = "297F37CA8AACEA50EFD694C539340F0A6E46EC58B9586B343F3516FE2F060B4E",
        ["system32/shlwapi.dll"] = "A0F17B58878E7EC955A5B6BAE161E63E44125D30689F36F41E2F60DA2CBDCD06",
        ["system32/browseui.dll"] = "5A3D70701A5785DD4B11BAE7AC3A3A6E331FD524A7F39E85EB699CB494D0AA1D",
        ["system32/shdocvw.dll"] = "FCED5C4246464E8CEBBF8B0D58EB18A7DE994711F8E933C911BE19B0AF58F196",
        ["system32/devmgmt.exe"] = "C073D67E8819A682D0F65EA1E779535769C1EC4C982F2DC5AE7F67A899F9FBB1",
        ["system32/devmgr.dll"] = "AB5017217084B604C366B19D993FCE052944441135BBE0F1D2A249184CC70E79",
        ["system32/newdev.dll"] = "6B4E48B643CAF86DB9083A28C3473BC507D08B83622023D921ED0D2D6F048458",
        ["system32/sndvol32.exe"] = "D1E8A8B1F5743314FD6877FB90362937587799D5542AABC6431AC2D4E12C2FF0",
        ["system32/control.exe"] = "C607BAAF4D7B1C0416B56E4256E32184529E4344516CAB12BD32FDA8C8D9B6F0",
        ["system32/rundll32.exe"] = "AC5381822C47E6EFCEC1656DA9EECF0F2970C4F1BD61893EDA003EC52F1CEA2B",
        ["system32/stobject.dll"] = "F93C9705CE282049DA4DFACCB26D08066B62E14D8C1165EE9009F951A781534E",
        ["system32/mmsys.cpl"] = "7C45DB0C1006180BA22524E04739752A308EE655D8FCD9FDD4EF40B26949420E",
        ["system32/main.cpl"] = "CEC43A7039CB79005A9613655D9CAF515AE355B4302689FCE75E4089A868E714",
        ["system32/sysdm.cpl"] = "8058BAC865422FDB231FF78736911671AE262CCEB6009EB9668D56C6DA76641A",
        ["system32/appwiz.cpl"] = "8F454E119E79A5067C237386C213F8E2BD852DC7D3056C785C1CF6C7F07EE942",
        ["system32/intl.cpl"] = "43749996D81CA1DC5E460152B51E532B02F51204C0418F1862DDF2DDF54DC8AD",
        ["system32/access.cpl"] = "820F39A0B636FD9F8C8A8AE675FBB0B8B5F9254F2220D4883B50961F810BF56F",
        ["system32/powercfg.cpl"] = "1E28CA9369B7D957C08EB796A6E4B8FEABC6C4FA56E03AE2449CD4FE1A293E5C",
        ["system32/usrmgr.cpl"] = "F93BB3444916E9D8359AC0C455F4A822BA3AABE4001E31223175999606C544A8",
        ["system32/timedate.cpl"] = "808A175FC493F1B9273466837E39797525D791C322EF63723B959C64145CFEA4",
        ["system32/w32time.dll"] = "18990BC4CEFD55B505596B111439F4470C2CAE6AD7177703E82D592526635D02",
        ["system32/desk.cpl"] = "411D1C539CABFF8555F96478A210718B063741009CAB4EE1E0FFD5FAE3310CFB",
        ["system32/uxtheme.dll"] = "D4FE0051DED8A826F3ACD07C15392C1F3A978ED7C8089501F917964751660121"
    };
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

    internal static IEnumerable<string> RequiredFiles => Sources.Keys.Append("system32/taskmgr.exe");

    public static void ValidateSource(string reactOsRoot) => _ = ReadSources(reactOsRoot);

    private static Dictionary<string, byte[]> ReadSources(string reactOsRoot)
    {
        var inputs = new Dictionary<string, byte[]>();
        foreach (var (relative, expected) in Sources)
        {
            var path = Path.Combine(reactOsRoot, relative);
            var bytes = File.ReadAllBytes(path);
            if (Convert.ToHexString(SHA256.HashData(bytes)) != expected)
                throw new InvalidDataException($"Unsupported ReactOS shell profile: {relative}. Expected x64 bba112f.");
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
