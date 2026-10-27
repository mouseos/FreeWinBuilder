using System.Security.Cryptography;

namespace HyperVGui.Core;

/// <summary>Creates BIOS and UEFI bootable installation media.</summary>
public static class IsoBuilder
{
    public static void Build(string media, string installWim, string stage, string output, string oscdimg)
    {
        if (Directory.Exists(stage) || File.Exists(output)) throw new IOException("Output already exists.");
        using (var tool = File.OpenRead(oscdimg))
            if (!Convert.ToHexString(SHA256.HashData(tool)).Equals("2000160B2C5044691B2F9A0AC308E5207F273D4880A572457AF16D05886BA861", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unexpected OSCDIMG binary.");
        foreach (var file in new[] { "boot/etfsboot.com", "efi/microsoft/boot/efisys.bin", "sources/boot.wim", "setup.exe" })
            if (!File.Exists(Path.Combine(media, file))) throw new FileNotFoundException("Installation media is incomplete.", file);
        Directory.CreateDirectory(stage);
        foreach (var directory in Directory.EnumerateDirectories(media, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(stage, Path.GetRelativePath(media, directory)));
        foreach (var source in Directory.EnumerateFiles(media, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(media, source);
            if (relative.Equals(Path.Combine("sources", "install.wim"), StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(source, Path.Combine(stage, relative), false);
        }
        File.Copy(installWim, Path.Combine(stage, "sources", "install.wim"), false);
        var bios = Path.GetFullPath(Path.Combine(stage, "boot", "etfsboot.com"));
        var uefi = Path.GetFullPath(Path.Combine(stage, "efi", "microsoft", "boot", "efisys.bin"));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        CollectionBuilder.Run(oscdimg, ["-m", "-o", "-u2", "-udfver102", "-lHVGUI2019",
            $"-bootdata:2#p0,e,b{bios}#pEF,e,b{uefi}", Path.GetFullPath(stage), Path.GetFullPath(output)]);
        if (!File.Exists(output)) throw new IOException("ISO creation did not produce an output file.");
    }
}
