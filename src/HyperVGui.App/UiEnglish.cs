using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace HyperVGui.App;

internal static class UiEnglish
{
    private static readonly Dictionary<string, string> Text = new()
    {
        ["作成手順"] = "Steps", ["はじめに"] = "Get started", ["入力ISO"] = "Source ISOs",
        ["作成設定"] = "Build options", ["内容の確認"] = "Review", ["ISOの作成"] = "Build ISO",
        ["Hyper-V Server 2019 のGUI付きISOを作成します。"] = "Create a Hyper-V Server 2019 installation ISO with a desktop.",
        ["必要なもの"] = "What you need",
        ["Hyper-V Server と同じ言語の Windows Server 2019 ISOを選択し、保存先とオプションを確認します。"] = "Choose a Windows Server 2019 ISO in the same language as Hyper-V Server, then select an output folder and options.",
        ["元のISOは変更せず、新しいISOを保存します。"] = "The original ISOs are not modified.",
        ["同じ言語のISOを2つ指定してください。"] = "Select two ISOs in the same language.",
        ["Hyper-V Server 2019（推奨: 17763.737）"] = "Hyper-V Server 2019 (recommended: 17763.737)",
        ["Windows Server 2019 Desktop Experience（推奨: 17763.1）"] = "Windows Server 2019 Desktop Experience (recommended: 17763.1)",
        ["ISOを選択"] = "Select an ISO", ["参照…"] = "Browse…", ["Hyper-V Server ISOを選択"] = "Select Hyper-V Server ISO",
        ["GUIのISO"] = "Windows Server ISO", ["GUIのISOを選択"] = "Select Windows Server ISO",
        ["保存先と追加する機能を選択します。"] = "Choose an output folder and optional features.",
        ["保存先フォルダー"] = "Output folder", ["保存先フォルダーを選択"] = "Select output folder",
        ["Wi-Fi機能を追加する"] = "Add Wi-Fi support",
        ["保存先の中に新しい作業フォルダーを作成します。"] = "A new job folder will be created in the output folder.",
        ["選択した内容を確認して、ISOの作成を開始します。"] = "Review your choices and start the build.",
        ["作成を開始しています…"] = "Starting the build…", ["保存先を開く"] = "Open output folder",
        ["処理ログ"] = "Build log", ["戻る"] = "Back", ["次へ"] = "Next"
    };

    private static string Convert(string value) => Text.TryGetValue(value, out var translated) ? translated : value;

    public static void Apply(DependencyObject element)
    {
        var name = AutomationProperties.GetName(element);
        if (!string.IsNullOrEmpty(name)) AutomationProperties.SetName(element, Convert(name));
        if (element is TextBlock text) text.Text = Convert(text.Text);
        if (element is TextBox box) box.PlaceholderText = Convert(box.PlaceholderText);
        if (element is Expander expander)
        {
            if (expander.Header is string header) expander.Header = Convert(header);
            if (expander.Content is DependencyObject child) Apply(child);
        }
        else if (element is ItemsControl items)
            foreach (var item in items.Items.OfType<DependencyObject>()) Apply(item);
        else if (element is ContentControl content)
        {
            if (content.Content is string value) content.Content = Convert(value);
            else if (content.Content is DependencyObject child) Apply(child);
        }
        else if (element is Border border && border.Child is not null) Apply(border.Child);
        else if (element is Panel panel)
            foreach (var child in panel.Children) Apply(child);
    }
}
