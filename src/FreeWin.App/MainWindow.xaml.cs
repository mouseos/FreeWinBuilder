using System.Diagnostics;
using System.Globalization;
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
    private readonly string root;
    private readonly DispatcherQueueTimer timer;
    private Process? process;
    private string? jobPath;
    private int currentStep;
    private bool changingStep;
    private readonly bool japanese = (Environment.GetEnvironmentVariable("FREEWIN_LANGUAGE") ?? CultureInfo.CurrentUICulture.Name).StartsWith("ja", StringComparison.OrdinalIgnoreCase);
    private string T(string ja, string en) => japanese ? ja : en;

    public MainWindow()
    {
        InitializeComponent();
        if (!japanese) UiEnglish.Apply(Root);
        root = FindRoot();
        AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(WinRT.Interop.WindowNative.GetWindowHandle(this))).Resize(new SizeInt32(1050, 760));
        DestinationPath.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(2);
        timer.Tick += (_, _) => RefreshStatus();
        ShowStep(0);
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
            ReviewText.Text = $"Hyper-V ISO\n{HyperPath.Text}\n\nWindows Server ISO\n{DonorPath.Text}\n\n{T("保存先", "Output folder")}\n{DestinationPath.Text}\n\n{T("Wi-Fi機能", "Wi-Fi")}: {(WirelessOption.IsChecked == true ? T("追加する", "Include") : T("追加しない", "Do not include"))}";
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
            var sevenZip = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe");
            if (new[] { worker, qemu, qemuImg, oscdimg, sevenZip }.Any(p => !File.Exists(p))) throw new InvalidOperationException(T("必要なツールがありません。READMEの準備手順を実行してください。", "Required tools are missing. Follow the setup instructions in the README."));
            jobPath = Path.Combine(Path.GetFullPath(DestinationPath.Text), "hyperv-gui-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
            var settingsDirectory = Path.Combine(root, ".work", "jobs"); Directory.CreateDirectory(settingsDirectory);
            var settings = Path.Combine(settingsDirectory, Path.GetFileName(jobPath) + ".json");
            File.WriteAllText(settings, JsonSerializer.Serialize(new { Job = jobPath, Hyper = Path.GetFullPath(HyperPath.Text), Gui = Path.GetFullPath(DonorPath.Text), Qemu = qemu, QemuImg = qemuImg, Oscdimg = oscdimg, SevenZip = sevenZip, Wireless = WirelessOption.IsChecked == true, English = !japanese }));
            var start = new ProcessStartInfo(worker) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root };
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
        var success = process.ExitCode == 0 && File.Exists(Path.Combine(jobPath, "HyperV2019-GUI.iso"));
        StatusText.Text = success ? T("ISOの作成が完了しました。", "The ISO is ready.") : T("作成に失敗しました。処理ログを確認してください。", "The build failed. Check the log.");
        OpenButton.IsEnabled = Directory.Exists(jobPath);
        process.Dispose(); process = null;
    }

    private void OnOpenOutput(object sender, RoutedEventArgs e)
    { if (jobPath is not null && Directory.Exists(jobPath)) Process.Start(new ProcessStartInfo("explorer.exe", jobPath) { UseShellExecute = true }); }
}
