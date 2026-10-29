using System.Text.RegularExpressions;

namespace FreeWin.Core;

public sealed record ValidationOsPackage(string Source, string RelativeDestination);

/// <summary>Resolves the input media's official GenImage feature definitions for RAM boot.</summary>
public static class ValidationOsPackages
{
    public static IReadOnlyList<ValidationOsPackage> Plan(string isoRoot)
    {
        var configRoot = Path.GetFullPath(Path.Combine(isoRoot, "GenImage", "Configs"));
        var cabRoot = Path.GetFullPath(Path.Combine(isoRoot, "cabs"));
        var definitions = Directory.GetFiles(configRoot, "*.pkg", SearchOption.AllDirectories);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packages = new HashSet<(string Name, string Folder)>();
        string ResolveInclude(string from, string include)
        {
            foreach (var directory in new[] { Path.GetDirectoryName(from)!, configRoot, Path.Combine(configRoot, "Common") })
            {
                var path = Path.GetFullPath(Path.Combine(directory, include));
                if (!path.StartsWith(configRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Feature include escapes the media configuration directory.");
                if (File.Exists(path)) return path;
            }
            var matches = definitions.Where(p => Path.GetFileName(p).Equals(Path.GetFileName(include), StringComparison.OrdinalIgnoreCase)).ToArray();
            return matches.Length == 1 ? matches[0] : throw new InvalidDataException("Missing or ambiguous feature include: " + include);
        }
        void Read(string file)
        {
            if (!seen.Add(file)) return;
            foreach (var raw in File.ReadLines(file))
            {
                var line = raw.Split('#', 2)[0].Trim();
                if (line.Length == 0) continue;
                var include = Regex.Match(line, @"^\+include\s+(\S+)$", RegexOptions.IgnoreCase);
                if (include.Success) { Read(ResolveInclude(file, include.Groups[1].Value)); continue; }
                var entry = Regex.Match(line, @"^([\w.-]+)(?:\s+\[([\w.-]+)\])?$");
                if (!entry.Success) throw new InvalidDataException($"Unsupported feature definition in {file}: {line}");
                var name = Regex.Replace(entry.Groups[1].Value, @"\.cab$", "", RegexOptions.IgnoreCase);
                // GenImage excludes Setup when BootFromX is selected.
                if (name.Equals("Microsoft-WinVOS-Setup-Package", StringComparison.OrdinalIgnoreCase)) continue;
                var folder = entry.Groups[2].Success ? entry.Groups[2].Value : Path.GetFileName(Path.GetDirectoryName(file))!;
                packages.Add((name, folder));
            }
        }
        foreach (var file in definitions.Order(StringComparer.OrdinalIgnoreCase))
            if (!Path.GetRelativePath(configRoot, file).Split(Path.DirectorySeparatorChar).Any(p => p.Equals("Test", StringComparison.OrdinalIgnoreCase))) Read(file);

        // Some official optional components have no predefined .pkg (for example
        // DeveloperTools). Include the desktop-capable catalog as well as features.
        // Test harnesses, reduced runlevels and disk-only OOBE are separate scenarios.
        foreach (var category in new[] { "Common", "Experimental", "Extra", "Legacy" })
        {
            var neutral = Path.Combine(cabRoot, category, "neutral");
            if (!Directory.Exists(neutral)) continue;
            foreach (var cab in Directory.GetFiles(neutral, "*.cab"))
            {
                var name = Path.GetFileNameWithoutExtension(cab);
                if (name.Contains("-WOW64-Package", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Microsoft-WinVOS-Setup-Package", StringComparison.OrdinalIgnoreCase)) continue;
                if (!packages.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) packages.Add((name, category));
            }
        }

        var categories = Directory.GetDirectories(cabRoot).Where(p => !Path.GetFileName(p).Equals("Kernel-Packages", StringComparison.OrdinalIgnoreCase)).ToArray();
        var languages = categories.Append(cabRoot).SelectMany(Directory.GetDirectories)
            .Select(Path.GetFileName).OfType<string>().Where(p => p.Equals("neutral", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(p, @"^[a-z]{2,3}-[a-z0-9-]+$", RegexOptions.IgnoreCase))
            .Append("neutral").Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var result = new Dictionary<string, ValidationOsPackage>(StringComparer.OrdinalIgnoreCase);
        void Add(string source, string relative)
        {
            if (result.TryGetValue(relative, out var existing) && !existing.Source.Equals(source, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Ambiguous package destination: " + relative);
            result[relative] = new(source, relative);
        }
        foreach (var package in packages.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            foreach (var language in languages)
            {
                var nativeName = package.Name + ".cab";
                var variants = new[] { nativeName, nativeName.Replace("-Package", "-WOW64-Package", StringComparison.OrdinalIgnoreCase) }.Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var variant in variants)
                {
                    var locations = new[] { Path.Combine(cabRoot, package.Folder), cabRoot }.Concat(categories).Distinct(StringComparer.OrdinalIgnoreCase);
                    var source = locations.Select(p => Path.Combine(p, language, variant)).FirstOrDefault(File.Exists);
                    if (source is not null) Add(source, Path.Combine(language, variant));
                    else if (language.Equals("neutral", StringComparison.OrdinalIgnoreCase) && variant == nativeName)
                        throw new FileNotFoundException("Required neutral package is missing: " + nativeName);
                    var kernelName = variant.Replace("Microsoft-OneCore-", "Microsoft-WinVOS-", StringComparison.OrdinalIgnoreCase)
                        .Replace("Microsoft-WinVOS-", "Microsoft-WinVOS-Kernel-", StringComparison.OrdinalIgnoreCase);
                    var kernel = Path.Combine(cabRoot, "Kernel-Packages", language, kernelName);
                    if (File.Exists(kernel)) Add(kernel, Path.Combine("Kernel-Packages", language, kernelName));
                }
            }
        if (!packages.Any(p => p.Name.Equals("Microsoft-WinVOS-Power-Package", StringComparison.OrdinalIgnoreCase)))
            foreach (var language in languages)
            {
                var relative = Path.Combine("Kernel-Packages", language, "Microsoft-Windows-Kernel-Package-PDC.cab");
                var source = Path.Combine(cabRoot, relative);
                if (File.Exists(source)) Add(source, relative);
            }
        if (result.Count == 0) throw new InvalidDataException("No Validation OS feature packages found.");
        return result.Values.OrderBy(p => p.RelativeDestination, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
