using System.Text;
using System.Text.Json;
using HyperVGui.Core;

if (args.Length != 1) { Console.Error.WriteLine("Expected a job configuration file."); return 2; }
var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(args[0])) ?? throw new InvalidDataException("Invalid job settings.");
var job = Path.GetFullPath(settings.Job);
Directory.CreateDirectory(job);
using var log = new StreamWriter(new FileStream(Path.Combine(job, "build.log"), FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
void Status(string phase, string message)
{
    if (settings.English) message = EnglishMessage.Translate(message);
    log.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}");
    var state = JsonSerializer.Serialize(new { phase, message });
    var path = Path.Combine(job, "build.json");
    File.WriteAllText(path + ".tmp", state); File.Move(path + ".tmp", path, true);
}
try
{
    var recipe = Path.Combine(job, "recipe.json");
    using (var resource = typeof(SourceBuilder).Assembly.GetManifestResourceStream("HyperVGui.Core.Resources.server2019-ja-17763.737-gui.json") ?? throw new InvalidDataException("Missing component recipe."))
    using (var dest = File.Create(recipe)) resource.CopyTo(dest);
    Status("preparing", "入力ISOから部品を準備中");
    var sources = SourceBuilder.Prepare(settings.Hyper, settings.Gui, Path.Combine(job, "sources"), recipe, settings.SevenZip, message => Status("preparing", message));
    Status("servicing", "Windowsイメージを作成中");
    var capture = ServicingVm.Apply(sources, job, settings.Qemu, settings.QemuImg, settings.Oscdimg, settings.SevenZip, settings.Wireless, message => Status("servicing", message));
    var install = Path.Combine(job, "install.wim");
    Status("exporting", "インストーラーを準備中");
    InstallImage.Prepare(capture, Path.Combine(sources.InstallerMedia, "sources", "install.wim"), install);
    var iso = Path.Combine(job, "HyperV2019-GUI.iso");
    Status("exporting", "ISOを作成中");
    IsoBuilder.Build(sources.InstallerMedia, install, Path.Combine(job, "iso-stage"), iso, settings.Oscdimg);
    Status("complete", "ISOの作成が完了しました。");
    return 0;
}
catch (Exception ex)
{
    log.WriteLine(settings.English ? EnglishMessage.Translate(ex.ToString()) : ex.ToString());
    Status("failed", "作成に失敗しました。ログを確認してください。");
    return 1;
}

internal sealed record Settings(string Job, string Hyper, string Gui, string Qemu, string QemuImg, string Oscdimg, string SevenZip, bool Wireless, bool English = false);

internal static class EnglishMessage
{
    private static readonly (string Japanese, string English)[] Phrases =
    [
        ("必要なコンポーネントが入力ISOに見つからないか、内容が一致しません: ", "A required component is missing or does not match: "),
        ("。別の同一言語・同一系列のISOをお試しください。", ". Try another ISO of the same language and release."),
        ("入力ISOから部品を準備中", "Preparing components from the source ISOs"),
        ("仮想マシンでWindowsイメージを作成中", "Building the Windows image in a virtual machine"),
        ("作成したWindowsイメージを回収中", "Extracting the Windows image"),
        ("コンポーネント定義を復号中", "Decoding component manifests"),
        ("必要なツールがありません", "Required tools are missing"),
        ("入力ISOが見つかりません: ", "Source ISO not found: "),
        ("ISOを確認中: ", "Checking ISO: "), ("ISOを展開中: ", "Extracting ISO: "),
        ("イメージ確認: ", "Image: "), ("GUI部品を準備中: ", "Preparing GUI components: "),
        ("Windowsイメージを作成中", "Building the Windows image"),
        ("インストーラーを準備中", "Preparing the installer"), ("ISOを作成中", "Creating the ISO"),
        ("ISOの作成が完了しました。", "The ISO is ready."),
        ("作成に失敗しました。ログを確認してください。", "The build failed. Check the log."),
        ("推奨ビルドは", "The recommended build is "), ("です。入力版の部品を照合します: ", ". Checking components: "),
        ("追加パッケージ ", "additional packages: "), ("、コンポーネント ", ", components: "),
        (" 部品の対応付けが完了しました（媒体に存在しない言語パック候補 ", " component mapping complete (missing optional language packs: "),
        (" 件を除外）。", " excluded)."), ("件を照合中", " items checked"), ("件", " items")
    ];

    public static string Translate(string message)
    {
        foreach (var (japanese, english) in Phrases) message = message.Replace(japanese, english, StringComparison.Ordinal);
        return message;
    }
}
