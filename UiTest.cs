using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Tempo;

/// <summary>
/// Developer check that drives the real UI (Tempo.exe --demo --uitest out.txt, with TEMPO_DATA_DIR set):
/// every page and tab in every language must build without errors, every control must have a name a screen reader
/// can announce, and no "{0}"-style placeholder may be left on screen. Dialogs are rendered, not shown.
/// </summary>
public static class UiTest
{
    static readonly StringBuilder Log = new();
    static int fails, passes;

    static void Check(string name, bool ok, string? detail = null)
    {
        Log.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(ok || detail == null ? "" : "  -> " + detail)}");
        if (ok) passes++; else fails++;
    }

    static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>(); stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop(); yield return n;
            if (n is UIElement { IsVisible: false }) continue;
            for (int i = VisualTreeHelper.GetChildrenCount(n) - 1; i >= 0; i--) stack.Push(VisualTreeHelper.GetChild(n, i));
        }
    }

    /// <summary>Controls a screen reader would land on that have no name to read out.</summary>
    static List<string> Unnamed(DependencyObject root)
    {
        var bad = new List<string>();
        foreach (var n in Walk(root))
        {
            if (n is not Control c || !c.IsVisible || !c.Focusable) continue;
            if (c is not (ButtonBase or ComboBox or TextBox)) continue;
            if (c.TemplatedParent is ComboBox) continue;   // the combo's own toggle part
            var peer = UIElementAutomationPeer.CreatePeerForElement(c);
            var name = peer?.GetName() ?? "";
            if (string.IsNullOrWhiteSpace(name)) bad.Add(c.GetType().Name + (c is ContentControl { Content: { } ct } ? $"({ct.GetType().Name})" : ""));
        }
        return bad;
    }

    static List<string> LeftoverPlaceholders(DependencyObject root) =>
        Walk(root).OfType<TextBlock>().Where(t => t.IsVisible && System.Text.RegularExpressions.Regex.IsMatch(t.Text ?? "", @"\{\d\}")).Select(t => t.Text).Take(3).ToList();

    public static async void Run(MainWindow win, string path)
    {
        try
        {
            var pages = new[] { "Overview", "Projects", "Clients", "Entries", "Invoices", "Reports", "Settings" };
            foreach (var lang in Loc.Languages)
            {
                Loc.Set(lang.Code); State.Data.Settings.Language = lang.Code;
                win.RefreshLanguage();
                foreach (var key in pages)
                {
                    var tabs = key switch { "Entries" => new[] { "Entries", "Day", "Timesheet", "Expenses" }, "Invoices" => new[] { "Invoices", "Quotes", "Recurring" }, _ => new[] { "" } };
                    foreach (var tab in tabs)
                    {
                        var label = $"[{lang.Code}] {key}{(tab.Length > 0 && tab != key ? " / " + tab : "")}";
                        try
                        {
                            if (key == "Entries") EntriesPage.SetTab(tab);
                            if (key == "Invoices") InvoicesPage.SetTab(tab);
                            win.Navigate(key);
                            await Task.Delay(120);
                            var root = (FrameworkElement)win.Content; root.UpdateLayout();
                            Check($"{label}: builds", true);
                            if (lang.Code == "en")
                            {
                                var bad = Unnamed(root);
                                Check($"{label}: every control has a screen-reader name", bad.Count == 0, string.Join(", ", bad.Distinct().Take(6)));
                            }
                            var ph = LeftoverPlaceholders(root);
                            Check($"{label}: no unfilled {{0}} placeholders", ph.Count == 0, string.Join(" | ", ph));
                        }
                        catch (Exception ex) { Check($"{label}: builds", false, ex.GetType().Name + ": " + ex.Message); }
                    }
                }
            }
            Loc.Set("en"); State.Data.Settings.Language = "en"; win.RefreshLanguage();
            EntriesPage.SetTab("Entries"); InvoicesPage.SetTab("Invoices");

            // dialogs, rendered to files instead of shown
            Dialogs.CaptureDir = System.IO.Path.GetDirectoryName(path);
            void Dlg(string name, Action open) { try { Dialogs.CaptureTag = "uitest-" + name; open(); Check($"dialog {name}: opens", true); } catch (Exception ex) { Check($"dialog {name}: opens", false, ex.Message); } }
            Dlg("project", () => ProjectsPage.EditProject(State.Data.Projects.FirstOrDefault()));
            Dlg("new-project", () => ProjectsPage.EditProject(null));
            Dlg("client", () => ClientsPage.EditClient(State.Data.Clients.FirstOrDefault()));
            Dlg("entry", () => EntriesPage.EditEntry(State.Data.Sessions.FirstOrDefault()));
            Dlg("new-entry", () => EntriesPage.EditEntry(null));
            Dlg("split", () => EntryDialogs.Split(State.Data.Sessions.First()));
            Dlg("expense", () => ExpensesView.Edit(null));
            Dialogs.CaptureDir = null;

            // command palette opens, filters and closes
            win.Navigate("Overview");
            CommandPalette.Open();
            await Task.Delay(150);
            var paletteOpen = win.RootGridPublic.Children.OfType<Grid>().Any(g => Panel.GetZIndex(g) == 300);
            CommandPalette.Open();
            await Task.Delay(50);
            Check("command palette opens and closes", paletteOpen && win.RootGridPublic.Children.OfType<Grid>().All(g => Panel.GetZIndex(g) != 300));
        }
        catch (Exception ex) { Check("ui test ran", false, ex.ToString()); }
        Log.AppendLine(fails == 0 ? $"ALL PASSED ({passes})" : $"{fails} FAILED");
        System.IO.File.WriteAllText(path, Log.ToString());
        Application.Current.Shutdown();
    }
}
