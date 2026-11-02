// SPDX-License-Identifier: MIT
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace GpStudio;

public sealed partial class MainWindow : Window
{
    private Project project = new();
    private bool english = true, rebuilding, busy, verifyingUi;
    private Border? validationCard;
    private Button? locateButton;
    private int selectedPage;
    private string outputDirectory = "", projectFile = "", savedSnapshot = "";
    private Generated? preview;
    private TabControl pages = new();
    private WrapPanel navigation = new();
    private TextBlock status = new(), validation = new(), location = new();
    private readonly DispatcherTimer validationTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private static readonly Brush Ink = Brush("#172B4D"), Muted = Brush("#61728A"), Accent = Brush("#2563EB"), Line = Brush("#E1E8F2");
    private string State => JsonSerializer.Serialize(project, Data.Json);
    private bool Dirty => savedSnapshot.Length > 0 && savedSnapshot != State;

    public MainWindow()
    {
        Width = Math.Min(1440, SystemParameters.WorkArea.Width - 32);
        Height = Math.Min(940, SystemParameters.WorkArea.Height - 32);
        MinWidth = Math.Min(1080, Width); MinHeight = Math.Min(680, Height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); FontSize = 13;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        Foreground = Ink; Background = Brush("#F3F6FB");
        // Start in English; language changes apply to the current session.
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/GP-Project-Generator;component/VisualStyles.xaml", UriKind.Relative) });
        savedSnapshot = State;
        validationTimer.Tick += (_, _) => { validationTimer.Stop(); ValidateInputs(); if (selectedPage == 2 && preview == null) _ = RefreshPreview(); };
        void Shortcut(Key key, ModifierKeys modifiers, Action action)
        {
            var command = new RoutedCommand(); InputBindings.Add(new KeyBinding(command, key, modifiers));
            CommandBindings.Add(new CommandBinding(command, (_, _) => Guard(action)));
        }
        Shortcut(Key.N, ModifierKeys.Control, NewProject);
        Shortcut(Key.O, ModifierKeys.Control, OpenProject);
        Shortcut(Key.S, ModifierKeys.Control, () => SaveProject(false));
        Shortcut(Key.S, ModifierKeys.Control | ModifierKeys.Shift, () => SaveProject(true));
        Shortcut(Key.F, ModifierKeys.Control, () => { if (selectedPage == 2) fileFilter.Focus(); else { pages.SelectedIndex = 1; searchInput.Focus(); } });
        Closing += (_, e) => { if (!verifyingUi && !ConfirmSave()) e.Cancel = true; };
        Render();
    }
    private void Changed()
    {
        preview = null; source.Text = ""; fileTree.Items.Clear();
        Title = (Dirty ? "● " : "") + project.Name + " · GP Project Generator";
        validationTimer.Stop(); if (!verifyingUi) validationTimer.Start();
        SetStatus(T("配置已修改", "Configuration changed"));
    }
    private void SetStatus(string message, bool error = false) { status.Text = message; status.Foreground = error ? Brush("#B63636") : Muted; }
    private bool ValidateInputs()
    {
        try { Generator.Validate(project); if (locateButton != null) locateButton.Visibility = Visibility.Collapsed; if (validationCard != null) validationCard.Visibility = ActualHeight > 0 && ActualHeight < 800 ? Visibility.Collapsed : Visibility.Visible; validation.Text = T("✓  配置有效", "✓  Configuration valid"); validation.Foreground = Brush("#237653"); return true; }
        catch (Exception e) { if (locateButton != null) locateButton.Visibility = Visibility.Visible; if (validationCard != null) validationCard.Visibility = Visibility.Visible; validation.Text = T("需要完善 · ", "Needs attention · ") + e.Message; validation.Foreground = Brush("#B63636"); return false; }
    }
    private void Render()
    {
        rebuilding = true;
        var root = new DockPanel { Margin = new Thickness(22, 16, 22, 14), Background = Background };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var actions = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(Button(T("新建", "New"), NewProject));
        actions.Children.Add(Button(T("打开配置", "Open configuration"), OpenProject));
        actions.Children.Add(Button(T("保存配置", "Save configuration"), () => SaveProject(false)));
        var language = new ComboBox { Width = 115, Margin = new Thickness(4, 0, 0, 8), ToolTip = "语言 / Language" };
        language.Items.Add(new ComboBoxItem { Content = "中文" }); language.Items.Add(new ComboBoxItem { Content = "English" });
        language.SelectedIndex = english ? 1 : 0;
        System.Windows.Automation.AutomationProperties.SetAutomationId(language, "Language");
        language.SelectionChanged += (_, _) => { if (rebuilding) return; english = language.SelectedIndex == 1;
            Render(); };
        actions.Children.Add(language); DockPanel.SetDock(actions, Dock.Right); header.Children.Add(actions);
        var brand = new StackPanel(); brand.Children.Add(Heading(T("CA / TA 工程生成器", "CA / TA Project Generator"), 25));
        brand.Children.Add(Text(T("定义目标，选择能力，生成可编译工程   ·   GP Core 1.4", "Define a project, choose capabilities, generate buildable code   ·   GP Core 1.4"), 12, Muted));
        header.Children.Add(brand); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        navigation = new WrapPanel(); DockPanel.SetDock(navigation, Dock.Right); footer.Children.Add(navigation);
        status = Text(T("配置工程并选择所需功能", "Configure your project and choose capabilities"), 12, Muted); status.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(status); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var check = new DockPanel();
        var locate = Button(T("定位问题", "Locate issue"), () => { selectedPage = 1; pages.SelectedIndex = 1; foreach (var f in project.Features) { var p = Snapshot(); p.Features = [f]; try { Generator.Validate(p); } catch { activeFeature = f; activeUseCase = Workflows.For(f).Id; RenderEditor(); return; } } pages.SelectedIndex = 0; });
        locateButton = locate; DockPanel.SetDock(locate, Dock.Right); check.Children.Add(locate);
        validation = Text("", 12); validation.VerticalAlignment = VerticalAlignment.Center; check.Children.Add(validation);
        var checkCard = Card(check, 5); validationCard = checkCard; checkCard.Margin = new Thickness(0, 8, 0, 0); DockPanel.SetDock(checkCard, Dock.Bottom); root.Children.Add(checkCard);
        pages = new TabControl { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        pages.Items.Add(new TabItem { Header = T("01   定义工程", "01   Define project"), Content = ProjectPage() });
        pages.Items.Add(new TabItem { Header = T("02   选择功能", "02   Choose features"), Content = FeaturesPage() });
        pages.Items.Add(new TabItem { Header = T("03   检查与导出", "03   Review & export"), Content = ExportPage() });
        pages.Items.Add(new TabItem { Header = T("辅助工具", "Utilities"), Content = ToolsPage() });
        pages.SelectedIndex = Math.Clamp(selectedPage, 0, 3);
        pages.SelectionChanged += (_, e) => { if (e.Source != pages || rebuilding) return; selectedPage = pages.SelectedIndex; UpdateNavigation(); if (selectedPage == 2 && preview == null && !verifyingUi) _ = RefreshPreview(); };
        root.Children.Add(pages); Content = root;
        root.SizeChanged += (_, _) => {
            bool compact = root.ActualHeight < 800;
            brand.Children[1].Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            header.Margin = new Thickness(0, 0, 0, compact ? 6 : 14);
            footer.Margin = new Thickness(0, compact ? 6 : 10, 0, 0);
            if (validationCard != null && validation.Text.StartsWith("✓")) validationCard.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        };
        rebuilding = false; UpdateNavigation(); ValidateInputs(); Title = (Dirty ? "● " : "") + project.Name + " · GP Project Generator";
    }
    private void UpdateNavigation()
    {
        navigation.Children.Clear();
        if (selectedPage > 0 && selectedPage < 3) navigation.Children.Add(Button(T("← 上一步", "← Back"), () => pages.SelectedIndex--));
        if (selectedPage < 2) navigation.Children.Add(Button(selectedPage == 0 ? T("选择功能 →", "Choose features →") : T("检查生成结果 →", "Review generated code →"), () => pages.SelectedIndex++, true));
        if (selectedPage == 2) navigation.Children.Add(Button(T("导出工程", "Export project"), () => _ = ExportProject(), true));
    }
    private UIElement ProjectPage()
    {
        var page = new StackPanel { Margin = new Thickness(4, 12, 4, 0) };
        var intro = new StackPanel(); intro.Children.Add(Heading(T("程序介绍", "Program overview"), 18));
        intro.Children.Add(Text(T("覆盖 TEE Internal Core API v1.4、TEE Client API v1.0 及 Errata and Precisions v2.0，生成 OP-TEE CA / TA 工程。", "Generate OP-TEE CA / TA projects for TEE Internal Core API v1.4, TEE Client API v1.0 and Errata and Precisions v2.0."), 13, Muted));
        intro.Children.Add(Text(T("定义工程 → 选择功能与参数 → 检查并导出代码", "Define project → Choose features and parameters → Review and export code"), 12));
        page.Children.Add(Card(intro, 18));
        var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        var form = new StackPanel(); form.Children.Add(Heading(T("工程信息", "Project details"), 20));
        Input(T("工程名称", "Project name"), project.Name, v => { project.Name = v; Changed(); }, form);
        form.Children.Add(Text(T("小写字母、数字、下划线；以字母开头", "Lowercase letters, digits and underscores; start with a letter"), 12, Muted));
        Input(T("描述", "Description"), project.Description, v => { project.Description = v; Changed(); }, form);
        var uuid = Input("UUID", project.Uuid, v => { project.Uuid = v; Changed(); }, form);
        var uuidActions = new WrapPanel(); uuidActions.Children.Add(Button(T("重新生成", "Generate UUID"), () => uuid.Text = Guid.NewGuid().ToString()));
        uuidActions.Children.Add(Button(T("复制 UUID", "Copy UUID"), () => Clipboard.SetText(uuid.Text))); form.Children.Add(uuidActions);
        form.Children.Add(Text("xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx", 12, Muted));
        var left = Card(form, 18); left.Margin = new Thickness(0, 0, 10, 0); grid.Children.Add(left);
        var right = new StackPanel(); right.Children.Add(Heading(T("配套 CA", "Companion CA"), 20));
        var outputChoices = new List<RadioButton>();
        var generateCa = Check(T("生成配套 CA", "Generate companion CA"), project.GenerateCa, v => {
            project.GenerateCa = v;
            foreach (var option in outputChoices) option.IsEnabled = v;
            Changed();
        }, right);
        System.Windows.Automation.AutomationProperties.SetAutomationId(generateCa, "GenerateCa");
        foreach (var item in new[] { ("executable", T("可执行文件", "Executable"), T("用于命令行运行所选示例", "Run selected examples from the command line")), ("shared", T("动态库 .so", "Shared library .so"), T("应用动态链接，配套 libteec.so", "Link dynamically; deploy with libteec.so")), ("static", T("静态库 .a", "Static library .a"), T("应用静态链接，单独链接 libteec.a", "Link statically; link libteec.a separately")) })
        {
            var content = new StackPanel(); content.Children.Add(Heading(item.Item2, 14)); content.Children.Add(Text(item.Item3, 12, Muted));
            var radio = new RadioButton { Content = content, Tag = item.Item1, GroupName = "CaOutput", IsChecked = project.CaOutput == item.Item1, IsEnabled = project.GenerateCa, Style = (Style)FindResource("OutputOption") };
            radio.Checked += (_, _) => { if (!rebuilding) { project.CaOutput = item.Item1; Changed(); } }; outputChoices.Add(radio); right.Children.Add(radio);
        }
        var settings = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        Input(T("版本（如 1.0.1）", "Version (e.g. 1.0.1)"), project.Version, v => { project.Version = v; Changed(); }, settings);
        Input(T("栈（KiB）", "Stack (KiB)"), project.StackKiB.ToString(), v => { project.StackKiB = int.TryParse(v, out int n) ? n : 0; Changed(); }, settings);
        Input(T("堆（KiB）", "Heap (KiB)"), project.HeapKiB.ToString(), v => { project.HeapKiB = int.TryParse(v, out int n) ? n : 0; Changed(); }, settings);
        var instanceOptions = new StackPanel();
        instanceOptions.Children.Add(Heading(T("实例与会话", "Instances and sessions"), 14));
        bool syncingInstances = false;
        CheckBox? singleInstance = null, keepAlive = null;
        void UpdateInstances(bool single, bool keep)
        {
            if (syncingInstances) return;
            syncingInstances = true;
            try {
                project.SingleInstance = single;
                project.KeepAlive = keep;
                singleInstance!.IsChecked = single;
                keepAlive!.IsChecked = keep;
            }
            finally { syncingInstances = false; }
            Changed();
        }
        singleInstance = Check(T("单实例", "Single instance"), project.SingleInstance,
            v => UpdateInstances(v, v && project.KeepAlive), instanceOptions);
        System.Windows.Automation.AutomationProperties.SetAutomationId(singleInstance, "SingleInstance");
        Check(T("多会话", "Multiple sessions"), project.MultiSession,
            v => { project.MultiSession = v; Changed(); }, instanceOptions);
        keepAlive = Check(T("持久化 TA", "Keep TA instance alive"), project.KeepAlive,
            v => UpdateInstances(v || project.SingleInstance, v), instanceOptions);
        System.Windows.Automation.AutomationProperties.SetAutomationId(keepAlive, "KeepAlive");
        instanceOptions.Children.Add(Text(T(
            "持久化 TA：全部会话关闭后保留实例，启用时自动勾选单实例。多会话仅在单实例模式下生效。",
            "Keep-alive retains the instance after all sessions close and enables single-instance mode. Multiple sessions applies to single-instance mode."), 12, Muted));
        Check(T("返回可恢复的 panic 错误", "Return recoverable panic errors"), project.PanicSafe,
            v => { project.PanicSafe = v; Changed(); }, instanceOptions);
        Check(T("会话上下文（保存每个会话的调用次数）", "Session context (per-session call count)"), project.SessionContext,
            v => { project.SessionContext = v; Changed(); }, instanceOptions);
        var instanceCard = Card(instanceOptions, 12);
        instanceCard.Background = Brush("#F7F9FD");
        instanceCard.Margin = new Thickness(0, 2, 0, 12);
        settings.Children.Add(instanceCard);
        var groupSettings = new StackPanel {
            Visibility = project.Login.Contains("GROUP") ? Visibility.Visible : Visibility.Collapsed
        };
        Choice(T("CA 登录方式", "CA login method"), Generator.LoginMethods.Select(v => (v, v)), project.Login, v => {
            project.Login = v;
            groupSettings.Visibility = v.Contains("GROUP") ? Visibility.Visible : Visibility.Collapsed;
            Changed();
        }, settings);
        Input(T("用户组 ID", "Group ID"), project.GroupId.ToString(), v => {
            project.GroupId = long.TryParse(v, out long n) ? n : -1;
            Changed();
        }, groupSettings);
        settings.Children.Add(groupSettings);
        var advanced = new Expander { Header = T("TA 运行配置 · 版本、内存和实例属性", "TA runtime · version, memory and instances"), Content = settings, IsExpanded = runtimeExpanded };
        advanced.Expanded += (_, _) => runtimeExpanded = true; advanced.Collapsed += (_, _) => runtimeExpanded = false; right.Children.Add(advanced);
        right.Children.Add(Text(T("OP-TEE 默认栈 16 KiB、堆 256 KiB，可展开调整。", "OP-TEE defaults: 16 KiB stack, 256 KiB heap. Expand to adjust."), 12, Muted));
        location = Text(projectFile.Length == 0 ? T("配置未保存", "Configuration not saved") : projectFile, 12, Muted); right.Children.Add(location);
        var rightCard = Card(right, 18); Grid.SetColumn(rightCard, 1); grid.Children.Add(rightCard); page.Children.Add(grid);
        page.Children.Add(Text(T("AI 调用：将 AI_GUIDE.md 提供给可读写本机文件的 AI，即可按需求配置、校验并生成工程。", "AI integration: provide AI_GUIDE.md to an assistant with local file access to configure, validate and generate a project."), 12, Muted));
        return Scroll(page);
    }
    private bool runtimeExpanded;
    private Project Snapshot() => JsonSerializer.Deserialize<Project>(State, Data.Json)!;
    private bool ConfirmSave()
    {
        if (!Dirty || verifyingUi) return true;
        var answer = MessageBox.Show(this, T("当前配置有未保存的修改。是否先保存？", "Save your configuration changes before continuing?"), Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer == MessageBoxResult.No || answer == MessageBoxResult.Yes && SaveProject(false);
    }
    private void NewProject()
    {
        if (!ConfirmSave()) return;
        project = new(); projectFile = ""; outputDirectory = ""; savedSnapshot = State; selectedPage = 0;
        activeFeature = null; drafts.Clear(); dataModes.Clear(); history.Clear(); preview = null; Render();
    }
    private void OpenProject()
    {
        var dialog = new OpenFileDialog { Filter = "Project configuration (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        Project value = Data.Read(dialog.FileName);
        Generator.Validate(value);
        foreach (var f in value.Features) { Scenarios.Get(f.Kind); Scenarios.Algorithm(f); Workflows.For(f); }
        if (!ConfirmSave()) return;
        project = value; projectFile = dialog.FileName; savedSnapshot = State; preview = null;
        drafts.Clear(); dataModes.Clear(); history.Clear(); activeFeature = null; Render();
    }
    private bool SaveProject(bool saveAs)
    {
        if (!ValidateInputs()) throw new ArgumentException(validation.Text);
        string path = projectFile;
        if (saveAs || path.Length == 0) { var dialog = new SaveFileDialog { Filter = "Project configuration (*.json)|*.json", FileName = project.Name + ".json", DefaultExt = ".json", AddExtension = true }; if (dialog.ShowDialog(this) != true) return false; path = dialog.FileName; }
        Data.Save(path, project); projectFile = path; savedSnapshot = State; location.Text = path; Title = project.Name + " · GP Project Generator"; SetStatus(T("配置已保存：", "Configuration saved: ") + path); return true;
    }
}
