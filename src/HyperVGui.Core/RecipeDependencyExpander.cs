using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace HyperVGui.Core;

/// <summary>Collects additional signed package dependencies introduced by localized media.</summary>
public static class RecipeDependencyExpander
{
    private sealed record Component(string Stem, XElement Identity);
    private static string Attribute(XElement value, string name, string fallback = "") => (string?)value.Attribute(name) ?? fallback;
    private static string PackagePart(XElement value, string name, bool allowEmpty = false)
    {
        var part = Attribute(value, name);
        if ((!allowEmpty && part.Length == 0) || part is "." or ".." || part.IndexOfAny(['\\', '/', ':', '~', '\0']) >= 0)
            throw new InvalidDataException("Invalid package identity field: " + name);
        return part;
    }
    private static string Key(XElement value, string? language = null, string? version = null) => string.Join("|",
        Attribute(value, "name"), version ?? Attribute(value, "version"), Attribute(value, "processorArchitecture"),
        Attribute(value, "publicKeyToken"), language ?? Attribute(value, "language", "neutral")).ToLowerInvariant();
    private static string ReferenceKey(XElement value)
    {
        var version = Attribute(value, "version");
        if (version.Length == 0 && value.Parent is not null)
        {
            var versions = value.Parent.Elements().Where(x => x.Name.LocalName == "bindingRedirect")
                .Select(x => Attribute(x, "newVersion")).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (versions.Length == 1) version = versions[0];
        }
        return Key(value, version: version);
    }
    private static XElement Identity(XDocument xml, string path) => xml.Root?.Elements().FirstOrDefault(x => x.Name.LocalName == "assemblyIdentity")
        ?? throw new InvalidDataException("Missing assembly identity: " + path);

    public static void Expand(JsonNode recipe, string donor, string target, string decoded, string language,
        IReadOnlySet<string> omittedComponents, Action<string>? progress = null)
    {
        var components = new Dictionary<string, Component>(StringComparer.OrdinalIgnoreCase);
        var omittedReferences = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var japaneseReferences = new Dictionary<string, List<XElement>>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(decoded, "*.xml"))
        {
            var xml = XDocument.Load(path);
            var identity = Identity(xml, path);
            var stem = Path.GetFileNameWithoutExtension(path);
            if (!language.Equals("ja-JP", StringComparison.OrdinalIgnoreCase))
            {
                var references = xml.Descendants().Where(x => x.Name.LocalName == "dependentAssembly")
                    .Where(x => x.Elements().Any(y => y.Name.LocalName == "assemblyIdentity" &&
                        Attribute(y, "language", "neutral").Equals("ja-JP", StringComparison.OrdinalIgnoreCase)))
                    .Select(x => new XElement(x)).ToList();
                if (references.Count > 0) japaneseReferences[stem] = references;
            }
            if (omittedComponents.Count > 0)
            {
                var omitted = xml.Descendants().Where(x => x.Name.LocalName == "dependentAssembly")
                    .SelectMany(x => x.Elements().Where(y => y.Name.LocalName == "assemblyIdentity"))
                    .Select(ReferenceKey).FirstOrDefault(omittedComponents.Contains);
                if (omitted is not null) omittedReferences[stem] = omitted;
            }
            var key = Key(identity);
            if (!components.TryAdd(key, new Component(stem, identity)))
                throw new InvalidDataException("Ambiguous component identity: " + key);
        }
        var packageDir = Path.Combine(donor, "Windows", "servicing", "Packages");
        var targetManifests = Path.Combine(target, "Windows", "WinSxS", "Manifests");
        foreach (var collection in recipe["collections"]!.AsArray())
        {
            var files = collection!["files"]!.AsArray();
            var paths = new HashSet<string>(files.Select(x => x!["path"]!.GetValue<string>()), StringComparer.OrdinalIgnoreCase);
            foreach (var manifest in paths.Where(x => !x.Contains('\\') && x.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase)))
                if (omittedReferences.TryGetValue(Path.GetFileNameWithoutExtension(manifest), out var omitted))
                    throw new InvalidDataException("A retained component references an omitted language component: " + omitted);
            var packageQueue = new Queue<string>(paths.Where(x => !x.Contains('\\') && x.EndsWith(".mum", StringComparison.OrdinalIgnoreCase) && !x.Equals("update.mum", StringComparison.OrdinalIgnoreCase)));
            var componentQueue = new Queue<XElement>();
            foreach (var manifest in paths.Where(x => !x.Contains('\\') && x.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase)))
                if (japaneseReferences.TryGetValue(Path.GetFileNameWithoutExtension(manifest), out var dependencies))
                    foreach (var dependency in dependencies)
                        foreach (var identity in dependency.Elements().Where(x => x.Name.LocalName == "assemblyIdentity")) componentQueue.Enqueue(identity);
            var packageSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var componentSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var addedPackages = 0;
            var addedComponents = 0;

            void Add(string relative, string source)
            {
                if (paths.Contains(relative)) return;
                if (!File.Exists(source)) throw new FileNotFoundException("Missing localized dependency: " + relative, source);
                var bytes = ComponentPayload.Unpack(File.ReadAllBytes(source));
                files.Add(new JsonObject { ["path"] = relative, ["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)) });
                paths.Add(relative);
            }

            while (packageQueue.Count > 0)
            {
                var name = packageQueue.Dequeue();
                if (!packageSeen.Add(name)) continue;
                var source = Path.Combine(packageDir, name);
                if (!File.Exists(source)) throw new FileNotFoundException("Missing package definition: " + name, source);
                var xml = XDocument.Load(source);
                foreach (var identity in xml.Descendants().Where(x => x.Name.LocalName == "assemblyIdentity" && x.Parent?.Name.LocalName is "component" or "driver"))
                    if (omittedComponents.Contains(ReferenceKey(identity)))
                        throw new InvalidDataException("A retained package references an omitted language component: " + ReferenceKey(identity));
                foreach (var identity in xml.Descendants().Where(x => x.Name.LocalName == "update")
                    .SelectMany(x => x.Elements().Where(y => y.Name.LocalName == "package"))
                    .SelectMany(x => x.Elements().Where(y => y.Name.LocalName == "assemblyIdentity")))
                {
                    var packageLanguage = PackagePart(identity, "language", allowEmpty: true);
                    if (packageLanguage.Equals("neutral", StringComparison.OrdinalIgnoreCase)) packageLanguage = "";
                    var child = $"{PackagePart(identity, "name")}~{PackagePart(identity, "publicKeyToken")}~{PackagePart(identity, "processorArchitecture")}~{packageLanguage}~{PackagePart(identity, "version")}.mum";
                    if (paths.Contains(child)) continue;
                    var childSource = Path.Combine(packageDir, child);
                    if (!File.Exists(childSource)) throw new FileNotFoundException("Missing referenced package: " + child, childSource);
                    Add(child, childSource);
                    Add(Path.ChangeExtension(child, ".cat"), Path.ChangeExtension(childSource, ".cat"));
                    addedPackages++;
                    packageQueue.Enqueue(child);
                    var childXml = XDocument.Load(childSource);
                    foreach (var component in childXml.Descendants().Where(x => x.Name.LocalName is "component" or "driver")
                        .SelectMany(x => x.Elements().Where(y => y.Name.LocalName == "assemblyIdentity"))) componentQueue.Enqueue(component);
                }
            }

            while (componentQueue.Count > 0)
            {
                var request = componentQueue.Dequeue();
                var version = Attribute(request, "version");
                if (version.Length == 0 && request.Parent is not null)
                {
                    var versions = request.Parent.Elements().Where(x => x.Name.LocalName == "bindingRedirect")
                        .Select(x => Attribute(x, "newVersion")).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    if (versions.Length == 1) version = versions[0];
                }
                var requestedLanguage = Attribute(request, "language", "neutral");
                var languages = requestedLanguage == "*" ? new[] { "neutral", language, "en-US" }.Distinct(StringComparer.OrdinalIgnoreCase) : [requestedLanguage];
                var matches = languages.Select(x => Key(request, x, version)).Where(components.ContainsKey).Select(x => components[x]).DistinctBy(x => x.Stem, StringComparer.OrdinalIgnoreCase).ToArray();
                if (matches.Length == 0)
                {
                    // The default English resource for the x86 servicing stack is built into
                    // its neutral component on this media; require that exact component on target.
                    if (language.Equals("en-US", StringComparison.OrdinalIgnoreCase) &&
                        Attribute(request, "name").Equals("Microsoft-Windows-ServicingStack.Resources", StringComparison.OrdinalIgnoreCase) &&
                        Attribute(request, "processorArchitecture").Equals("x86", StringComparison.OrdinalIgnoreCase) &&
                        Directory.EnumerateFiles(targetManifests, "x86_microsoft-windows-servicingstack_*_" + version + "_none_*.manifest").Any()) continue;
                    throw new InvalidDataException("Missing referenced component: " + Key(request, requestedLanguage, version));
                }
                foreach (var component in matches)
                {
                    if (!componentSeen.Add(component.Stem) || paths.Contains(component.Stem + ".manifest")) continue;
                    var manifest = Path.Combine(decoded, component.Stem + ".xml");
                    Add(component.Stem + ".manifest", manifest);
                    addedComponents++;
                    var xml = XDocument.Load(manifest);
                    var folder = Path.Combine(donor, "Windows", "WinSxS", component.Stem);
                    foreach (var file in xml.Root!.Elements().Where(x => x.Name.LocalName == "file"))
                    {
                        var fileName = Attribute(file, "name");
                        var sourceName = Attribute(file, "sourceName", fileName);
                        if (fileName.Length == 0 || sourceName.Length == 0 || Path.GetFileName(fileName) != fileName || Path.GetFileName(sourceName) != sourceName)
                            throw new InvalidDataException("Invalid component filename: " + component.Stem);
                        var candidate = new[] { fileName, sourceName }.FirstOrDefault(x => File.Exists(Path.Combine(folder, x)))
                            ?? throw new FileNotFoundException("Missing component payload: " + component.Stem + "/" + fileName);
                        Add(Path.Combine(component.Stem, candidate), Path.Combine(folder, candidate));
                    }
                    foreach (var dependency in xml.Descendants().Where(x => x.Name.LocalName == "dependentAssembly")
                        .SelectMany(x => x.Elements().Where(y => y.Name.LocalName == "assemblyIdentity"))) componentQueue.Enqueue(dependency);
                }
            }
            collection["packages"] = (int)collection["packages"]! + addedPackages;
            collection["components"] = (int)collection["components"]! + addedComponents;
            progress?.Invoke($"{collection["name"]}: 追加パッケージ {addedPackages}、コンポーネント {addedComponents}");
        }
    }
}
