using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Tempo;

public class SettingsPage : PageBase
{
    public override bool RebuildOnChange => false;   // fields save themselves; rebuilding would steal focus
    TextBlock? zoneClock;

    public override void Build()
    {
        var d = State.Data; var s = d.Settings; var b = d.Billing;
        var root = new StackPanel();
        root.Children.Add(Ui.Header("Settings", "Make Tempo fit how you work, bill and travel."));

        // ---------- Appearance ----------
        var theme = Ui.Combo(new[] { "System", "Light", "Dark" }, s.Theme);
        theme.SelectionChanged += (_, _) => { s.Theme = (string)theme.SelectedItem; State.Save(); App.ApplyTheme(); State.Notify(); };
        var material = Ui.Combo(Backdrop.Supported ? new[] { "Mica", "Acrylic", "Solid" } : new[] { "Solid" }, Backdrop.Supported ? s.Backdrop : "Solid");
        material.SelectionChanged += (_, _) => { s.Backdrop = (string)material.SelectedItem; State.Save(); foreach (Window w in Application.Current.Windows) Backdrop.Apply(w); };
        var a = new StackPanel();
        a.Children.Add(Section("Appearance", "Glass material follows Windows 11. Older Windows falls back to a solid look."));
        var lang = Ui.Combo(Loc.Languages.ToList(), Loc.CurrentLang);
        lang.SelectionChanged += (_, _) =>
        {
            if (lang.SelectedItem is Loc.Lang l && l.Code != Loc.Current)
            {
                s.Language = l.Code; Loc.Set(l.Code); State.Save();
                MainWindow.Instance.RefreshLanguage();
            }
        };
        a.Children.Add(Pair(Ui.Field("THEME", theme), Ui.Field("WINDOW MATERIAL", material)));
        a.Children.Add(Ui.Field("LANGUAGE", lang, 0));
        root.Children.Add(Block(a));

        // ---------- Time & region ----------
        var zones = TimeZoneInfo.GetSystemTimeZones().ToList();
        var zone = Ui.Combo(zones, zones.FirstOrDefault(z => z.Id == s.TimeZoneId) ?? TimeZoneInfo.Local, "DisplayName");
        zoneClock = Ui.Text("", 12.5, "MutedBrush");
        zone.SelectionChanged += (_, _) =>
        {
            if (zone.SelectedItem is TimeZoneInfo z) { s.TimeZoneId = z.Id; State.Save(); Tick(); State.Toast(Loc.F("Time zone updated. New and existing entries now display in {0}", z.StandardName)); }
        };
        var hour = Ui.Combo(new[] { "12-hour", "24-hour" }, s.Use24Hour ? "24-hour" : "12-hour");
        hour.SelectionChanged += (_, _) => { s.Use24Hour = (string)hour.SelectedItem == "24-hour"; State.Save(); };
        var week = Ui.Combo(new[] { "Monday", "Sunday", "Saturday" }, s.WeekStart);
        week.SelectionChanged += (_, _) => { s.WeekStart = (string)week.SelectedItem; State.Save(); };
        var goal = Ui.Input(Fmt.Dec((decimal)s.WeeklyGoalHours), "30");
        goal.LostFocus += (_, _) => { if (Ui.TryDec(goal.Text, out var g) && g is > 0 and <= 168) { s.WeeklyGoalHours = (double)g; State.Save(); } else goal.Text = Fmt.Dec((decimal)s.WeeklyGoalHours); };
        var t = new StackPanel();
        t.Children.Add(Section("Time & region", "Entries are stored in UTC, so changing zone never corrupts history. Invoices record the zone they were issued in."));
        t.Children.Add(Ui.Field("TIME ZONE", zone, 4));
        t.Children.Add(zoneClock.WithMargin(2, 0, 0, 14));
        t.Children.Add(Pair(Ui.Field("CLOCK", hour), Ui.Field("WEEK STARTS ON", week)));
        t.Children.Add(Ui.Field("WEEKLY GOAL (HOURS)", goal, 0));
        root.Children.Add(Block(t));

        // ---------- Billing defaults ----------
        var cur = Ui.Combo(Fmt.Currencies.ToList(), s.CurrencyCode);
        cur.SelectionChanged += (_, _) => { s.CurrencyCode = (string)cur.SelectedItem; State.Save(); };
        var round = Ui.Combo(new[] { "Exact time", "Up to 6 minutes", "Up to 15 minutes", "Up to 30 minutes" },
            s.RoundingMinutes switch { 6 => "Up to 6 minutes", 15 => "Up to 15 minutes", 30 => "Up to 30 minutes", _ => "Exact time" });
        round.SelectionChanged += (_, _) => { s.RoundingMinutes = (string)round.SelectedItem switch { "Up to 6 minutes" => 6, "Up to 15 minutes" => 15, "Up to 30 minutes" => 30, _ => 0 }; State.Save(); };
        var terms = Ui.Input(s.PaymentTermsDays.ToString(), "14");
        terms.LostFocus += (_, _) => { if (int.TryParse(terms.Text, out var n) && n is >= 0 and <= 365) { s.PaymentTermsDays = n; State.Save(); } else terms.Text = s.PaymentTermsDays.ToString(); };
        var taxL = Ui.Input(s.TaxLabel, "Tax / VAT / GST");
        taxL.LostFocus += (_, _) => { s.TaxLabel = string.IsNullOrWhiteSpace(taxL.Text) ? "Tax" : taxL.Text.Trim(); State.Save(); };
        var taxP = Ui.Input(Fmt.Dec(s.DefaultTaxPercent), "0");
        taxP.LostFocus += (_, _) => { if (Ui.TryDec(taxP.Text, out var n) && n is >= 0 and <= 100) { s.DefaultTaxPercent = n; State.Save(); } else taxP.Text = Fmt.Dec(s.DefaultTaxPercent); };
        var bd = new StackPanel();
        bd.Children.Add(Section("Billing defaults", "Used when you create a new invoice. You can still change them per invoice."));
        bd.Children.Add(Pair(Ui.Field("CURRENCY", cur), Ui.Field("ROUND BILLED TIME", round)));
        bd.Children.Add(Pair(Ui.Field("PAYMENT TERMS (DAYS)", terms), Ui.Field("DEFAULT TAX %", taxP)));
        bd.Children.Add(Ui.Field("TAX LABEL", taxL, 0));
        root.Children.Add(Block(bd));

        // ---------- Billing profile ----------
        var name = Ui.Input(b.Name, "Your name or studio");
        var email = Ui.Input(b.Email, "you@studio.com");
        var addr = Ui.Input(b.Address, "Street, city, postcode"); addr.AcceptsReturn = true; addr.MinHeight = 64; addr.TextWrapping = TextWrapping.Wrap; addr.VerticalContentAlignment = VerticalAlignment.Top;
        var taxId = Ui.Input(b.TaxId, "VAT / EIN (optional)");
        var pay = Ui.Input(b.PaymentInstructions, "Bank transfer details, PayPal link, etc."); pay.AcceptsReturn = true; pay.MinHeight = 80; pay.TextWrapping = TextWrapping.Wrap; pay.VerticalContentAlignment = VerticalAlignment.Top;
        var note = Ui.Input(b.Note, "Thank you for your business.");
        name.LostFocus += (_, _) => { b.Name = name.Text.Trim(); State.Save(); };
        email.LostFocus += (_, _) => { b.Email = email.Text.Trim(); State.Save(); };
        addr.LostFocus += (_, _) => { b.Address = addr.Text.Trim(); State.Save(); };
        taxId.LostFocus += (_, _) => { b.TaxId = taxId.Text.Trim(); State.Save(); };
        pay.LostFocus += (_, _) => { b.PaymentInstructions = pay.Text.Trim(); State.Save(); };
        note.LostFocus += (_, _) => { b.Note = note.Text.Trim(); State.Save(); };

        var logoLbl = Ui.Text(string.IsNullOrEmpty(b.LogoPath) ? Loc.T("No logo") : Path.GetFileName(b.LogoPath), 13, "MutedBrush");
        var logoRow = new StackPanel { Orientation = Orientation.Horizontal };
        logoRow.Children.Add(Ui.Btn("Choose logo…", () => PickLogo(b, logoLbl), "GlassButton"));
        logoRow.Children.Add(Ui.Btn("Remove", () => { b.LogoPath = ""; State.Save(); logoLbl.Text = Loc.T("No logo"); }, "GhostButton").WithMargin(8, 0, 0, 0));
        logoRow.Children.Add(logoLbl.WithMargin(12, 0, 0, 0));
        logoLbl.VerticalAlignment = VerticalAlignment.Center;

        var bp = new StackPanel();
        bp.Children.Add(Section("Billing profile", "Appears on every invoice as the sender."));
        bp.Children.Add(Pair(Ui.Field("NAME OR STUDIO", name), Ui.Field("EMAIL", email)));
        bp.Children.Add(Ui.Field("ADDRESS", addr));
        bp.Children.Add(Pair(Ui.Field("TAX / VAT ID", taxId), Ui.Field("FOOTER NOTE", note)));
        bp.Children.Add(Ui.Field("PAYMENT INSTRUCTIONS", pay));
        bp.Children.Add(Ui.Field("LOGO (PNG OR JPG)", logoRow, 0));
        root.Children.Add(Block(bp));

        // ---------- Behaviour ----------
        var idle = Ui.Combo(new[] { "Off", "5 minutes", "10 minutes", "15 minutes", "30 minutes" },
            s.IdleMinutes switch { 5 => "5 minutes", 10 => "10 minutes", 15 => "15 minutes", 30 => "30 minutes", _ => "Off" });
        idle.SelectionChanged += (_, _) => { s.IdleMinutes = (string)idle.SelectedItem switch { "5 minutes" => 5, "10 minutes" => 10, "15 minutes" => 15, "30 minutes" => 30, _ => 0 }; State.Save(); };
        var tray = Ui.Combo(new[] { "Quit Tempo", "Keep running in the tray" }, s.CloseToTray ? "Keep running in the tray" : "Quit Tempo");
        tray.SelectionChanged += (_, _) => { s.CloseToTray = (string)tray.SelectedItem != "Quit Tempo"; State.Save(); };
        var bh = new StackPanel();
        bh.Children.Add(Section("Behaviour", "Tempo lives in the notification area so the timer is one click away."));
        bh.Children.Add(Pair(Ui.Field("ASK ME AFTER BEING IDLE FOR", idle), Ui.Field("WHEN I CLOSE THE WINDOW", tray)));
        bh.Children.Add(Ui.Text("Idle detection only measures the time since your last keypress or mouse move. It never records what you do.", 12, "FaintBrush", wrap: true));
        root.Children.Add(Block(bh));

        // ---------- Data ----------
        var dp = new StackPanel();
        dp.Children.Add(Section("Your data", Loc.F("{0} projects · {1} entries · {2} invoices. Nothing is ever deleted automatically.", d.Projects.Count, d.Sessions.Count, d.Invoices.Count)));
        dp.Children.Add(Ui.Text(State.DataPath, 12, "FaintBrush").WithMargin(0, 0, 0, 12));
        var row = new WrapPanel();
        row.Children.Add(Ui.Btn("Open data folder", () => { try { Process.Start(new ProcessStartInfo(State.Dir) { UseShellExecute = true }); } catch { } }, "GlassButton").WithMargin(0, 0, 8, 8));
        row.Children.Add(Ui.Btn("Export backup…", ExportBackup, "GlassButton", Icons.Download).WithMargin(0, 0, 8, 8));
        row.Children.Add(Ui.Btn("Restore backup…", RestoreBackup, "GlassButton").WithMargin(0, 0, 8, 8));
        dp.Children.Add(row);
        root.Children.Add(Block(dp));

        // ---------- About ----------
        var ab = new StackPanel();
        var ver = typeof(SettingsPage).Assembly.GetName().Version?.ToString(3) ?? "";
        ab.Children.Add(Section("About", Loc.F("Tempo {0}. A free, open-source studio time tracker for Windows 11.", ver)));
        ab.Children.Add(Ui.Text("Everything, including the invoice PDF writer, is built in. Tempo has no third-party code and makes no network requests.", 12, "FaintBrush", wrap: true));
        root.Children.Add(Block(ab));

        // ---------- Shortcuts ----------
        var sc = new StackPanel();
        sc.Children.Add(Section("Keyboard shortcuts", ""));
        foreach (var (k, v) in new[] { ("Ctrl + Space", "Start / pause the timer"), ("Ctrl + 1 … 5", "Jump between pages"), ("Ctrl + M", "Switch between full and mini view"), ("Enter / Esc", "Confirm / cancel dialogs") })
        {
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) }); g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(Ui.Text(k, 13, "TextBrush", FontWeights.SemiBold));
            var vt = Ui.Text(Loc.T(v), 13, "MutedBrush"); Grid.SetColumn(vt, 1); g.Children.Add(vt);
            sc.Children.Add(g);
        }
        root.Children.Add(Block(sc));

        Content = root;
        Tick();
    }

    public override void Tick()
    {
        if (zoneClock == null) return;
        zoneClock.Text = Loc.F("Current time there: {0}  ·  UTC{1}", Fmt.Time(State.NowLocal), State.OffsetText(DateTime.UtcNow));
    }

    static Border Block(UIElement c) { var b = Ui.Card(c); b.Margin = new Thickness(0, 0, 0, 18); return b; }

    static StackPanel Section(string title, string sub)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        sp.Children.Add(Ui.Text(Loc.T(title), 18, "TextBrush", FontWeights.SemiBold));
        if (sub.Length > 0) sp.Children.Add(Ui.Text(Loc.T(sub), 13, "MutedBrush", wrap: true).WithMargin(0, 2, 0, 0));
        return sp;
    }

    static Grid Pair(FrameworkElement a, FrameworkElement b)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); g.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(b, 2); g.Children.Add(a); g.Children.Add(b);
        return g;
    }

    static void PickLogo(BillingProfile b, TextBlock label)
    {
        var dlg = new OpenFileDialog { Title = "Choose a logo", Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var bmp = new BitmapImage(); bmp.BeginInit(); bmp.UriSource = new Uri(dlg.FileName); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit();   // validates it's a real image
            var dest = Path.Combine(State.Dir, "logo" + Path.GetExtension(dlg.FileName).ToLowerInvariant());
            File.Copy(dlg.FileName, dest, true);
            b.LogoPath = dest; State.Save(); label.Text = Path.GetFileName(dlg.FileName);
        }
        catch { State.Toast("That file couldn't be used as a logo.", true); }
    }

    static void ExportBackup()
    {
        State.Save();
        var dlg = new SaveFileDialog { Title = "Export backup", Filter = "Tempo backup (*.json)|*.json", FileName = $"tempo-backup-{DateTime.Now:yyyy-MM-dd}.json" };
        if (dlg.ShowDialog() != true) return;
        try { File.Copy(State.DataPath, dlg.FileName, true); State.Toast("Backup saved"); }
        catch (Exception ex) { State.Toast(Loc.T("Backup failed: ") + ex.Message, true); }
    }

    static void RestoreBackup()
    {
        var dlg = new OpenFileDialog { Title = "Restore backup", Filter = "Tempo data (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;
        Workspace? ws;
        try { ws = JsonSerializer.Deserialize<Workspace>(File.ReadAllText(dlg.FileName), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch { State.Toast("That file isn't a valid Tempo backup.", true); return; }
        if (ws == null) { State.Toast("That file isn't a valid Tempo backup.", true); return; }
        if (!Dialogs.Confirm("Restore backup", Loc.F("Replace your current data with this backup ({0} projects, {1} entries)? Your current data is saved as a .bak copy first.", ws.Projects?.Count ?? 0, ws.Sessions?.Count ?? 0), "Restore", true)) return;
        State.Save();
        try { File.Copy(State.DataPath, State.DataPath + ".before-restore", true); } catch { }
        State.Data = ws; State.Normalize(); App.ApplyTheme();
        State.Notify(); MainWindow.Instance.Navigate("Overview"); State.Toast("Backup restored");
    }
}
