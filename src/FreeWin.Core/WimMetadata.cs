using System.Buffers.Binary;
using System.Text;
using System.Xml.Linq;

namespace FreeWin.Core;

public sealed record WimImage(int Index, string Edition, string Architecture, string Build, string Revision, string[] Languages, string InstallationType, string DefaultLanguage);

/// <summary>Reads the uncompressed image metadata resource from a Windows image.</summary>
public static class WimMetadata
{
    public static IReadOnlyList<WimImage> Read(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[208];
        stream.ReadExactly(header);
        if (!header[..8].SequenceEqual("MSWIM\0\0\0"u8)) throw new InvalidDataException("Not a WIM image.");
        var size = BinaryPrimitives.ReadUInt64LittleEndian(header[72..]) & 0x00ffffffffffffff;
        var offset = BinaryPrimitives.ReadUInt64LittleEndian(header[80..]);
        var original = BinaryPrimitives.ReadUInt64LittleEndian(header[88..]);
        if (size != original || size == 0 || size > 16 * 1024 * 1024 || offset > (ulong)stream.Length || size > (ulong)stream.Length - offset)
            throw new InvalidDataException("Invalid WIM metadata resource.");
        stream.Position = checked((long)offset);
        var data = new byte[checked((int)size)];
        stream.ReadExactly(data);
        var xml = XDocument.Parse(Encoding.Unicode.GetString(data).TrimStart('\ufeff').TrimEnd('\0'));
        return xml.Root!.Elements("IMAGE").Select(image => {
            var windows = image.Element("WINDOWS") ?? throw new InvalidDataException("Missing Windows metadata.");
            var version = windows.Element("VERSION") ?? throw new InvalidDataException("Missing version metadata.");
            return new WimImage((int?)image.Attribute("INDEX") ?? throw new InvalidDataException("Missing image index."),
                (string?)windows.Element("EDITIONID") ?? "", (string?)windows.Element("ARCH") ?? "",
                (string?)version.Element("BUILD") ?? "", (string?)version.Element("SPBUILD") ?? "",
                windows.Element("LANGUAGES")?.Elements("LANGUAGE").Select(e => e.Value).ToArray() ?? [],
                (string?)windows.Element("INSTALLATIONTYPE") ?? "",
                (string?)windows.Element("LANGUAGES")?.Element("DEFAULT") ?? "");
        }).ToArray();
    }
}
