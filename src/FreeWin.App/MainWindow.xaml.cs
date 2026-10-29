using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Windows.Storage.Pickers;

namespace FreeWin.App;

public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
    private readonly string root;
    private readonly DispatcherQueueTimer timer;
    private Process? process;
    private string? jobPath;
    private int currentStep;
    private bool changingStep;
    private readonly bool japanese = (Environment.GetEnvironmentVariable("FREEWIN_LANGUAGE") ?? CultureInfo.CurrentUICulture.Name).StartsWith("ja", StringComparison.OrdinalIgnoreCase);
    private string T(string ja, string en) => japanese ? ja : en;
    private bool IsValidationOs => TargetChoice.SelectedIndex == 0;

    public MainWindow()
    {
        InitializeComponent();
        if (!japanese) UiEnglish.Apply(Root);
        ApplyTarget();
        root = FindRoot();
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(handle));
        var dpi = GetDpiForWindow(handle);
        var scale = dpi == 0 ? 1 : dpi / 96.0;
        var preferredWidth = (int)Math.Round(1050 * scale);
        var preferredHeight = (int)Math.Round(760 * scale);
        var area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Nearest)?.WorkArea;
        if (area is { } work)
        {
            var margin = (int)Math.Round(32 * scale);
            var width = Math.Min(preferredWidth, Math.Max(1, work.Width - margin));
            var height = Math.Min(preferredHeight, Math.Max(1, work.Height - margin));
            appWindow.MoveAndResize(new RectInt32(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height));
        }
        else appWindow.Resize(new SizeInt32(preferredWidth, preferredHeight));
        DestinationPath.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(2);
        timer.Tick += (_, _) => RefreshStatus();
        ShowStep(0);
    }

    private void OnTargetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceHint is null) return;
        HyperPath.Text = ""; DonorPath.Text = "";
        ApplyTarget();
    }

    private void ApplyTarget()
    {
        TargetChoice.Header = T("対象OS", "Target OS");
        WelcomeDescription.Text = IsValidationOs
            ? T("Validation OSのReactOSデスクトップ付き起動ISOを作成します。", "Create a bootable Validation OS ISO with the ReactOS desktop.")
            : T("Hyper-V Server 2019 のGUI付きISOを作成します。", "Create a Hyper-V Server 2019 ISO with a desktop.");
        RequirementsDescription.Text = IsValidationOs
            ? T("Validation OS x64 ISO、ReactOS x64 ISO、Windows ADKのDeployment ToolsとWinPEアドオン、7-Zipが必要です。", "You need a Validation OS x64 ISO, a ReactOS x64 ISO, Windows ADK Deployment Tools, the WinPE add-on, and 7-Zip.")
            : T("Hyper-V Serverと同じ言語のWindows Server 2019 ISOを用意してください。", "Use Hyper-V Server and Windows Server 2019 ISOs in the same language.");
        SourceHint.Text = IsValidationOs
            ? T("Validation OSの公式機能を追加し、ReactOSのシェルを組み込みます。", "Add official Validation OS features and integrate the ReactOS shell.")
            : T("同じ言語のISOを2つ指定してください。", "Select two ISOs in the same language.");
        FirstIsoLabel.Text = IsValidationOs ? "Microsoft Validation OS x64" : T("Hyper-V Server 2019（推奨: 17763.737）", "Hyper-V Server 2019 (recommended: 17763.737)");
        SecondIsoLabel.Text = IsValidationOs ? T("ReactOS x64（動作確認済み: 0.4.17-dev-1268-gbba112f）", "ReactOS x64 (tested: 0.4.17-dev-1268-gbba112f)") : T("Windows Server 2019 Desktop Experience（推奨: 17763.1）", "Windows Server 2019 Desktop Experience (recommended: 17763.1)");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(HyperPath, FirstIsoLabel.Text);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(DonorPath, SecondIsoLabel.Text);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(FirstIsoBrowseButton,
            T(FirstIsoLabel.Text + "のISOを選択", "Select " + FirstIsoLabel.Text + " ISO"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SecondIsoBrowseButton,
            T(SecondIsoLabel.Text + "のISOを選択", "Select " + SecondIsoLabel.Text + " ISO"));
        WirelessOption.Visibility = IsValidationOs ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string FindRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "scripts", "Install-Dependencies.ps1"))) return dir.FullName;
        return Environment.CurrentDirectory;
    }

    private void ShowStep(int step)
    {
        currentStep = step;
        var panels = new[] { WelcomePanel, SourcePanel, SettingsPanel, ReviewPanel, BuildPanel };
        for (var i = 0; i < panels.Length; i++) panels[i].Visibility = i == step ? Visibility.Visible : Visibility.Collapsed;
        changingStep = true;
        StepRail.SelectedIndex = step;
        changingStep = false;
        BackButton.IsEnabled = step > 0 && step < 4;
        NextButton.Content = step == 3 ? T("ISOを作成", "Build ISO") : T("次へ", "Next");
        NextButton.Visibility = step == 4 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnStepChanged(object sender, SelectionChangedEventArgs e)
    {
        if (changingStep) return;
        var selected = StepRail.SelectedIndex;
        if (selected < 0 || selected > currentStep || currentStep == 4) { changingStep = true; StepRail.SelectedIndex = currentStep; changingStep = false; return; }
        ShowStep(selected);
    }

    private void OnBack(object sender, RoutedEventArgs e) { if (currentStep > 0 && currentStep < 4) ShowStep(currentStep - 1); }

    private async void OnNext(object sender, RoutedEventArgs e)
    {
        if (currentStep == 1 && new[] { HyperPath.Text, DonorPath.Text }.Any(p => !File.Exists(p) || !p.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)))
        { await ShowError(T("2つのISOファイルを選択してください。", "Select both ISO files.")); return; }
        if (currentStep == 2 && !Directory.Exists(DestinationPath.Text))
        { await ShowError(T("保存先フォルダーが見つかりません。", "The output folder does not exist.")); return; }
        if (currentStep == 2)
            ReviewText.Text = $"{FirstIsoLabel.Text}\n{HyperPath.Text}\n\n{SecondIsoLabel.Text}\n{DonorPath.Text}\n\n{T("保存先", "Output folder")}\n{DestinationPath.Text}" + (IsValidationOs ? "" : $"\n\n{T("Wi-Fi機能", "Wi-Fi")}: {(WirelessOption.IsChecked == true ? T("追加する", "Include") : T("追加しない", "Do not include"))}");
        if (currentStep == 3) { await StartBuild(); return; }
        ShowStep(currentStep + 1);
    }

    private async void OnChooseIso(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".iso");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        switch ((string)((Button)sender).Tag) { case "hyper": HyperPath.Text = file.Path; break; case "donor": DonorPath.Text = file.Path; break; }
    }

    private async void OnChooseFolder(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker(); picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync(); if (folder is not null) DestinationPath.Text = folder.Path;
    }

    private async Task StartBuild()
    {
        try
        {
            var worker = Path.Combine(AppContext.BaseDirectory, "worker", "FreeWin.Worker.exe");
            var qemu = Path.Combine(root, ".work", "qemu", "qemu-system-x86_64.exe");
            var qemuImg = Path.Combine(root, ".work", "qemu", "qemu-img.exe");
            var oscdimg = Path.Combine(root, ".work", "ms-tools", "oscdimg.exe");
            var adk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "Assessment and Deployment Kit");
            var bootFiles = Path.Combine(adk, "Deployment Tools", "amd64", "Oscdimg");
            var winPeMedia = Path.Combine(adk, "Windows Preinstallation Environment", "amd64", "Media");
            if (File.Exists(Path.Combine(bootFiles, "oscdimg.exe"))) oscdimg = Path.Combine(bootFiles, "oscdimg.exe");
            var sevenZip = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe");
            var required = new[] { worker, oscdimg, sevenZip }.Concat(IsValidationOs
                ? new[] { Path.Combine(winPeMedia, "bootmgr"), Path.Combine(bootFiles, "etfsboot.com"), Path.Combine(bootFiles, "efisys_noprompt.bin") }
                : new[] { qemu, qemuImg });
            if (required.Any(p => !File.Exists(p))) throw new InvalidOperationException(T("必要なツールがありません。READMEの準備手順を実行してください。", "Required tools are missing. Follow the setup instructions in the README."));
            jobPath = Path.Combine(Path.GetFullPath(DestinationPath.Text), "freewin-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
            var settingsDirectory = Path.Combine(root, ".work", "jobs"); Directory.CreateDirectory(settingsDirectory);
            var settings = Path.Combine(settingsDirectory, Path.GetFileName(jobPath) + ".json");
            File.WriteAllText(settings, JsonSerializer.Serialize(new { Job = jobPath, Hyper = Path.GetFullPath(HyperPath.Text), Gui = Path.GetFullPath(DonorPath.Text), Qemu = qemu, QemuImg = qemuImg, Oscdimg = oscdimg, SevenZip = sevenZip, Wireless = WirelessOption.IsChecked == true, English = !japanese,
                Target = IsValidationOs ? "ValidationOS" : "HyperV2019", Validation = Path.GetFullPath(HyperPath.Text), ReactOs = Path.GetFullPath(DonorPath.Text), WinPeMedia = winPeMedia, BootFiles = bootFiles }));
            var start = new ProcessStartInfo(worker) { UseShellExecute = IsValidationOs, CreateNoWindow = true, WorkingDirectory = root };
            if (IsValidationOs) { start.Verb = "runas"; start.WindowStyle = ProcessWindowStyle.Hidden; }
            start.ArgumentList.Add(settings);
            process = Process.Start(start) ?? throw new InvalidOperationException(T("ビルダーを起動できませんでした。", "Could not start the builder."));
            ShowStep(4); BuildProgress.IsIndeterminate = true; StatusText.Text = T("作成中…", "Building…"); timer.Start();
        }
        catch (Exception ex) { await ShowError(ex.Message); }
    }

    private async Task ShowError(string message) => await new ContentDialog { XamlRoot = Root.XamlRoot, Title = T("確認してください", "Check your inputs"), Content = message, CloseButtonText = T("閉じる", "Close") }.ShowAsync();

    private void RefreshStatus()
    {
        if (process is null || jobPath is null) return;
        try
        {
            var logPath = Path.Combine(jobPath, "build.log");
            if (File.Exists(logPath))
            {
                using var file = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (file.Length > 65536) file.Seek(-65536, SeekOrigin.End);
                using var reader = new StreamReader(file, Encoding.UTF8, true);
                var text = reader.ReadToEnd(); if (BuildLog.Text != text) BuildLog.Text = text;
            }
            var state = Path.Combine(jobPath, "build.json");
            if (File.Exists(state)) { using var doc = JsonDocument.Parse(File.ReadAllText(state)); if (doc.RootElement.TryGetProperty("message", out var message)) StatusText.Text = message.GetString() ?? T("作成中…", "Building…"); }
        }
        catch (IOException) { }
        catch (JsonException) { }
        if (!process.HasExited) return;
        timer.Stop(); BuildProgress.IsIndeterminate = false; BuildProgress.Value = process.ExitCode == 0 ? 100 : 0;
        var success = process.ExitCode == 0 && File.Exists(IsValidationOs
            ? Path.Combine(jobPath, "validation-build", "iso", "FreeWin-ValidationOS.iso")
            : Path.Combine(jobPath, "HyperV2019-GUI.iso"));
        StatusText.Text = success ? T("ISOの作成が完了しました。", "The ISO is ready.") : T("作成に失敗しました。処理ログを確認してください。", "The build failed. Check the log.");
        OpenButton.IsEnabled = Directory.Exists(jobPath);
        process.Dispose(); process = null;
    }

    private void OnOpenOutput(object sender, RoutedEventArgs e)
    {
        if (jobPath is null) return;
        var iso = IsValidationOs
            ? Path.Combine(jobPath, "validation-build", "iso", "FreeWin-ValidationOS.iso")
            : Path.Combine(jobPath, "HyperV2019-GUI.iso");
        var folder = File.Exists(iso) ? Path.GetDirectoryName(iso)! : jobPath;
        if (Directory.Exists(folder)) Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }
}
