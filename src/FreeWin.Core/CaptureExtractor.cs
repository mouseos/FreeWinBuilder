using System.Buffers.Binary;

namespace FreeWin.Core;

/// <summary>Extracts the captured Windows image from the builder's stopped virtual disk.</summary>
public static class CaptureExtractor
{
    public static string Extract(string scratch, string output, string qemuImg, string sevenZip, string language)
    {
        if (Directory.Exists(output)) throw new IOException("Capture output already exists.");
        if (Path.GetFullPath(scratch).Contains(',')) throw new ArgumentException("The virtual disk path cannot contain a comma.");
        Directory.CreateDirectory(output);
        var mbr = Path.Combine(output, "mbr.bin");
        CollectionBuilder.Run(qemuImg, ["dd", "-f", "qcow2", "if=" + Path.GetFullPath(scratch), "of=" + mbr, "bs=512", "count=1"]);
        var sector = File.ReadAllBytes(mbr);
        if (sector.Length != 512 || sector[510] != 0x55 || sector[511] != 0xaa) throw new InvalidDataException("Invalid virtual disk partition table.");
        var partitions = new List<(byte Type, uint Start, uint Sectors)>();
        for (var index = 0; index < 4; index++)
        {
            var entry = sector.AsSpan(446 + 16 * index, 16);
            var count = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            if (count > 0) partitions.Add((entry[4], BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]), count));
        }
        if (partitions.Count != 1 || partitions[0].Type != 7 || partitions[0].Start != 2048 ||
            ((ulong)partitions[0].Start + partitions[0].Sectors) * 512 > 64UL * 1024 * 1024 * 1024)
            throw new InvalidDataException("Unexpected virtual disk partition layout.");
        var extracted = Path.Combine(output, "files");
        CollectionBuilder.Run(sevenZip, ["x", scratch, "-o" + extracted, "patched.wim", "-y", "-bb0", "-bsp0"]);
        var image = Path.Combine(extracted, "patched.wim");
        var metadata = WimMetadata.Read(image).Single();
        if (metadata.Edition != "ServerHyper" || !metadata.Languages.Contains(language, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Captured image identity does not match the target.");
        return image;
    }
}
