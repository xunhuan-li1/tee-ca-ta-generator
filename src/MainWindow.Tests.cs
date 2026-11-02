// SPDX-License-Identifier: MIT
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace GpStudio;
public sealed partial class MainWindow
{
    internal object VerifyUi(string reportPath)
    {
        verifyingUi = true; validationTimer.Stop(); int checks = 0, forms = 0; string startupLanguage = english ? "en" : "zh-CN";
        string directory = Path.GetDirectoryName(Path.GetFullPath(reportPath))!;
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks++; }
        void Capture(string name, int width = 1276, int height = 780) {
            var element = (FrameworkElement)Content; element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); var background = new DrawingVisual(); using (var drawing = background.RenderOpen()) drawing.DrawRectangle(Background, null, new Rect(0, 0, width, height)); bitmap.Render(background); bitmap.Render(element);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(directory, name)); png.Save(stream);
        }
        foreach (bool language in new[] { true, false }) {
            english = language;
            foreach (var w in Workflows.All) { project.Features = [w.Create()]; activeUseCase = w.Id; activeFeature = project.Features[0]; selectedPage = 1; Render(); var element = (FrameworkElement)Content; element.Measure(new Size(1320, 860)); element.Arrange(new Rect(0, 0, 1320, 860)); element.UpdateLayout(); forms++; }
        }
        project = new(); english = true; selectedPage = 0; Render(); Capture("ui-project.png"); Capture("ui-project-small.png", 1080, 680);
        string before = State; english = false; Render(); Check(before == State, "Language changes preserve data"); Capture("ui-project-zh.png"); english = true;
        // Exercise the same routed Checked/Unchecked handlers on a scrolled, compact page.
        IEnumerable<DependencyObject> Descendants(DependencyObject parent) {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
                var child = VisualTreeHelper.GetChild(parent, i);
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }
        foreach (bool language in new[] { true, false }) {
            english = language; project = new(); selectedPage = 0; runtimeExpanded = true; Render();
            Capture(language ? "ui-runtime-en.png" : "ui-runtime-zh.png", 1080, 680);
            var root = (FrameworkElement)Content;
            var scroller = (ScrollViewer)((TabItem)pages.Items[0]).Content;
            CheckBox FindCheck(string id) => Descendants(root).OfType<CheckBox>().SingleOrDefault(c =>
                System.Windows.Automation.AutomationProperties.GetAutomationId(c) == id) ?? throw new InvalidOperationException("Missing checkbox: " + id);
            var single = FindCheck("SingleInstance");
            var keep = FindCheck("KeepAlive");
            var version = Descendants(root).OfType<TextBox>().SingleOrDefault(c =>
                System.Windows.Automation.AutomationProperties.GetName(c) == T("版本（如 1.0.1）", "Version (e.g. 1.0.1)")) ?? throw new InvalidOperationException("Missing version field");
            version.Text = "";
            for (int iteration = 0; iteration < 3; iteration++) {
                single.IsChecked = true; keep.IsChecked = true;
                scroller.ScrollToVerticalOffset(280); root.UpdateLayout();
                double offset = scroller.VerticalOffset;
                Check(offset > 100, "Runtime settings test starts scrolled");
                System.Windows.Input.FocusManager.SetFocusedElement(this, single);
                single.IsChecked = false; root.UpdateLayout();
                Check(!project.SingleInstance && !project.KeepAlive && keep.IsChecked == false,
                    "Disabling single instance clears keep-alive in model and UI");
                Check(ReferenceEquals(Content, root) && Math.Abs(scroller.VerticalOffset - offset) < 1 && runtimeExpanded,
                    "Single-instance toggle preserves page, scroll and expander");
                Check(ReferenceEquals(System.Windows.Input.FocusManager.GetFocusedElement(this), single),
                    "Single-instance toggle preserves logical focus");
                System.Windows.Input.FocusManager.SetFocusedElement(this, keep);
                keep.IsChecked = true; root.UpdateLayout();
                Check(project.SingleInstance && project.KeepAlive && single.IsChecked == true,
                    "Enabling keep-alive selects single instance in model and UI");
                Check(ReferenceEquals(Content, root) && Math.Abs(scroller.VerticalOffset - offset) < 1,
                    "Keep-alive toggle preserves page and scroll");
                Check(ReferenceEquals(System.Windows.Input.FocusManager.GetFocusedElement(this), keep) && version.Text == "" && project.Version == "",
                    "Keep-alive preserves logical focus and unfinished text input");
            }
            version.Text = "1.0.1";
            Check(project.Version == "1.0.1" && ValidateInputs(), "Dotted TA version is valid");
            string versionPath = Path.Combine(directory, language ? "version-en.json" : "version-zh.json");
            Data.Save(versionPath, project);
            Check(Data.Read(versionPath).Version == "1.0.1", "Dotted TA version survives save and load");
            Check(Generator.Generate(project).Files["ta/user_ta_header_defines.h"].Contains("#define TA_VERSION \"1.0.1\""),
                "Dotted TA version is emitted as a C string");
            var generate = FindCheck("GenerateCa");
            generate.IsChecked = false; root.UpdateLayout();
            Check(ReferenceEquals(Content, root) && Descendants(root).OfType<RadioButton>().Where(c => c.GroupName == "CaOutput").All(c => !c.IsEnabled),
                "Companion CA updates output availability without rebuilding");
            generate.IsChecked = true;
            var login = Descendants(root).OfType<ComboBox>().SingleOrDefault(c =>
                System.Windows.Automation.AutomationProperties.GetName(c) == T("CA 登录方式", "CA login method")) ?? throw new InvalidOperationException("Missing login field");
            login.SelectedItem = login.Items.Cast<ComboBoxItem>().Single(i => (string)i.Tag == "TEEC_LOGIN_GROUP");
            root.UpdateLayout();
            var groupInput = Descendants(root).OfType<TextBox>().SingleOrDefault(c =>
                System.Windows.Automation.AutomationProperties.GetName(c) == T("用户组 ID", "Group ID")) ?? throw new InvalidOperationException("Missing group field");
            Check(ReferenceEquals(Content, root) && ((StackPanel)groupInput.Parent).Visibility == Visibility.Visible,
                "Group login reveals its field without rebuilding");
            groupInput.Text = "42";
            login.SelectedItem = login.Items.Cast<ComboBoxItem>().Single(i => (string)i.Tag == "TEEC_LOGIN_PUBLIC");
            root.UpdateLayout();
            Check(ReferenceEquals(Content, root) && ((StackPanel)groupInput.Parent).Visibility == Visibility.Collapsed && project.GroupId == 42,
                "Public login hides and retains the group ID without rebuilding");
            scroller.ScrollToBottom(); root.UpdateLayout();
            Capture(language ? "ui-runtime-scrolled-en.png" : "ui-runtime-scrolled-zh.png", 1080, 680);
        }
        foreach (bool language in new[] { true, false }) {
            foreach (var size in new[] { new Size(1320, 860), new Size(1080, 680) }) {
                english = language;
                project = new();
                project.Features = new[] { "random", "time", "sm2_sign", "sm2_kep", "cipher" }
                    .Select(id => Workflows.All.Single(w => w.Id == id).Create()).ToList();
                activeFeature = project.Features[^1];
                activeUseCase = "cipher";
                selectedPage = 1;
                Render();
                var root = (FrameworkElement)Content;
                void ArrangeSelection() {
                    root.Measure(size);
                    root.Arrange(new Rect(new Point(), size));
                    root.UpdateLayout();
                }
                ArrangeSelection();
                TControl FindSelection<TControl>(string id) where TControl : FrameworkElement =>
                    Descendants(root).OfType<TControl>().Single(control =>
                        System.Windows.Automation.AutomationProperties.GetAutomationId(control) == id);
                var card = FindSelection<Border>("SelectedFeaturesCard");
                var toggle = FindSelection<Expander>("SelectedFeaturesToggle");
                var clear = FindSelection<Button>("ClearSelection");
                var undo = FindSelection<Button>("UndoSelection");
                Rect Bounds(FrameworkElement element) => element.TransformToAncestor(card)
                    .TransformBounds(new Rect(element.RenderSize));
                toggle.IsExpanded = true;
                ArrangeSelection();
                Rect countBounds = Bounds(selectedCount), clearBounds = Bounds(clear), undoBounds = Bounds(undo);
                Check(countBounds.Right + 8 <= clearBounds.Left && clearBounds.Right + 7 <= undoBounds.Left,
                    "Selection count and actions do not overlap");
                Check(Math.Abs(countBounds.Top + countBounds.Height / 2 - clearBounds.Top - clearBounds.Height / 2) < 4,
                    "Selection count and actions are vertically aligned");
                Check(Math.Abs(card.ActualWidth - undoBounds.Right - 11) < 2,
                    "Selection actions align with the card right edge");
                var trayScroll = Descendants(card).OfType<ScrollViewer>().Single();
                Check(Bounds(trayScroll).Top >= clearBounds.Bottom + 7 && selectedTray.Children.Count == 5,
                    "Selection chips occupy a separate full-width row");
                Capture($"ui-selection-{(language ? "en" : "zh")}-{size.Width}.png", (int)size.Width, (int)size.Height);
                toggle.IsExpanded = false;
                ArrangeSelection();
                Check(trayScroll.Visibility == Visibility.Collapsed && clear.Visibility == Visibility.Visible &&
                    undo.Visibility == Visibility.Visible && clear.ActualHeight > 0 && undo.ActualHeight > 0,
                    "Collapsed selection retains separate actions");
                clear.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                ArrangeSelection();
                Check(project.Features.Count == 0 && !toggle.IsExpanded && ReferenceEquals(Content, root),
                    "Clear keeps selection collapsed without rebuilding the page");
                undo.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                ArrangeSelection();
                Check(project.Features.Count == 5 && !toggle.IsExpanded,
                    "Undo restores selected features without toggling the tray");
                project.Features = Workflows.All.Select(w => w.Create()).ToList();
                RenderSelected();
                toggle.IsExpanded = true;
                ArrangeSelection();
                Check(selectedTray.Children.Count == Workflows.All.Length && trayScroll.ActualHeight <= 76 &&
                    trayScroll.ExtentHeight > trayScroll.ViewportHeight,
                    "Many selected features wrap within a bounded scrolling area");
            }
        }
        foreach (bool language in new[] { true, false }) {
            english = language;
            project = new();
            selectedPage = 1;
            activeUseCase = "cipher";
            activeFeature = Workflows.All.Single(w => w.Id == "cipher").Create();
            project.Features = [activeFeature];
            Render();
            foreach (string algorithmId in new[] { "TEE_ALG_AES_ECB_NOPAD", "TEE_ALG_AES_XTS", "TEE_ALG_SM4_CBC_NOPAD", "TEE_ALG_DES_CBC_NOPAD", "TEE_ALG_DES3_CBC_NOPAD" }) {
                activeFeature.Algorithm = algorithmId;
                activeFeature.Options.Clear();
                RenderEditor();
                var sizes = (ComboBox)optionInputs["keyBits"];
                Check(sizes.Items.Cast<ComboBoxItem>().All(item => int.Parse((string)item.Tag) > 0),
                    "Key-size choices contain only positive sizes");
                Check(sizes.SelectedItem is ComboBoxItem selected &&
                    (string)selected.Tag == Scenarios.Algorithm(activeFeature)!.KeyBits.ToString(),
                    "Key-size field displays the actual algorithm default");
            }
            activeFeature.Algorithm = "TEE_ALG_AES_ECB_NOPAD";
            activeFeature.Options["keyBits"] = "192";
            RenderEditor();
            var algorithms = (ComboBox)optionInputs["algorithm"];
            algorithms.SelectedItem = algorithms.Items.Cast<ComboBoxItem>().Single(item => (string)item.Tag == "TEE_ALG_SM4_CBC_NOPAD");
            RenderEditor();
            Check(Scenarios.Value(activeFeature, "keyBits") == "128" &&
                ((ComboBoxItem)((ComboBox)optionInputs["keyBits"]).SelectedItem).Tag.Equals("128"),
                "Switching from AES-192 to SM4 selects its valid default size");
            activeFeature = Workflows.All.Single(w => w.Id == "ae").Create();
            project.Features = [activeFeature];
            activeUseCase = "ae";
            activeFeature.Algorithm = "TEE_ALG_AES_CCM";
            activeFeature.Options["tagBits"] = "32";
            Render();
            var aeAlgorithms = (ComboBox)optionInputs["algorithm"];
            aeAlgorithms.SelectedItem = aeAlgorithms.Items.Cast<ComboBoxItem>()
                .Single(item => (string)item.Tag == "TEE_ALG_AES_GCM");
            RenderEditor();
            Check(Scenarios.Value(activeFeature, "tagBits") == "128" && ValidateInputs(),
                "CCM-to-GCM switch replaces an unsupported tag length with the default");
            activeFeature = Workflows.All.Single(w => w.Id == "mac").Create();
            project.Features = [activeFeature];
            activeUseCase = "mac";
            Render();
            var keySizeInput = (TextBox)optionInputs["keyBits"];
            Check(int.Parse(keySizeInput.Text) > 0, "Numeric key-size input starts with a positive default");
            keySizeInput.Text = "0";
            Check(!ValidateInputs(), "Manually entering zero key bits is rejected");
            string legacyPath = Path.Combine(directory, language ? "legacy-key-size-en.json" : "legacy-key-size-zh.json");
            Data.Save(legacyPath, project);
            Project restored = Data.Read(legacyPath);
            Check(!restored.Features[0].Options.ContainsKey("keyBits") &&
                RequestBuilder.KeyBits(restored.Features[0], Scenarios.Algorithm(restored.Features[0])!) > 0,
                "Legacy zero-marker configurations resolve to a real default size");
            keySizeInput.Text = Scenarios.Algorithm(activeFeature)!.KeyBits.ToString();
            Check(ValidateInputs(), "Valid key size clears the validation error");
        }
        english = true; project = new();
        project.Features.Clear(); activeUseCase = "cipher"; activeFeature = null; selectedPage = 1; Render();
        int count = project.Features.Count; _ = Current(); Check(project.Features.Count == count, "Viewing a card must not select it");
        Toggle(Workflows.All.Single(w => w.Id == "cipher")); Check(project.Features.Count == 1, "Include action"); UndoSelection(); Check(project.Features.Count == 0, "Undo selection"); Toggle(Workflows.All.Single(w => w.Id == "cipher"));
        ValidateInputs(); Feature f = Current(); f.Options["keySource"] = "input"; f.Options["ivSource"] = "input"; RenderEditor(); Check(optionInputs.ContainsKey("iv") && optionInputs.ContainsKey("key"), "Manual key and IV fields");
        f.Options["key"] = "00112233445566778899aabbccddeeff"; f.Algorithm = "TEE_ALG_SM4_CBC_NOPAD"; RenderEditor(); Check(f.Options["key"].StartsWith("0011"), "Explicit inputs survive algorithm changes");
        string priorAlgorithm = f.Algorithm;
        f.Algorithm = "TEE_ALG_AES_ECB_NOPAD";
        f.Options["iv"] = "unfinished IV input";
        RenderEditor();
        Check(!optionInputs.ContainsKey("iv") && ValidateInputs() && RequestBuilder.Build(f).Iv.Length == 0,
            "CBC-to-ECB switch ignores hidden IV input");
        Check(Generator.Generate(project).Files.Values.Any(text => text.Contains("#define GP_IV_BYTES 0")),
            "ECB generates no IV after switching from a manual-IV mode");
        f.Options.Remove("iv"); f.Algorithm = priorAlgorithm; RenderEditor();
        ValidateInputs(); Capture("ui-features.png"); Capture("ui-features-small.png", 1080, 680);
        var oaep = new Feature { Kind = "encrypt", Algorithm = "TEE_ALG_RSAES_PKCS1_OAEP_MGF1_SHA256" };
        project.Features.Add(oaep);
        activeFeature = oaep; activeUseCase = "rsa_encrypt"; RenderEditor();
        Check(optionInputs.ContainsKey("mgfHash") && optionInputs["keyBits"] is ComboBox, "OAEP hash and key-size choices");
        oaep.Options["keySource"] = "input"; oaep.Options["rsaCrt"] = "true"; RenderEditor();
        Check(optionInputs.ContainsKey("rsaP") && optionInputs.ContainsKey("rsaQinv"), "RSA CRT input fields");
        project.Features.Remove(oaep);
        activeFeature = f; activeUseCase = "cipher"; RenderEditor();
        featureCategory = "Storage"; allFeatures = true; RenderAvailable(); Check(available.Children.Count == Workflows.All.Count(w => w.Category == "Storage"), "Category filter"); featureCategory = ""; search = "SM4"; RenderAvailable(); Check(available.Children.Count > 0, "Algorithm search"); search = "";
        project = new(); project.Features = [new Feature { Kind = "digest", Algorithm = "TEE_ALG_SHA256" }]; preview = Generator.Generate(project); selectedPage = 2; Render(); Check(source.Text.Length > 0, "Source preview"); Capture("ui-review.png");
        foreach (bool language in new[] { true, false }) {
            foreach (var size in new[] { new Size(1320, 860), new Size(1080, 680) }) {
                english = language;
                selectedPage = 2;
                fileSearch = "";
                Render();
                var root = (FrameworkElement)Content;
                void ArrangePreview() {
                    root.Measure(size);
                    root.Arrange(new Rect(new Point(), size));
                    root.UpdateLayout();
                }
                ArrangePreview();
                var folderPanel = Descendants(root).OfType<StackPanel>().Single(panel =>
                    System.Windows.Automation.AutomationProperties.GetAutomationId(panel) == "ExportFolderPanel");
                Rect RootBounds(FrameworkElement element) => element.TransformToAncestor(root)
                    .TransformBounds(new Rect(element.RenderSize));
                Check(RootBounds(folderPanel).Top > RootBounds(source).Bottom &&
                    RootBounds(folderPanel).Bottom <= size.Height,
                    "Export folder is below the source preview within the window");
                void Jump(string path) {
                    var button = Descendants(root).OfType<Button>().Single(control =>
                        System.Windows.Automation.AutomationProperties.GetAutomationId(control) == "PreviewJump:" + path);
                    button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    ArrangePreview();
                    Check(fileTree.SelectedItem is TreeViewItem item && (string)item.Tag == path &&
                        sourceTitle.Text == path && source.Text == preview!.Files[path],
                        "Preview shortcut synchronizes tree selection, title and source");
                    var leaf = (TreeViewItem)fileTree.SelectedItem;
                    Rect bounds = leaf.TransformToAncestor(fileTree).TransformBounds(new Rect(leaf.RenderSize));
                    Check(bounds.Top >= -1 && bounds.Bottom <= fileTree.ActualHeight + 1,
                        "Preview shortcut scrolls the selected file into view");
                }
                foreach (var folderNode in fileTree.Items.OfType<TreeViewItem>().Where(item => item.Items.Count > 0))
                    folderNode.IsExpanded = false;
                ArrangePreview();
                Jump("ta/ta_entry.c");
                Check(fileTree.Items.OfType<TreeViewItem>().Single(item => Equals(item.Header, "ta")).IsExpanded,
                    "TA shortcut expands its parent directory");
                fileFilter.Text = "ta/";
                ArrangePreview();
                Jump("ca/client_api.h");
                Check(fileSearch == "" && fileFilter.Text == "", "Shortcut clears a filter hiding its target");
                fileFilter.Text = "no-such-file";
                ArrangePreview();
                Check(fileTree.Items.Count == 0 && source.Text.Length == 0, "Empty file search clears stale source");
                Jump("README.md");
                Jump("ta/ta_entry.c");
                ShowPreview();
                ArrangePreview();
                Check((string)((TreeViewItem)fileTree.SelectedItem).Tag == "ta/ta_entry.c",
                    "Refreshing preview retains selected file");
                var folder = Descendants(folderPanel).OfType<TextBox>().Single();
                string previousFolder = outputDirectory;
                folder.Text = Path.Combine(directory, "preview-export");
                Check(ReferenceEquals(root, Content) && outputDirectory == folder.Text &&
                    (string)((TreeViewItem)fileTree.SelectedItem).Tag == "ta/ta_entry.c",
                    "Editing export folder preserves preview selection");
                Capture($"ui-preview-{(language ? "en" : "zh")}-{size.Width}.png", (int)size.Width, (int)size.Height);
                folder.Text = previousFolder;
            }
        }
        foreach (bool language in new[] { true, false }) {
            english = language; selectedPage = 1; activeUseCase = "digest";
            activeFeature = project.Features[0]; activeFeature.Options.Clear(); Render();
            Check(optionInputs["copyReuse"] is CheckBox checkbox && checkbox.IsChecked == false,
                "Digest copy and reuse defaults off");
            Check(((WrapPanel)optionInputs["streaming"]).Children.Count == 2,
                "Processing has two independent mode buttons");
            ((CheckBox)optionInputs["copyReuse"]).IsChecked = true;
            Check(Scenarios.Value(activeFeature, "streaming") == "false", "Copy preserves single pass");
            ((Button)((WrapPanel)optionInputs["streaming"]).Children[1]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            RenderEditor();
            Check(Scenarios.Value(activeFeature, "streaming") == "true" && ((CheckBox)optionInputs["copyReuse"]).IsChecked == true,
                "Streaming preserves copy selection");
            string digestPath = Path.Combine(directory, language ? "digest-en.json" : "digest-zh.json");
            Data.Save(digestPath, project);
            Feature restored = Data.Read(digestPath).Features[0];
            Check(Scenarios.Value(restored, "streaming") == "true" && Scenarios.Value(restored, "copyReuse") == "true",
                "Independent digest options survive save and load");
            ((CheckBox)optionInputs["copyReuse"]).IsChecked = false;
            Check(Scenarios.Value(activeFeature, "streaming") == "true", "Disabling copy preserves streaming");
            Capture(language ? "ui-digest-en.png" : "ui-digest-zh.png");
        }
        english = true;
        selectedPage = 3; selectedUtility = 0; Render(); Capture("ui-za.png");
        Check(Convert.ToHexString(CryptoUtilities.Sm3(Encoding.ASCII.GetBytes("abc"))) == "66C7F0F462EEEDD9D1F2D46BDC10E4E24167C4875CF2F7A2297DA02B8F4BA8E0", "SM3 abc vector");
        Check(Convert.ToHexString(CryptoUtilities.Sm3(Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("abcd", 16))))) == "DEBE9FF92275B8A138604889C18E5A4D6FDB70E5387E5765293DCBA39C0C5732", "SM3 multi-block vector");
        var sm2 = CryptoUtilities.Sm2PublicKey(ExampleInputs.Keys["sm2"]["ephemeralPrivate"]);
        Check(Convert.ToHexString(sm2.X).Equals(ExampleInputs.Keys["sm2"]["ephemeralX"], StringComparison.OrdinalIgnoreCase) && Convert.ToHexString(sm2.Y).Equals(ExampleInputs.Keys["sm2"]["ephemeralY"], StringComparison.OrdinalIgnoreCase), "SM2 scalar multiplication");
        byte[] za = CryptoUtilities.Za(Convert.FromHexString(zaId), Convert.FromHexString(zaX), Convert.FromHexString(zaY)); Check(za.Length == 32, "ZA output"); Data.Save(Path.Combine(directory, "za-check.json"), new { Id = zaId, X = zaX, Y = zaY, Za = Convert.ToHexString(za) });
        bool rejected = false; try { CryptoUtilities.Za([], new byte[32], new byte[32]); } catch (ArgumentException) { rejected = true; } Check(rejected, "Reject invalid public key");
        using var key = RSA.Create(2048); var request = new CertificateRequest("CN=Generator utility check", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1); using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        byte[] der = cert.Export(X509ContentType.Cert); certificateInfo = CryptoUtilities.Certificate(der); Check(certificateInfo.Spki.SequenceEqual(key.ExportSubjectPublicKeyInfo()), "SPKI extraction"); Check(CryptoUtilities.Certificate(Encoding.ASCII.GetBytes(PemEncoding.WriteString("CERTIFICATE", der))).Der.SequenceEqual(der), "PEM/DER roundtrip");
        selectedUtility = 1; Render(); Capture("ui-certificate.png"); selectedUtility = 2; Render(); Capture("ui-reference.png");
        string parent = Path.Combine(directory, "path-check"); Directory.CreateDirectory(parent); Directory.CreateDirectory(Path.Combine(parent, "demo")); Check(AvailablePath(parent, "demo").EndsWith("demo_2"), "GUI export suffix");
        return new { Success = true, StartupLanguage = startupLanguage, Forms = forms, Interactions = checks, UseCases = Workflows.All.Length, Languages = new[] { "en", "zh-CN" } };
    }
}
