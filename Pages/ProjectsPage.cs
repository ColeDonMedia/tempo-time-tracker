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
        names.Children.Add(Ui.Text(string.IsNullOrWhiteSpace(p.Client) ? Loc.T("No client set") : Loc.F("for {0}", p.Client), 13, "MutedBrush"));
        Grid.SetColumn(names, 1); top.Children.Add(names);
        var rate = Ui.Pill(Loc.F("{0} / hr", Fmt.Money(p.HourlyRate)), "TextBrush", "FieldBrush");
        Grid.SetColumn(rate, 2); top.Children.Add(rate);
        sp.Children.Add(top);

        var stats = new UniformGridLike();
        var tracked = sessions.Sum(s => s.DurationMilliseconds);
        sp.Children.Add(stats.Row(("TRACKED", Fmt.Hm(tracked)), ("UNBILLED", Fmt.Money(State.UnbilledFor(p.Id)))));

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
        var invCount = d.Invoices.Count(i => i.ProjectId == p.Id);
        var billed = ss.Count(s => s.InvoiceId != null);
        var msg = Loc.F("Permanently delete “{0}”?", p.Name);
        if (ss.Count > 0)
        {
            msg += "\n\n" + Loc.N(ss.Count, "This also deletes its {0} time entry ({1}).", "This also deletes its {0} time entries ({1}).", Fmt.Hm(ss.Sum(s => s.DurationMilliseconds)));
            if (billed > 0) msg += " " + Loc.F("{0} of them are already billed.", billed);
        }
        if (invCount > 0)
            msg += "\n\n" + Loc.N(invCount, "Its {0} invoice record is kept in Invoices history (delete it there if you want it gone too).", "Its {0} invoice records are kept in Invoices history (delete them there if you want them gone too).");
        msg += "\n\n" + Loc.T("This can't be undone. Archiving hides a project without deleting anything.");
        if (!Dialogs.Confirm("Delete project", msg, "Delete project", true)) return;
        d.Sessions.RemoveAll(s => s.ProjectId == p.Id);
        d.Projects.Remove(p);
        if (d.Timer.ProjectId == p.Id) d.Timer.ProjectId = null;
        State.Notify();
        State.Toast(Loc.F("Project “{0}” deleted", p.Name));
    }

    sealed class UniformGridLike
    {
        public Grid Row(params (string Label, string Value)[] items)
        {
            var g = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            for (int i = 0; i < items.Length; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition());
                var s = new StackPanel();
                s.Children.Add(Ui.Text(Loc.T(items[i].Label), 10.5, "FaintBrush", FontWeights.SemiBold));
                s.Children.Add(Ui.Text(items[i].Value, 16, "TextBrush", FontWeights.SemiBold).WithMargin(0, 2, 0, 0));
                Grid.SetColumn(s, i); g.Children.Add(s);
            }
            return g;
        }
    }

    public static void EditProject(Project? editing)
    {
        var name = Ui.Input(editing?.Name ?? "", "e.g. Brand identity refresh");
        var client = Ui.Input(editing?.Client ?? "", "e.g. Hashmark");
        var email = Ui.Input(editing?.ClientEmail ?? "", "billing@client.com");
        var address = Ui.Input(editing?.ClientAddress ?? "", "Street, city, postcode");
        address.AcceptsReturn = true; address.MinHeight = 64; address.TextWrapping = TextWrapping.Wrap;
        address.VerticalContentAlignment = VerticalAlignment.Top;
        var rate = Ui.Input(editing != null ? Fmt.Dec(editing.HourlyRate) : "", "0");
        var cur = State.Data.Settings.CurrencyCode;
        var err = Dialogs.ErrorLabel();

        string color = editing?.Color ?? "coral";
        var swatches = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in Ui.ProjectColors)
        {
            var rb = new RadioButton
            {
                Style = (Style)Application.Current.FindResource("SwatchButton"), GroupName = "color",
                Background = new System.Windows.Media.SolidColorBrush(Ui.ProjColor(c)), IsChecked = c == color, ToolTip = char.ToUpper(c[0]) + c[1..]
            };
            var cc = c; rb.Checked += (_, _) => color = cc;
            System.Windows.Automation.AutomationProperties.SetName(rb, cc);
            swatches.Children.Add(rb);
        }

        var body = new StackPanel();
        body.Children.Add(Ui.Field("PROJECT NAME", name));
        body.Children.Add(Ui.Field("PERSON OR BRAND (BILL TO)", client));
        body.Children.Add(Ui.Field("CLIENT EMAIL (OPTIONAL)", email));
        body.Children.Add(Ui.Field("CLIENT ADDRESS (OPTIONAL)", address));
        body.Children.Add(Ui.Field(Loc.F("HOURLY RATE ({0})", cur), rate));
        body.Children.Add(Ui.Field("COLOUR", swatches));
        body.Children.Add(err);

        Dialogs.Show(editing == null ? "New project" : "Edit project", body, editing == null ? "Create project" : "Save changes", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { Dialogs.SetError(err, "Add a project name."); return false; }
            if (string.IsNullOrWhiteSpace(client.Text)) { Dialogs.SetError(err, "Add the person or brand this project is billed to."); return false; }
            if (!Ui.TryDec(rate.Text, out var r) || r < 0) { Dialogs.SetError(err, "Enter an hourly rate like 85 or 37.50."); return false; }
            var p = editing ?? new Project();
            p.Name = name.Text.Trim(); p.Client = client.Text.Trim(); p.ClientEmail = email.Text.Trim();
            p.ClientAddress = address.Text.Trim(); p.HourlyRate = Math.Round(r, 2); p.Color = color;
            if (editing == null) State.Data.Projects.Add(p);
            State.Notify();
            State.Toast(editing == null ? Loc.F("Project “{0}” created", p.Name) : "Project updated");
            return true;
        }, 500);
    }
}

