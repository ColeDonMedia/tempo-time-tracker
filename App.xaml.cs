using System.Windows;
using Microsoft.Win32;

namespace Tempo;

public partial class App : Application
{
    static Mutex? _single;
    public static bool IsDark { get; private set; } = true;
    public static string[] Args = Array.Empty<string>();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Args = e.Args;

        // One window at a time: two instances would overwrite each other's data file.
        _single = new Mutex(true, "Tempo.Studio.SingleInstance." + State.Dir.GetHashCode(), out bool first);
        if (!first)
        {
            MessageBox.Show("Tempo is already running.", "Tempo", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            State.Save();
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(State.Dir, "tempo-error.log"), DateTime.Now + Environment.NewLine + ex.Exception + Environment.NewLine + Environment.NewLine); } catch { }
            ex.Handled = true;
            if (MessageBox.Show(Loc.T("Something went wrong, but your data is saved.") + "\n\n" + ex.Exception.Message + "\n\n" + Loc.T("Report this problem on GitHub? You'll see exactly what is sent before anything is posted."),
                    "Tempo", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                ReportProblem();
        };

        State.Load();
        Loc.Init();
        if (Args.Contains("--lang") && Environment.GetEnvironmentVariable("TEMPO_DATA_DIR") != null)
        {
            var li = Array.IndexOf(Args, "--lang");
            if (li + 1 < Args.Length) { State.Data.Settings.Language = Args[li + 1]; Loc.Set(Args[li + 1]); }
        }
        if (Args.Contains("--demo") && Environment.GetEnvironmentVariable("TEMPO_DATA_DIR") != null)
            DemoData.Seed();
        if (Args.Contains("--selftest") && Environment.GetEnvironmentVariable("TEMPO_DATA_DIR") != null)
        {
            SelfTest.Run(Args[Array.IndexOf(Args, "--selftest") + 1]);
            Shutdown(); return;
        }
        if (Args.Contains("--make-icon"))
        {
            MakeIcon(Args[Array.IndexOf(Args, "--make-icon") + 1]);
            Shutdown(); return;
        }
        if (Args.Contains("--sample-pdf") && Environment.GetEnvironmentVariable("TEMPO_DATA_DIR") != null)
        {
            // Realistic sample invoice from the fictional demo data (used for the README image).
            var pr = State.Data.Projects.First(x => x.Id == "p4");
            var (lines, sampleSessions) = InvoiceService.BuildLines(pr, State.TodayLocal.AddDays(-30), State.TodayLocal, "Task", 0);
            var b = State.Data.Billing;
            var sample = new Invoice
            {
                Language = Loc.Current, Number = "INV-2026-003", ProjectId = pr.Id, ProjectName = pr.Name, ClientName = pr.Client, ClientEmail = "billing@orbitlabs.example",
                ClientAddress = "400 Market Street, Suite 12\nSan Francisco, CA 94111", PoNumber = "PO-7731",
                IssuedUtc = DateTime.UtcNow, DueUtc = DateTime.UtcNow.AddDays(14), PeriodFrom = State.TodayLocal.AddDays(-30), PeriodTo = State.TodayLocal,
                TimeZoneId = State.Tz.Id, Currency = "USD", Lines = lines, TaxLabel = "Sales tax", TaxPercent = 8.5m,
                Note = b.Note, PaymentInstructions = b.PaymentInstructions, StudioName = b.Name, StudioEmail = b.Email, StudioAddress = b.Address
            };
            var ti = Array.IndexOf(Args, "--template");
            sample.Template = ti >= 0 && ti + 1 < Args.Length ? Args[ti + 1] : "Classic";
            sample.PayLink = "https://pay.example.com/invoice/INV-2026-003?amount=1129.05";
            sample.IncludeTimesheet = true; sample.Timesheet = InvoiceService.TimesheetLines(sampleSessions);
            if (Args.Contains("--payments")) { sample.Payments.Add(new Payment { Amount = 300m, DateUtc = DateTime.UtcNow.AddDays(-5) }); sample.LateFeeAmount = 25m; }
            if (Args.Contains("--quote"))
            {
                sample.Kind = "Quote"; sample.Number = "QUO-2026-004"; sample.Status = "Accepted"; sample.PayLink = ""; sample.IncludeTimesheet = false; sample.Timesheet.Clear();
                sample.Lines = new() { new InvoiceLine { Description = "Investor deck design (20 slides)", Hours = 1, Rate = 2400, Amount = 2400, Kind = "Custom" }, new InvoiceLine { Description = "Two rounds of revisions", Hours = 2, Rate = 150, Amount = 300, Kind = "Custom" } };
            }
            if (Args.Contains("--taxes")) sample.Taxes = new() { new TaxLine { Label = "GST", Percent = 5 }, new TaxLine { Label = "PST", Percent = 7 } };
            if (Args.Contains("--reverse-charge")) { sample.ReverseCharge = true; sample.ClientTaxId = "DE123456789"; }
            InvoiceService.ApplyTotals(sample);
            InvoiceService.WritePdf(sample, Args[Array.IndexOf(Args, "--sample-pdf") + 1]);
            Shutdown(); return;
        }
        if (Args.Contains("--selftest-pdf") && Environment.GetEnvironmentVariable("TEMPO_DATA_DIR") != null)
        {
            var inv = State.Data.Invoices.First(); inv.StudioAddress = "12 Harbour Street, Portland, OR 97201"; inv.Lines.AddRange(Enumerable.Range(1, 30).Select(i => new InvoiceLine { Description = "Žluťoučký kůň — 日本語 中文 한국어 — línea " + i, Hours = 1.25m, Rate = 95, Amount = 118.75m })); InvoiceService.ApplyTotals(inv); inv.TaxPercent = 8.5m; inv.TaxAmount = Math.Round((inv.Subtotal - inv.DiscountAmount) * 0.085m, 2); inv.Total = inv.Subtotal + inv.TaxAmount;
            InvoiceService.WritePdf(inv, Args[Array.IndexOf(Args, "--selftest-pdf") + 1]);
            Shutdown(); return;
        }
        var shot = (Args.Contains("--shot") || Args.Contains("--uitest")) && Environment.GetEnvironmentVariable("TEMPO_DATA_DIR") != null;
        if (shot)
        {
            State.Data.Settings.Backdrop = "Solid";
            var ti = Array.IndexOf(Args, "--theme");
            if (ti >= 0 && ti + 1 < Args.Length) State.Data.Settings.Theme = Args[ti + 1];
        }
        ApplyTheme();
        SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            if (State.Data.Settings.Theme == "System") ApplyTheme();
        });

        var win = new MainWindow();
        MainWindow = win;
        if (shot)
        {
            win.Width = 1360; win.Height = 880; win.WindowStartupLocation = WindowStartupLocation.Manual; win.Left = -6000; win.Top = 0;
            win.Show();
            if (Args.Contains("--uitest")) UiTest.Run(win, Args[Array.IndexOf(Args, "--uitest") + 1]);
            else RunShots(win, Args[Array.IndexOf(Args, "--shot") + 1]);
            return;
        }
        // Started by Windows at sign-in ("Start with Windows"): stay in the tray until clicked.
        if (!Args.Contains("--background")) win.Show();
        if (State.LoadNotice != null) State.Toast(State.LoadNotice, true);
    }

    /// <summary>Developer tool: renders each page of the app window to PNG (used for the README). Only with demo data.</summary>
    static async void RunShots(MainWindow win, string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        var prefix = (Loc.Current == "en" ? "" : Loc.Current + "-") + (IsDark ? "dark" : "light");
        foreach (var (key, name) in new[] { ("Overview", "overview"), ("Projects", "projects"), ("Clients", "clients"), ("Entries", "entries"), ("Reports", "reports"), ("Invoices", "invoices"), ("Settings", "settings") })
        {
            win.Navigate(key);
            await Task.Delay(1200);
            var root = (FrameworkElement)win.Content;
            root.UpdateLayout();
            int w = (int)root.ActualWidth, h = (int)root.ActualHeight;
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            var bg = new System.Windows.Media.DrawingVisual();
            using (var dc = bg.RenderOpen()) dc.DrawRectangle(win.Background, null, new Rect(0, 0, w, h));
            rtb.Render(bg); rtb.Render(root);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using var fs = System.IO.File.Create(System.IO.Path.Combine(dir, $"{prefix}-{name}.png"));
            enc.Save(fs);
        }
        // the Entries page's other two tabs
        foreach (var (tabName, file) in new[] { ("Day", "day"), ("Timesheet", "timesheet"), ("Expenses", "expenses") })
        {
            EntriesPage.SetTab(tabName);
            win.Navigate("Entries");
            await Task.Delay(1000);
            var tr = (FrameworkElement)win.Content; tr.UpdateLayout();
            var trb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)tr.ActualWidth, (int)tr.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            var tbg = new System.Windows.Media.DrawingVisual();
            using (var dc = tbg.RenderOpen()) dc.DrawRectangle(win.Background, null, new Rect(0, 0, (int)tr.ActualWidth, (int)tr.ActualHeight));
            trb.Render(tbg); trb.Render(tr);
            var tenc = new System.Windows.Media.Imaging.PngBitmapEncoder(); tenc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(trb));
            using (var tfs = System.IO.File.Create(System.IO.Path.Combine(dir, $"{prefix}-{file}.png"))) tenc.Save(tfs);
        }
        EntriesPage.SetTab("Entries");
        async Task Snap(string file)
        {
            await Task.Delay(900);
            var r = (FrameworkElement)win.Content; r.UpdateLayout();
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)r.ActualWidth, (int)r.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            var bgv = new System.Windows.Media.DrawingVisual();
            using (var dc = bgv.RenderOpen()) dc.DrawRectangle(win.Background, null, new Rect(0, 0, (int)r.ActualWidth, (int)r.ActualHeight));
            bmp.Render(bgv); bmp.Render(r);
            var en = new System.Windows.Media.Imaging.PngBitmapEncoder(); en.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var f = System.IO.File.Create(System.IO.Path.Combine(dir, $"{prefix}-{file}.png")); en.Save(f);
        }
        // bulk selection bar
        foreach (var s in State.Data.Sessions.OrderByDescending(x => x.StartedAtUtc).Take(3)) EntriesPage.Selected.Add(s.Id);
        win.Navigate("Entries");
        await Snap("entries-bulk");
        EntriesPage.Selected.Clear();
        // lower half of Settings (local check only: Settings shots show file paths, never publish them)
        win.Navigate("Settings");
        await Task.Delay(600);
        win.Scroller.ScrollToVerticalOffset(2350);
        await Snap("settings-lower");
        // integrations card in its connected state (fake key in the scratch data folder; nothing is contacted)
        SecretStore.Set(State.Dir, IntegrationClient.GitHub, "screenshot-not-a-real-token");
        win.Navigate("Settings");
        await Task.Delay(600);
        win.Scroller.ScrollToVerticalOffset(3150);
        await Snap("settings-integrations");
        SecretStore.Set(State.Dir, IntegrationClient.GitHub, null);
        // command palette
        win.Navigate("Overview");
        CommandPalette.Open();
        await Snap("palette");
        CommandPalette.Open();   // toggles closed
        foreach (var (tabName, file) in new[] { ("Quotes", "quotes"), ("Recurring", "recurring") })
        {
            InvoicesPage.SetTab(tabName);
            win.Navigate("Invoices");
            await Task.Delay(1000);
            var ir = (FrameworkElement)win.Content; ir.UpdateLayout();
            var irb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ir.ActualWidth, (int)ir.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            var ibg = new System.Windows.Media.DrawingVisual();
            using (var dc = ibg.RenderOpen()) dc.DrawRectangle(win.Background, null, new Rect(0, 0, (int)ir.ActualWidth, (int)ir.ActualHeight));
            irb.Render(ibg); irb.Render(ir);
            var ienc = new System.Windows.Media.Imaging.PngBitmapEncoder(); ienc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(irb));
            using (var ifs = System.IO.File.Create(System.IO.Path.Combine(dir, $"{prefix}-{file}.png"))) ienc.Save(ifs);
        }
        InvoicesPage.SetTab("Invoices");

        // dialogs (rendered, not shown)
        Dialogs.CaptureDir = dir;
        void Dlg(string tag, Action show) { Dialogs.CaptureTag = (Loc.Current == "en" ? "" : Loc.Current + "-") + tag; try { show(); } catch { } }
        Dlg("project-edit", () => ProjectsPage.EditProject(State.Data.Projects.FirstOrDefault()));
        Dlg("client-edit", () => ClientsPage.EditClient(State.Data.Clients.FirstOrDefault()));
        Dlg("entry-edit", () => EntriesPage.EditEntry(State.Data.Sessions.FirstOrDefault()));
        Dialogs.CaptureDir = null;

        // the compact view, with a demo timer running
        var t = State.Data.Timer;
        t.ProjectId = State.Data.Projects.FirstOrDefault()?.Id; t.Task = "Brand sketches";
        t.AccumulatedMilliseconds = 4_320_000; t.StartedAtUtc = DateTime.UtcNow.AddHours(-1); t.RunSinceUtc = DateTime.UtcNow;
        var miniWin = new MiniWindow { Left = -6000, Top = 0 };
        miniWin.Show();
        await Task.Delay(1200);
        var mroot = (FrameworkElement)miniWin.Content;
        mroot.UpdateLayout();
        int mw = (int)mroot.ActualWidth, mh = (int)mroot.ActualHeight;
        var mrtb = new System.Windows.Media.Imaging.RenderTargetBitmap(mw, mh, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        var mbg = new System.Windows.Media.DrawingVisual();
        using (var dc = mbg.RenderOpen()) dc.DrawRectangle(miniWin.Background, null, new Rect(0, 0, mw, mh));
        mrtb.Render(mbg); mrtb.Render(mroot);
        var menc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        menc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(mrtb));
        using (var mfs = System.IO.File.Create(System.IO.Path.Combine(dir, $"{prefix}-mini.png"))) menc.Save(mfs);
        miniWin.Shutdown();
        Current.Shutdown();
    }

    /// <summary>Opens a pre-filled GitHub issue in the browser. Nothing is sent until the user reviews it and presses Submit there.</summary>
    public static void ReportProblem()
    {
        string log = "";
        try
        {
            var path = System.IO.Path.Combine(State.Dir, "tempo-error.log");
            if (System.IO.File.Exists(path)) log = System.IO.File.ReadAllText(path);
        }
        catch { }
        var url = Problem.IssueUrl(UpdateService.CurrentVersion, $"{Environment.OSVersion.VersionString}", log, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName, Environment.MachineName, State.Data.Settings.SyncFolder);
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    /// <summary>Developer tool: builds a multi-size .ico (PNG-compressed) from the Tempo logo.</summary>
    static void MakeIcon(string path)
    {
        var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
        var pngs = sizes.Select(px =>
        {
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(Ui.LogoBitmap(px)));
            using var ms = new System.IO.MemoryStream(); enc.Save(ms); return ms.ToArray();
        }).ToList();
        using var fs = System.IO.File.Create(path);
        using var w = new System.IO.BinaryWriter(fs);
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0); w.Write((byte)0); w.Write((ushort)1); w.Write((ushort)32);
            w.Write(pngs[i].Length); w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var b in pngs) w.Write(b);
    }

    public static void ApplyTheme()
    {
        var t = State.Data.Settings.Theme;
        IsDark = t == "Dark" || (t != "Light" && SystemUsesDark());
        var dict = new ResourceDictionary { Source = new Uri(IsDark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative) };
        Current.Resources.MergedDictionaries[0] = dict;
        foreach (Window w in Current.Windows) Backdrop.Apply(w);
    }

    static bool SystemUsesDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return true; }
    }
}
