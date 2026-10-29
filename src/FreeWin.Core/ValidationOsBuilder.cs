using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace FreeWin.Core;

/// <summary>Creates the Validation OS RAM ISO directly from user-provided Microsoft and ReactOS media.</summary>
public static class ValidationOsBuilder
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string name, string existing, IntPtr security);

    public static string Build(string validationIso, string reactOsIso, string sevenZip,
        string winPeMedia, string bootFiles, string oscdimg, string output, Action<string>? log = null)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("Validation OS servicing requires administrator rights.");
        foreach (var path in new[] { validationIso, reactOsIso, sevenZip, oscdimg,
            Path.Combine(winPeMedia, "bootmgr"), Path.Combine(bootFiles, "etfsboot.com"), Path.Combine(bootFiles, "efisys_noprompt.bin") })
            if (!File.Exists(path)) throw new FileNotFoundException("Required build input missing.", path);
        output = Path.GetFullPath(output);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Build output already exists.");
        Directory.CreateDirectory(output);
        var vos = Path.Combine(output, "validation-source");
        var ros = Path.Combine(output, "reactos-source");
        void Run(string tool, string[] args, bool reboot = false) => ValidationOsMedia.Run(tool, args, log, reboot);
        log?.Invoke("Extracting official Validation OS RAM image, packages and servicing tools.");
        // Do not extract the unused, large disk image.
        Run(sevenZip, ["x", "-y", "-bd", "-o" + vos, Path.GetFullPath(validationIso), "ValidationOS.wim", "cabs/*", "GenImage/Configs/*", "GenImage/Tools/DISM/*"]);
        var source = Path.Combine(vos, "ValidationOS.wim");
        var ram = WimMetadata.Read(source).SingleOrDefault(i => i.Index == 2 && i.Architecture == "9")
            ?? throw new InvalidDataException("The source must include the official x64 Validation OS RAM image at index 2.");
        var packages = ValidationOsPackages.Plan(vos);
        log?.Invoke($"Validation OS {ram.Build}.{ram.Revision}: {packages.Count} official package files selected.");
        var dism = Path.Combine(vos, "GenImage", "Tools", "DISM", "amd64", "dism.exe");
        if (!File.Exists(dism)) throw new FileNotFoundException("Media-matched x64 DISM is missing.", dism);

        log?.Invoke("Extracting ReactOS userland.");
        Run(sevenZip, ["x", "-y", "-bd", "-o" + ros, Path.GetFullPath(reactOsIso), "amd64/reactos.cab"]);
        var rosRoot = Path.Combine(ros, "reactos");
        var cab = Path.Combine(ros, "amd64", "reactos.cab");
        if (File.Exists(cab))
        {
            var flat = Path.Combine(ros, "files");
            var required = ReactOsShellBuilder.RequiredFiles.Select(p => Path.GetFileName(p)!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            Run(sevenZip, ["e", "-y", "-bd", "-o" + flat, cab, .. required]);
            Directory.CreateDirectory(Path.Combine(rosRoot, "system32"));
            foreach (var name in required)
                File.Copy(Path.Combine(flat, name), Path.Combine(name == "explorer.exe" ? rosRoot : Path.Combine(rosRoot, "system32"), name));
        }
        else Run(sevenZip, ["x", "-y", "-bd", "-o" + ros, Path.GetFullPath(reactOsIso), "reactos/*"]);
        ReactOsShellBuilder.ValidateSource(rosRoot);

        var stage = Path.Combine(output, "packages");
        foreach (var package in packages)
        {
            var target = Path.Combine(stage, package.RelativeDestination);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!CreateHardLinkW(target, package.Source, IntPtr.Zero)) File.Copy(package.Source, target);
        }
        var image = Path.Combine(output, "ValidationOS.wim");
        var mount = Path.Combine(output, "servicing-mount");
        var scratch = Path.Combine(output, "scratch");
        Directory.CreateDirectory(mount); Directory.CreateDirectory(scratch);
        Run(dism, ["/Export-Image", "/SourceImageFile:" + source, "/SourceIndex:2", "/DestinationImageFile:" + image, "/Compress:fast", "/Bootable", "/LogPath:" + Path.Combine(output, "export.log")]);
        var mounted = false;
        try
        {
            Run(dism, ["/Mount-Image", "/ImageFile:" + image, "/Index:1", "/MountDir:" + mount, "/LogPath:" + Path.Combine(output, "mount.log")]);
            mounted = true;
            void AddPackages(string folder, string logName)
            {
                if (!Directory.Exists(folder)) return;
                // Language satellites are inapplicable until their neutral parents exist.
                // Keep the official GenImage ordering, not filesystem/alphabetical order.
                var directories = Directory.GetDirectories(folder).Where(p => Directory.GetFiles(p, "*.cab").Length > 0)
                    .OrderBy(p => Path.GetFileName(p).Equals("neutral", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
                if (directories.Length == 0) return;
                log?.Invoke("Package directories (neutral first): " + string.Join(", ", directories.Select(Path.GetFileName)));
                Run(dism, ["/Image:" + mount, "/Add-Package", "/ScratchDir:" + scratch, "/LogPath:" + Path.Combine(output, logName), .. directories.Select(p => "/PackagePath:" + p)], true);
            }
            log?.Invoke("Adding official Validation OS feature packages.");
            AddPackages(stage, "packages.log");
            AddPackages(Path.Combine(stage, "Kernel-Packages"), "kernel-packages.log");
            var hive = "FWB_RAM_" + Guid.NewGuid().ToString("N");
            ValidationOsMedia.WithHive("HKLM", hive, Path.Combine(mount, "Windows", "System32", "config", "SYSTEM"), log, () =>
            {
                using (var control = Registry.LocalMachine.CreateSubKey(hive + @"\ControlSet001\Control")) control.SetValue("SystemBootDriveLetter", 88, RegistryValueKind.DWord);
                using (var cache = Registry.LocalMachine.CreateSubKey(hive + @"\ControlSet001\Services\XtaCache")) cache.SetValue("Start", 4, RegistryValueKind.DWord);
                using var setup = Registry.LocalMachine.OpenSubKey(hive + @"\Setup", true);
                setup?.DeleteValue("SystemPartition", false);
            });
            Run(dism, ["/Unmount-Image", "/MountDir:" + mount, "/Commit", "/LogPath:" + Path.Combine(output, "commit.log")]);
            mounted = false;
        }
        finally
        {
            if (mounted) log?.Invoke("Servicing failed; the mounted image is preserved for diagnosis: " + mount);
        }
        return ValidationOsMedia.Build(image, rosRoot, winPeMedia, bootFiles, dism, oscdimg, Path.Combine(output, "iso"), log);
    }
}
