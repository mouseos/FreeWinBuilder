using System.Diagnostics;
using System.Security.Principal;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FreeWin.Core;

/// <summary>Builds RAM-boot media from a serviced Validation OS index-2 image.</summary>
public static class ValidationOsMedia
{
    public static string Build(string preparedWim, string reactOsRoot, string winPeMedia,
        string bootFiles, string dism, string oscdimg, string output, Action<string>? log = null)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("Validation OS servicing requires administrator rights.");
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Media output already exists.");
        var bios = Path.GetFullPath(Path.Combine(bootFiles, "etfsboot.com"));
        var efi = Path.GetFullPath(Path.Combine(bootFiles, "efisys_noprompt.bin"));
        foreach (var file in new[] { preparedWim, bios, efi, dism, oscdimg, Path.Combine(winPeMedia, "bootmgr") })
            if (!File.Exists(file)) throw new FileNotFoundException("Required media component missing.", file);
        var images = WimMetadata.Read(preparedWim);
        if (images.Count != 1 || images[0].Architecture != "9")
            throw new InvalidDataException("Expected a single exported x64 Validation OS RAM image.");

        Directory.CreateDirectory(output);
        var media = Path.Combine(output, "media");
        CopyTree(winPeMedia, media);
        Directory.CreateDirectory(Path.Combine(media, "sources"));
        var boot = Path.Combine(media, "sources", "boot.wim");
        if (File.Exists(boot)) File.SetAttributes(boot, FileAttributes.Normal);
        File.Copy(preparedWim, boot, true);
        File.SetAttributes(boot, FileAttributes.Normal);
        var mount = Path.Combine(output, "mount");
        Directory.CreateDirectory(mount);
        var mounted = false;
        try
        {
            Run(dism, ["/Mount-Image", $"/ImageFile:{boot}", "/Index:1", $"/MountDir:{mount}", $"/LogPath:{output}/mount.log"], log);
            mounted = true;
            Configure(mount, reactOsRoot, log);
            Run(dism, ["/Unmount-Image", $"/MountDir:{mount}", "/Commit", $"/LogPath:{output}/commit.log"], log);
            mounted = false;
        }
        finally
        {
            if (mounted) log?.Invoke("Media configuration failed; the mounted image is preserved for diagnosis: " + mount);
        }
        var iso = Path.GetFullPath(Path.Combine(output, "FreeWin-ValidationOS.iso"));
        Run(oscdimg, ["-m", "-o", "-u2", "-udfver102", "-lFREEWINVOS",
            $"-bootdata:2#p0,e,b{bios}#pEF,e,b{efi}", Path.GetFullPath(media), iso], log);
        if (!File.Exists(iso)) throw new IOException("ISO output was not created.");
        return iso;
    }

    private static void Configure(string mount, string reactOsRoot, Action<string>? log)
    {
        var windows = Path.Combine(mount, "Windows");
        foreach (var tool in new[] { "mmc.exe", "devmgmt.msc", "devmgr.dll", "compmgmt.msc", "diskmgmt.msc", "services.msc", "eventvwr.msc" })
            if (!File.Exists(Path.Combine(windows, "System32", tool)))
                throw new InvalidDataException("The official DeveloperTools package is required; missing: " + tool);
        var branding = Path.Combine(windows, "System32", "winbrand.dll");
        if (!File.Exists(branding))
        {
            // DeveloperTools' dmdlgs.dll imports this absent VOS component.
            // Provide its product-name formatter without replacing an OS DLL.
            using var resource = typeof(ValidationOsMedia).Assembly.GetManifestResourceStream("FreeWin.Core.Resources.winbrand.dll")
                ?? throw new InvalidOperationException("Build the native Validation OS adapters before publishing.");
            using var file = new FileStream(branding, FileMode.CreateNew, FileAccess.Write);
            resource.CopyTo(file);
            log?.Invoke("Added the missing Validation OS product-name formatting adapter.");
        }
        var soft = "FWB_Software_" + Guid.NewGuid().ToString("N");
        var profile = "FWB_Profile_" + Guid.NewGuid().ToString("N");
        WithHive("HKLM", soft, Path.Combine(windows, "System32", "config", "SOFTWARE"), log, () =>
        {
            using (var version = Registry.LocalMachine.OpenSubKey(soft + @"\Microsoft\Windows NT\CurrentVersion"))
                if (!string.Equals(version?.GetValue("SystemRoot") as string, @"X:\Windows", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("This is a disk image, not the official Validation OS RAM image (index 2).");
            var packages = Path.Combine(windows, "servicing", "Packages");
            if (Directory.EnumerateFiles(packages, "Microsoft-WinVOS-Setup-Package~*.mum").Any())
                throw new InvalidDataException("The Setup package is incompatible with Validation OS RAM boot.");
            ReactOsShellBuilder.Prepare(reactOsRoot, windows, Path.Combine(mount, "RosShell"));
            using (var key = Registry.LocalMachine.CreateSubKey(soft + @"\Microsoft\Windows NT\CurrentVersion\Winlogon"))
                key.SetValue("Shell", @"%SystemDrive%\RosShell\FreeWinShell.exe explorer.exe", RegistryValueKind.ExpandString);
            using (var key = Registry.LocalMachine.CreateSubKey(soft + @"\Classes\CLSID\{4622AD11-FF23-11d0-8D34-00A0C90F2719}\InprocServer32"))
            {
                key.SetValue("", @"%SystemDrive%\RosShell\roshell.dll", RegistryValueKind.ExpandString);
                key.SetValue("ThreadingModel", "Apartment");
            }
            foreach (var app in new[] { "explorer.exe", "control.exe", "rundll32.exe", "sndvol32.exe" })
            {
                using var key = Registry.LocalMachine.CreateSubKey(soft + @"\Microsoft\Windows\CurrentVersion\App Paths\" + app);
                key.SetValue("", @"%SystemDrive%\RosShell\" + app, RegistryValueKind.ExpandString);
                key.SetValue("Path", @"%SystemDrive%\RosShell", RegistryValueKind.ExpandString);
            }
            using (var key = Registry.LocalMachine.CreateSubKey(soft + @"\Classes\CLSID\{35CEC8A3-2BE6-11D2-8773-92E220524153}\InprocServer32"))
            {
                key.SetValue("", @"%SystemDrive%\RosShell\stobject.dll", RegistryValueKind.ExpandString);
                key.SetValue("ThreadingModel", "Both");
            }
            using (var key = Registry.LocalMachine.CreateSubKey(soft + @"\Microsoft\Windows\CurrentVersion\ShellServiceObjectDelayLoad"))
                // FreeWinShell owns the tray object and records its lifecycle.
                key.DeleteValue("SysTray", false);
            using (var key = Registry.LocalMachine.CreateSubKey(soft + @"\Microsoft\Windows\CurrentVersion\Control Panel\Cpls"))
                foreach (var file in Directory.GetFiles(Path.Combine(mount, "RosShell"), "*.cpl"))
                    key.SetValue("FreeWin." + Path.GetFileNameWithoutExtension(file), @"X:\RosShell\" + Path.GetFileName(file));
            // VOS lists desk/sysdm/mmsys under "don't load". Shells that honour it
            // would hide the private applets registered above, so unlist only those.
            using (var key = Registry.LocalMachine.OpenSubKey(soft + @"\Microsoft\Windows\CurrentVersion\Control Panel\don't load", true))
                if (key is not null)
                    foreach (var file in Directory.GetFiles(Path.Combine(mount, "RosShell"), "*.cpl"))
                        key.DeleteValue(Path.GetFileName(file), false);
            // These official namespace entries point at absent System32\rundll32.exe.
            // Route them to the private settings implementations, keeping native MMC entries.
            using var restore = new RegistryRestoreScope();
            foreach (var (classId, options) in new[]
            {
                ("{6DFD7C5C-2451-11d3-A299-00C04F8EF6AF}", "Options_RunDLL 0"),
                ("{0DF44EAA-FF21-4412-828E-260A8728E7F1}", "Options_RunDLL 1"),
                ("{F2DDFC82-8F12-4CDD-B7DC-D4FE1425AA4D}", "Control_RunDLL \"%SystemRoot%\\System32\\mmsys.cpl\"")
            })
            {
                using var key = restore.Open(Registry.LocalMachine, soft + @"\Classes\CLSID\" + classId + @"\shell\open\command");
                key.SetValue("", "\"%SystemDrive%\\RosShell\\rundll32.exe\" \"%SystemDrive%\\RosShell\\roshell.dll\"," + options, RegistryValueKind.ExpandString);
            }
            // VOS can retain an Internet Options namespace reference without its
            // COM class or applet. Remove only this orphan reference, not a tool.
            const string internetOptions = "{A3DD4F92-658A-410F-84FD-6FBBBEF2FFFE}";
            using (var registration = Registry.LocalMachine.OpenSubKey(soft + @"\Classes\CLSID\" + internetOptions))
                if (registration is null)
                    restore.DeleteLeaf(Registry.LocalMachine, soft + @"\Microsoft\Windows\CurrentVersion\Explorer\ControlPanel\NameSpace\" + internetOptions);
            // Explorer enumerates mmsys.cpl directly. Keep its canonical COM
            // registration, but avoid listing the same Windows Sound app twice.
            if (File.Exists(Path.Combine(windows, "System32", "mmsys.cpl")))
                restore.DeleteLeaf(Registry.LocalMachine, soft + @"\Microsoft\Windows\CurrentVersion\Explorer\ControlPanel\NameSpace\{F2DDFC82-8F12-4CDD-B7DC-D4FE1425AA4D}");
        });
        WithHive("HKU", profile, Path.Combine(mount, "Users", "Default", "NTUSER.DAT"), log, () =>
        {
            var explorer = profile + @"\Software\Microsoft\Windows\CurrentVersion\Explorer";
            // Windows' modern/legacy namespace references are not desktop icons
            // in the classic shell. Keep their registrations and use the normal
            // per-user visibility settings rather than removing OS components.
            string[] hidden = [
                "{1CF1260C-4DD0-4EBB-811F-33C572699FDE}", "{374DE290-123F-4565-9164-39C4925E467B}",
                "{3ADD1653-EB32-4CB0-BBD7-DFA0ABB5ACCA}", "{A0953C92-50DC-43BF-BE83-3742FED03C9C}",
                "{A8CDFF1C-4878-43BE-B5FD-F8091C1C60D0}", "{B4BFCC3A-DB2C-424C-B029-7FE99A87C641}",
                "{F874310E-B6B7-47DC-BC84-B9E6B38F5903}", "{E88865EA-0E1C-4E20-9AA6-EDCD0212C87C}",
                "{031E4825-7B94-4DC3-B131-E946B44C8DD5}", "{871C5380-42A0-1069-A2EA-08002B30309D}",
                "{26EE0668-A00A-44D7-9371-BEB064C98683}", "{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}",
                "{59031A47-3F72-44A7-89C5-5595FE6B30EE}"
            ];
            foreach (var mode in new[] { "ClassicStartMenu", "NewStartPanel" })
            {
                using var key = Registry.Users.CreateSubKey(explorer + @"\HideDesktopIcons\" + mode);
                foreach (var classId in hidden) key.SetValue(classId, 1, RegistryValueKind.DWord);
            }
            using var panel = Registry.Users.CreateSubKey(explorer + @"\Desktop\NameSpace\{21EC2020-3AEA-1069-A2DD-08002B30309D}");
            // Settings for the shell's private theme engine (same values the fork installs
            // under ThemeManager). The native theme service keeps its own ThemeManager key.
            if (File.Exists(Path.Combine(mount, "RosShell", "Themes", "Aero", "aero.msstyles")))
            {
                using var theme = Registry.Users.CreateSubKey(profile + @"\Software\Microsoft\Windows\CurrentVersion\" + ReactOsShellBuilder.ThemeKey);
                theme.SetValue("ThemeActive", "1");
                theme.SetValue("DllName", @"%SystemDrive%\RosShell\Themes\Aero\aero.msstyles", RegistryValueKind.ExpandString);
                theme.SetValue("ColorName", "Normal Color");
                theme.SetValue("SizeName", "Normal Size");
            }
        });
        var system = "FWB_System_" + Guid.NewGuid().ToString("N");
        WithHive("HKLM", system, Path.Combine(windows, "System32", "config", "SYSTEM"), log, () =>
        {
            using var hive = Registry.LocalMachine.OpenSubKey(system, true)!;
            foreach (var set in hive.GetSubKeyNames().Where(n => n.StartsWith("ControlSet", StringComparison.OrdinalIgnoreCase)))
            {
                using var service = hive.OpenSubKey(set + @"\Services\vds", true)
                    ?? throw new InvalidDataException("The official DiskTools Virtual Disk service is missing.");
                // Start the official storage service before storage snap-ins use it.
                service.SetValue("Start", 2, RegistryValueKind.DWord);
                using var events = hive.OpenSubKey(set + @"\Services\EventLog", true)
                    ?? throw new InvalidDataException("The official EventLog service is missing.");
                // Initialize logging in a separate host, outside the temporary
                // MiniNT mode, without retaining a former shared PE RPC server.
                events.SetValue("Start", 3, RegistryValueKind.DWord);
                events.SetValue("Type", 0x10, RegistryValueKind.DWord);
                var eventCommand = events.GetValue("ImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                if (string.IsNullOrWhiteSpace(eventCommand))
                    throw new InvalidDataException("The official EventLog service command is missing.");
                if (eventCommand.Contains("svchost.exe", StringComparison.OrdinalIgnoreCase) &&
                    !System.Text.RegularExpressions.Regex.IsMatch(eventCommand, @"(?:^|\s)-s(?:\s|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    eventCommand += " -s EventLog";
                events.SetValue("ImagePath", eventCommand, RegistryValueKind.ExpandString);
            }
        });
        foreach (var app in new[] { "taskmgr.exe" })
            File.Copy(Path.Combine(reactOsRoot, "system32", app), Path.Combine(mount, "RosShell", app));
        CreateShortcuts(mount);
    }

    private static void CreateShortcuts(string mount)
    {
        // Use the standard Windows/LiberNT Start menu layout and keep every
        // official Validation OS shortcut (Administrative Tools, System Tools).
        var programs = Path.Combine(mount, "ProgramData", "Microsoft", "Windows", "Start Menu", "Programs");
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true)!)!;
        try
        {
            var entries = new List<(string Folder, string Name, string Target, string Arguments)>
            {
                ("Accessories", "Notepad", @"X:\Windows\System32\notepad.exe", ""),
                ("Accessories", "Command Prompt", @"X:\Windows\System32\cmd.exe", ""),
                (@"Accessories\Entertainment", "Volume Control", @"X:\RosShell\sndvol32.exe", "")
            };
            foreach (var (name, file) in new[] {
                ("Device Manager", "devmgmt.msc"), ("Disk Management", "diskmgmt.msc"),
                ("Computer Management", "compmgmt.msc"), ("Services", "services.msc"), ("Event Viewer", "eventvwr.msc") })
                if (File.Exists(Path.Combine(mount, "Windows", "System32", file)))
                    entries.Add(("Administrative Tools", name, @"X:\Windows\System32\mmc.exe", @"X:\Windows\System32\" + file));
            foreach (var (folder, name, target, arguments) in entries)
            {
                var directory = Path.Combine(programs, folder);
                // Validation OS already ships some of these (for example Services as "services.lnk").
                if (Directory.Exists(directory) && Directory.EnumerateFiles(directory, "*.lnk").Any(p =>
                        Path.GetFileNameWithoutExtension(p).Equals(name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                Directory.CreateDirectory(directory);
                dynamic link = shell.CreateShortcut(Path.Combine(directory, name + ".lnk"));
                try { link.TargetPath = target; link.Arguments = arguments; link.WorkingDirectory = Path.GetDirectoryName(target)!; link.IconLocation = target + ",0"; link.Save(); }
                finally { Marshal.FinalReleaseComObject(link); }
            }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    internal static void WithHive(string root, string name, string file, Action<string>? log, Action action)
    {
        var reg = Path.Combine(Environment.SystemDirectory, "reg.exe");
        Run(reg, ["load", root + @"\" + name, file], log);
        try { action(); }
        finally { Run(reg, ["unload", root + @"\" + name], log); }
    }

    private static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
    }

    internal static void Run(string executable, string[] arguments, Action<string>? log, bool allowRebootRequired = false)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Tool did not start: " + executable);
        var gate = new object(); var recent = new Queue<string>();
        async Task Drain(StreamReader reader)
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
                lock (gate) { recent.Enqueue(line); if (recent.Count > 12) recent.Dequeue(); log?.Invoke(line); }
        }
        var stdout = Drain(process.StandardOutput); var stderr = Drain(process.StandardError);
        process.WaitForExit(); Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
        if (process.ExitCode != 0 && !(allowRebootRequired && process.ExitCode == 3010))
            throw new IOException($"{Path.GetFileName(executable)} failed ({process.ExitCode}):\n{string.Join('\n', recent)}");
    }
}
