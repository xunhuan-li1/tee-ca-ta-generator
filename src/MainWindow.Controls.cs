using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace GpStudio;
public sealed partial class MainWindow
{
    private static Brush Brush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    private string T(string chinese, string en) => english ? en : chinese;
    private static TextBlock Text(string text, double size = 14, Brush? color = null) => new()
    {
        Text = text, FontSize = size, Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8)
    };
    private static TextBlock Heading(string text, double size = 21)
    {
        TextBlock label = Text(text, size);
        label.FontWeight = FontWeights.SemiBold;
        return label;
    }
    private Button Button(string label, Action action, bool primary = false)
    {
        var button = new Button { Content = label };
        if (primary) { button.Background = Accent; button.Foreground = Brushes.White; button.BorderBrush = Accent; }
        button.Click += (_, _) => Guard(action);
        return button;
    }
    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception e) { SetStatus(e.Message, true); }
    }
    private static Border Card(UIElement child, double padding = 22) => new()
    {
        Child = child, Background = Brushes.White, BorderBrush = Line, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10), Padding = new Thickness(padding)
    };
    private static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 10, 0)
    };
    private TextBox Input(string label, string value, Action<string> changed, StackPanel parent, bool hex = false)
    {
        parent.Children.Add(Text(label, 13));
        var input = new TextBox { Text = value, TextWrapping = TextWrapping.Wrap, AcceptsReturn = hex, MaxHeight = hex ? 120 : 72, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        System.Windows.Automation.AutomationProperties.SetName(input, label);
        if (hex) input.FontFamily = new FontFamily("Consolas");
        input.TextChanged += (_, _) => { if (!rebuilding) changed(input.Text); };
        parent.Children.Add(input);
        return input;
    }
    private ComboBox Choice(string label, IEnumerable<(string Value, string Label)> items, string value,
                            Action<string> changed, StackPanel parent)
    {
        parent.Children.Add(Text(label, 13));
        var combo = new ComboBox { MaxDropDownHeight = 360, IsTextSearchEnabled = true };
        System.Windows.Automation.AutomationProperties.SetName(combo, label);
        foreach (var item in items) combo.Items.Add(new ComboBoxItem { Content = item.Label, Tag = item.Value });
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == value);
        combo.SelectionChanged += (_, _) =>
        {
            if (!rebuilding && combo.SelectedItem is ComboBoxItem selectedItem) changed((string)selectedItem.Tag);
        };
        parent.Children.Add(combo);
        return combo;
    }
    private CheckBox Check(string label, bool value, Action<bool> changed, StackPanel parent)
    {
        var check = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 4, 0, 12) };
        check.Checked += (_, _) => { if (!rebuilding) changed(true); };
        check.Unchecked += (_, _) => { if (!rebuilding) changed(false); };
        parent.Children.Add(check);
        return check;
    }
    private UIElement ReferencePage()
    {
        var panel = new StackPanel();
        panel.Children.Add(Heading(T("规范范围", "Specification coverage")));
        panel.Children.Add(Text(T("提供 Core 1.4（不含第九章、诊断和兼容接口）、Client 1.0 及其 Errata 2.0 的接口调用示例。当前目录包含 122 个算法标识符（部分仅提供能力查询）。示例、编译通过和设备运行支持是不同层面的结果。", "Examples cover Core 1.4 excluding chapter 9, diagnostics and compatibility APIs, Client 1.0 and Errata 2.0. The catalog contains 122 algorithm identifiers (some are query-only). Generated examples, successful builds and device runtime support are distinct results."), 14, Muted));
        foreach (var item in Data.Catalog.Sources) panel.Children.Add(Text(item.Key + ": " + item.Value, 12, Muted));
        panel.Children.Add(Heading(T("接口索引", "API index"), 18));
        foreach (var group in Data.Catalog.Apis.GroupBy(a => a.Group))
        {
            var body = new StackPanel { Margin = new Thickness(10) };
            foreach (Api api in group)
            {
                var row = Text(api.Prototype, 12);
                row.FontFamily = new FontFamily("Consolas");
                body.Children.Add(row);
            }
            panel.Children.Add(new Expander { Header = $"{group.Key}  ({group.Count()})", Content = body, Margin = new Thickness(0, 4, 0, 8) });
        }
        return Card(Scroll(panel));
    }
}
