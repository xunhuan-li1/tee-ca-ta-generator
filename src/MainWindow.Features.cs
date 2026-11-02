// SPDX-License-Identifier: MIT
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace GpStudio;

public sealed partial class MainWindow
{
    private string activeUseCase = "sm2_encrypt", search = "", featureCategory = "";
    private bool allFeatures, onlySelected;
    private Feature? activeFeature;
    private readonly Dictionary<string, Feature> drafts = new();
    private readonly Stack<string> history = new();
    private StackPanel available = new(), editor = new();
    private WrapPanel selectedTray = new();
    private TextBox searchInput = new();
    private ScrollViewer editorScroll = new();
    private TextBlock featureCount = new(), selectedCount = new();
    private TextBlock? featureCaption, featureDescription;
    private void CompactEditor() { var visibility = editorScroll.ActualHeight > 0 && editorScroll.ActualHeight < 460 ? Visibility.Collapsed : Visibility.Visible; if (featureCaption != null) featureCaption.Visibility = visibility; if (featureDescription != null) featureDescription.Visibility = visibility; }
    private readonly Dictionary<string, string> dataModes = new();
    private readonly Dictionary<string, FrameworkElement> optionInputs = new();
    private string Category(string name) => name switch {
        "SM" => T("国密算法", "SM algorithms"), "Crypto" => T("密码算法", "Cryptography"),
        "Core" => T("基础能力与属性", "Core and properties"), "Storage" => T("对象与安全存储", "Objects and storage"),
        "Communication" => T("会话通信", "Communication"), "Arithmetic" => T("大整数", "Big integers"),
        _ => name };
    private string Label(UseCase w) => english ? w.English : w.Chinese;
    private Feature Current()
    {
        if (activeFeature != null && project.Features.Contains(activeFeature) && Workflows.For(activeFeature).Id == activeUseCase) return activeFeature;
        activeFeature = project.Features.FirstOrDefault(f => Workflows.For(f).Id == activeUseCase);
        if (activeFeature != null) return activeFeature;
        if (!drafts.TryGetValue(activeUseCase, out var draft)) drafts[activeUseCase] = draft = Workflows.All.Single(w => w.Id == activeUseCase).Create();
        return draft;
    }
    private void Remember() => history.Push(State);
    private void UndoSelection()
    {
        if (!history.TryPop(out string? state)) return;
        project = JsonSerializer.Deserialize<Project>(state, Data.Json)!; activeFeature = null;
        Changed(); RenderAvailable(); RenderSelected(); RenderEditor(); ValidateInputs();
    }
    private void Toggle(UseCase useCase)
    {
        if (Workflows.NeedsCa(useCase) && !project.GenerateCa) return;
        Remember();
        Feature? chosen = activeFeature != null && project.Features.Contains(activeFeature) && Workflows.For(activeFeature).Id == useCase.Id ? activeFeature : project.Features.FirstOrDefault(f => Workflows.For(f).Id == useCase.Id);
        if (chosen != null) { project.Features.Remove(chosen); drafts[useCase.Id] = chosen; if (activeFeature == chosen) activeFeature = null; }
        else { if (!drafts.TryGetValue(useCase.Id, out var draft)) draft = useCase.Create(); project.Features.Add(draft); activeFeature = draft; }
        activeUseCase = useCase.Id; Changed(); RenderAvailable(); RenderSelected(); RenderEditor();
    }
    private UIElement FeaturesPage()
    {
        var root = new DockPanel { Margin = new Thickness(4, 12, 4, 0) };
        var toolbar = new DockPanel();
        var clear = Button(T("清除筛选", "Clear filters"), () => { search = ""; featureCategory = ""; onlySelected = false; Render(); });
        DockPanel.SetDock(clear, Dock.Right); toolbar.Children.Add(clear);
        var selectedOnly = new CheckBox { Content = T("只看已选", "Selected only"), IsChecked = onlySelected, Margin = new Thickness(8, 8, 14, 0) };
        selectedOnly.Checked += (_, _) => { onlySelected = true; RenderAvailable(); }; selectedOnly.Unchecked += (_, _) => { onlySelected = false; RenderAvailable(); };
        DockPanel.SetDock(selectedOnly, Dock.Right); toolbar.Children.Add(selectedOnly);
        searchInput = new TextBox { Text = search, Margin = new Thickness(0, 0, 8, 12), Tag = T("搜索用途 / 算法 / 属性，例如 SM4、签名、UUID", "Search capabilities / algorithms / properties, e.g. SM4, signature, UUID") };
        searchInput.TextChanged += (_, _) => { search = searchInput.Text; RenderAvailable(); }; toolbar.Children.Add(searchInput);
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        var selection = new Grid();
        selection.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        selection.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var selectionHeader = new DockPanel { LastChildFill = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var clearSelection = Button(T("清空", "Clear"), () => {
            Remember();
            foreach (var feature in project.Features)
                drafts[Workflows.For(feature).Id] = feature;
            project.Features.Clear();
            activeFeature = null;
            Changed();
            RenderAvailable();
            RenderSelected();
            RenderEditor();
        });
        var undoSelection = Button(T("撤销", "Undo"), UndoSelection);
        foreach (var action in new[] { clearSelection, undoSelection }) {
            action.Margin = new Thickness(8, 0, 0, 0);
            action.MinHeight = 36;
            actions.Children.Add(action);
        }
        System.Windows.Automation.AutomationProperties.SetAutomationId(clearSelection, "ClearSelection");
        System.Windows.Automation.AutomationProperties.SetAutomationId(undoSelection, "UndoSelection");
        DockPanel.SetDock(actions, Dock.Right);
        selectionHeader.Children.Add(actions);
        selectedCount = Text("", 12);
        selectedCount.Margin = new Thickness(0);
        selectedCount.VerticalAlignment = VerticalAlignment.Center;
        var selectedExpander = new Expander {
            Header = selectedCount,
            IsExpanded = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(selectedExpander, "SelectedFeaturesToggle");
        selectionHeader.Children.Add(selectedExpander);
        selection.Children.Add(selectionHeader);
        selectedTray = new WrapPanel();
        var selectedScroll = new ScrollViewer {
            Content = selectedTray,
            MaxHeight = 76,
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        selectedExpander.Expanded += (_, _) => selectedScroll.Visibility = Visibility.Visible;
        selectedExpander.Collapsed += (_, _) => selectedScroll.Visibility = Visibility.Collapsed;
        Grid.SetRow(selectedScroll, 1);
        selection.Children.Add(selectedScroll);
        var selectionCard = Card(selection, 10);
        System.Windows.Automation.AutomationProperties.SetAutomationId(selectionCard, "SelectedFeaturesCard");
        selectionCard.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(selectionCard, Dock.Bottom);
        root.Children.Add(selectionCard);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(188) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 265 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star), MinWidth = 340 });
        var sidebar = new DockPanel { Margin = new Thickness(0, 4, 10, 0) };
        var scope = new StackPanel(); scope.Children.Add(Text(T("浏览范围", "Browse scope"), 11, Muted));
        var scopes = new ComboBox(); scopes.Items.Add(T("常用功能", "Common features")); scopes.Items.Add(T("全部功能", "All features")); scopes.SelectedIndex = allFeatures ? 1 : 0;
        scopes.SelectionChanged += (_, _) => { if (!rebuilding) { allFeatures = scopes.SelectedIndex == 1; RenderAvailable(); } }; scope.Children.Add(scopes);
        DockPanel.SetDock(scope, Dock.Top); sidebar.Children.Add(scope);
        var categories = new ListBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        categories.Items.Add(new ListBoxItem { Content = T("全部分类", "All categories"), Tag = "", Padding = new Thickness(10, 10, 4, 10) });
        foreach (var group in Workflows.All.Select(w => w.Category).Distinct()) categories.Items.Add(new ListBoxItem { Content = Category(group), Tag = group, Padding = new Thickness(10, 10, 4, 10) });
        categories.SelectedItem = categories.Items.Cast<ListBoxItem>().First(c => (string)c.Tag == featureCategory);
        categories.SelectionChanged += (_, _) => { if (!rebuilding && categories.SelectedItem is ListBoxItem c) { featureCategory = (string)c.Tag; RenderAvailable(); } };
        sidebar.Children.Add(categories); grid.Children.Add(sidebar);
        var list = new DockPanel(); featureCount = Text("", 12, Muted); DockPanel.SetDock(featureCount, Dock.Top); list.Children.Add(featureCount);
        available = new StackPanel(); list.Children.Add(Scroll(available)); Grid.SetColumn(list, 1); grid.Children.Add(list);
        var splitter = new GridSplitter { Width = 4, HorizontalAlignment = HorizontalAlignment.Center, Background = Line, ResizeDirection = GridResizeDirection.Columns }; Grid.SetColumn(splitter, 2); grid.Children.Add(splitter);
        editor = new StackPanel(); editorScroll = Scroll(editor); editorScroll.SizeChanged += (_, _) => CompactEditor(); var detail = Card(editorScroll, 18); Grid.SetColumn(detail, 3); grid.Children.Add(detail);
        root.Children.Add(grid); bool? compactSelection = null; root.SizeChanged += (_, _) => { bool compact = root.ActualHeight < 500; if (compactSelection != compact) { compactSelection = compact; selectedExpander.IsExpanded = !compact; } }; RenderAvailable(); RenderSelected(); RenderEditor(); return root;
    }
    private void RenderAvailable()
    {
        available.Children.Clear(); int count = 0;
        foreach (UseCase w in Workflows.All)
        {
            var chosen = project.Features.Where(f => Workflows.For(f).Id == w.Id).ToArray();
            if (featureCategory.Length > 0 && featureCategory != w.Category || onlySelected && chosen.Length == 0 || !allFeatures && w.Advanced && chosen.Length == 0 && search.Length == 0) continue;
            var definition = Scenarios.Get(w.Kind);
            string haystack = string.Join(" ", new[] { w.Id, w.Chinese, w.English, definition.Description, definition.DescriptionEn,
                string.Join(" ", Workflows.Algorithms(w).Select(a => a.Id)), w.Kind == "session" ? "UUID TEE_OpenTASession" : "" });
            if (!search.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase))) continue;
            var cardBody = new DockPanel(); var check = new CheckBox { IsChecked = chosen.Length > 0, IsEnabled = project.GenerateCa || !Workflows.NeedsCa(w), Margin = new Thickness(0, 3, 8, 0), VerticalAlignment = VerticalAlignment.Top };
            System.Windows.Automation.AutomationProperties.SetAutomationId(check, "Select_" + w.Id);
            check.Checked += (_, _) => { if (!rebuilding) Toggle(w); }; check.Unchecked += (_, _) => { if (!rebuilding) Toggle(w); };
            DockPanel.SetDock(check, Dock.Left); cardBody.Children.Add(check);
            var body = new StackPanel(); body.Children.Add(Heading(Label(w), 14));
            body.Children.Add(Text(Workflows.Description(w, english), 12, Muted));
            body.Children.Add(Text(chosen.Length == 0 ? T("查看流程与配置参数", "View workflow and parameters") : T("✓ 已加入生成", "✓ Included in generation"), 11, chosen.Length == 0 ? Muted : Accent));
            if (chosen.Length > 0 && chosen[0].Algorithm.Length > 0) body.Children.Add(Text(chosen[0].Algorithm.Replace("TEE_ALG_", ""), 11, Muted));
            var button = Button("", () => { activeUseCase = w.Id; activeFeature = chosen.FirstOrDefault(); RenderEditor(); });
            button.Content = body; button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0); button.Padding = new Thickness(0); button.Margin = new Thickness(0); button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            cardBody.Children.Add(button); var card = Card(cardBody, 13); card.Margin = new Thickness(0, 0, 4, 8); if (w.Id == activeUseCase) card.BorderBrush = Accent;
            available.Children.Add(card); count++;
        }
        featureCount.Text = T($"{count} 项功能", $"{count} features");
        if (count == 0) available.Children.Add(Text(T("没有匹配的功能。请清除筛选或切换为全部功能。", "No matching features. Clear filters or choose All features."), 14, Muted));
    }
    private void RenderSelected()
    {
        selectedTray.Children.Clear(); selectedCount.Text = T($"已选 {project.Features.Count} 项功能", $"{project.Features.Count} selected features");
        foreach (Feature f in project.Features)
        {
            UseCase w = Workflows.For(f); var chip = Button(Label(w) + (!project.GenerateCa && Workflows.NeedsCa(w) ? T(" · 暂停", " · paused") : ""), () => { activeUseCase = w.Id; activeFeature = f; RenderEditor(); });
            chip.FontSize = 11; chip.Padding = new Thickness(9, 5, 9, 5); chip.Background = Brush("#EFF5FF"); selectedTray.Children.Add(chip);
        }
    }
    private void RenderEditor()
    {
        double offset = editorScroll.VerticalOffset; bool prior = rebuilding; rebuilding = true; editor.Children.Clear(); optionInputs.Clear();
        Feature feature = Current(); UseCase w = Workflows.All.Single(w => w.Id == activeUseCase); Scenario scenario = Scenarios.Get(w.Kind);
        featureCaption = Text(Category(w.Category) + (w.Advanced ? T(" / 进阶示例", " / Advanced") : T(" / 常用功能", " / Common")), 12, Muted); editor.Children.Add(featureCaption);
        editor.Children.Add(Heading(Label(w), 21));
        var actions = new WrapPanel(); bool chosen = project.Features.Contains(feature);
        var add = Button(chosen ? T("✓ 已加入 · 移除", "✓ Included · Remove") : T("＋ 加入生成", "+ Include in generation"), () => Toggle(w), chosen);
        add.IsEnabled = project.GenerateCa || !Workflows.NeedsCa(w); actions.Children.Add(add);
        if (scenario.Fields.Length > 0) actions.Children.Add(Button(T("重置参数", "Reset parameters"), () => { Remember(); feature.Options.Clear(); feature.Algorithm = w.DefaultAlgorithm; dataModes.Clear(); Changed(); RenderEditor(); RenderSelected(); }));
        if (!project.GenerateCa && Workflows.NeedsCa(w)) actions.Children.Add(Button(T("启用 CA", "Enable CA"), () => { project.GenerateCa = true; Changed(); Render(); }));
        editor.Children.Add(actions);
        editor.Children.Add(new Border { Height = 1, Background = Line, Margin = new Thickness(0, 6, 0, 10) });
        featureDescription = Text(Workflows.Description(w, english), 13, Muted); editor.Children.Add(featureDescription);
        var flow = new StackPanel(); flow.Children.Add(Text(Workflows.Description(w, english), 12));
        flow.Children.Add(Text(T("输入由 CA 传入，TA 在私有内存中处理并返回结果；错误码由调用方检查。", "Inputs arrive from the CA; the TA processes them in private memory and returns results. The caller checks each result."), 12, Muted));
        editor.Children.Add(new Expander { Header = T("示例流程与返回结果", "Example workflow and results"), Content = Card(flow, 12), Margin = new Thickness(0, 6, 0, 12) });
        if (scenario.Id == "property" && w.Id != "custom_property") Choice(T("属性选项", "Property preset"), PropertyPresets.All.Select(p => (p.Name, p.Name)), Scenarios.Value(feature, "name"), v => { var p = PropertyPresets.All.Single(p => p.Name == v); feature.Options["set"] = p.Set; feature.Options["name"] = p.Name; feature.Options["type"] = p.Type; Changed(); DeferEditor(); }, editor);
        if (scenario.Category == "Crypto" || scenario.Id == "algorithm_support")
        {
            var algorithm = Choice(T("算法与模式", "Algorithm and mode"), Workflows.Algorithms(w).Select(a => (a.Id, a.Id.Replace("TEE_ALG_", ""))), feature.Algorithm,
                value => {
                    feature.Algorithm = value;
                    foreach (Option field in scenario.Fields) {
                        string[]? choices = FieldRules.Choices(feature, field);
                        if (choices != null && feature.Options.TryGetValue(field.Key, out string? selected) &&
                            !choices.Contains(selected))
                            feature.Options.Remove(field.Key);
                    }
                    Changed(); RenderAvailable(); RenderSelected(); DeferEditor();
                }, editor);
            optionInputs["algorithm"] = algorithm;
        }
        foreach (Option field in scenario.Fields.Where(f => FieldRules.Visible(feature, f.Key)).OrderBy(f => f.Key is "message" or "aad" ? 2 : f.Key is "streaming" or "copyReuse" ? 1 : 0))
        {
            string label = field.Key == "keyBits" && Scenarios.Algorithm(feature) is Algorithm selectedAlgorithm && Core14.IsPqc(selectedAlgorithm)
                ? T("参数集", "Parameter set") : english ? field.English : field.Label;
            string[]? choicesForField = FieldRules.Choices(feature, field);
            if (field.Type == "toggle")
            {
                var check = new CheckBox {
                    Content = label,
                    IsChecked = Scenarios.Value(feature, field.Key) == "true",
                    Margin = new Thickness(0, 8, 0, 8)
                };
                void SetToggle(object sender, RoutedEventArgs args) {
                    if (rebuilding) return;
                    feature.Options[field.Key] = check.IsChecked == true ? "true" : "false";
                    Changed();
                    RenderSelected();
                }
                check.Checked += SetToggle;
                check.Unchecked += SetToggle;
                optionInputs[field.Key] = check;
                editor.Children.Add(check);
                editor.Children.Add(Text(T(
                    "可与一次处理或分段更新组合使用，复制操作状态并校验重置复用后的摘要。",
                    "Combine with single-pass or streaming processing to copy the operation state and verify the digest after reset and reuse."), 12, Muted));
            }
            else if (choicesForField != null)
            {
                string value = Scenarios.Value(feature, field.Key);
                void Set(string next) { feature.Options[field.Key] = next; if (field.Key == "zaSource" && next == "input") feature.Options["keySource"] = "input"; Changed(); RenderSelected(); DeferEditor(); }
                if (choicesForField.Length <= 3 && field.Key != "keyBits")
                {
                    editor.Children.Add(Text(label, 13)); var choices = new WrapPanel();
                    foreach (string next in choicesForField) { var b = Button(field.Key == "streaming" ? next == "true" ? T("分段更新", "Streaming") : T("一次处理", "Single pass") : ChoiceLabel(next), () => Set(next)); if (next == value) { b.Background = Brush("#EFF5FF"); b.BorderBrush = Accent; b.Foreground = Accent; } choices.Children.Add(b); }
                    editor.Children.Add(choices); optionInputs[field.Key] = choices;
                }
                else optionInputs[field.Key] = Choice(label, choicesForField.Select(v => (v, ChoiceLabel(v))), value, Set, editor);
            }
            else if (field.Type == "hex") DataEditor(feature, field);
            else
            {
                var input = Input(label, Scenarios.Value(feature, field.Key), v => { feature.Options[field.Key] = v; Changed(); }, editor); optionInputs[field.Key] = input;
                if (field.Key == "targetUuid") { var row = new WrapPanel(); row.Children.Add(Button(T("随机生成 UUID", "Generate UUID"), () => input.Text = Guid.NewGuid().ToString())); row.Children.Add(Button(T("复制 UUID", "Copy UUID"), () => Clipboard.SetText(input.Text))); editor.Children.Add(row); }
                if (field.Key == "bytes") { var row = new WrapPanel(); foreach (int n in new[] { 16, 32, 64, 128, 256, 512 }) row.Children.Add(Button(n.ToString(), () => input.Text = n.ToString())); editor.Children.Add(row); }
            }
        }
        if (scenario.Fields.Length == 0) editor.Children.Add(Text(T("此场景已包含完整流程，直接加入即可。", "This scenario contains a complete workflow; include it to generate."), 13));
        var references = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var api = new Expander { Header = T("查看涉及的 GP 接口", "View related GP interfaces"), Content = references, Margin = new Thickness(0, 14, 0, 0) };
        api.Expanded += (_, _) => { if (references.Text.Length == 0) references.Text = RelatedApis(feature); };
        editor.Children.Add(api); rebuilding = prior;
        CompactEditor(); editorScroll.ScrollToVerticalOffset(offset);
    }
    private string RelatedApis(Feature f)
    {
        string template = Generator.TemplateName(f);
        string code = Core14.CheckedCalls(Generator.SelectTemplate(f, Data.Asset("templates/" + template + ".c")), project.PanicSafe);
        return string.Join("\n\n", Data.Catalog.Apis.Where(a => code.Contains(a.Name + "(" )).Select(a => a.Prototype));
    }
    private void DeferEditor() => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
        bool prior = rebuilding;
        try { Guard(RenderEditor); }
        finally { rebuilding = prior; }
    }));
    private string ChoiceLabel(string value) => value switch {
        "same" => T("与 OAEP 摘要一致", "Same as OAEP digest"),
        "generate" => T("自动生成", "Generate automatically"), "input" => T("手动输入", "Provide input"), "compute" => T("生成 ZA 计算函数", "Generate ZA function"),
        "true" => T("启用", "Enabled"), "false" => T("关闭", "Disabled"), "current_ta" => T("当前 TA", "Current TA"), "current_client" => T("当前客户端", "Current client"),
        "implementation" => T("TEE 实现", "TEE implementation"), "initiator" => T("发起方", "Initiator"), "responder" => T("响应方", "Responder"),
        "roundtrip" => T("创建、读写校验并删除", "Create, verify and delete"), "create" => T("创建并保留", "Create and retain"), "read" => T("读取", "Read"), "write" => T("写入", "Write"),
        "truncate" => T("截断", "Truncate"), "rename" => T("重命名", "Rename"), "delete" => T("删除", "Delete"), _ => value };
    private void DataEditor(Feature feature, Option field)
    {
        string id = activeUseCase + "." + field.Key; string value = Scenarios.Value(feature, field.Key); bool textMode = dataModes.GetValueOrDefault(id) == "text";
        editor.Children.Add(Text((english ? field.English : field.Label).Replace(" (hex)", "").Replace("（十六进制）", ""), 13));
        var tools = new WrapPanel();
        if (field.Key is "message" or "aad") foreach (var mode in new[] { ("hex", T("十六进制", "Hexadecimal")), ("text", T("文本（UTF-8）", "Text (UTF-8)")) }) tools.Children.Add(Button(mode.Item2, () => { if (mode.Item1 == "text") _ = new UTF8Encoding(false, true).GetString(RequestBuilder.Hex(Scenarios.Value(feature, field.Key), field.Key, 2048)); dataModes[id] = mode.Item1; RenderEditor(); }));
        var input = new TextBox { Text = textMode ? Encoding.UTF8.GetString(RequestBuilder.Hex(value, field.Key, 2048)) : value, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily(textMode ? "Segoe UI" : "Consolas"), MaxHeight = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var count = Text("", 11, Muted);
        void Count() { try { int n = textMode ? Encoding.UTF8.GetByteCount(input.Text) : RequestBuilder.Hex(input.Text, field.Key, 2048).Length; count.Text = T($"{n} 字节", $"{n} bytes"); count.Foreground = Muted; } catch (ArgumentException) { count.Text = T("十六进制格式不完整", "Incomplete hexadecimal input"); count.Foreground = Brush("#B63636"); } }
        tools.Children.Add(Button(T("整理格式", "Format hex"), () => { byte[] bytes = RequestBuilder.Hex(Scenarios.Value(feature, field.Key), field.Key, 2048); if (!textMode) input.Text = Convert.ToHexString(bytes).ToLowerInvariant(); }));
        tools.Children.Add(Button(T("复制", "Copy"), () => Clipboard.SetText(input.Text))); editor.Children.Add(tools); editor.Children.Add(input); editor.Children.Add(count);
        input.TextChanged += (_, _) => { if (rebuilding) return; feature.Options[field.Key] = textMode ? Convert.ToHexString(Encoding.UTF8.GetBytes(input.Text)).ToLowerInvariant() : input.Text; Count(); Changed(); };
        if (field.Key is "private" or "ephemeralPrivate") input.LostKeyboardFocus += (_, _) => {
            if (feature.Algorithm.Contains("SM2") && Scenarios.Value(feature, field.Key).Replace(" ", "").Length == 64) DeferEditor();
        };
        Count(); optionInputs[field.Key] = input;
    }
}
