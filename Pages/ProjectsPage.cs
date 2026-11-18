using System.Windows;
using System.Windows.Controls;

namespace Tempo;

public class ProjectsPage : PageBase
{
    public override void Build()
    {
        var d = State.Data;
        var root = new StackPanel();
        root.Children.Add(Ui.Header("Your projects", "Every client, brand and hourly rate in one tidy place.",
            Ui.Btn("New project", () => EditProject(null), "PrimaryButton", Icons.Plus)));

        var active = d.Projects.Where(p => !p.Archived).ToList();
        var archived = d.Projects.Where(p => p.Archived).ToList();

        if (active.Count == 0)
            root.Children.Add(Ui.Card(Ui.Empty(Icons.Folder, "No projects yet",
                "Connect your work to a person or brand, set an hourly rate, and you're ready to track.",
                Ui.Btn("Create your first project", () => EditProject(null), "PrimaryButton", Icons.Plus))));

        var wrap = new WrapPanel();
        foreach (var p in active) wrap.Children.Add(Card(p));
        root.Children.Add(wrap);

        if (archived.Count > 0)
        {
            root.Children.Add(Ui.Text("ARCHIVED", 11, "FaintBrush", FontWeights.SemiBold).WithMargin(4, 22, 0, 10));
            var w2 = new WrapPanel();
            foreach (var p in archived) w2.Children.Add(Card(p));
            root.Children.Add(w2);
        }
        Content = root;
    }

    Border Card(Project p)
    {
        var d = State.Data;
        var sessions = d.Sessions.Where(s => s.ProjectId == p.Id).ToList();
        var sp = new StackPanel();

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dot = Ui.Dot(p.Color, 14); dot.Margin = new Thickness(0, 0, 12, 0);
        top.Children.Add(dot);
        var names = new StackPanel();
        names.Children.Add(Ui.Text(p.Name, 17, "TextBrush", FontWeights.SemiBold));
        var cn = State.ClientName(p);
        names.Children.Add(Ui.Text(string.IsNullOrWhiteSpace(cn) ? Loc.T("No client set") : Loc.F("for {0}", cn), 13, "MutedBrush"));
        Grid.SetColumn(names, 1); top.Children.Add(names);
        var rate = Ui.Pill(Loc.F("{0} / hr", Fmt.Money(p.HourlyRate)), "TextBrush", "FieldBrush");
        Grid.SetColumn(rate, 2); top.Children.Add(rate);
        sp.Children.Add(top);

        var tracked = sessions.Sum(s => s.DurationMilliseconds);
        sp.Children.Add(ClientsPage.StatRow(("TRACKED", Fmt.Hm(tracked)), ("UNBILLED", Fmt.Money(State.UnbilledFor(p.Id)))));

        var usage = Budget.Usage(p);
        if (usage != null)
        {
            var bsp = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            var head = p.BudgetType is "Fixed" or "Retainer" ? $"{Loc.T(p.BudgetType == "Fixed" ? "Fixed fee" : "Retainer")} {Fmt.Money(p.BudgetValue)}  ·  {usage.Text}" : usage.Text;
            bsp.Children.Add(Ui.Text(head, 12, usage.Over ? "DangerBrush" : usage.Warn ? "WarnBrush" : "MutedBrush", wrap: true));
            if (p.BudgetType is "Hours" or "Amount")
                bsp.Children.Add(Ui.Progress(usage.Fraction, usage.Over ? "DangerBrush" : usage.Warn ? "WarnBrush" : "AccentGradientBrush").WithMargin(0, 6, 0, 0));
            sp.Children.Add(bsp);
        }
        if (!p.Billable) sp.Children.Add(Ui.Pill("Non-billable", "WarnBrush", "WarnSoftBrush").HorizontalLeft().WithMargin(0, 10, 0, 0));

        var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        if (!p.Archived)
        {
            actions.Children.Add(Ui.Btn("Track", () =>
            {
                if (State.HasTimer) { State.Toast("Stop or save the running timer first", true); return; }
                d.Timer.ProjectId = p.Id; State.Save(); MainWindow.Instance.Navigate("Overview");
            }, "PrimaryButton", Icons.Play).WithMargin(0, 0, 8, 6));
            actions.Children.Add(Ui.Btn("Invoice", () => MainWindow.Instance.OpenInvoiceFor(p.Id), "GlassButton", Icons.Doc).WithMargin(0, 0, 8, 6));
            actions.Children.Add(Ui.IconBtn(Icons.Edit, "Edit project", () => EditProject(p)).WithMargin(0, 0, 2, 6));
            actions.Children.Add(Ui.IconBtn(Icons.Archive, "Archive project", () =>
            {
                if (State.Running && d.Timer.ProjectId == p.Id) { State.Toast("This project's timer is running", true); return; }
                p.Archived = true; State.Notify(); State.Toast(Loc.F("{0} archived. Its entries and invoices are kept.", p.Name));
            }).WithMargin(0, 0, 2, 6));
            actions.Children.Add(Ui.IconBtn(Icons.Trash, "Delete project", () => DeleteProject(p), true).WithMargin(0, 0, 2, 6));
        }
        else
        {
            actions.Children.Add(Ui.Btn("Restore", () => { p.Archived = false; State.Notify(); }, "GlassButton", Icons.Archive).WithMargin(0, 0, 8, 6));
            actions.Children.Add(Ui.IconBtn(Icons.Trash, "Delete project", () => DeleteProject(p), true).WithMargin(0, 0, 2, 6));
        }
        foreach (var ch in actions.Children.OfType<Button>())
        {
            bool labelled = ch.Content is StackPanel panel && panel.Children.Count > 1;
            ch.Padding = labelled ? new Thickness(10, 8, 10, 8) : new Thickness(4);
            if (!labelled) ch.Margin = new Thickness(0, 0, 0, 6);
        }
        sp.Children.Add(actions);

        var c = Ui.Card(sp, 16);
        c.Width = 336; c.Margin = new Thickness(0, 0, 14, 16);
        if (p.Archived) c.Opacity = 0.7;
        return c;
    }

    static void DeleteProject(Project p)
    {
        var d = State.Data;
        if (State.HasTimer && d.Timer.ProjectId == p.Id) { State.Toast("Stop or save this project's timer first.", true); return; }
        var ss = d.Sessions.Where(s => s.ProjectId == p.Id).ToList();
        var ex = d.Expenses.Where(e => e.ProjectId == p.Id).ToList();
        var invCount = d.Invoices.Count(i => i.ProjectId == p.Id);
        var billed = ss.Count(s => s.InvoiceId != null);
        var msg = Loc.F("Delete “{0}”?", p.Name);
        if (ss.Count > 0)
        {
            msg += "\n\n" + Loc.N(ss.Count, "This also deletes its {0} time entry ({1}).", "This also deletes its {0} time entries ({1}).", Fmt.Hm(ss.Sum(s => s.DurationMilliseconds)));
            if (billed > 0) msg += " " + Loc.F("{0} of them are already billed.", billed);
        }
        if (invCount > 0)
            msg += "\n\n" + Loc.N(invCount, "Its {0} invoice record is kept in Invoices history (delete it there if you want it gone too).", "Its {0} invoice records are kept in Invoices history (delete them there if you want them gone too).");
        msg += "\n\n" + Loc.T("You can restore it from Settings → Recently deleted. Archiving hides a project without deleting anything.");
        if (!Dialogs.Confirm("Delete project", msg, "Delete project", true)) return;

        var t = TrashBin.Add("Project", new ProjectBundle { Project = p, Sessions = ss, Expenses = ex }, p.Name);
        d.Sessions.RemoveAll(s => s.ProjectId == p.Id);
        d.Expenses.RemoveAll(e => e.ProjectId == p.Id);
        d.Projects.Remove(p);
        if (d.Timer.ProjectId == p.Id) d.Timer.ProjectId = null;
        State.Notify();
        State.ToastUndo(Loc.F("Project “{0}” deleted", p.Name), t);
    }

    public static void EditProject(Project? editing, Client? preset = null)
    {
        var d = State.Data;
        var cur = d.Settings.CurrencyCode;
        var name = Ui.Input(editing?.Name ?? "", "e.g. Brand identity refresh");

        // ---- client picker (with "+ New client…")
        var choices = d.Clients.Where(c => !c.Archived).OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var newItem = new Client { Id = "__new", Name = Loc.T("＋ New client…") };
        List<Client> Items() => choices.Concat(new[] { newItem }).ToList();
        Client? last = choices.FirstOrDefault(c => c.Id == (editing?.ClientId ?? preset?.Id)) ?? (choices.Count == 1 && editing == null ? choices[0] : null);
        var cc = Ui.Combo(Items(), last);
        var rate = Ui.Input(editing != null ? Fmt.Dec(editing.HourlyRate) : (last?.DefaultRate is decimal r1 ? Fmt.Dec(r1) : ""), "0");
        bool updating = false;
        cc.SelectionChanged += (_, _) =>
        {
            if (updating || cc.SelectedItem is not Client c) return;
            if (c.Id == "__new")
            {
                var nc = ClientsPage.EditClient(null);
                updating = true;
                if (nc != null)
                {
                    choices.Add(nc); choices.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
                    cc.ItemsSource = Items(); cc.SelectedItem = nc; last = nc;
                    if (string.IsNullOrWhiteSpace(rate.Text) && nc.DefaultRate is decimal r2) rate.Text = Fmt.Dec(r2);
                }
                else cc.SelectedItem = last;
                updating = false;
                return;
            }
            last = c;
            if (editing == null && string.IsNullOrWhiteSpace(rate.Text) && c.DefaultRate is decimal dr) rate.Text = Fmt.Dec(dr);
        };

        var bill = Ui.Check("Billable by default", editing?.Billable ?? true);

        // ---- budget
        string TypeKey(string disp) => disp == "Fixed fee" ? "Fixed" : disp;
        string TypeDisp(string key) => key == "Fixed" ? "Fixed fee" : key;
        var bt = Ui.Combo(new[] { "None", "Hours", "Amount", "Fixed fee", "Retainer" }, TypeDisp(string.IsNullOrEmpty(editing?.BudgetType) ? "None" : editing!.BudgetType));
        var bv = Ui.Input(editing is { BudgetValue: > 0 } ? Fmt.Dec(editing.BudgetValue) : "", "0");
        var bp = Ui.Combo(new[] { "Total", "Per month" }, (editing?.BudgetPeriod ?? "Total") == "Monthly" ? "Per month" : "Total");
        bp.MinWidth = 150;
        var bvLabel = Ui.Text("BUDGET", 12, "MutedBrush", FontWeights.SemiBold);
        var bvGrid = new Grid();
        bvGrid.ColumnDefinitions.Add(new ColumnDefinition()); bvGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); bvGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bvGrid.Children.Add(bv); Grid.SetColumn(bp, 2); bvGrid.Children.Add(bp);
        var bvRow = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        bvRow.Children.Add(bvLabel.WithMargin(0, 0, 0, 6)); bvRow.Children.Add(bvGrid);
        void UpdateBudgetUi()
        {
            var k = TypeKey((string)bt.SelectedItem);
            bvRow.Visibility = k == "None" ? Visibility.Collapsed : Visibility.Visible;
            bp.Visibility = k is "Hours" or "Amount" ? Visibility.Visible : Visibility.Collapsed;
            bvLabel.Text = k switch { "Hours" => Loc.T("BUDGET (HOURS)"), "Amount" => Loc.F("BUDGET ({0})", cur), "Fixed" => Loc.F("FIXED FEE ({0})", cur), _ => Loc.F("MONTHLY FEE ({0})", cur) };
        }
        bt.SelectionChanged += (_, _) => UpdateBudgetUi();
        UpdateBudgetUi();

        var err = Dialogs.ErrorLabel();
        string color = editing?.Color ?? "blue";
        var swatches = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in Ui.ProjectColors)
        {
            var rb = new RadioButton
            {
                Style = (Style)Application.Current.FindResource("SwatchButton"), GroupName = "color",
                Background = new System.Windows.Media.SolidColorBrush(Ui.ProjColor(c)), IsChecked = c == color, ToolTip = char.ToUpper(c[0]) + c[1..]
            };
            var cc2 = c; rb.Checked += (_, _) => color = cc2;
            System.Windows.Automation.AutomationProperties.SetName(rb, cc2);
            swatches.Children.Add(rb);
        }

        var body = new StackPanel();
        body.Children.Add(Ui.Field("PROJECT NAME", name));
        body.Children.Add(Ui.Field("CLIENT", cc));
        body.Children.Add(Ui.Field(Loc.F("HOURLY RATE ({0})", cur), rate));
        body.Children.Add(bill.WithMargin(0, 0, 0, 14));
        body.Children.Add(Ui.Field("BUDGET TYPE", bt));
        body.Children.Add(bvRow);
        body.Children.Add(Ui.Field("COLOUR", swatches));
        body.Children.Add(err);

        Dialogs.Show(editing == null ? "New project" : "Edit project", body, editing == null ? "Create project" : "Save changes", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { Dialogs.SetError(err, "Add a project name."); return false; }
            if (cc.SelectedItem is not Client client || client.Id == "__new") { Dialogs.SetError(err, "Choose or create a client for this project."); return false; }
            if (!Ui.TryDec(rate.Text, out var r) || r < 0) { Dialogs.SetError(err, "Enter an hourly rate like 85 or 37.50."); return false; }
            var type = TypeKey((string)bt.SelectedItem);
            decimal bval = 0;
            if (type != "None" && (!Ui.TryDec(bv.Text, out bval) || bval <= 0)) { Dialogs.SetError(err, "Enter a budget like 40 or 1500."); return false; }

            var p = editing ?? new Project();
            var period = type is "Hours" or "Amount" && (string)bp.SelectedItem == "Per month" ? "Monthly" : "Total";
            if (p.BudgetType != type || p.BudgetValue != bval || p.BudgetPeriod != period) { p.AlertedWarn = p.AlertedOver = false; p.AlertPeriodKey = ""; }
            p.Name = name.Text.Trim(); p.ClientId = client.Id; p.Client = client.Name; p.ClientEmail = client.Email; p.ClientAddress = client.Address;
            p.HourlyRate = Math.Round(r, 2); p.Color = color; p.Billable = bill.IsChecked == true;
            p.BudgetType = type; p.BudgetValue = type == "None" ? 0 : bval; p.BudgetPeriod = period;
            if (editing == null) State.Data.Projects.Add(p);
            State.Notify();
            State.Toast(editing == null ? Loc.F("Project “{0}” created", p.Name) : "Project updated");
            return true;
        }, 520);
    }
}
