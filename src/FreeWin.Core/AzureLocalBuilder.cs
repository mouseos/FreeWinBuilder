using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace FreeWin.Core;

/// <summary>
/// Adds the Windows Server Desktop Experience packages to an Azure Local (Azure Stack HCI)
/// image with DISM and writes a Windows PE based installation ISO.
/// </summary>
public static class AzureLocalBuilder
{
    // Desktop Experience feature packages of Windows Server Datacenter/Standard that Azure
    // Local lacks, and the Windows shell experience (taskbar, Start menu, File Explorer).
    private static readonly string[] DesktopPackages =
    [
        "Microsoft-Windows-EditionPack-Server-Package", "Microsoft-Windows-EditionPack-Server-WOW64-Package",
        "Microsoft-Windows-EditionPack-Server-Optional-Package", "Microsoft-Windows-EditionPack-Server-Optional-WOW64-Package",
        "Microsoft-Windows-EditionPack-Server-Full-Package", "Microsoft-Windows-EditionPack-Server-Full-WOW64-Package",
        "Microsoft-Windows-Server-DesktopUX-Package", "Microsoft-Windows-Server-DesktopUX-WOW64-Package",
        "Microsoft-Windows-EditionPack-Server-Roles-Full-Package", "Microsoft-Windows-EditionPack-Server-Roles-Full-WOW64-Package",
        "Microsoft-Windows-Client-Features-SharedWithServer-Package", "Microsoft-Windows-Client-Features-SharedWithServer-WOW64-Package",
        "Microsoft-Windows-Desktop-Required-ClientOnly-SharedWithServer-Package", "Microsoft-Windows-Desktop-Required-ClientOnly-SharedWithServer-WOW64-Package",
        "UserExperience-Desktop-Package"
    ];

    public const string IsoName = "FreeWin-AzureLocal.iso";

    public static string Build(string azureLocalSource, string serverSource, string languagePack, string sevenZip,
        string winPeMedia, string bootFiles, string oscdimg, string output, Action<string>? log = null)
    {
        using (var identity = WindowsIdentity.GetCurrent())
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                throw new InvalidOperationException("Azure Local servicing requires administrator rights.");
        var winPe = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(winPeMedia))!, "en-us", "winpe.wim");
        foreach (var path in new[] { azureLocalSource, serverSource, languagePack, sevenZip, oscdimg, winPe,
            Path.Combine(winPeMedia, "bootmgr"), Path.Combine(bootFiles, "etfsboot.com"), Path.Combine(bootFiles, "efisys_noprompt.bin") })
            if (!File.Exists(path)) throw new FileNotFoundException("Required build input missing.", path);
        output = Path.GetFullPath(output);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Build output already exists.");
        Directory.CreateDirectory(output);
        var dism = Path.Combine(Environment.SystemDirectory, "dism.exe");
        void Run(string tool, string[] args) => ValidationOsMedia.Run(tool, args, log, true);

        var azureImage = ResolveImage(azureLocalSource, Path.Combine(output, "azure-local-source"), sevenZip, Run, log);
        var serverImage = ResolveImage(serverSource, Path.Combine(output, "server-source"), sevenZip, Run, log);
        var azure = WimMetadata.Read(azureImage).FirstOrDefault(i => i.Edition.Equals("ServerAzureStackHCICor", StringComparison.OrdinalIgnoreCase) && i.Architecture == "9")
            ?? throw new InvalidDataException("The Azure Local source has no x64 ServerAzureStackHCICor image.");
        var server = WimMetadata.Read(serverImage)
            .Where(i => i.Architecture == "9" && i.InstallationType.Equals("Server", StringComparison.OrdinalIgnoreCase)
                && (i.Edition.Equals("ServerDatacenter", StringComparison.OrdinalIgnoreCase) || i.Edition.Equals("ServerStandard", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(i => i.Edition.Equals("ServerDatacenter", StringComparison.OrdinalIgnoreCase) ? 0 : 1).FirstOrDefault()
            ?? throw new InvalidDataException("The Windows Server source has no x64 Desktop Experience (Datacenter or Standard) image.");
        log?.Invoke($"Azure Local {azure.Build}.{azure.Revision} (index {azure.Index}, {string.Join(",", azure.Languages)}); " +
            $"Windows Server {server.Edition} {server.Build}.{server.Revision} (index {server.Index}, {string.Join(",", server.Languages)}).");
        if (azure.Build != server.Build)
            throw new InvalidDataException($"Azure Local build {azure.Build} and Windows Server build {server.Build} differ. Use media of the same build.");
        if (azure.Revision != server.Revision)
            log?.Invoke($"Warning: the revisions differ ({azure.Revision} and {server.Revision}). The tested combination uses the same revision.");

        var work = Path.Combine(output, "work");
        var scratch = Path.Combine(work, "scratch");
        var targetMount = Path.Combine(work, "mount-azure");
        var serverMount = Path.Combine(work, "mount-server");
        var source = Path.Combine(work, "packages");
        foreach (var d in new[] { scratch, targetMount, serverMount, source }) Directory.CreateDirectory(d);
        var targetWim = Path.Combine(work, "azure-local.wim");
        var serverWim = Path.Combine(work, "server.wim");
        log?.Invoke("Exporting the Azure Local and Windows Server images.");
        Run(dism, ["/Export-Image", "/SourceImageFile:" + azureImage, "/SourceIndex:" + azure.Index, "/DestinationImageFile:" + targetWim, "/Compress:fast", "/LogPath:" + Path.Combine(work, "export-azure.log")]);
        Run(dism, ["/Export-Image", "/SourceImageFile:" + serverImage, "/SourceIndex:" + server.Index, "/DestinationImageFile:" + serverWim, "/Compress:fast", "/LogPath:" + Path.Combine(work, "export-server.log")]);

        bool targetMounted = false, serverMounted = false;
        try
        {
            Run(dism, ["/Mount-Image", "/ImageFile:" + targetWim, "/Index:1", "/MountDir:" + targetMount, "/LogPath:" + Path.Combine(work, "mount-azure.log")]);
            targetMounted = true;
            Run(dism, ["/Mount-Image", "/ImageFile:" + serverWim, "/Index:1", "/MountDir:" + serverMount, "/ReadOnly", "/LogPath:" + Path.Combine(work, "mount-server.log")]);
            serverMounted = true;

            var packages = PreparePackageSource(targetMount, serverMount, source, log);
            var languageSource = Path.Combine(work, "language-pack");
            var languagePackage = PrepareLanguagePack(languagePack, languageSource, source, server.Languages, dism, Run, log);
            var language = Path.GetFileName(languagePackage).Split('~')[3];
            // The display language's desktop resources come from the Windows Server image,
            // its core resources from the language pack.
            var features = LanguageFeatures(serverMount, targetMount, source, language);

            // A print component's offline installer opens Control\Print, which Azure Local omits.
            var system = "FWB_AzSys_" + Guid.NewGuid().ToString("N");
            ValidationOsMedia.WithHive("HKLM", system, Path.Combine(targetMount, "Windows", "System32", "config", "SYSTEM"), log, () =>
            {
                using var hive = Registry.LocalMachine.OpenSubKey(system, true)!;
                foreach (var set in hive.GetSubKeyNames().Where(n => n.StartsWith("ControlSet", StringComparison.OrdinalIgnoreCase)))
                    hive.CreateSubKey(set + @"\Control\Print")!.Dispose();
            });

            if (packages.Count > 0)
            {
                log?.Invoke("Adding Windows Server Desktop Experience packages: " + string.Join(", ", packages.Select(p => Path.GetFileName(p).Split('~')[0])));
                Run(dism, ["/Image:" + targetMount, "/Add-Package", "/ScratchDir:" + scratch, "/LogPath:" + Path.Combine(work, "add-desktop.log"),
                    .. packages.Select(p => "/PackagePath:" + p)]);
            }
            // Adding the Server language pack again installs the language resources of the
            // newly added desktop features. It resolves every child package from one folder.
            log?.Invoke("Adding the language pack: " + language);
            Run(dism, ["/Image:" + targetMount, "/Add-Package", "/PackagePath:" + languagePackage, "/ScratchDir:" + scratch,
                "/LogPath:" + Path.Combine(work, "add-language.log")]);
            if (features.Count > 0)
            {
                log?.Invoke("Adding language features (fonts, input method, handwriting, speech): " + string.Join(", ", features.Select(p => Path.GetFileName(p).Split('~')[0])));
                Run(dism, ["/Image:" + targetMount, "/Add-Package", "/ScratchDir:" + scratch, "/LogPath:" + Path.Combine(work, "add-language-features.log"),
                    .. features.Select(p => "/PackagePath:" + p)]);
            }
            if (!language.Equals(azure.DefaultLanguage, StringComparison.OrdinalIgnoreCase))
            {
                log?.Invoke("Setting the default language to " + language);
                Run(dism, ["/Image:" + targetMount, "/Set-AllIntl:" + language, "/LogPath:" + Path.Combine(work, "set-language.log")]);
            }

            var software = "FWB_AzSw_" + Guid.NewGuid().ToString("N");
            ValidationOsMedia.WithHive("HKLM", software, Path.Combine(targetMount, "Windows", "System32", "config", "SOFTWARE"), log, () =>
            {
                // Azure Local enables this policy; with it the console never starts a user logon.
                using (var policy = Registry.LocalMachine.OpenSubKey(software + @"\Microsoft\Windows\CurrentVersion\Policies\System", true))
                    if (policy?.GetValue("EnterpriseDeviceAuthOnly") is int value && value != 0)
                        policy.SetValue("EnterpriseDeviceAuthOnly", 0, RegistryValueKind.DWord);
                using var shells = Registry.LocalMachine.OpenSubKey(software + @"\Microsoft\Windows NT\CurrentVersion\Winlogon\AlternateShells\AvailableShells");
                if (!string.Equals(shells?.GetValue("60000") as string, "explorer.exe", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The Desktop Experience packages did not register explorer.exe as the shell.");
            });
            foreach (var file in new[] { @"Windows\explorer.exe", @"Windows\SystemApps\MicrosoftWindows.Client.CBS_cw5n1h2txyewy\AppxManifest.xml" })
                if (!File.Exists(Path.Combine(targetMount, file))) throw new InvalidDataException("The desktop shell is incomplete; missing " + file);

            Run(dism, ["/Unmount-Image", "/MountDir:" + serverMount, "/Discard", "/LogPath:" + Path.Combine(work, "unmount-server.log")]);
            serverMounted = false;
            Run(dism, ["/Unmount-Image", "/MountDir:" + targetMount, "/Commit", "/LogPath:" + Path.Combine(work, "commit-azure.log")]);
            targetMounted = false;
        }
        finally
        {
            if (serverMounted) try { Run(dism, ["/Unmount-Image", "/MountDir:" + serverMount, "/Discard"]); } catch (IOException) { }
            if (targetMounted) log?.Invoke("Servicing failed; the mounted image is preserved for diagnosis: " + targetMount);
        }

        var media = Path.Combine(output, "media");
        Directory.CreateDirectory(Path.Combine(media, "sources"));
        log?.Invoke("Compressing the installation image.");
        Run(dism, ["/Export-Image", "/SourceImageFile:" + targetWim, "/SourceIndex:1", "/DestinationImageFile:" + Path.Combine(media, "sources", "install.wim"),
            "/DestinationName:Azure Local with Desktop Experience", "/Compress:max", "/CheckIntegrity", "/LogPath:" + Path.Combine(work, "export-final.log")]);
        PrepareInstaller(winPeMedia, winPe, media, dism, Path.Combine(work, "mount-winpe"), log);

        var iso = Path.Combine(output, IsoName);
        var bios = Path.GetFullPath(Path.Combine(bootFiles, "etfsboot.com"));
        var efi = Path.GetFullPath(Path.Combine(bootFiles, "efisys_noprompt.bin"));
        log?.Invoke("Creating the ISO.");
        Run(oscdimg, ["-m", "-o", "-u2", "-udfver102", "-lFREEWINAZL", $"-bootdata:2#p0,e,b{bios}#pEF,e,b{efi}", media, iso]);
        if (!File.Exists(iso)) throw new IOException("ISO output was not created.");
        foreach (var folder in new[] { work, media, Path.Combine(output, "azure-local-source"), Path.Combine(output, "server-source") })
            DeleteTree(folder, log);
        return iso;
    }

    private static void DeleteTree(string folder, Action<string>? log)
    {
        if (!Directory.Exists(folder)) return;
        try
        {
            // Files copied from the component store keep their read-only attribute.
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(folder, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke("Could not remove the temporary folder " + folder + ": " + ex.Message);
        }
    }

    private static string ResolveImage(string input, string extractTo, string sevenZip, Action<string, string[]> run, Action<string>? log)
    {
        input = Path.GetFullPath(input);
        var extension = Path.GetExtension(input).ToLowerInvariant();
        if (extension is ".wim" or ".esd") return input;
        if (extension != ".iso") throw new InvalidDataException("Use an ISO, WIM or ESD file: " + input);
        log?.Invoke("Extracting the installation image from " + Path.GetFileName(input));
        run(sevenZip, ["x", "-y", "-bd", "-o" + extractTo, input, "sources/install.wim", "sources/install.esd"]);
        foreach (var name in new[] { "install.wim", "install.esd" })
        {
            var path = Path.Combine(extractTo, "sources", name);
            if (File.Exists(path)) return path;
        }
        throw new InvalidDataException("The ISO has no sources\\install.wim or install.esd: " + input);
    }

    /// <summary>Language feature packages (fonts, input method, OCR, speech) that the
    /// Windows Server image has for the display language and Azure Local lacks.</summary>
    private static List<string> LanguageFeatures(string server, string target, string source, string language)
    {
        var installed = Path.Combine(target, "Windows", "servicing", "Packages");
        return Directory.EnumerateFiles(Path.Combine(server, "Windows", "servicing", "Packages"), "Microsoft-Windows-LanguageFeatures-*-Package~31bf3856ad364e35~amd64~~*.mum")
            .Select(Path.GetFileName)
            .Where(name => name!.Contains("-" + language + "-Package~", StringComparison.OrdinalIgnoreCase) && !File.Exists(Path.Combine(installed, name)))
            .Select(name => Path.Combine(source, name!)).Where(File.Exists).ToList();
    }

    /// <summary>
    /// Expands the Windows Server language pack (UUP ESD) and merges it into the
    /// package folder without replacing staged files.
    /// </summary>
    private static string PrepareLanguagePack(string input, string extractTo, string source, string[] languages, string dism,
        Action<string, string[]> run, Action<string>? log)
    {
        input = Path.GetFullPath(input);
        Directory.CreateDirectory(extractTo);
        var extension = Path.GetExtension(input).ToLowerInvariant();
        log?.Invoke("Expanding the Windows Server language pack " + Path.GetFileName(input));
        if (extension is not (".esd" or ".wim")) throw new InvalidDataException("Use the Windows Server language pack ESD file: " + input);
        run(dism, ["/Apply-Image", "/ImageFile:" + input, "/Index:1", "/ApplyDir:" + extractTo]);
        var package = Directory.GetFiles(extractTo, "Microsoft-Windows-Server-LanguagePack-Package~31bf3856ad364e35~amd64~*.mum")
            .FirstOrDefault(p => languages.Contains(Path.GetFileName(p).Split('~')[3], StringComparer.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("The file is not a Windows Server language pack in a language of the Azure Local image: " + input);
        foreach (var file in Directory.EnumerateFiles(extractTo, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(source, Path.GetRelativePath(extractTo, file));
            if (File.Exists(destination)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        return Path.Combine(source, Path.GetFileName(package));
    }

    /// <summary>
    /// Stages the Desktop Experience packages of the Windows Server image as expanded
    /// packages: the signed package definitions and catalogs, plus every component
    /// manifest and payload that Azure Local does not contain. DISM installs them.
    /// </summary>
    private static List<string> PreparePackageSource(string target, string server, string source, Action<string>? log)
    {
        var targetPackages = Path.Combine(target, "Windows", "servicing", "Packages");
        var serverPackages = Path.Combine(server, "Windows", "servicing", "Packages");
        var installed = new HashSet<string>(Directory.EnumerateFiles(targetPackages).Select(p => Path.GetFileName(p)), StringComparer.OrdinalIgnoreCase);
        int definitions = 0, components = 0;
        foreach (var file in Directory.EnumerateFiles(serverPackages))
        {
            var name = Path.GetFileName(file);
            if (!name.EndsWith(".mum", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".cat", StringComparison.OrdinalIgnoreCase)) continue;
            // The Server language pack is added again to install the desktop language resources.
            if (installed.Contains(name) && !name.StartsWith("Microsoft-Windows-Server-LanguagePack-Package~", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(file, Path.Combine(source, name), true); definitions++;
        }
        // The language pack also lists Azure Local's own (installed) packages; they must
        // resolve from the same folder. Their components are already installed.
        foreach (var file in Directory.EnumerateFiles(targetPackages))
        {
            var destination = Path.Combine(source, Path.GetFileName(file));
            if ((file.EndsWith(".mum", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".cat", StringComparison.OrdinalIgnoreCase)) && !File.Exists(destination))
                File.Copy(file, destination);
        }
        var targetManifests = new HashSet<string>(Directory.EnumerateFiles(Path.Combine(target, "Windows", "WinSxS", "Manifests")).Select(p => Path.GetFileName(p)), StringComparer.OrdinalIgnoreCase);
        var sxs = Path.Combine(server, "Windows", "WinSxS");
        foreach (var manifest in Directory.EnumerateFiles(Path.Combine(sxs, "Manifests"), "*.manifest"))
        {
            var name = Path.GetFileName(manifest);
            if (targetManifests.Contains(name)) continue;
            File.Copy(manifest, Path.Combine(source, name), true);
            var payload = Path.Combine(sxs, Path.GetFileNameWithoutExtension(name));
            if (Directory.Exists(payload)) CopyTree(payload, Path.Combine(source, Path.GetFileNameWithoutExtension(name)));
            components++;
        }
        log?.Invoke($"Package source: {definitions} package definitions and {components} components from the Windows Server image.");
        var result = new List<string>();
        foreach (var package in DesktopPackages)
        {
            if (Directory.EnumerateFiles(targetPackages, package + "~31bf3856ad364e35~amd64~~*.mum").Any()) continue;
            var mum = Directory.GetFiles(source, package + "~31bf3856ad364e35~amd64~~*.mum").SingleOrDefault()
                ?? throw new InvalidDataException("The Windows Server image lacks " + package + ".");
            result.Add(mum);
        }
        return result;
    }

    private static void PrepareInstaller(string winPeMedia, string winPe, string media, string dism, string mount, Action<string>? log)
    {
        log?.Invoke("Preparing the Windows PE installer.");
        foreach (var directory in Directory.EnumerateDirectories(winPeMedia, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(media, Path.GetRelativePath(winPeMedia, directory)));
        foreach (var file in Directory.EnumerateFiles(winPeMedia, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(media, Path.GetRelativePath(winPeMedia, file)));
        var boot = Path.Combine(media, "sources", "boot.wim");
        File.Copy(winPe, boot);
        File.SetAttributes(boot, FileAttributes.Normal);
        Directory.CreateDirectory(mount);
        ValidationOsMedia.Run(dism, ["/Mount-Image", "/ImageFile:" + boot, "/Index:1", "/MountDir:" + mount], log);
        var committed = false;
        try
        {
            File.WriteAllText(Path.Combine(mount, "Windows", "System32", "startnet.cmd"),
                "@echo off\r\nwpeinit\r\nfor %%d in (C D E F G H I J K L M N O P Q R S T U V W Y Z) do if exist %%d:\\FreeWin\\install.cmd (call %%d:\\FreeWin\\install.cmd %%d: & goto :eof)\r\necho The installation media was not found.\r\n",
                Encoding.ASCII);
            ValidationOsMedia.Run(dism, ["/Unmount-Image", "/MountDir:" + mount, "/Commit"], log);
            committed = true;
        }
        finally { if (!committed) ValidationOsMedia.Run(dism, ["/Unmount-Image", "/MountDir:" + mount, "/Discard"], log); }
        var folder = Path.Combine(media, "FreeWin");
        Directory.CreateDirectory(folder);
        using (var script = typeof(AzureLocalBuilder).Assembly.GetManifestResourceStream("FreeWin.Core.Resources.InstallAzureLocal.cmd")
            ?? throw new InvalidOperationException("Missing installer script."))
        using (var destination = File.Create(Path.Combine(folder, "install.cmd")))
            script.CopyTo(destination);
        File.WriteAllText(Path.Combine(folder, "list.txt"), "list disk\r\n", Encoding.ASCII);
        File.WriteAllText(Path.Combine(folder, "uefi.txt"),
            "select disk 0\r\nclean\r\nconvert gpt\r\ncreate partition efi size=260\r\nformat quick fs=fat32 label=System\r\nassign letter=S\r\n" +
            "create partition msr size=16\r\ncreate partition primary\r\nformat quick fs=ntfs label=Windows\r\nassign letter=W\r\n", Encoding.ASCII);
        File.WriteAllText(Path.Combine(folder, "bios.txt"),
            "select disk 0\r\nclean\r\nconvert mbr\r\ncreate partition primary size=500\r\nformat quick fs=ntfs label=System\r\nassign letter=S\r\nactive\r\n" +
            "create partition primary\r\nformat quick fs=ntfs label=Windows\r\nassign letter=W\r\n", Encoding.ASCII);
    }

    private static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), true);
    }
}
