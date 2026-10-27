using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HyperVGui.Core;

/// <summary>Maps the reference recipe to the matching language components in a donor image.</summary>
public static class RecipeLocalizer
{
    private sealed record Identity(string name, string version, string architecture, string token, string language, bool isLanguagePackMarker);
    private static readonly HashSet<string> OptionalLanguagePackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft-Windows-Server-Features-Package0510", "Microsoft-Windows-Server-Features-Package0511",
        "Microsoft-Windows-Server-Features-Package0710", "Microsoft-Windows-Server-Features-Package0910",
        "Microsoft-Windows-Server-Features-Package091120", "Microsoft-Windows-Server-Features-Package0911",
        "Microsoft-Windows-Server-Features-Package0912", "Microsoft-Windows-Server-Features-Package09"
    };
    private static readonly HashSet<string> OptionalLanguageMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "microsoft-windows-server-features-deployment05100-Deployment", "microsoft-windows-server-features-deployment05110-Deployment",
        "microsoft-windows-server-features-deployment07100-Deployment", "microsoft-windows-server-features-deployment090-Deployment",
        "microsoft-windows-server-features-deployment09100-Deployment", "microsoft-windows-server-features-deployment09110-Deployment",
        "microsoft-windows-server-features-deployment09120-Deployment", "microsoft-windows-server-features-deployment0911200-Deployment"
    };

    private static string Key(string name, string version, string architecture, string token) =>
        string.Join("|", name, version, architecture, token).ToLowerInvariant();
    private static string IdentityKey(Identity identity, string? language = null) =>
        string.Join("|", identity.name, identity.version, identity.architecture, identity.token, language ?? identity.language).ToLowerInvariant();

    public static void Localize(string recipePath, string donor, string target, string decoded, string language, Action<string>? progress = null)
    {
        if (language.Equals("ja-JP", StringComparison.OrdinalIgnoreCase)) return;
        language = CultureInfo.GetCultureInfo(language).Name;
        using var resource = typeof(RecipeLocalizer).Assembly.GetManifestResourceStream("HyperVGui.Core.Resources.server2019-reference-identities.json")
            ?? throw new InvalidDataException("Missing reference component identities.");
        var reference = JsonSerializer.Deserialize<Dictionary<string, Identity>>(resource)
            ?? throw new InvalidDataException("Invalid reference component identities.");
        var localized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(decoded, "*.xml"))
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            if (!stem.Contains("_" + language + "_", StringComparison.OrdinalIgnoreCase)) continue;
            var root = XDocument.Load(path).Root ?? throw new InvalidDataException("Invalid component manifest.");
            var identity = root.Elements().FirstOrDefault(x => x.Name.LocalName == "assemblyIdentity")
                ?? throw new InvalidDataException("Missing component identity: " + stem);
            if (!string.Equals((string?)identity.Attribute("language"), language, StringComparison.OrdinalIgnoreCase)) continue;
            var key = Key((string?)identity.Attribute("name") ?? "", (string?)identity.Attribute("version") ?? "",
                (string?)identity.Attribute("processorArchitecture") ?? "", (string?)identity.Attribute("publicKeyToken") ?? "");
            if (!localized.TryAdd(key, stem)) throw new InvalidDataException("Ambiguous language component identity: " + key);
        }
        var recipe = JsonNode.Parse(File.ReadAllText(recipePath)) ?? throw new InvalidDataException("Invalid component recipe.");
        var missingMarkers = 0;
        var omittedComponents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unavailable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // A reference recipe may contain fallback languages absent from another media.
        // Remove an entire unavailable component only after its exact identity is checked
        // against the retained dependency graph below.
        foreach (var collection in recipe["collections"]!.AsArray())
        foreach (var entry in collection!["files"]!.AsArray())
        {
            var path = entry!["path"]!.GetValue<string>();
            var separator = path.IndexOfAny(['\\', '/']);
            var head = separator < 0 ? path : path[..separator];
            var stem = head.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase) ? head[..^9] : head;
            if (!reference.TryGetValue(stem, out var identity) || identity.language.Equals("ja-JP", StringComparison.OrdinalIgnoreCase)) continue;
            var found = separator < 0 && path.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase)
                ? File.Exists(Path.Combine(decoded, stem + ".xml"))
                : new[] { donor, target }.Any(x => File.Exists(Path.Combine(x, "Windows", "WinSxS", path)));
            if (!found) unavailable.Add(stem);
        }
        foreach (var collection in recipe["collections"]!.AsArray())
        {
            var root = collection!["root"]!;
            var translatedRoot = string.Equals((string?)root["language"], "ja-JP", StringComparison.OrdinalIgnoreCase);
            if (translatedRoot) root["language"] = language;
            var package = $"{root["name"]}~{root["publicKeyToken"]}~{root["processorArchitecture"]}~{(translatedRoot ? language : "")}~{root["version"]}";
            var result = new JsonArray();
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in collection["files"]!.AsArray())
            {
                var path = entry!["path"]!.GetValue<string>();
                var original = path;
                var translated = false;
                var separator = path.IndexOfAny(['\\', '/']);
                var head = separator < 0 ? path : path[..separator];
                var stem = head.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase) ? head[..^9] : head;
                if (unavailable.Contains(stem) && reference.TryGetValue(stem, out var unavailableIdentity))
                {
                    omittedComponents.Add(IdentityKey(unavailableIdentity));
                    missingMarkers++;
                    continue;
                }
                if (reference.TryGetValue(stem, out var identity) && identity.language.Equals("ja-JP", StringComparison.OrdinalIgnoreCase))
                {
                    var key = Key(identity.name, identity.version, identity.architecture, identity.token);
                    if (!localized.TryGetValue(key, out var mapped))
                    {
                        if (identity.isLanguagePackMarker && OptionalLanguageMarkers.Contains(identity.name)) { omittedComponents.Add(IdentityKey(identity, language)); missingMarkers++; continue; }
                        throw new InvalidDataException($"Missing {language} component: {identity.name} ({identity.version}).");
                    }
                    path = separator < 0 ? mapped + ".manifest" : mapped + "\\" + Regex.Replace(path[(separator + 1)..], "ja-JP", language, RegexOptions.IgnoreCase);
                    translated = true;
                }
                else if (path.Contains("~ja-JP~", StringComparison.OrdinalIgnoreCase))
                {
                    path = Regex.Replace(path, "~ja-JP~", "~" + language + "~", RegexOptions.IgnoreCase);
                    var source = Path.Combine(donor, "Windows", "servicing", "Packages", path);
                    var packageName = path.Split('~')[0];
                    if (!File.Exists(source) && OptionalLanguagePackages.Contains(packageName)) { missingMarkers++; continue; }
                    translated = true;
                }
                else if (translatedRoot && (path.Equals("update.mum", StringComparison.OrdinalIgnoreCase) || path.Equals("update.cat", StringComparison.OrdinalIgnoreCase)))
                    translated = true;

                var hash = entry["sha256"]!.GetValue<string>();
                if (translated)
                {
                    string source;
                    if (separator < 0 && path.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                        source = Path.Combine(decoded, Path.GetFileNameWithoutExtension(path) + ".xml");
                    else if (!path.Contains('\\') && !path.Contains('/'))
                    {
                        var file = path.Equals("update.mum", StringComparison.OrdinalIgnoreCase) || path.Equals("update.cat", StringComparison.OrdinalIgnoreCase)
                            ? package + Path.GetExtension(path) : path;
                        source = Path.Combine(donor, "Windows", "servicing", "Packages", file);
                    }
                    else
                        source = new[] { donor, target }.Select(x => Path.Combine(x, "Windows", "WinSxS", path)).FirstOrDefault(File.Exists)
                            ?? throw new FileNotFoundException($"Missing {language} component payload for {original}.", path);
                    if (!File.Exists(source)) throw new FileNotFoundException($"Missing {language} package for {original}.", source);
                    hash = Convert.ToHexString(SHA256.HashData(ComponentPayload.Unpack(File.ReadAllBytes(source))));
                }
                if (seen.TryGetValue(path, out var existing))
                {
                    if (!existing.Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Conflicting component payload: " + path);
                    continue;
                }
                seen.Add(path, hash);
                result.Add(new JsonObject { ["path"] = path, ["sha256"] = hash });
            }
            collection["files"] = result;
        }
        RecipeDependencyExpander.Expand(recipe, donor, target, decoded, language, omittedComponents, progress);
        progress?.Invoke($"{language} 部品の対応付けが完了しました（媒体に存在しない言語パック候補 {missingMarkers} 件を除外）。");
        File.WriteAllText(recipePath, recipe.ToJsonString());
    }
}
