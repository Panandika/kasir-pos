using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kasir.Avalonia.Forms.Admin;
using Kasir.Avalonia.Forms.Master;
using Kasir.Avalonia.Forms.POS;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Security;
using NUnit.Framework;

namespace Kasir.Avalonia.Tests.Forms;

// Owner report #20 (v2.10.1): "Lanjutkan"/"Batal" on the cloud pairing screen and
// "OK"/"Tutup" on the calculator showed their label stuck in the top-left corner of the
// box, and some labels were cut off ("Batal (Esc)"). Root cause: Fluent leaves Button
// content Stretch-aligned, so with a fixed Width/Height the label TextBlock filled the
// box, and fixed Width clipped longer labels. The shared Button style now centers the
// label and pads it, and views use MinWidth/MinHeight so a button grows instead of
// clipping. These tests render real views (Skia text shaping) at the store PCs' screen
// sizes and check every Button label fits and is centered.
[TestFixture]
[NonParallelizable]
public class ButtonLayoutTests
{
    private string _credDir = "";

    [SetUp]
    public void SetUp()
    {
        _credDir = Path.Combine(Path.GetTempPath(), "kasir-btn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_credDir);
        CloudSyncCredentialStore.DirectoryOverride = _credDir;
    }

    [TearDown]
    public void TearDown()
    {
        CloudSyncCredentialStore.DirectoryOverride = null;
        try { Directory.Delete(_credDir, true); } catch { }
    }

    // Views that can be built without a database. DB-backed views are covered by
    // Axaml_buttons_do_not_use_fixed_size below.
    private static readonly Dictionary<string, Func<Control>> Views = new()
    {
        ["FirstRunView"] = () => new FirstRunView(),
        ["CloudImportView"] = () => new CloudImportView(),
        ["CloudSyncSetupView"] = () => new CloudSyncSetupView(),
        ["BackupView"] = () => new BackupView(),
        ["CalculatorDialogOverlay"] = () => new CalculatorDialogOverlay(),
        ["WholesaleTierOverlay"] = () => new WholesaleTierOverlay(),
        ["MsgBoxOverlay.Confirm"] = () => new MsgBoxOverlay("Konfirmasi", "Tinggalkan transaksi yang sedang berjalan?", true),
        ["InputDialogOverlay"] = () => new InputDialogOverlay("Detail Item", new[] { "Qty", "Harga" }, new[] { "1", "0" }),
    };

    private static IEnumerable<TestCaseData> ViewCases()
    {
        foreach (var name in Views.Keys)
            foreach (var (w, h) in new[] { (1024, 768), (1366, 768) })
                foreach (var variant in new[] { "Dark", "Light" })
                    yield return new TestCaseData(name, w, h, variant).SetName($"Buttons_fit_and_center_{name}_{w}x{h}_{variant}");
    }

    [AvaloniaTest]
    [TestCaseSource(nameof(ViewCases))]
    public void Buttons_fit_and_are_centered(string viewName, int width, int height, string variant)
    {
        Application.Current!.RequestedThemeVariant = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var window = new Window { Width = width, Height = height, Content = Views[viewName]() };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var buttons = AuthoredButtons(window).ToList();
            Assert.That(buttons, Is.Not.Empty, $"{viewName}: expected at least one visible button");
            var problems = buttons.SelectMany(Problems).ToList();
            Assert.That(problems, Is.Empty, $"{viewName} {width}x{height} {variant}:\n" + string.Join("\n", problems));
        }
        finally
        {
            window.Close();
        }
    }

    // Root-cause guard: a plain Button with a big fixed box must show its label in the
    // middle, not the top-left corner (what the owner saw on the pairing screen).
    [AvaloniaTest]
    public void Shared_style_centers_label_in_oversized_button()
    {
        var button = new Button { Content = "Lanjutkan", Width = 240, Height = 80 };
        var window = new Window { Width = 400, Height = 300, Content = button };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.That(button.HorizontalContentAlignment, Is.EqualTo(global::Avalonia.Layout.HorizontalAlignment.Center));
            Assert.That(button.VerticalContentAlignment, Is.EqualTo(global::Avalonia.Layout.VerticalAlignment.Center));
            Assert.That(Problems(button), Is.Empty);
        }
        finally
        {
            window.Close();
        }
    }

    // Shared style lets a button grow to fit its label instead of clipping it.
    [AvaloniaTest]
    public void Shared_style_grows_button_to_fit_long_label()
    {
        var button = new Button { Content = "Konfirmasi Pembayaran (Enter)", MinWidth = 60 };
        var panel = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Children = { button } };
        var window = new Window { Width = 800, Height = 200, Content = panel };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.That(button.Padding.Left, Is.GreaterThanOrEqualTo(8), "label needs horizontal breathing room");
            Assert.That(button.Bounds.Height, Is.GreaterThanOrEqualTo(32), "MinHeight comes from ControlHeight");
            Assert.That(Problems(button), Is.Empty);
        }
        finally
        {
            window.Close();
        }
    }

    // DB-backed screens (SaleView overlays, PaymentOverlay, ShiftView, LoginView, reports,
    // master data) cannot be built here, so guard the pattern that caused the clipping:
    // a fixed Width/Height on a text Button. Icon-only buttons opt out with Padding="0".
    [Test]
    public void Axaml_buttons_do_not_use_fixed_size()
    {
        var root = FindAvaloniaProjectDir();
        var offenders = new List<string>();
        var element = new Regex(@"<Button\b[^>]*>", RegexOptions.Singleline);
        var fixedSize = new Regex(@"\s(Width|Height)=""");
        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in element.Matches(text))
            {
                if (m.Value.Contains("Padding=\"0\"")) continue;
                if (fixedSize.IsMatch(m.Value))
                    offenders.Add($"{Path.GetRelativePath(root, file)}: {Regex.Replace(m.Value, @"\s+", " ")}");
            }
        }
        var codeButton = new Regex(@"new Button\s*\{[^}]*\}", RegexOptions.Singleline);
        var codeFixed = new Regex(@"(^|[\s,{])(Width|Height)\s*=");
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
            var text = File.ReadAllText(file);
            foreach (Match m in codeButton.Matches(text))
            {
                if (m.Value.Contains("Padding = new Thickness(0)")) continue;
                if (codeFixed.IsMatch(m.Value))
                    offenders.Add($"{Path.GetRelativePath(root, file)}: {Regex.Replace(m.Value, @"\s+", " ")}");
            }
        }
        Assert.That(offenders, Is.Empty,
            "Text buttons must use MinWidth/MinHeight (fixed sizes clip Indonesian labels):\n" + string.Join("\n", offenders));
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static IEnumerable<Button> AuthoredButtons(Visual root) =>
        root.GetVisualDescendants().OfType<Button>()
            .Where(b => b.TemplatedParent == null && b.IsEffectivelyVisible && b.Bounds.Width > 0);

    internal static IEnumerable<string> Problems(Button b)
    {
        var id = $"{b.Name ?? b.GetType().Name} '{b.Content as string ?? b.Content?.GetType().Name}'";
        var presenter = b.GetVisualDescendants().OfType<ContentPresenter>()
            .FirstOrDefault(p => p.Name == "PART_ContentPresenter");
        var child = presenter?.Child;
        if (child == null) yield break;

        var innerW = b.Bounds.Width - b.Padding.Left - b.Padding.Right - b.BorderThickness.Left - b.BorderThickness.Right;
        var innerH = b.Bounds.Height - b.Padding.Top - b.Padding.Bottom - b.BorderThickness.Top - b.BorderThickness.Bottom;

        double textW, textH;
        if (b.Content is string s)
        {
            var probe = new TextBlock { Text = s, FontFamily = b.FontFamily, FontSize = b.FontSize, FontWeight = b.FontWeight };
            probe.Measure(Size.Infinity);
            textW = probe.DesiredSize.Width;
            textH = probe.DesiredSize.Height;
        }
        else
        {
            textW = child.DesiredSize.Width;
            textH = child.DesiredSize.Height;
        }

        if (textW > innerW + 0.5)
            yield return $"{id}: label {textW:F1}px wider than space inside padding {innerW:F1}px (button {b.Bounds.Width:F0}px)";
        if (textH > innerH + 0.5)
            yield return $"{id}: label {textH:F1}px taller than space inside padding {innerH:F1}px (button {b.Bounds.Height:F0}px)";

        if (b.Content is string)
        {
            // A stretched label TextBlock draws its text at its top-left = off-center.
            if (child is TextBlock tb && tb.TextAlignment != TextAlignment.Center && child.Bounds.Width > textW + 2)
                yield return $"{id}: label stretched to {child.Bounds.Width:F1}px for {textW:F1}px of text (left-aligned)";
            if (child.Bounds.Height > textH + 2)
                yield return $"{id}: label stretched to {child.Bounds.Height:F1}px for {textH:F1}px of text (top-aligned)";
        }

        if (child.TranslatePoint(new Point(0, 0), b) is { } p)
        {
            var left = p.X;
            var right = b.Bounds.Width - (p.X + child.Bounds.Width);
            var top = p.Y;
            var bottom = b.Bounds.Height - (p.Y + child.Bounds.Height);
            if (Math.Abs(left - right) > 2) yield return $"{id}: not horizontally centered (left {left:F1}, right {right:F1})";
            if (Math.Abs(top - bottom) > 2) yield return $"{id}: not vertically centered (top {top:F1}, bottom {bottom:F1})";
        }
    }

    private static string FindAvaloniaProjectDir()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Kasir.Avalonia");
            if (File.Exists(Path.Combine(candidate, "Kasir.Avalonia.csproj"))) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Kasir.Avalonia project folder not found above " + TestContext.CurrentContext.TestDirectory);
    }
}
