using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace FreeWin.Core;

/// <summary>Applies the components inside a dedicated, file-backed WinPE virtual machine.</summary>
public static class ServicingVm
{
    public static string Apply(SourceResult source, string job, string qemu, string qemuImg, string oscdimg,
        string sevenZip, bool wireless, Action<string>? progress = null)
    {
        var vm = Path.Combine(job, "servicing");
        if (Directory.Exists(vm)) throw new IOException("Servicing directory already exists.");
        Directory.CreateDirectory(vm);
        var input = Path.Combine(vm, "input"); var packages = Path.Combine(vm, "package-media");
        var floppy = Path.Combine(vm, "autostart");
        Directory.CreateDirectory(input); Directory.CreateDirectory(packages); Directory.CreateDirectory(floppy);
        File.WriteAllText(Path.Combine(input, "FREEWIN-SERVICING.TAG"), "Hyper-V GUI servicing\r\n", Encoding.ASCII);
        File.WriteAllText(Path.Combine(packages, "FREEWIN-PACKAGES.TAG"), "Hyper-V GUI packages\r\n", Encoding.ASCII);
        File.WriteAllText(Path.Combine(input, "diskpart.txt"), "select disk 0\r\nconvert mbr\r\ncreate partition primary\r\nformat fs=ntfs quick label=FREEWIN-SCRATCH\r\nassign letter=S\r\nexit\r\n", Encoding.ASCII);
        using (var resource = typeof(ServicingVm).Assembly.GetManifestResourceStream("FreeWin.Core.Resources.ApplyImage.cmd") ?? throw new InvalidDataException("Missing servicing resource."))
        using (var destination = File.Create(Path.Combine(input, "apply.cmd"))) resource.CopyTo(destination);
        var inputLocale = source.Language.Equals("ja-JP", StringComparison.OrdinalIgnoreCase) ? "0411:00000411" : source.Language;
        var unattended = Autounattend.Replace("ja-JP", source.Language, StringComparison.OrdinalIgnoreCase)
            .Replace("0411:00000411", inputLocale, StringComparison.OrdinalIgnoreCase);
        File.WriteAllText(Path.Combine(floppy, "Autounattend.xml"), unattended, new UTF8Encoding(true));
        var names = wireless ? new[] { "shell-neutral", "shell-ja", "wireless-neutral", "wireless-ja" } : new[] { "shell-neutral", "shell-ja" };
        var packageDirectory = Path.Combine(packages, "packages"); Directory.CreateDirectory(packageDirectory);
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var name in names)
        {
            progress?.Invoke("GUI部品を準備中: " + name);
            var collection = Path.Combine(source.Collections, name);
            var metadata = JsonNode.Parse(File.ReadAllText(Path.Combine(collection, "collection.json")))!;
            if (metadata["missing"]!.AsArray().Count != 0 || (bool?)metadata["diagnostic_incomplete"] == true)
                throw new InvalidDataException("Incomplete servicing collection.");
            var root = metadata["root"]!;
            var language = root["language"]!.GetValue<string>() == "neutral" ? "" : root["language"]!.GetValue<string>();
            var identity = $"{root["name"]}~{root["publicKeyToken"]}~{root["processorArchitecture"]}~{language}~{root["version"]}";
            var destination = Path.Combine(packageDirectory, (++index).ToString("D2"));
            CopyDirectory(collection, destination);
            var definition = XDocument.Load(Path.Combine(collection, "update.mum"));
            var delegated = definition.Descendants().Any(x => x.Name.LocalName == "package" && x.Elements().Any(y => y.Name.LocalName == "parent" && (string?)y.Attribute("integrate") == "delegate"));
            if (!delegated)
                foreach (var feature in definition.Descendants().Where(x => x.Name.LocalName == "update" && x.Elements().Any(y => y.Name.LocalName == "selectable")))
                {
                    var featureName = (string?)feature.Attribute("name");
                    if (!string.IsNullOrEmpty(featureName) && !owners.TryAdd(featureName, identity)) throw new InvalidDataException("Ambiguous feature owner.");
                }
        }
        var features = wireless ? new[] { "Server-Shell", "WirelessNetworking" } : new[] { "Server-Shell" };
        File.WriteAllLines(Path.Combine(input, "features.txt"), features.Select(f => owners.TryGetValue(f, out var owner) ? f + "|" + owner : f), Encoding.ASCII);
        var inputDisk = Path.Combine(vm, "input.raw"); var floppyDisk = Path.Combine(vm, "autostart.img");
        var scratch = Path.Combine(vm, "scratch.qcow2"); var packageIso = Path.Combine(vm, "packages.iso");
        CollectionBuilder.Run(qemuImg, ["convert", "-f", "vvfat", "-O", "raw", "fat:" + input.Replace('\\', '/'), inputDisk]);
        CollectionBuilder.Run(qemuImg, ["convert", "-f", "vvfat", "-O", "raw", "fat:floppy:" + floppy.Replace('\\', '/'), floppyDisk]);
        CollectionBuilder.Run(qemuImg, ["create", "-f", "qcow2", scratch, "64G"]);
        CollectionBuilder.Run(oscdimg, ["-u2", "-udfver102", "-h", "-m", "-o", "-lFREEWIN_PKGS", packages, packageIso]);
        progress?.Invoke("仮想マシンでWindowsイメージを作成中");
        var port = ReservePort();
        var start = new ProcessStartInfo(qemu) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-accel", "whpx", "-machine", "pc", "-smp", "2", "-m", "4096", "-cdrom", source.HyperIso,
            "-drive", $"file={scratch.Replace('\\', '/')},format=qcow2,if=ide,index=0",
            "-drive", $"file={inputDisk.Replace('\\', '/')},format=raw,if=ide,index=1",
            "-drive", $"file={floppyDisk.Replace('\\', '/')},format=raw,if=floppy,readonly=on,index=0",
            "-drive", $"file={packageIso.Replace('\\', '/')},media=cdrom,readonly=on,if=ide,index=3",
            "-boot", "order=d", "-nic", "none", "-display", "none", "-monitor", "none", "-qmp", $"tcp:127.0.0.1:{port},server=on,wait=off",
            "-serial", "file:" + Path.Combine(vm, "serial.log"), "-no-reboot" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Cannot start the servicing VM.");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try { SendBootKeys(port, process); }
        catch
        {
            // This is the isolated VM started for this job, with only job-owned virtual disks.
            if (!process.HasExited) { process.Kill(); process.WaitForExit(); }
            throw;
        }
        process.WaitForExit();
        File.WriteAllText(Path.Combine(vm, "qemu.log"), stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
        if (process.ExitCode != 0) throw new IOException($"Servicing VM failed ({process.ExitCode}).");
        var logs = Path.Combine(vm, "logs");
        CollectionBuilder.Run(sevenZip, ["x", inputDisk, "-o" + logs, "servicing.log", "-y", "-bb0", "-bsp0"]);
        var log = File.ReadAllText(Path.Combine(logs, "servicing.log"));
        if (!log.Split('\n').Any(x => x.Trim() == "RESULT=0")) throw new InvalidDataException("Windows image servicing failed. See servicing.log.");
        progress?.Invoke("作成したWindowsイメージを回収中");
        return CaptureExtractor.Extract(scratch, Path.Combine(vm, "capture"), qemuImg, sevenZip, source.Language);
    }

    private static int ReservePort()
    {
        var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start();
        try { return ((IPEndPoint)socket.LocalEndpoint).Port; } finally { socket.Stop(); }
    }

    private static void SendBootKeys(int port, Process process)
    {
        var limit = DateTime.UtcNow.AddSeconds(30);
        TcpClient client;
        while (true)
        {
            client = new TcpClient();
            try { client.Connect(IPAddress.Loopback, port); break; }
            catch (SocketException) { client.Dispose(); if (process.HasExited || DateTime.UtcNow >= limit) throw; Thread.Sleep(250); }
        }
        using var connection = client;
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true) { AutoFlush = true, NewLine = "\n" };
        _ = reader.ReadLine() ?? throw new IOException("Missing QMP greeting.");
        void Command(object command)
        {
            writer.WriteLine(JsonSerializer.Serialize(command));
            while (true)
            {
                var line = reader.ReadLine() ?? throw new IOException("QMP disconnected.");
                using var response = JsonDocument.Parse(line);
                if (response.RootElement.TryGetProperty("error", out var error)) throw new IOException("QMP: " + error);
                if (response.RootElement.TryGetProperty("return", out _)) return;
            }
        }
        Command(new { execute = "qmp_capabilities" });
        for (var i = 0; i < 13; i++)
        {
            var key = i == 12 ? "ret" : "spc";
            Command(new { execute = "send-key", arguments = new { keys = new[] { new { type = "qcode", data = key } } } });
            Thread.Sleep(500);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), false);
    }

    private const string Autounattend = """
        <?xml version="1.0" encoding="utf-8"?>
        <unattend xmlns="urn:schemas-microsoft-com:unattend" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State">
          <settings pass="windowsPE">
            <component name="Microsoft-Windows-International-Core-WinPE" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
              <SetupUILanguage><UILanguage>ja-JP</UILanguage></SetupUILanguage><InputLocale>0411:00000411</InputLocale><SystemLocale>ja-JP</SystemLocale><UILanguage>ja-JP</UILanguage><UserLocale>ja-JP</UserLocale>
            </component>
            <component name="Microsoft-Windows-Setup" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
              <RunSynchronous><RunSynchronousCommand wcm:action="add"><Order>1</Order><Path>cmd.exe /c for %d in (C D E F G H I J K L M N O P Q R T U V W Y Z) do if exist %d:\FREEWIN-SERVICING.TAG call %d:\apply.cmd</Path><Description>Apply Hyper-V GUI</Description></RunSynchronousCommand></RunSynchronous>
            </component>
          </settings>
        </unattend>
        """;
}
