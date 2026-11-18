using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Tempo;

/// <summary>Settings card for the bring-your-own-key integrations, their connect dialogs, and the "Pick a task" picker.</summary>
public static class IntegrationsUi
{
    sealed record Service(string Id, string Name, string What, string HowTo, string KeyPage);

    static readonly Service[] Services =
    {
        new(IntegrationClient.Stripe, "Stripe", "Marks invoices paid when your client pays through a Stripe payment link.",
            "In Stripe: Developers → API keys → Create restricted key. Allow only 'Checkout Sessions: Read'. Then use a Stripe payment link (buy.stripe.com/…) as the invoice's pay link. Tempo adds the invoice number to the link and checks every 15 minutes.",
            "https://dashboard.stripe.com/apikeys"),
        new(IntegrationClient.GitHub, "GitHub", "Pick an issue or pull request assigned to you as the entry description.",
            "In GitHub: Settings → Developer settings → Personal access tokens → Fine-grained tokens → Generate. Give it read-only access to Issues and Pull requests on the repositories you work on.",
            "https://github.com/settings/personal-access-tokens/new"),
        new(IntegrationClient.Jira, "Jira", "Pick an open Jira issue assigned to you as the entry description.",
            "Create an API token at id.atlassian.com → Security → API tokens. Enter your Jira site (e.g. yourteam.atlassian.net) and the email you sign in with.",
            "https://id.atlassian.com/manage-profile/security/api-tokens"),
        new(IntegrationClient.Asana, "Asana", "Pick an open Asana task assigned to you as the entry description.",
            "In Asana: open the developer console (My settings → Apps → Manage developer apps) and create a personal access token.",
            "https://app.asana.com/0/my-apps"),
    };

    /// <summary>Masked key field styled like the other inputs.</summary>
    static (Border Box, PasswordBox Pwd) Secret(string label)
    {
        var p = new PasswordBox { BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, Padding = new Thickness(12, 9, 12, 9), VerticalContentAlignment = VerticalAlignment.Center };
        p.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        p.SetResourceReference(PasswordBox.CaretBrushProperty, "TextBrush");
        System.Windows.Automation.AutomationProperties.SetName(p, Loc.T(label));
        var b = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = p };
        b.SetResourceReference(Border.BackgroundProperty, "FieldBrush");
        b.SetResourceReference(Border.BorderBrushProperty, "FieldBorderBrush");
        p.GotKeyboardFocus += (_, _) => b.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        p.LostKeyboardFocus += (_, _) => b.SetResourceReference(Border.BorderBrushProperty, "FieldBorderBrush");
        return (b, p);
    }

    public static Border SettingsCard(Action rebuild, Func<UIElement, Border> block, Func<string, string, StackPanel> section)
    {
        var sp = new StackPanel();
        sp.Children.Add(section("Integrations", "Optional. Each one uses your own key from that service, stored encrypted on this PC only. Nothing is shared with anyone else."));
        foreach (var s in Services)
        {
            var connected = SecretStore.Has(State.Dir, s.Id);
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
            var acts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (connected)
            {
                acts.Children.Add(Ui.Pill("Connected", "GoodBrush", "GoodSoftBrush").WithMargin(0, 0, 8, 0));
                Button test = null!;
                test = Ui.Btn("Test", async () =>
                {
                    test.IsEnabled = false; test.Content = Loc.T("Testing…");
                    var err = await IntegrationClient.TestAsync(s.Id);
                    test.IsEnabled = true; test.Content = Loc.T("Test");
                    State.Toast(err == null ? Loc.F("{0} is working.", s.Name) : $"{s.Name}: {err}", err != null);
                }, "GlassButton");
                acts.Children.Add(test.WithMargin(0, 0, 6, 0));
                acts.Children.Add(Ui.Btn("Disconnect", () =>
                {
                    // the key can't be shown again, so make sure: reconnecting means creating a new one
                    if (!Dialogs.Confirm(Loc.F("Disconnect {0}?", s.Name), Loc.F("Tempo will delete your {0} key from this PC. To reconnect later you'll need to paste a key again.", s.Name), "Disconnect", true)) return;
                    Disconnect(s.Id); State.Toast(Loc.F("{0} disconnected. Its key was deleted from this PC.", s.Name)); rebuild();
                }, "DangerButton"));
            }
            else acts.Children.Add(Ui.Btn("Connect…", () => Connect(s, rebuild), "GlassButton", Icons.Link));
            DockPanel.SetDock(acts, Dock.Right); row.Children.Add(acts);
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            info.Children.Add(Ui.Text(s.Name, 14.5, "TextBrush", FontWeights.SemiBold));
            info.Children.Add(Ui.Text(Loc.T(s.What), 12.5, "MutedBrush", wrap: true));
            row.Children.Add(info);
            sp.Children.Add(row);
        }
        if (SecretStore.Has(State.Dir, IntegrationClient.Stripe))
        {
            var st = IntegrationClient.LastStripeError is string e ? Loc.F("Last Stripe check failed: {0}", e)
                   : IntegrationClient.LastStripeCheckUtc is DateTime t ? Loc.F("Last Stripe check: {0}", Fmt.Time(State.ToLocal(t))) : Loc.T("Stripe is checked every 15 minutes while Tempo is open.");
            var line = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            var stt = Ui.Text(st, 12, IntegrationClient.LastStripeError != null ? "WarnBrush" : "FaintBrush"); stt.VerticalAlignment = VerticalAlignment.Center; stt.Margin = new Thickness(0, 0, 8, 0);
            line.Children.Add(stt);
            line.Children.Add(Ui.Btn("Check Stripe now", async () => { var changed = await IntegrationClient.CheckStripeAsync(); AnnounceStripe(changed, true); rebuild(); }, "GhostButton"));
            sp.Children.Add(line);
        }
        return block(sp);
    }

    static void Disconnect(string id)
    {
        SecretStore.Set(State.Dir, id, null);
        if (id == IntegrationClient.Jira) { SecretStore.Set(State.Dir, IntegrationClient.JiraEmail, null); SecretStore.Set(State.Dir, IntegrationClient.JiraSite, null); }
    }

    /// <summary>
    /// Asks for the key, then tests it. A key that doesn't work is removed again and the dialog reopens with the reason,
    /// so the card never shows "Connected" for a key that can't connect.
    /// </summary>
    static void Connect(Service s, Action rebuild, string? error = null, string siteText = "", string emailText = "")
    {
        var body = new StackPanel();
        body.Children.Add(Ui.Text(Loc.T(s.HowTo), 13, "MutedBrush", wrap: true).WithMargin(0, 0, 0, 8));
        var open = Ui.Btn(Loc.F("Open the {0} key page", s.Name), () =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(s.KeyPage) { UseShellExecute = true }); } catch { }   // fixed, known https address
        }, "GhostButton", Icons.Link);
        body.Children.Add(open.HorizontalLeft().WithMargin(-8, 0, 0, 10));
        TextBox? site = null, email = null;
        if (s.Id == IntegrationClient.Jira)
        {
            site = Ui.Input(siteText, "yourteam.atlassian.net"); email = Ui.Input(emailText, "you@company.com");
            body.Children.Add(Ui.Field("JIRA SITE", site, 4));
            body.Children.Add(Ui.Text("The address you see in your browser when Jira is open.", 12, "FaintBrush", wrap: true).WithMargin(0, 0, 0, 12));
            body.Children.Add(Ui.Field("EMAIL", email));
        }
        var (box, pwd) = Secret(s.Id == IntegrationClient.Stripe ? "RESTRICTED KEY" : "API TOKEN");
        body.Children.Add(Ui.Field(s.Id == IntegrationClient.Stripe ? "RESTRICTED KEY" : "API TOKEN", box, 6));
        if (s.Id != IntegrationClient.Stripe)
            body.Children.Add(Ui.Text("Tip: tokens can expire. If this stops working later, create a new one and connect again.", 12, "FaintBrush", wrap: true).WithMargin(0, 0, 0, 4));
        body.Children.Add(Ui.Text("The key is encrypted for your Windows account and saved on this PC only. It is never included in backups, sync files or problem reports.", 12, "FaintBrush", wrap: true).WithMargin(0, 0, 0, 8));
        var err = Dialogs.ErrorLabel(); body.Children.Add(err);
        if (error != null) Dialogs.SetError(err, error);
        var ok = Dialogs.Show(Loc.F("Connect {0}", s.Name), body, "Save and test", () =>
        {
            var key = pwd.Password.Trim();
            if (key.Length < 8 || key.Any(char.IsWhiteSpace)) { Dialogs.SetError(err, "That doesn't look like a key. Paste it exactly as the site shows it."); return false; }
            if (s.Id == IntegrationClient.Stripe && !key.StartsWith("rk_", StringComparison.Ordinal)) { Dialogs.SetError(err, "Use a restricted key (it starts with rk_) with read access only, not your secret key."); return false; }
            if (s.Id == IntegrationClient.Jira)
            {
                if (WorkItems.NormalizeJiraSite(site!.Text) is not string norm) { Dialogs.SetError(err, "Enter your Jira site, like yourteam.atlassian.net."); return false; }
                if (!email!.Text.Contains('@')) { Dialogs.SetError(err, "Enter the email you sign in to Jira with."); return false; }
                SecretStore.Set(State.Dir, IntegrationClient.JiraSite, norm);
                SecretStore.Set(State.Dir, IntegrationClient.JiraEmail, email.Text.Trim());
            }
            SecretStore.Set(State.Dir, s.Id, key);
            return true;
        }, 540);
        pwd.Clear();
        if (!ok) return;
        VerifyAfterConnect(s, rebuild, site?.Text ?? "", email?.Text ?? "");
    }

    static async void VerifyAfterConnect(Service s, Action rebuild, string siteText, string emailText)
    {
        State.Toast(Loc.F("Checking your {0} key…", s.Name));
        var e = await IntegrationClient.TestAsync(s.Id);
        if (e == null) { State.Toast(Loc.F("{0} connected.", s.Name)); rebuild(); return; }
        Disconnect(s.Id);   // never keep a key that doesn't work
        rebuild();
        Connect(s, rebuild, e, siteText, emailText);
    }

    public static void AnnounceStripe(List<Invoice> changed, bool always = false)
    {
        if (changed.Count == 0) { if (always) State.Toast(IntegrationClient.LastStripeError ?? Loc.T("No new Stripe payments."), IntegrationClient.LastStripeError != null); return; }
        var paid = changed.Where(i => i.Status == "Paid").Select(i => i.Number).ToList();
        State.Toast(paid.Count > 0 ? Loc.F("Paid through Stripe: {0}", string.Join(", ", paid)) : Loc.F("Stripe payment recorded on {0}", string.Join(", ", changed.Select(i => i.Number))));
    }

    /// <summary>A small button that fills <paramref name="target"/> with one of your assigned tasks. Null when no service is connected.</summary>
    public static Button? PickButton(TextBox target)
    {
        var names = new[] { (IntegrationClient.GitHub, "GitHub"), (IntegrationClient.Jira, "Jira"), (IntegrationClient.Asana, "Asana") }
            .Where(x => SecretStore.Has(State.Dir, x.Item1)).Select(x => x.Item2).ToList();
        if (names.Count == 0) return null;
        return Ui.IconBtn(Icons.Search, Loc.F("Pick a task from {0}", string.Join(", ", names)), () => Pick(target));
    }

    static void Pick(TextBox target)
    {
        var search = Ui.Input("", "Filter tasks");
        var list = new StackPanel();
        list.Children.Add(Ui.Text("Loading your tasks…", 13, "FaintBrush"));
        var body = new StackPanel();
        body.Children.Add(Ui.Field("FILTER", search, 8));
        body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        WorkItem? chosen = null;
        List<WorkItem> all = new(); List<string> errors = new(); var shown = new List<WorkItem>();
        bool loading = true;

        void Choose(WorkItem w, FrameworkElement from) { chosen = w; if (Window.GetWindow(from) is Window win) win.DialogResult = true; }

        void Fill()
        {
            list.Children.Clear();
            if (loading) { list.Children.Add(Ui.Text("Loading your tasks…", 13, "FaintBrush")); return; }
            foreach (var e in errors) list.Children.Add(Ui.Text(e, 12.5, "WarnBrush", wrap: true).WithMargin(0, 0, 0, 6));
            if (errors.Count > 0)
                list.Children.Add(Ui.Btn("Try again", () => _ = Load(true), "GlassButton").HorizontalLeft().WithMargin(0, 0, 0, 10));
            var q = search.Text.Trim();
            var matches = all.Where(w => q.Length == 0 || $"{w.Key} {w.Title} {w.Container}".Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
            shown = matches.Take(100).ToList();
            if (shown.Count == 0 && errors.Count == 0) list.Children.Add(Ui.Text(all.Count == 0 ? "No open tasks assigned to you." : "No matches", 13, "FaintBrush"));
            foreach (var w in shown)
            {
                var item = w;
                var g = new StackPanel();
                g.Children.Add(Ui.Text(WorkItems.Describe(w), 13.5, "TextBrush", FontWeights.SemiBold));
                g.Children.Add(Ui.Text($"{w.Source}{(w.Container.Length > 0 ? " · " + w.Container : "")}", 12, "MutedBrush"));
                var btn = new Button { Content = g, Style = (Style)Application.Current.FindResource("GhostButton"), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 1, 0, 1) };
                System.Windows.Automation.AutomationProperties.SetName(btn, $"{WorkItems.Describe(w)}, {w.Source}");
                btn.Click += (_, _) => Choose(item, btn);
                list.Children.Add(btn);
            }
            if (matches.Count > 100) list.Children.Add(Ui.Text("Showing the first 100. Type to narrow the list.", 12, "FaintBrush").WithMargin(10, 6, 0, 0));
        }
        search.TextChanged += (_, _) => { if (!loading) Fill(); };
        // Enter in the filter takes the first match instead of closing the dialog
        search.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter && shown.Count > 0) { e.Handled = true; Choose(shown[0], search); } };
        search.Loaded += (_, _) => search.Focus();

        async Task Load(bool refresh)
        {
            loading = true; Fill();
            (all, errors) = await IntegrationClient.TasksAsync(refresh);
            loading = false; Fill();
        }
        _ = Load(false);

        Dialogs.Show("Pick a task", body, "Close", null, 560);
        if (chosen != null) target.Text = WorkItems.Describe(chosen);
    }
}
