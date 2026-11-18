using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Tempo;

/// <summary>Ctrl+K: type to jump to a page, start a timer on a project, or run a common action.</summary>
public sealed class CommandPalette
{
    sealed record Item(string Title, string Sub, string Icon, Action Run);

    readonly Grid overlay = new();
    readonly TextBox box;
    readonly StackPanel list = new();
    readonly List<Item> all;
    readonly ScrollViewer scroller;
    readonly IInputElement? returnFocus = Keyboard.FocusedElement;   // where focus goes back to on close
    List<Item> shown = new();
    int sel;
    static CommandPalette? open;

    public static void Open()
    {
        if (open != null) { open.Close(); return; }
        open = new CommandPalette();
    }

    CommandPalette()
    {
        all = BuildItems();

        overlay.Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0));
        Panel.SetZIndex(overlay, 300);
        Grid.SetColumnSpan(overlay, 20); Grid.SetRowSpan(overlay, 20);   // over the sidebar and title bar too
        overlay.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == overlay) Close(); };

        box = Ui.Input("", "Search or jump to…");
        box.FontSize = 16; box.Padding = new Thickness(14, 12, 14, 12);
        System.Windows.Automation.AutomationProperties.SetName(box, Loc.T("Search"));
        box.TextChanged += (_, _) => { sel = 0; Refresh(); };
        box.PreviewKeyDown += OnKey;

        var inner = new StackPanel();
        inner.Children.Add(box);
        scroller = new ScrollViewer { Content = list, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 10, 0, 0), Focusable = false };
        inner.Children.Add(scroller);
        System.Windows.Automation.AutomationProperties.SetName(list, Loc.T("Search"));
        var card = Ui.Card(inner, 14);
        System.Windows.Automation.AutomationProperties.SetName(card, Loc.T("Search or jump to…"));
        KeyboardNavigation.SetTabNavigation(overlay, KeyboardNavigationMode.Cycle);   // Tab never escapes to the page behind
        card.Width = 580; card.VerticalAlignment = VerticalAlignment.Top; card.HorizontalAlignment = HorizontalAlignment.Center; card.Margin = new Thickness(0, 96, 0, 0);
        card.Background = Ui.B("PopupBrush");
        // the popup brush is see-through glass; put a solid layer under it so page text never shows through the list
        var solid = new Border { CornerRadius = card.CornerRadius, Width = card.Width, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Margin = card.Margin,
                                 Background = new SolidColorBrush(App.IsDark ? Color.FromRgb(0x0B, 0x12, 0x20) : Color.FromRgb(0xF7, 0xF9, 0xFC)) };
        solid.SetBinding(FrameworkElement.HeightProperty, new System.Windows.Data.Binding(nameof(FrameworkElement.ActualHeight)) { Source = card });
        overlay.Children.Add(solid);
        overlay.Children.Add(card);

        MainWindow.Instance.RootGridPublic.Children.Add(overlay);
        Refresh();
        box.Focus();
    }

    void Close()
    {
        MainWindow.Instance.RootGridPublic.Children.Remove(overlay);
        open = null;
        if (returnFocus is UIElement { IsVisible: true } back) back.Focus();
    }

    List<Item> BuildItems()
    {
        var d = State.Data;
        var items = new List<Item>();
        void Page(string key, string title, string icon) => items.Add(new Item(Loc.F("Go to {0}", Loc.T(title)), "", icon, () => MainWindow.Instance.Navigate(key)));

        // timer
        foreach (var p in State.ActiveProjects.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var proj = p;
            var running = State.HasTimer && d.Timer.ProjectId == proj.Id;
            items.Add(new Item(Loc.F(State.HasTimer && !running ? "Switch timer to {0}" : "Start timer: {0}", proj.Name), State.ClientName(proj), Icons.Play, () =>
            {
                if (State.Running && running) return;
                if (State.HasTimer && d.Timer.ProjectId != proj.Id) State.SwitchProject(proj);
                if (!State.Running) State.StartTimer(proj.Id, d.Timer.Task);
            }));
        }
        if (State.Running) items.Add(new Item(Loc.T("Pause timer"), "", Icons.Pause, State.PauseTimer));
        else if (State.HasTimer) items.Add(new Item(Loc.T("Resume timer"), "", Icons.Play, () => { if (State.Proj(d.Timer.ProjectId) is Project pr) State.StartTimer(pr.Id, d.Timer.Task); }));
        if (State.HasTimer) items.Add(new Item(Loc.T("Stop & save timer"), "", Icons.Stop, () => { var ms = State.ElapsedMs; var pr = State.Proj(d.Timer.ProjectId); var s = State.StopTimer(); if (s != null) State.Toast(Loc.F("Saved {0} to {1}", Fmt.Hm(ms), pr?.Name)); }));

        // create
        items.Add(new Item(Loc.T("Add time entry"), "", Icons.Plus, () => EntriesPage.EditEntry(null)));
        items.Add(new Item(Loc.T("Add expense"), "", Icons.Receipt, () => ExpensesView.Edit(null)));
        items.Add(new Item(Loc.T("New project"), "", Icons.Folder, () => ProjectsPage.EditProject(null)));
        items.Add(new Item(Loc.T("New client"), "", Icons.Users, () => ClientsPage.EditClient(null)));
        items.Add(new Item(Loc.T("New invoice"), "", Icons.Doc, () => { InvoicesPage.SetTab("Invoices"); MainWindow.Instance.Navigate("Invoices"); }));
        items.Add(new Item(Loc.T("New quote"), "", Icons.Doc, () => { InvoicesPage.SetTab("Quotes"); MainWindow.Instance.Navigate("Invoices"); }));
        items.Add(new Item(Loc.T("Mini timer"), "Ctrl+M", Icons.Mini, MainWindow.Instance.ShowMini));

        // go to
        Page("Overview", "Overview", Icons.Home); Page("Projects", "Projects", Icons.Folder); Page("Clients", "Clients", Icons.Users);
        Page("Entries", "Time entries", Icons.Clock); Page("Invoices", "Invoices", Icons.Doc); Page("Reports", "Reports", Icons.Chart); Page("Settings", "Settings", Icons.Sliders);

        // jump to a specific client / invoice
        foreach (var c in d.Clients.Where(c => !c.Archived).OrderBy(c => c.Name))
        {
            var cl = c;
            items.Add(new Item(c.Name, Loc.T("Client"), Icons.Users, () => { MainWindow.Instance.Navigate("Clients"); ClientsPage.EditClient(cl); }));
        }
        foreach (var inv in d.Invoices.OrderByDescending(i => i.IssuedUtc).Take(30))
        {
            var iv = inv;
            items.Add(new Item(inv.Number, inv.ClientName, Icons.Doc, () => { InvoicesPage.SetTab(iv.Kind == "Quote" ? "Quotes" : "Invoices"); MainWindow.Instance.Navigate("Invoices"); }));
        }
        return items;
    }

    static int Score(Item it, string q)
    {
        var t = it.Title.ToLowerInvariant(); var s = it.Sub.ToLowerInvariant();
        if (t.StartsWith(q)) return 3;
        if (t.Contains(' ' + q)) return 2;
        if (t.Contains(q)) return 1;
        return s.Contains(q) ? 0 : -1;
    }

    void Refresh()
    {
        var q = box.Text.Trim().ToLowerInvariant();
        shown = q.Length == 0
            ? all.Take(12).ToList()
            : all.Select(i => (Item: i, Score: Score(i, q))).Where(x => x.Score >= 0).OrderByDescending(x => x.Score).Select(x => x.Item).Take(12).ToList();
        if (sel >= shown.Count) sel = Math.Max(0, shown.Count - 1);
        Render();
    }

    void Render()
    {
        list.Children.Clear();
        if (shown.Count == 0) { list.Children.Add(Ui.Text("No matches", 13.5, "FaintBrush").WithMargin(12, 10, 12, 10)); return; }
        for (int i = 0; i < shown.Count; i++)
        {
            var it = shown[i]; int idx = i;
            var g = new Grid { Margin = new Thickness(2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(Ui.Icon(it.Icon, 16, i == sel ? "AccentBrush" : "MutedBrush", it.Icon is Icons.Play or Icons.Pause or Icons.Stop));
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(Ui.Text(it.Title, 14, "TextBrush", FontWeights.SemiBold));
            if (it.Sub.Length > 0) text.Children.Add(Ui.Text(it.Sub, 12, "MutedBrush"));
            Grid.SetColumn(text, 1); g.Children.Add(text);
            var b = new Border { Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(10), Child = g, Cursor = Cursors.Hand };
            b.SetResourceReference(Border.BackgroundProperty, i == sel ? "AccentSoftBrush" : "PopupBrush");
            System.Windows.Automation.AutomationProperties.SetName(b, it.Sub.Length > 0 ? $"{it.Title}, {it.Sub}" : it.Title);
            b.MouseEnter += (_, _) => { if (sel != idx) { sel = idx; Render(); } };
            b.MouseLeftButtonUp += (_, _) => Run(idx);
            list.Children.Add(b);
            if (i == sel) b.Loaded += (_, _) => b.BringIntoView();
        }
        // the search box tells screen readers which result Enter will run
        if (sel < shown.Count)
        {
            var cur = shown[sel];
            System.Windows.Automation.AutomationProperties.SetHelpText(box, cur.Sub.Length > 0 ? $"{cur.Title}, {cur.Sub}" : cur.Title);
            System.Windows.Automation.AutomationProperties.SetLiveSetting(box, System.Windows.Automation.AutomationLiveSetting.Polite);
            (System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(box) ?? System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(box))
                ?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        }
    }

    void Run(int idx)
    {
        if (idx < 0 || idx >= shown.Count) return;
        var it = shown[idx];
        Close();
        MainWindow.Instance.Dispatcher.BeginInvoke(new Action(it.Run));   // after the overlay is gone, so dialogs open cleanly
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: e.Handled = true; Close(); break;
            case Key.Down: e.Handled = true; if (shown.Count > 0) { sel = (sel + 1) % shown.Count; Render(); } break;
            case Key.Up: e.Handled = true; if (shown.Count > 0) { sel = (sel - 1 + shown.Count) % shown.Count; Render(); } break;
            case Key.Enter: e.Handled = true; Run(sel); break;
            case Key.Tab: e.Handled = true; break;   // keep focus in the palette
            case Key.PageDown: e.Handled = true; if (shown.Count > 0) { sel = Math.Min(shown.Count - 1, sel + 5); Render(); } break;
            case Key.PageUp: e.Handled = true; if (shown.Count > 0) { sel = Math.Max(0, sel - 5); Render(); } break;
        }
    }
}
