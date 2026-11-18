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
    public Grid RootGridPublic => RootGrid;

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
        ("Clients", "Clients", Icons.Users),
        ("Entries", "Time entries", Icons.Clock),
        ("Invoices", "Invoices", Icons.Doc),
        ("Reports", "Reports", Icons.Chart),
        ("Settings", "Settings", Icons.Sliders),
    };

    public MainWindow()
    {
        InitializeComponent();
        Instance = this;
        Icon = Ui.LogoBitmap(64);
        LogoHost.Content = Ui.Logo(40);
        SourceInitialized += (_, _) => Backdrop.Apply(this);
        StateChanged += (_, _) =>
        {
            MaxGlyph.Data = Geometry.Parse(WindowState == WindowState.Maximized ? "M2.5,2.5 V0.5 H9.5 V7.5 H7.5 M0.5,2.5 H7.5 V9.5 H0.5 Z" : "M0.5,0.5 H9.5 V9.5 H0.5 Z");
            MaxBtn.ToolTip = Loc.T(WindowState == WindowState.Maximized ? "Restore down" : "Maximize");
            var b = SystemParameters.WindowResizeBorderThickness;
            RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(b.Left + 2, b.Top + 2, b.Right + 2, b.Bottom + 2) : new Thickness(0);
        };

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
        BuildSearchButton();
        ApplyChrome();
        State.Changed += OnChanged;
        State.ToastRequested += ShowToast;
        State.ToastActionRequested += ShowToastAction;
        tick.Tick += (_, _) => OnTick();
        tick.Start();
        PreviewKeyDown += OnKey;
        tray = new TrayService(this);
        hotkey = new HotkeyService();
        hotkey.Pressed += () => Dispatcher.BeginInvoke(new Action(ToggleTimerGlobal));
        ApplyHotkey(false);
        Closing += (_, e) =>
        {
            State.Save();
            if (State.Data.Settings.CloseToTray && !tray!.Quitting) { e.Cancel = true; Hide(); return; }
            BackupService.RunNow();
        };
        Closed += (_, _) => { mini?.Shutdown(); tray?.Dispose(); hotkey?.Dispose(); };
        Navigate("Overview");
        UpdateMini();
        Dispatcher.BeginInvoke(new Action(RunRecurring), DispatcherPriority.Loaded);
        Dispatcher.BeginInvoke(new Action(() => { lastBackupCheck = DateTime.UtcNow; BackupService.RunIfDue(); }), DispatcherPriority.ApplicationIdle);
        Dispatcher.BeginInvoke(new Action(CheckForUpdate), DispatcherPriority.ApplicationIdle);
    }

    DateTime lastUpdateCheck = DateTime.MinValue;

    /// <summary>Only when the user turned it on in Settings: at most once a day, ask GitHub for a newer release.</summary>
    public async void CheckForUpdate()
    {
        if (!State.Data.Settings.CheckUpdates || (DateTime.UtcNow - lastUpdateCheck).TotalHours < 24) return;
        lastUpdateCheck = DateTime.UtcNow;
        if (await UpdateService.CheckAsync() is not { } rel) return;
        ShowToastAction(Loc.F("Tempo {0} is available.", rel.Tag.TrimStart('v')), Loc.T("View release"), () =>
        {
            if (UpdateService.IsTrustedUrl(rel.Url))
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(rel.Url) { UseShellExecute = true }); } catch { }
        });
    }

    HotkeyService? hotkey;
    Button? searchBtn;
    DateTime lastBackupCheck = DateTime.UtcNow;
    DateTime? notTrackingSince, lastReminder, lastEndOfDayWarned;

    /// <summary>Registers the global start/pause shortcut from Settings. Returns false if another app owns it.</summary>
    public bool ApplyHotkey(bool tell = true)
    {
        if (hotkey == null) return false;
        var ok = hotkey.Apply(State.Data.Settings.Hotkey);
        if (!ok && tell) ShowToast("That shortcut is already used by another app.", true);
        return ok;
    }

    /// <summary>The global shortcut: pause if running, else resume (or restart on the last project), else open Tempo.</summary>
    void ToggleTimerGlobal()
    {
        if (State.Running) { State.PauseTimer(); Notify("Timer paused"); return; }
        var p = State.Proj(State.Data.Timer.ProjectId)
                ?? State.Data.Sessions.OrderByDescending(s => s.EndedAtUtc).Select(s => State.Proj(s.ProjectId)).FirstOrDefault(x => x is { Archived: false });
        if (p != null)
        {
            State.StartTimer(p.Id, State.HasTimer ? State.Data.Timer.Task : "");
            Notify(Loc.F("Now tracking {0}", p.Name));
            return;
        }
        ShowFull(); Navigate("Overview");
    }

    /// <summary>A toast when Tempo is on screen, a Windows notification when it's hidden in the tray or mini view.</summary>
    void Notify(string text)
    {
        if (IsVisible && WindowState != WindowState.Minimized) ShowToast(text);
        else tray?.Balloon("Tempo", Loc.T(text));
    }

    void CheckReminders()
    {
        var s = State.Data.Settings;
        var nowUtc = DateTime.UtcNow;
        if (State.Running) notTrackingSince = null;
        else notTrackingSince ??= nowUtc;
        if (Reminders.ShouldRemind(State.Running, Idle.IdleFor, notTrackingSince, lastReminder, s.ReminderMinutes, nowUtc))
        {
            lastReminder = nowUtc;
            Notify("You're not tracking time. Want to start the timer?");
        }
        var nowLocal = State.ToLocal(nowUtc);
        if (Reminders.ShouldWarnEndOfDay(State.Running, s.EndOfDayTime, nowLocal, lastEndOfDayWarned))
        {
            lastEndOfDayWarned = nowLocal.Date;
            Notify("Your timer is still running. Did you forget to stop it?");
        }
    }

    DateTime lastRecurringCheck = DateTime.MinValue;
    void RunRecurring()
    {
        lastRecurringCheck = DateTime.UtcNow;
        var n = RecurringService.RunDue();
        if (n > 0) ShowToast(Loc.F("{0} recurring invoice(s) created. Open Invoices to export them.", n));
    }

    void Min_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    void Max_Click(object sender, RoutedEventArgs e) { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    void Close_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    void ApplyChrome()
    {
        MinBtn.ToolTip = Loc.T("Minimize");
        MaxBtn.ToolTip = Loc.T(WindowState == WindowState.Maximized ? "Restore down" : "Maximize");
        CloseBtn.ToolTip = Loc.T("Close");
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
    void BuildSearchButton()
    {
        if (searchBtn != null) SidebarDock.Children.Remove(searchBtn);
        searchBtn = Ui.Btn("Search…", CommandPalette.Open, "GlassButton", Icons.Search, "Search or jump to…");
        searchBtn.ToolTip += " (Ctrl+K)";
        searchBtn.HorizontalContentAlignment = HorizontalAlignment.Left;
        searchBtn.Margin = new Thickness(0, 0, 0, 18);
        DockPanel.SetDock(searchBtn, Dock.Top);
        SidebarDock.Children.Insert(1, searchBtn);
    }

    public void RefreshLanguage()
    {
        BuildSearchButton();
        ApplyChrome();
        UpdateMini();
        Navigate(currentKey == "" ? "Overview" : currentKey);
    }

    public void Navigate(string key)
    {
        PageBase page = key switch
        {
            "Projects" => new ProjectsPage(),
            "Clients" => new ClientsPage(),
            "Entries" => new EntriesPage(),
            "Invoices" => new InvoicesPage(),
            "Reports" => new ReportsPage(),
            "Settings" => new SettingsPage(),
            _ => new OverviewPage(),
        };
        page.Build();
        current = page; currentKey = key;
        navigating = true; nav[key].IsChecked = true; navigating = false;
        PageHost.Content = page;
        Scroller.ScrollToTop();

        if (!SystemParameters.ClientAreaAnimation) { PageHost.Opacity = 1; PageHost.RenderTransform = null; return; }   // Windows "Animation effects" off
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
        InvoicesPage.SetTab("Invoices");
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
        if ((DateTime.UtcNow - lastRecurringCheck).TotalMinutes >= 60) RunRecurring();
        if (State.Data.Settings.CheckUpdates && (DateTime.UtcNow - lastUpdateCheck).TotalHours >= 24) CheckForUpdate();
        if ((DateTime.UtcNow - lastBackupCheck).TotalHours >= 6) { lastBackupCheck = DateTime.UtcNow; BackupService.RunIfDue(); }
        if (DateTime.UtcNow.Second % 15 == 0) CheckReminders();
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
        if (e.Key == Key.K) { e.Handled = true; CommandPalette.Open(); }
        else if (e.Key == Key.M) { e.Handled = true; ShowMini(); }
        else if (e.Key == Key.Space)
        {
            e.Handled = true;
            ToggleTimerShortcut();
        }
        else if (e.Key >= Key.D1 && e.Key < Key.D1 + Pages.Length)
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

    Action? toastAction;

    void ToastAction_Click(object sender, RoutedEventArgs e)
    {
        var a = toastAction; toastAction = null;
        ToastBorder.BeginAnimation(OpacityProperty, null); ToastBorder.Opacity = 0; ToastBorder.IsHitTestVisible = false;
        a?.Invoke();
    }

    /// <summary>A toast with one button (used for Undo). It stays a little longer so there is time to click it.</summary>
    public void ShowToastAction(string message, string label, Action action)
    {
        toastAction = action;
        ToastText.Text = message;
        ToastAction.Content = label;
        Announce();
        ToastAction.Visibility = Visibility.Visible;
        ToastBorder.IsHitTestVisible = true;
        ToastDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "GoodBrush");
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(6500))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(6900))));
        a.Completed += (_, _) => { if (ToastBorder.Opacity == 0) { ToastBorder.IsHitTestVisible = false; toastAction = null; } };
        ToastBorder.BeginAnimation(OpacityProperty, a);
    }

    /// <summary>Lets screen readers (Narrator, NVDA) read toasts out loud as they appear.</summary>
    void Announce()
    {
        System.Windows.Automation.AutomationProperties.SetLiveSetting(ToastText, System.Windows.Automation.AutomationLiveSetting.Polite);
        var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(ToastText) ?? System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(ToastText);
        peer?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }

    public void ShowToast(string message, bool error = false)
    {
        ToastAction.Visibility = Visibility.Collapsed; ToastBorder.IsHitTestVisible = false; toastAction = null;
        ToastText.Text = Loc.T(message);
        Announce();
        ToastDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, error ? "WarnBrush" : "GoodBrush");
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(error ? 5200 : 3000))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(error ? 5600 : 3400))));
        ToastBorder.BeginAnimation(OpacityProperty, a);
    }
}
