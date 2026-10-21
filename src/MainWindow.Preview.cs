// SPDX-License-Identifier: MIT
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace GpStudio;
public sealed partial class MainWindow
{
    private TreeView fileTree = new();
    private TextBox source = new(), fileFilter = new();
    private TextBlock sourceTitle = new(), previewStats = new(), outputTarget = new();
    private string fileSearch = "", activePreviewPath = "";
    private readonly Dictionary<string, TreeViewItem> previewItems = new(StringComparer.Ordinal);
    private UIElement ExportPage()
    {
        var root = new DockPanel { Margin = new Thickness(4, 12, 4, 0) };
        var header = new StackPanel(); var actions = new WrapPanel();
        actions.Children.Add(Button(T("检查并更新预览", "Validate and refresh"), () => _ = RefreshPreview()));
        foreach (var jump in new[] { (T("CA 公开接口", "CA public API"), "ca/client_api.h"), (T("TA 入口", "TA entry"), "ta/ta_entry.c"), (T("编译说明", "Build instructions"), "README.md") }) {
            var button = Button(jump.Item1, () => ShowFile(jump.Item2));
            button.IsEnabled = jump.Item2 != "ca/client_api.h" || project.GenerateCa;
            System.Windows.Automation.AutomationProperties.SetAutomationId(button, "PreviewJump:" + jump.Item2);
            actions.Children.Add(button);
        }
        header.Children.Add(actions);
        previewStats = Text("", 13); header.Children.Add(previewStats);
        var notes = new TextBox { Text = T("标准 OP-TEE TA 开发包、交叉编译器和 libteec 需另行提供；生成成功不代表已经编译或完成设备验证。", "Provide an OP-TEE TA development kit, cross compiler and libteec separately. Generation is distinct from compilation and device validation."), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        header.Children.Add(new Expander { Header = T("平台条件", "Platform requirements"), Content = notes });
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var exportPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        exportPanel.Children.Add(Text(T("导出目录", "Export parent folder"), 12));
        var folderRow = new DockPanel();
        var folderInput = new TextBox { Text = outputDirectory, Margin = new Thickness(0) };
        System.Windows.Automation.AutomationProperties.SetName(folderInput, T("导出目录", "Export parent folder"));
        System.Windows.Automation.AutomationProperties.SetAutomationId(folderInput, "ExportFolder");
        folderInput.TextChanged += (_, _) => { outputDirectory = folderInput.Text; UpdateTarget(); };
        var chooseFolder = Button(T("选择导出目录", "Choose export folder"), () => {
            var dialog = new OpenFolderDialog { Title = T("选择工程的父目录", "Select the parent folder") };
            if (dialog.ShowDialog(this) == true) folderInput.Text = dialog.FolderName;
        });
        chooseFolder.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(chooseFolder, Dock.Right);
        folderRow.Children.Add(chooseFolder);
        folderRow.Children.Add(folderInput);
        exportPanel.Children.Add(folderRow);
        outputTarget = Text("", 12, Muted);
        outputTarget.Margin = new Thickness(0, 6, 0, 0);
        exportPanel.Children.Add(outputTarget);
        UpdateTarget();
        System.Windows.Automation.AutomationProperties.SetAutomationId(exportPanel, "ExportFolderPanel");
        DockPanel.SetDock(exportPanel, Dock.Bottom);
        root.Children.Add(exportPanel);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        var browser = new DockPanel(); fileFilter = new TextBox { Text = fileSearch, Tag = T("筛选文件路径", "Filter file paths") };
        fileFilter.TextChanged += (_, _) => { fileSearch = fileFilter.Text; ShowPreview(); }; DockPanel.SetDock(fileFilter, Dock.Top); browser.Children.Add(fileFilter);
        fileTree = new TreeView { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        fileTree.SelectedItemChanged += (_, _) => { if (fileTree.SelectedItem is TreeViewItem item && item.Tag is string path) DisplayFile(path); }; browser.Children.Add(fileTree);
        grid.Children.Add(Card(browser, 12)); var split = new GridSplitter { Width = 4, Background = Line, HorizontalAlignment = HorizontalAlignment.Center }; Grid.SetColumn(split, 1); grid.Children.Add(split);
        var codePane = new DockPanel(); sourceTitle = Text(T("选择文件查看代码", "Select a file to review"), 12, Muted); DockPanel.SetDock(sourceTitle, Dock.Top); codePane.Children.Add(sourceTitle);
        source = new TextBox { IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 12, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        codePane.Children.Add(source); var codeCard = Card(codePane, 12); Grid.SetColumn(codeCard, 2); grid.Children.Add(codeCard); root.Children.Add(grid); ShowPreview(); return root;
    }
    internal static string AvailablePath(string parent, string name)
    {
        string path = Path.Combine(parent, name); int suffix = 2;
        while (Directory.Exists(path) || File.Exists(path)) path = Path.Combine(parent, name + "_" + suffix++);
        return path;
    }
    private void UpdateTarget()
    {
        try { if (!Path.IsPathFullyQualified(outputDirectory)) throw new ArgumentException(); outputTarget.Text = T("工程目录：", "Project folder: ") + AvailablePath(Path.GetFullPath(outputDirectory), project.Name); }
        catch { outputTarget.Text = T("请选择有效的绝对路径，工程将创建在该目录下。", "Choose an absolute parent path. A new project folder will be created inside it."); }
    }
    private void DisplayFile(string path)
    {
        if (preview == null || !preview.Files.TryGetValue(path, out string? content)) return;
        activePreviewPath = path;
        source.Text = content;
        sourceTitle.Text = path;
    }
    private void ShowFile(string path)
    {
        if (preview == null || !preview.Files.ContainsKey(path)) return;
        if (!previewItems.ContainsKey(path)) {
            // A shortcut must also reveal files hidden by the current filter.
            fileSearch = "";
            if (fileFilter.Text.Length > 0) fileFilter.Clear();
            else ShowPreview();
        }
        if (!previewItems.TryGetValue(path, out TreeViewItem? item)) return;
        for (ItemsControl? parent = ItemsControl.ItemsControlFromItemContainer(item);
             parent is TreeViewItem directory;
             parent = ItemsControl.ItemsControlFromItemContainer(directory))
            directory.IsExpanded = true;
        DisplayFile(path);
        item.IsSelected = true;
        fileTree.UpdateLayout();
        item.BringIntoView();
    }
    private void ShowPreview()
    {
        fileTree.Items.Clear();
        previewItems.Clear();
        if (preview == null) return;
        TreeViewItem FileItem(string path, string label) {
            var item = new TreeViewItem { Header = label, Tag = path };
            previewItems.Add(path, item);
            return item;
        }
        foreach (var group in preview.Files.Keys
            .Where(path => path.Contains(fileSearch, StringComparison.OrdinalIgnoreCase))
            .GroupBy(path => path.Contains('/') ? path.Split('/')[0] : "")) {
            if (group.Key.Length == 0) {
                foreach (string path in group) fileTree.Items.Add(FileItem(path, path));
            } else {
                var directory = new TreeViewItem { Header = group.Key, IsExpanded = true };
                foreach (string path in group)
                    directory.Items.Add(FileItem(path, path[(group.Key.Length + 1)..]));
                fileTree.Items.Add(directory);
            }
        }
        previewStats.Text = T($"{preview.Files.Count} 个文件 · {project.Features.Count} 项功能 · 尚未编译", $"{preview.Files.Count} files · {project.Features.Count} features · Not compiled");
        string? selectedPath = previewItems.ContainsKey(activePreviewPath) ? activePreviewPath :
            previewItems.ContainsKey("ca/client_api.h") ? "ca/client_api.h" :
            previewItems.ContainsKey("ta/ta_entry.c") ? "ta/ta_entry.c" : previewItems.Keys.FirstOrDefault();
        if (selectedPath != null) ShowFile(selectedPath);
        else {
            source.Clear();
            sourceTitle.Text = T("没有匹配的文件", "No matching files");
        }
    }
    private async Task RefreshPreview()
    {
        if (busy || !ValidateInputs()) return; busy = true; SetStatus(T("正在检查配置并生成预览…", "Validating and generating preview…"));
        try {
            Project snapshot = Snapshot(); Generated result = await Task.Run(() => Generator.Generate(snapshot));
            if (JsonSerializer.Serialize(snapshot, Data.Json) != State) { SetStatus(T("配置已变化，请重新检查。", "Configuration changed; refresh the preview.")); return; }
            preview = result; ShowPreview(); SetStatus(T("检查通过；尚未执行编译。", "Validation passed; compilation has not been run."));
        } catch (Exception e) { SetStatus(e.Message, true); } finally { busy = false; }
    }
    private async Task ExportProject()
    {
        if (busy || !ValidateInputs()) return;
        if (!Path.IsPathFullyQualified(outputDirectory) || File.Exists(outputDirectory)) { SetStatus(T("请选择有效的导出父目录。", "Choose a valid absolute parent folder."), true); return; }
        busy = true;
        try {
            Project snapshot = Snapshot(); string destination = AvailablePath(Path.GetFullPath(outputDirectory), snapshot.Name);
            var generated = await Task.Run(() => { var value = Generator.Generate(snapshot); Generator.Export(value, destination); return value; });
            if (JsonSerializer.Serialize(snapshot, Data.Json) == State) { preview = generated; ShowPreview(); }
            SetStatus(T("工程已导出：", "Project exported: ") + destination); UpdateTarget();
        } catch (Exception e) { SetStatus(e.Message, true); } finally { busy = false; }
    }
}
