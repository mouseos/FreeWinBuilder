using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace FreeWin.Core;

/// <summary>Restores the installer edition flag on a copy of a captured WIM.</summary>
public static class InstallImage
{
    [DllImport("wimgapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr WIMCreateFile(string path, uint access, uint creation, uint flags, uint compression, IntPtr result);
    [DllImport("wimgapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WIMSetTemporaryPath(IntPtr wim, string path);
    [DllImport("wimgapi.dll", SetLastError = true)] private static extern IntPtr WIMLoadImage(IntPtr wim, uint index);
    [DllImport("wimgapi.dll", SetLastError = true)] private static extern bool WIMCloseHandle(IntPtr handle);
    [DllImport("wimgapi.dll", SetLastError = true)] private static extern bool WIMGetImageInformation(IntPtr image, out IntPtr buffer, out uint size);
    [DllImport("wimgapi.dll", SetLastError = true)] private static extern bool WIMSetImageInformation(IntPtr image, byte[] buffer, uint size);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);

    private static void Check(bool success) { if (!success) throw new Win32Exception(Marshal.GetLastWin32Error()); }

    private static T WithImage<T>(string path, bool write, Func<IntPtr, T> operation)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "freewin-wim-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var wim = IntPtr.Zero;
        var image = IntPtr.Zero;
        try
        {
            wim = WIMCreateFile(Path.GetFullPath(path), write ? 0x40000000u : 0x80000000u, 3, 0, 0, IntPtr.Zero);
            if (wim == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            Check(WIMSetTemporaryPath(wim, temporary));
            image = WIMLoadImage(wim, 1);
            if (image == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            return operation(image);
        }
        finally
        {
            if (image != IntPtr.Zero) WIMCloseHandle(image);
            if (wim != IntPtr.Zero) WIMCloseHandle(wim);
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }
    }

    private static XElement ReadImage(string path) => WithImage(path, false, image => {
        Check(WIMGetImageInformation(image, out var data, out var size));
        try
        {
            var bytes = new byte[checked((int)size)];
            Marshal.Copy(data, bytes, 0, bytes.Length);
            return XElement.Parse(Encoding.Unicode.GetString(bytes).TrimStart('\ufeff').TrimEnd('\0'));
        }
        finally { LocalFree(data); }
    });

    private static void Validate(XElement image)
    {
        if (image.Name != "IMAGE" || (string?)image.Attribute("INDEX") != "1" ||
            (string?)image.Element("WINDOWS")?.Element("EDITIONID") != "ServerHyper" ||
            (string?)image.Element("WINDOWS")?.Element("ARCH") != "9" ||
            (string?)image.Element("WINDOWS")?.Element("VERSION")?.Element("BUILD") != "17763" ||
            !image.Element("WINDOWS")!.Element("LANGUAGES")!.Elements("LANGUAGE").Any())
            throw new InvalidDataException("Unexpected captured Windows image identity.");
    }

    public static void Prepare(string captured, string reference, string output)
    {
        if (File.Exists(output)) throw new IOException("Output image already exists.");
        var source = ReadImage(captured); var referenceImage = ReadImage(reference);
        Validate(source); Validate(referenceImage);
        var sourceLanguages = source.Element("WINDOWS")!.Element("LANGUAGES")!.Elements("LANGUAGE").Select(x => x.Value).OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        var referenceLanguages = referenceImage.Element("WINDOWS")!.Element("LANGUAGES")!.Elements("LANGUAGE").Select(x => x.Value).OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        if (!sourceLanguages.SequenceEqual(referenceLanguages, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Captured image languages differ from the source ISO.");
        var before = new XElement(source);
        if ((string?)referenceImage.Element("FLAGS") != "ServerHyperCore") throw new InvalidDataException("Unexpected reference installer flag.");
        var flags = source.Element("FLAGS");
        if (flags is not null && flags.Value != "ServerHyperCore") throw new InvalidDataException("Captured image has conflicting installer flags.");
        File.Copy(captured, output, false);
        if (flags is null) source.Add(new XElement("FLAGS", "ServerHyperCore"));
        WithImage(output, true, image => {
            var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(source.ToString(SaveOptions.DisableFormatting) + "\0")).ToArray();
            Check(WIMSetImageInformation(image, bytes, checked((uint)bytes.Length)));
            return true;
        });
        var actual = ReadImage(output); Validate(actual);
        if ((string?)actual.Element("FLAGS") != "ServerHyperCore") throw new InvalidDataException("Installer flag was not saved.");
        foreach (var image in new[] { before, actual })
            foreach (var tag in new[] { "FLAGS", "LASTMODIFICATIONTIME" }) image.Elements(tag).Remove();
        if (!XNode.DeepEquals(before, actual)) throw new InvalidDataException("Unexpected captured image metadata change.");
    }
}
