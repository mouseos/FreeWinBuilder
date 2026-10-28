using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FreeWin.Core;

/// <summary>Reconstructs pinned servicing collections from the user-supplied images.</summary>
public static class CollectionBuilder
{
    private sealed record Pending(string Collection, string Path, string Hash, string Member);

    public static void Build(string recipePath, string donor, string target, string decoded,
        string baselineWim, int donorIndex, string language, string output, string sevenZip, Action<string>? progress = null)
    {
        if (Directory.Exists(output)) throw new IOException("Collection output already exists.");
        var image = WimMetadata.Read(baselineWim).Single(x => x.Index == donorIndex);
        if (image.Build != "17763" || image.Architecture != "9" || !image.Languages.Contains(language, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"A matching {language} Server 2019 x64 image is required for this recipe.");
        var recipe = JsonNode.Parse(File.ReadAllText(recipePath)) ?? throw new InvalidDataException("Missing recipe.");
        if ((int?)recipe["format"] != 1) throw new InvalidDataException("Unsupported recipe version.");
        Directory.CreateDirectory(output);
        var pending = new List<Pending>();
        foreach (var collection in recipe["collections"]!.AsArray())
        {
            var name = collection!["name"]!.GetValue<string>();
            if (SafeRelative(name) != name || name.Contains(Path.DirectorySeparatorChar)) throw new InvalidDataException("Invalid collection name.");
            var root = collection["root"]!;
            var packageLanguage = root["language"]!.GetValue<string>() == "neutral" ? "" : root["language"]!.GetValue<string>();
            var package = $"{root["name"]}~{root["publicKeyToken"]}~{root["processorArchitecture"]}~{packageLanguage}~{root["version"]}";
            var directory = Path.Combine(output, name);
            Directory.CreateDirectory(directory);
            var checkedCount = 0;
            foreach (var item in collection["files"]!.AsArray())
            {
                var path = SafeRelative(item!["path"]!.GetValue<string>());
                var hash = item["sha256"]!.GetValue<string>();
                if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid recipe hash.");
                var fileName = Path.GetFileName(path);
                var extension = Path.GetExtension(path);
                IEnumerable<string> candidates;
                if (path == fileName)
                {
                    var original = fileName.Equals("update.mum", StringComparison.OrdinalIgnoreCase) || fileName.Equals("update.cat", StringComparison.OrdinalIgnoreCase) ? package + extension : fileName;
                    candidates = extension.Equals(".manifest", StringComparison.OrdinalIgnoreCase)
                        ? [Path.Combine(decoded, Path.GetFileNameWithoutExtension(fileName) + ".xml")]
                        : new[] { donor, target }.Select(source => Path.Combine(source, "Windows", "servicing", "Packages", original));
                }
                else candidates = new[] { donor, target }.Select(source => Path.Combine(source, "Windows", "WinSxS", path));
                var destination = Path.Combine(directory, path);
                if (!candidates.Any(candidate => TryCopy(candidate, destination, hash)))
                    pending.Add(new Pending(name, path, hash, Path.Combine(donorIndex.ToString(), "Windows", "WinSxS", path)));
                if (++checkedCount % 1000 == 0) progress?.Invoke($"{name}: {checkedCount}件を照合中");
            }
            progress?.Invoke($"{name}: {pending.Count} baseline files pending");
        }

        var extracted = Path.Combine(output, "baseline-extract");
        if (pending.Count > 0)
        {
            var list = Path.Combine(output, "baseline-members.txt");
            File.WriteAllLines(list, pending.Select(x => x.Member).Distinct(StringComparer.OrdinalIgnoreCase), new UTF8Encoding(false));
            Run(sevenZip, ["x", baselineWim, "-o" + extracted, "-i@" + list, "-scsUTF-8", "-y", "-bsp0", "-bso0"]);
            foreach (var item in pending)
                if (!TryCopy(Path.Combine(extracted, item.Member), Path.Combine(output, item.Collection, item.Path), item.Hash))
                    throw new InvalidDataException($"必要なコンポーネントが入力ISOに見つからないか、内容が一致しません: {item.Collection}/{item.Path}。別の同一言語・同一系列のISOをお試しください。");
        }
        foreach (var collection in recipe["collections"]!.AsArray())
        {
            var name = collection!["name"]!.GetValue<string>();
            var manifest = new JsonObject {
                ["root"] = collection["root"]!.DeepClone(), ["packages"] = collection["packages"]!.DeepClone(),
                ["components"] = collection["components"]!.DeepClone(), ["files"] = collection["files"]!.DeepClone(),
                ["missing"] = new JsonArray(), ["diagnostic_incomplete"] = false
            };
            File.WriteAllText(Path.Combine(output, name, "collection.json"), manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static string SafeRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':')) throw new InvalidDataException("Invalid recipe path.");
        var parts = path.Replace('/', '\\').Split('\\');
        if (parts.Any(p => p is "" or "." or "..")) throw new InvalidDataException("Invalid recipe path.");
        return Path.Combine(parts);
    }

    private static bool TryCopy(string source, string destination, string expected)
    {
        if (!File.Exists(source)) return false;
        byte[] contents;
        try { contents = ComponentPayload.Unpack(File.ReadAllBytes(source)); }
        catch (InvalidDataException) { return false; }
        if (!Convert.ToHexString(SHA256.HashData(contents)).Equals(expected, StringComparison.OrdinalIgnoreCase)) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
        file.Write(contents);
        return true;
    }

    internal static void Run(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Unable to start external tool.");
        process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException($"External tool failed: {Path.GetFileName(executable)} ({process.ExitCode}).");
    }
}
