using System.Text.Json;

namespace HyperVGui.Core;

public sealed record SourceResult(string InstallerMedia, string Collections, string HyperIso, string Language = "ja-JP");

/// <summary>Prepares component collections from original installation media.</summary>
public static class SourceBuilder
{
    private sealed record Input(string Role, string Path, string RecommendedRevision);

    public static SourceResult Prepare(string hyperIso, string guiIso, string output,
        string recipe, string sevenZip, Action<string>? progress = null)
    {
        Input[] inputs = [
            new("hyper", hyperIso, "737"),
            new("gui", guiIso, "1")
        ];
        if (Directory.Exists(output)) throw new IOException("Source output already exists.");
        foreach (var input in inputs)
        {
            progress?.Invoke($"ISOを確認中: {input.Role}");
            if (!File.Exists(input.Path)) throw new FileNotFoundException($"入力ISOが見つかりません: {input.Role}", input.Path);
        }
        Directory.CreateDirectory(output);
        var roots = new Dictionary<string, string>();
        var donorIndex = 0;
        var language = "";
        foreach (var input in inputs)
        {
            progress?.Invoke($"ISOを展開中: {input.Role}");
            var media = Path.Combine(output, input.Role + "-media");
            var args = new List<string> { "x", input.Path, "-o" + media };
            if (input.Role != "hyper") args.Add("sources/install.wim");
            args.AddRange(["-y", "-bb0", "-bsp0"]);
            CollectionBuilder.Run(sevenZip, args);
            var wim = Path.Combine(media, "sources", "install.wim");
            var images = WimMetadata.Read(wim);
            var candidates = images.Where(x => x.Build == "17763" && x.Architecture == "9");
            var image = input.Role == "hyper"
                ? candidates.SingleOrDefault(x => x.Index == 1 && x.Edition.Equals("ServerHyper", StringComparison.OrdinalIgnoreCase))
                : candidates.Where(x => x.Languages.Contains(language, StringComparer.OrdinalIgnoreCase) && x.InstallationType.Equals("Server", StringComparison.OrdinalIgnoreCase) &&
                    (x.Edition.StartsWith("ServerStandard", StringComparison.OrdinalIgnoreCase) || x.Edition.StartsWith("ServerDatacenter", StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(x => x.Edition.StartsWith("ServerStandard", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(x => x.Index).FirstOrDefault();
            if (image is null) throw new InvalidDataException($"Compatible Server 2019 x64 image not found: {input.Role}; language={language}.");
            if (input.Role == "hyper")
            {
                language = string.IsNullOrWhiteSpace(image.DefaultLanguage) ? image.Languages.FirstOrDefault() ?? "" : image.DefaultLanguage;
                if (string.IsNullOrWhiteSpace(language) || !image.Languages.Contains(language, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException("The Hyper-V image has no supported default language.");
            }
            if (input.Role == "gui") donorIndex = image.Index;
            progress?.Invoke($"イメージ確認: {input.Role} 17763.{image.Revision}, index {image.Index}");
            if (!image.Revision.Equals(input.RecommendedRevision, StringComparison.OrdinalIgnoreCase))
                progress?.Invoke($"推奨ビルドは17763.{input.RecommendedRevision}です。入力版の部品を照合します: {input.Role}");
            var destination = Path.Combine(output, input.Role + "-files");
            var prefix = input.Role == "hyper" ? "" : image.Index + "\\";
            CollectionBuilder.Run(sevenZip, ["x", wim, "-o" + destination,
                prefix + "Windows\\WinSxS\\*", prefix + "Windows\\servicing\\Packages\\*", "-y", "-bb0", "-bsp0"]);
            roots[input.Role] = input.Role == "hyper" ? destination : Path.Combine(destination, image.Index.ToString());
        }
        var winsxs = Path.Combine(roots["gui"], "Windows", "WinSxS");
        var wcp = Directory.GetDirectories(winsxs, "amd64_microsoft-windows-servicingstack_*")
            .OrderByDescending(x => Version.TryParse(Path.GetFileName(x).Split('_').ElementAtOrDefault(3), out var version) ? version : new Version(0, 0))
            .Select(x => Path.Combine(x, "wcp.dll")).FirstOrDefault(File.Exists)
            ?? throw new InvalidDataException("The donor image does not contain a servicing stack decoder.");
        var dictionary = ManifestDecoder.ReadDictionary(wcp);
        var decoded = Path.Combine(output, "decoded-manifests");
        Directory.CreateDirectory(decoded);
        var manifests = Directory.EnumerateFiles(Path.Combine(winsxs, "Manifests"), "*.manifest");
        progress?.Invoke("コンポーネント定義を復号中");
        var decodedCount = 0;
        foreach (var manifest in manifests)
        {
            File.WriteAllBytes(Path.Combine(decoded, Path.GetFileNameWithoutExtension(manifest) + ".xml"), ManifestDecoder.Decode(dictionary, File.ReadAllBytes(manifest)));
            if (++decodedCount % 1000 == 0) progress?.Invoke($"コンポーネント定義を復号中: {decodedCount}件");
        }
        var collections = Path.Combine(output, "collections");
        RecipeLocalizer.Localize(recipe, roots["gui"], roots["hyper"], decoded, language, progress);
        CollectionBuilder.Build(recipe, roots["gui"], roots["hyper"], decoded,
            Path.Combine(output, "gui-media", "sources", "install.wim"), donorIndex, language, collections, sevenZip, progress);
        var result = new SourceResult(Path.GetFullPath(Path.Combine(output, "hyper-media")), Path.GetFullPath(collections), Path.GetFullPath(hyperIso), language);
        File.WriteAllText(Path.Combine(output, "sources.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return result;
    }
}
