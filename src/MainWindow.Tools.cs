// SPDX-License-Identifier: MIT
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace GpStudio;
public sealed partial class MainWindow
{
    private string zaId = "31323334353637383132333435363738", zaX = "32c4ae2c1f1981195f9904466a39c9948fe30bbff2660be1715a4589334c74c7", zaY = "bc3736a2f4f6779c59bdcee36b692153d0a9877cc62a474002df32e52139f0a0", zaMessage = "616263", zaResult = "", digestResult = "", pemText = "";
    private CertificateInfo? certificateInfo;
    private int selectedUtility;
    private bool utilityBusy;
    private UIElement ToolsPage()
    {
        var tabs = new TabControl { Margin = new Thickness(4, 12, 4, 0), BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        // Each panel is constructed only on first selection; no cryptography runs during navigation.
        var names = new[] { T("SM2 ZA", "SM2 ZA"), T("证书 / 公钥", "Certificates / public keys"), T("规范与接口", "Standards and APIs") };
        for (int i = 0; i < names.Length; i++) tabs.Items.Add(new TabItem { Header = names[i] });
        void Load() { if (tabs.SelectedItem is TabItem item && item.Content == null) item.Content = tabs.SelectedIndex switch { 0 => ZaPanel(), 1 => CertificatePanel(), _ => ReferencePage() }; }
        tabs.SelectionChanged += (_, e) => { if (e.Source != tabs) return; selectedUtility = tabs.SelectedIndex; Load(); };
        tabs.SelectedIndex = selectedUtility; Load(); return tabs;
    }
    private UIElement ZaPanel()
    {
        var panel = new StackPanel(); panel.Children.Add(Heading(T("SM2 ZA 与签名前摘要", "SM2 ZA and signature digest"), 20));
        panel.Children.Add(Text(T("ZA 绑定用户标识和公钥。签名输入为 e = SM3(ZA || 消息)。仅使用公钥即可计算。", "ZA binds the user identity and public key. The signature input is e = SM3(ZA || message). Only the public key is needed."), 13, Muted));
        var id = Input(T("用户标识 ID（十六进制，最长 8191 字节）", "User ID (hex, up to 8191 bytes)"), zaId, s => zaId = s, panel, true);
        var x = Input(T("公钥 X（32 字节，大端）", "Public X (32 bytes, big-endian)"), zaX, s => zaX = s, panel, true);
        var y = Input(T("公钥 Y（32 字节，大端）", "Public Y (32 bytes, big-endian)"), zaY, s => zaY = s, panel, true);
        Input(T("消息（十六进制，可为空）", "Message (hex, may be empty)"), zaMessage, s => zaMessage = s, panel, true);
        var row = new WrapPanel();
        var za = new TextBox { Text = zaResult, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, FontFamily = new FontFamily("Consolas") };
        var digest = new TextBox { Text = digestResult, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, FontFamily = new FontFamily("Consolas") };
        row.Children.Add(Button(T("计算 ZA 和 e", "Calculate ZA and e"), () => _ = CalculateZa(za, digest)));
        row.Children.Add(Button(T("载入当前签名公钥 / ID", "Load signing public key / ID"), () => {
            Feature? f = project.Features.FirstOrDefault(f => f.Algorithm == "TEE_ALG_SM2_DSA_SM3") ?? drafts.Values.FirstOrDefault(f => f.Algorithm == "TEE_ALG_SM2_DSA_SM3");
            if (f == null || Scenarios.Value(f, "keySource") != "input") throw new ArgumentException(T("请先在 SM2 签名页面选择输入密钥并填写公钥。", "Configure an input key in the SM2 signature scenario first."));
            id.Text = Scenarios.Value(f, "identity"); x.Text = Scenarios.Value(f, "publicX"); y.Text = Scenarios.Value(f, "publicY"); }));
        row.Children.Add(Button(T("复制 ZA", "Copy ZA"), () => Clipboard.SetText(zaResult)));
        row.Children.Add(Button(T("复制 e", "Copy e"), () => Clipboard.SetText(digestResult))); panel.Children.Add(row);
        panel.Children.Add(Text(T("ZA（32 字节）", "ZA (32 bytes)"), 13)); panel.Children.Add(za);
        panel.Children.Add(Text(T("e = SM3(ZA || 消息)", "e = SM3(ZA || message)"), 13)); panel.Children.Add(digest);
        return Card(Scroll(panel), 18);
    }
    private async Task CalculateZa(TextBox za, TextBox digest)
    {
        if (utilityBusy) return; utilityBusy = true;
        string[] snapshot = [zaId, zaX, zaY, zaMessage];
        try {
            var values = await Task.Run(() => { byte[] z = CryptoUtilities.Za(RequestBuilder.Hex(snapshot[0], "ID", 8191), RequestBuilder.Hex(snapshot[1], "X", 32), RequestBuilder.Hex(snapshot[2], "Y", 32)); byte[] m = RequestBuilder.Hex(snapshot[3], "message", 1024 * 1024); return (Convert.ToHexString(z), Convert.ToHexString(CryptoUtilities.Sm3([.. z, .. m]))); });
            if (!snapshot.SequenceEqual(new[] { zaId, zaX, zaY, zaMessage })) { SetStatus(T("输入已修改，请重新计算。", "Inputs changed; calculate again.")); return; }
            zaResult = za.Text = values.Item1; digestResult = digest.Text = values.Item2; SetStatus(T("ZA 与签名前摘要计算完成", "ZA and signature digest calculated"));
        } catch (Exception e) { SetStatus(e.Message, true); } finally { utilityBusy = false; }
    }
    private UIElement CertificatePanel()
    {
        var panel = new StackPanel(); panel.Children.Add(Heading(T("证书与公钥", "Certificates and public keys"), 20));
        panel.Children.Add(Text(T("查看单张 X.509 PEM/DER 证书、导出公钥与指纹。解析不执行签名、信任链或吊销验证。", "Inspect a single X.509 PEM/DER certificate and export its public key and fingerprints. Parsing does not validate signatures, trust chains or revocation."), 13, Muted));
        var pem = Input(T("证书 PEM", "Certificate PEM"), pemText, s => pemText = s, panel, true); pem.MaxHeight = 180;
        var details = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 180, MaxHeight = 400, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var row = new WrapPanel();
        row.Children.Add(Button(T("打开 PEM / DER", "Open PEM / DER"), () => { var d = new OpenFileDialog { Filter = "Certificate|*.pem;*.der;*.cer;*.crt|All files|*.*" }; if (d.ShowDialog(this) == true) { if (new FileInfo(d.FileName).Length > 1024 * 1024) throw new ArgumentException("Certificate exceeds 1 MiB."); _ = ParseCertificate(File.ReadAllBytes(d.FileName), details); } }));
        row.Children.Add(Button(T("解析 PEM", "Parse PEM"), () => _ = ParseCertificate(Encoding.UTF8.GetBytes(pem.Text), details))); panel.Children.Add(row);
        var exports = new WrapPanel();
        foreach (string kind in new[] { "certificate.pem", "certificate.der", "public_key.pem", "public_key.der", "certificate.h", "public_key.h" }) exports.Children.Add(Button(kind, () => ExportCertificate(kind)));
        exports.Children.Add(Button(T("将 SM2 公钥填入 ZA 工具", "Use SM2 public key in ZA tool"), () => { if (certificateInfo?.IsSm2 != true) throw new ArgumentException("The certificate does not contain an uncompressed SM2 public key."); zaX = Convert.ToHexString(certificateInfo.PublicKey[1..33]); zaY = Convert.ToHexString(certificateInfo.PublicKey[33..65]); selectedUtility = 0; Render(); }));
        panel.Children.Add(exports); panel.Children.Add(details); if (certificateInfo != null) details.Text = CertificateDescription(certificateInfo);
        return Card(Scroll(panel), 18);
    }
    private async Task ParseCertificate(byte[] input, TextBox details)
    {
        if (utilityBusy) return; utilityBusy = true;
        try { certificateInfo = await Task.Run(() => CryptoUtilities.Certificate(input)); details.Text = CertificateDescription(certificateInfo); SetStatus(T("证书解析完成", "Certificate parsed")); }
        catch (Exception e) { certificateInfo = null; details.Clear(); SetStatus(e.Message, true); }
        finally { utilityBusy = false; }
    }
    private string CertificateDescription(CertificateInfo c) =>
        T("主题：", "Subject: ") + c.Subject + "\n" + T("颁发者：", "Issuer: ") + c.Issuer + "\n" + T("序列号：", "Serial: ") + c.Serial + "\n" +
        T("有效期（UTC）：", "Validity (UTC): ") + c.Start.ToString("u") + " — " + c.End.ToString("u") + "\n" +
        T("公钥算法：", "Public-key algorithm: ") + c.Oid + "\n" + T("曲线：", "Curve: ") + c.Curve + "\n" + T("签名算法：", "Signature algorithm: ") + c.Signature +
        "\n\nSHA-256:\n" + Convert.ToHexString(SHA256.HashData(c.Der)) + "\nSM3:\n" + Convert.ToHexString(CryptoUtilities.Sm3(c.Der)) + "\n\n" + c.Extensions;
    private void ExportCertificate(string name)
    {
        var c = certificateInfo ?? throw new ArgumentException(T("请先解析证书。", "Parse a certificate first."));
        var d = new SaveFileDialog { FileName = name, Filter = "Export|*.*" }; if (d.ShowDialog(this) != true) return;
        bool publicKey = name.StartsWith("public_key"); byte[] bytes = publicKey ? c.Spki : c.Der;
        if (name.EndsWith(".pem")) File.WriteAllText(d.FileName, PemEncoding.WriteString(publicKey ? "PUBLIC KEY" : "CERTIFICATE", bytes), new UTF8Encoding(false));
        else if (name.EndsWith(".h")) File.WriteAllText(d.FileName, CryptoUtilities.CArray(publicKey ? "public_key_der" : "certificate_der", bytes), new UTF8Encoding(false));
        else File.WriteAllBytes(d.FileName, bytes);
        SetStatus(T("已导出：", "Exported: ") + d.FileName);
    }
}
