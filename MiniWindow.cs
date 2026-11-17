using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Tempo;

/// <summary>
/// The compact view: a 300 × 150 borderless glass window with the clock, start/pause/stop, a project picker and the task.
/// Picking another project saves the running session and starts the new project's timer straight away.
/// Drag it by any empty part of its surface.
/// </summary>
public sealed class MiniWindow : Window
{
    public const double MiniWidth = 300, MiniHeight = 150;

    TextBlock clock = null!;
    ComboBox combo = null!;
    TextBox task = null!;
    Border status = null!;
    TextBlock statusText = null!;
    bool building, allowClose;
    DateTime statusUntil;
    readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public MiniWindow()
    {
        Title = "Tempo";
        Width = MiniWidth;
        Height = MiniHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        FontSize = 13;
        UseLayoutRounding = true;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        this.SetResourceReference(ForegroundProperty, "TextBrush");
        Icon = Ui.LogoBitmap(32);
        Topmost = State.Data.Settings.MiniPinned;
        WindowStartupLocation = WindowStartupLocation.Manual;

        SourceInitialized += (_, _) => Backdrop.Apply(this);
        PreviewKeyDown += OnKey;
        State.Changed += () => Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) Build(); }), DispatcherPriority.Background);
        State.ToastRequested += (msg, _) => { if (IsVisible) Say(msg); };
        tick.Tick += (_, _) => Tick();
        IsVisibleChanged += (_, _) => { if (IsVisible) { Build(); tick.Start(); } else tick.Stop(); };
        Closing += (_, e) =>
        {
            if (allowClose || Dispatcher.HasShutdownStarted) return;
            e.Cancel = true;                      // Alt+F4 means "back to the full view", not "quit"
            MainWindow.Instance.ShowFull();
        };
        Build();
    }

    /// <summary>Places the window where it was last (or the bottom-right corner) and keeps it on screen.</summary>
    public void Prepare()
    {
        var s = State.Data.Settings;
        var wa = SystemParameters.WorkArea;
        double left = s.MiniLeft ?? wa.Right - MiniWidth - 24;
        double top = s.MiniTop ?? wa.Bottom - MiniHeight - 24;
        Left = Math.Max(wa.Left, Math.Min(left, wa.Right - MiniWidth));
        Top = Math.Max(wa.Top, Math.Min(top, wa.Bottom - MiniHeight));
        Topmost = s.MiniPinned;
    }

    public void SavePosition()
    {
        if (!IsVisible) return;
        State.Data.Settings.MiniLeft = Left;
        State.Data.Settings.MiniTop = Top;
        State.Save();
    }

    public void Shutdown() { SavePosition(); allowClose = true; Close(); }

    // ---------------------------------------------------------------- UI
    void Build()
    {
        building = true;
        var d = State.Data;
        var projects = State.ActiveProjects.ToList();
        var locked = State.Proj(d.Timer.ProjectId);
        if (locked != null && !projects.Contains(locked)) projects.Add(locked);

        var root = new Grid();
        root.Children.Add(Glow("Blob1C", "Blob1T", HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(-120, -140, 0, 0)));
        root.Children.Add(Glow("Blob2C", "Blob2T", HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0, 0, -130, -150)));

        var col = new Grid { Margin = new Thickness(12, 10, 12, 12) };
        col.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        col.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        col.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        col.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        col.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });

        // ---- row 1: clock + controls
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        clock = new TextBlock { FontSize = 28, FontWeight = FontWeights.SemiBold, Text = "00:00:00", VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Documents.Typography.SetNumeralAlignment(clock, FontNumeralAlignment.Tabular);
        top.Children.Add(clock);

        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var main = State.Running
            ? Ui.Btn("", ToggleTimer, "GlassButton", Icons.Pause, "Pause")
            : Ui.Btn("", ToggleTimer, "PrimaryButton", Icons.Play, State.HasTimer ? "Resume" : "Start timer");
        main.Padding = new Thickness(8); main.IsEnabled = projects.Count > 0;
        tools.Children.Add(main);
        if (State.HasTimer)
        {
            var stop = Ui.Btn("", StopAndSave, "GlassButton", Icons.Stop, "Stop & save");
            stop.Padding = new Thickness(8); stop.Margin = new Thickness(4, 0, 0, 0);
            tools.Children.Add(stop);
        }
        var pin = PinButton(); pin.Margin = new Thickness(6, 0, 0, 0);
        tools.Children.Add(pin);
        var expand = Ui.IconBtn(Icons.Expand, "Full view", () => MainWindow.Instance.ShowFull());
        expand.Padding = new Thickness(6);
        tools.Children.Add(expand);
        Grid.SetColumn(tools, 1); top.Children.Add(tools);
        col.Children.Add(top);

        // ---- row 3: project picker
        combo = Ui.Combo(projects, locked ?? projects.FirstOrDefault());
        combo.Padding = new Thickness(10, 5, 30, 5);
        combo.IsEnabled = projects.Count > 0;
        combo.ToolTip = Loc.T("Switching projects saves the running session and starts the new one.");
        combo.SelectionChanged += (_, _) => { if (!building && combo.SelectedItem is Project p) Switch(p); };
        Grid.SetRow(combo, 2); col.Children.Add(combo);

        // ---- row 5: task
        task = Ui.Input(d.Timer.Task, projects.Count == 0 ? "Create a project first so Tempo knows which rate to use." : "What are you working on?");
        task.Padding = new Thickness(10, 4, 10, 4);
        task.IsEnabled = projects.Count > 0;
        task.LostFocus += (_, _) => { d.Timer.Task = task.Text.Trim(); State.Save(); };
        Grid.SetRow(task, 4); col.Children.Add(task);

        // ---- transient message, shown over the task row
        statusText = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        statusText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        status = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 0, 10, 0), Visibility = Visibility.Collapsed, Child = statusText };
        status.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
        Grid.SetRow(status, 4); col.Children.Add(status);

        root.Children.Add(col);
        root.MouseLeftButtonDown += Drag;

        var frame = new Border { BorderThickness = new Thickness(1), Child = root };
        frame.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        Content = frame;
        building = false;
        Tick();
    }

    /// <summary>Drag the window from any empty area (not from buttons, fields or the project list).</summary>
    void Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        for (var d = e.OriginalSource as DependencyObject; d != null; d = System.Windows.Media.VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
            if (d is ButtonBase or TextBoxBase or ComboBox or Popup) return;
        try { DragMove(); } catch { }
    }

    static Ellipse Glow(string c, string t, HorizontalAlignment h, VerticalAlignment v, Thickness m)
    {
        var br = new RadialGradientBrush();
        br.GradientStops.Add(new GradientStop((Color)Application.Current.FindResource(c), 0));
        br.GradientStops.Add(new GradientStop((Color)Application.Current.FindResource(t), 1));
        return new Ellipse { Width = 300, Height = 300, HorizontalAlignment = h, VerticalAlignment = v, Margin = m, Fill = br, IsHitTestVisible = false };
    }

    Button PinButton()
    {
        var b = Ui.Btn("", () => { }, "IconButton", Icons.Pin, "Keep on top");
        b.Padding = new Thickness(6);
        void Paint() => b.Content = Ui.Icon(Icons.Pin, 16, Topmost ? "AccentBrush" : "MutedBrush");
        Paint();
        b.Click += (_, _) =>
        {
            Topmost = !Topmost;
            State.Data.Settings.MiniPinned = Topmost;
            State.Save();
            Paint();
        };
        return b;
    }

    // ---------------------------------------------------------------- behaviour
    void ToggleTimer()
    {
        var d = State.Data;
        if (State.Running) { d.Timer.Task = task.Text.Trim(); State.PauseTimer(); return; }
        if (combo.SelectedItem is not Project p) { State.Toast("Choose a project first", true); return; }
        State.StartTimer(p.Id, task.Text.Trim());
    }

    void StopAndSave()
    {
        var d = State.Data;
        d.Timer.Task = task.Text.Trim();
        var ms = State.ElapsedMs;
        var p = State.Proj(d.Timer.ProjectId);
        var s = State.StopTimer();
        State.Toast(s != null ? Loc.F("Saved {0} to {1}", Fmt.Hm(ms), p?.Name) : "Timer cleared (under 1 second)");
    }

    /// <summary>Choosing a different project: save what's running, then carry on tracking the new one.</summary>
    void Switch(Project p)
    {
        var d = State.Data;
        if (d.Timer.ProjectId == p.Id) return;
        d.Timer.Task = task.Text.Trim();
        bool wasRunning = State.Running;
        if (State.HasTimer)
        {
            var ms = State.ElapsedMs;
            var old = State.Proj(d.Timer.ProjectId);
            var s = State.StopTimer();
            if (s != null) State.Toast(Loc.F("Saved {0} to {1}", Fmt.Hm(ms), old?.Name));
        }
        d.Timer.ProjectId = p.Id;
        d.Timer.Task = "";
        if (wasRunning) { State.StartTimer(p.Id, ""); State.Toast(Loc.F("Now tracking {0}", p.Name)); }
        else State.Notify();
    }

    void Say(string msg)
    {
        if (statusText == null) return;
        statusText.Text = msg;
        status.Visibility = Visibility.Visible;
        statusUntil = DateTime.UtcNow.AddSeconds(3.5);
    }

    void Tick()
    {
        if (clock == null) return;
        clock.Text = Fmt.Clock(State.ElapsedMs);
        clock.SetResourceReference(TextBlock.ForegroundProperty, State.Running ? "TextBrush" : "MutedBrush");
        clock.ToolTip = Loc.T(State.Running ? "TIMER RUNNING" : State.HasTimer ? "TIMER PAUSED" : "READY WHEN YOU ARE");
        if (status.Visibility == Visibility.Visible && DateTime.UtcNow >= statusUntil) status.Visibility = Visibility.Collapsed;
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.M) { e.Handled = true; MainWindow.Instance.ShowFull(); }
        else if (e.Key == Key.Space) { e.Handled = true; ToggleTimer(); }
    }
}
