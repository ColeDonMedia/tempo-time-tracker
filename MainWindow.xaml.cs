using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Tempo;

public partial class MainWindow : Window
{
    public static MainWindow Instance { get; private set; } = null!;

    readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly Dictionary<string, RadioButton> nav = new();
    PageBase? current;
    string currentKey = "";
    bool navigating, longWarned, prompting;
    DateTime? idleSince;
    TrayService? tray;
    MiniWindow? mini;

    static readonly (string Key, string Title, string Icon)[] Pages =
    {
        ("Overview", "Overview", Icons.Home),
        ("Projects", "Projects", Icons.Folder),
        ("Entries", "Time entries", Icons.Clock),
        ("Invoices", "Invoices", Icons.Doc),
        ("Settings", "Settings", Icons.Sliders),
    };

    public MainWindow()
    {
        InitializeComponent();
        Instance = this;
        Icon = Ui.LogoBitmap(64);
        LogoHost.Content = Ui.Logo(40);
        SourceInitialized += (_, _) => Backdrop.Apply(this);

        int i = 1;
        foreach (var (key, title, icon) in Pages)
        {
            var rb = new RadioButton { Content = Loc.T(title), Tag = icon, Style = (Style)FindResource("NavButton"), GroupName = "nav", ToolTip = $"{Loc.T(title)} (Ctrl+{i})" };
            var k = key;
            rb.Checked += (_, _) => { if (!navigating) Navigate(k); };
            Nav.Children.Add(rb);
            nav[key] = rb;
            i++;
        }

        Topmost = State.Data.Settings.MainPinned;
        var miniBtn = Ui.Btn("Mini timer", ShowMini, "GlassButton", Icons.Mini, "Mini timer (Ctrl+M)");
        var pinBtn = Ui.Btn("", () => { }, "GlassButton", Icons.Pin, "Keep on top");
        pinBtn.Padding = new Thickness(11, 9, 11, 9);
        pinBtn.Margin = new Thickness(8, 0, 0, 0);
        void PaintPin() => pinBtn.Content = Ui.Icon(Icons.Pin, 16, Topmost ? "AccentBrush" : "MutedBrush");
        PaintPin();
        pinBtn.Click += (_, _) => { Topmost = !Topmost; State.Data.Settings.MainPinned = Topmost; State.Save(); PaintPin(); };
        var viewRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        viewRow.ColumnDefinitions.Add(new ColumnDefinition());
        viewRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        viewRow.Children.Add(miniBtn);
        Grid.SetColumn(pinBtn, 1); viewRow.Children.Add(pinBtn);
        DockPanel.SetDock(viewRow, Dock.Bottom);
        SidebarDock.Children.Insert(SidebarDock.Children.Count - 1, viewRow);
        ApplyChrome();
        State.Changed += OnChanged;
        State.ToastRequested += ShowToast;
        tick.Tick += (_, _) => OnTick();
        tick.Start();
        PreviewKeyDown += OnKey;
        tray = new TrayService(this);
        Closing += (_, e) =>
        {
            State.Save();
            if (State.Data.Settings.CloseToTray && !tray!.Quitting) { e.Cancel = true; Hide(); }
        };
        Closed += (_, _) => { mini?.Shutdown(); tray?.Dispose(); };
        Navigate("Overview");
        UpdateMini();
    }

    void ApplyChrome()
    {
        Tagline.Text = Loc.T("Studio time tracker");
        WorkspaceLabel.Text = Loc.T("WORKSPACE");
        MiniTimer.ToolTip = Loc.T("Open timer");
        int i = 1;
        foreach (var (key, title, _) in Pages)
        {
            nav[key].Content = Loc.T(title);
            nav[key].ToolTip = $"{Loc.T(title)} (Ctrl+{i})";
            i++;
        }
    }

    /// <summary>Re-applies the current language to the whole window without restarting.</summary>
    public void RefreshLanguage()
    {
        ApplyChrome();
        UpdateMini();
        Navigate(currentKey == "" ? "Overview" : currentKey);
    }

    public void Navigate(string key)
    {
        PageBase page = key switch
        {
            "Projects" => new ProjectsPage(),
            "Entries" => new EntriesPage(),
            "Invoices" => new InvoicesPage(),
            "Settings" => new SettingsPage(),
            _ => new OverviewPage(),
        };
        page.Build();
        current = page; currentKey = key;
        navigating = true; nav[key].IsChecked = true; navigating = false;
        PageHost.Content = page;
        Scroller.ScrollToTop();

        PageHost.Opacity = 0;
        var slide = new TranslateTransform(0, 12);
        PageHost.RenderTransform = slide;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });
    }

    /// <summary>Switch to the small always-on-top timer (the full window is hidden, nothing is lost).</summary>
    public void ShowMini()
    {
        mini ??= new MiniWindow();
        mini.Prepare();
        Hide();
        mini.Show();
        mini.Activate();
    }

    /// <summary>Back to the full window.</summary>
    public void ShowFull()
    {
        if (mini is { IsVisible: true }) { mini.SavePosition(); mini.Hide(); }
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public void OpenInvoiceFor(string projectId)
    {
        Navigate("Invoices");
        (current as InvoicesPage)?.SelectProject(projectId);
    }

    void OnChanged()
    {
        UpdateMini();
        if (current is { RebuildOnChange: true } p)
        {
            var off = Scroller.VerticalOffset;
            p.Build();
            Dispatcher.BeginInvoke(() => Scroller.ScrollToVerticalOffset(off), DispatcherPriority.Loaded);
        }
    }

    void OnTick()
    {
        current?.Tick();
        UpdateMini();
        tray?.Update();
        CheckIdle();
        if (State.Running && State.ElapsedMs > 8 * 3600_000L && !longWarned)
        {
            longWarned = true;
            ShowToast("Your timer has been running for over 8 hours. Still working?", true);
        }
        if (!State.Running) longWarned = false;
    }

    void UpdateMini()
    {
        if (!State.HasTimer || currentKey == "Overview") { MiniTimer.Visibility = Visibility.Collapsed; return; }
        MiniTimer.Visibility = Visibility.Visible;
        MiniClock.Text = Fmt.Clock(State.ElapsedMs);
        MiniState.Text = Loc.T(State.Running ? "TIMER RUNNING" : "TIMER PAUSED");
        MiniDot.Opacity = State.Running ? 1 : 0.4;
        var p = State.Proj(State.Data.Timer.ProjectId);
        MiniProject.Text = p?.Name ?? "";
    }

    void MiniTimer_Click(object sender, MouseButtonEventArgs e) => Navigate("Overview");

    void OnKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.M) { e.Handled = true; ShowMini(); }
        else if (e.Key == Key.Space)
        {
            e.Handled = true;
            ToggleTimerShortcut();
        }
        else if (e.Key >= Key.D1 && e.Key <= Key.D5)
        {
            e.Handled = true;
            Navigate(Pages[e.Key - Key.D1].Key);
        }
    }

    public void ToggleTimerShortcut()
    {
        if (State.Running) { State.PauseTimer(); ShowToast("Timer paused"); return; }
        var p = State.Proj(State.Data.Timer.ProjectId);
        if (State.HasTimer && p != null) { State.StartTimer(p.Id, State.Data.Timer.Task); ShowToast("Timer resumed"); return; }
        Navigate("Overview");
        ShowToast("Pick a project, then press Start (Ctrl+Space toggles after that).");
    }

    void CheckIdle()
    {
        var mins = State.Data.Settings.IdleMinutes;
        if (mins <= 0 || !State.Running || prompting) { if (!State.Running) idleSince = null; return; }
        var idle = Idle.IdleFor;
        if (idleSince == null && idle >= TimeSpan.FromMinutes(mins)) idleSince = DateTime.UtcNow - idle;
        else if (idleSince != null && idle < TimeSpan.FromSeconds(3))
        {
            var since = idleSince.Value; idleSince = null;
            PromptIdle(since);
        }
    }

    void PromptIdle(DateTime sinceUtc)
    {
        prompting = true;
        try
        {
            
            var away = DateTime.UtcNow - sinceUtc;
            var opts = new[] { "Keep the time", "Remove idle time and keep going", "Remove idle time and stop the timer" };
            var pick = Ui.Combo(opts, opts[1]);
            var body = new StackPanel();
            body.Children.Add(Ui.Text(Loc.F("You were away for {0} (since {1}). Your timer kept running.", Fmt.Hm((long)away.TotalMilliseconds), Fmt.Time(State.ToLocal(sinceUtc))), 14, "MutedBrush", wrap: true).WithMargin(0, 0, 0, 14));
            body.Children.Add(Ui.Field("WHAT SHOULD TEMPO DO?", pick, 6));
            Dialogs.Show("Welcome back", body, "Apply", () =>
            {
                var i = Array.IndexOf(opts, (string)pick.SelectedItem);
                if (i > 0) { State.RemoveIdle(sinceUtc, i == 2); ShowToast(i == 2 ? "Idle time removed and timer saved" : "Idle time removed"); }
                return true;
            }, 460);
        }
        finally { prompting = false; }
    }

    public void ShowToast(string message, bool error = false)
    {
        ToastText.Text = Loc.T(message);
        ToastDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, error ? "WarnBrush" : "GoodBrush");
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(error ? 5200 : 3000))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(error ? 5600 : 3400))));
        ToastBorder.BeginAnimation(OpacityProperty, a);
    }
}
