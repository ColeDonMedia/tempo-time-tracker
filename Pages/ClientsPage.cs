using System.Windows;
using System.Windows.Controls;

namespace Tempo;

public class ClientsPage : PageBase
{
    public override void Build()
    {
        var d = State.Data;
        var root = new StackPanel();
        root.Children.Add(Ui.Header("Clients", "People and brands you bill, with their default rate, terms and tax.",
            Ui.Btn("New client", () => EditClient(null), "PrimaryButton", Icons.Plus)));

        var clients = d.Clients.Where(c => !c.Archived).OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (clients.Count == 0)
            root.Children.Add(Ui.Card(Ui.Empty(Icons.Users, "No clients yet",
                "Add the people and brands you bill. Projects, invoices and rates can inherit their details.",
                Ui.Btn("Create your first client", () => EditClient(null), "PrimaryButton", Icons.Plus))));

        var wrap = new WrapPanel();
        foreach (var c in clients) wrap.Children.Add(Card(c));
        root.Children.Add(wrap);
        Content = root;
    }

    public static Grid StatRow(params (string Label, string Value)[] items)
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

    Border Card(Client c)
    {
        var d = State.Data;
        var projects = d.Projects.Where(p => p.ClientId == c.Id).ToList();
        var ids = projects.Select(p => p.Id).ToHashSet();
        var unbilled = projects.Sum(p => State.UnbilledFor(p.Id));
        var outstanding = d.Invoices.Where(i => i.Kind == "Invoice" && i.Status == "Issued" && (i.ClientId == c.Id || ids.Contains(i.ProjectId))).Sum(i => i.Balance);

        var sp = new StackPanel();
        sp.Children.Add(Ui.Text(c.Name, 17, "TextBrush", FontWeights.SemiBold));
        sp.Children.Add(Ui.Text(string.IsNullOrWhiteSpace(c.Email) ? " " : c.Email, 13, "MutedBrush"));
        sp.Children.Add(StatRow(("PROJECTS", projects.Count.ToString()), ("UNBILLED", Fmt.Money(unbilled)), ("OUTSTANDING", Fmt.Money(outstanding))));

        var actions = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        actions.Children.Add(Ui.Btn("New project", () => ProjectsPage.EditProject(null, c), "PrimaryButton", Icons.Plus).WithMargin(0, 0, 8, 6));
        actions.Children.Add(Ui.Btn("Invoice", () => { var p = projects.FirstOrDefault(); if (p != null) MainWindow.Instance.OpenInvoiceFor(p.Id); else State.Toast("Create a project first", true); }, "GlassButton", Icons.Doc).WithMargin(0, 0, 8, 6));
        actions.Children.Add(Ui.IconBtn(Icons.Edit, "Edit client", () => EditClient(c)).WithMargin(0, 0, 2, 6));
        actions.Children.Add(Ui.IconBtn(Icons.Trash, "Delete client", () => Delete(c), true).WithMargin(0, 0, 2, 6));
        foreach (var ch in actions.Children.OfType<Button>())
        {
            bool labelled = ch.Content is StackPanel panel && panel.Children.Count > 1;
            ch.Padding = labelled ? new Thickness(10, 8, 10, 8) : new Thickness(4);
            if (!labelled) ch.Margin = new Thickness(0, 0, 0, 6);
        }
        sp.Children.Add(actions);

        var card = Ui.Card(sp, 16);
        card.Width = 336; card.Margin = new Thickness(0, 0, 14, 16);
        return card;
    }

    static void Delete(Client c)
    {
        var d = State.Data;
        if (!Dialogs.Confirm("Delete client", Loc.F("Delete client “{0}”? Its projects are kept but will no longer be linked to a client.", c.Name), "Delete client", true)) return;
        var t = TrashBin.Add("Client", c, c.Name);
        foreach (var p in d.Projects.Where(p => p.ClientId == c.Id)) p.ClientId = "";
        d.Clients.Remove(c);
        State.Notify();
        State.ToastUndo(Loc.F("Client “{0}” deleted", c.Name), t);
    }

    /// <summary>Opens the client dialog. Returns the saved client (new or edited), or null if cancelled.</summary>
    public static Client? EditClient(Client? editing)
    {
        var d = State.Data;
        var name = Ui.Input(editing?.Name ?? "", "e.g. Hashmark");
        var email = Ui.Input(editing?.Email ?? "", "billing@client.com");
        var address = Ui.Input(editing?.Address ?? "", "Street, city, postcode");
        address.AcceptsReturn = true; address.MinHeight = 64; address.TextWrapping = TextWrapping.Wrap; address.VerticalContentAlignment = VerticalAlignment.Top;
        var taxId = Ui.Input(editing?.TaxId ?? "", "VAT / EIN (optional)");
        var rate = Ui.Input(editing?.DefaultRate is decimal r0 ? Fmt.Dec(r0) : "", "0");
        var defaultCur = "Default (from Settings)";
        var currencies = new List<string> { defaultCur }; currencies.AddRange(Fmt.Currencies);
        var cur = Ui.Combo(currencies, string.IsNullOrEmpty(editing?.Currency) ? defaultCur : editing!.Currency);
        var terms = Ui.Input(editing?.PaymentTermsDays?.ToString() ?? "", d.Settings.PaymentTermsDays.ToString());
        var tax = Ui.Input(editing?.TaxPercent is decimal t0 ? Fmt.Dec(t0) : "", Fmt.Dec(d.Settings.DefaultTaxPercent));
        var notes = Ui.Input(editing?.Notes ?? "", ""); notes.AcceptsReturn = true; notes.MinHeight = 56; notes.TextWrapping = TextWrapping.Wrap; notes.VerticalContentAlignment = VerticalAlignment.Top;
        var err = Dialogs.ErrorLabel();

        Grid Two(FrameworkElement a, FrameworkElement b)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); g.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(b, 2); g.Children.Add(a); g.Children.Add(b);
            return g;
        }

        var body = new StackPanel();
        body.Children.Add(Ui.Field("NAME", name));
        body.Children.Add(Two(Ui.Field("EMAIL", email), Ui.Field("TAX / VAT ID", taxId)));
        body.Children.Add(Ui.Field("ADDRESS", address));
        body.Children.Add(Two(Ui.Field("DEFAULT RATE", rate), Ui.Field("CURRENCY", cur)));
        body.Children.Add(Two(Ui.Field("PAYMENT TERMS (DAYS)", terms), Ui.Field("DEFAULT TAX %", tax)));
        var rc = Ui.Check("Reverse charge: business customer in another EU country (invoices carry no VAT)", editing?.ReverseCharge == true);
        body.Children.Add(rc.WithMargin(0, 0, 0, 14));
        body.Children.Add(Ui.Field("NOTES", notes));
        body.Children.Add(err);

        Client? result = null;
        Dialogs.Show(editing == null ? "New client" : "Edit client", body, editing == null ? "Create client" : "Save changes", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { Dialogs.SetError(err, "Add a client name."); return false; }
            decimal? rateV = null, taxV = null; int? termsV = null;
            if (!string.IsNullOrWhiteSpace(rate.Text)) { if (!Ui.TryDec(rate.Text, out var rr) || rr < 0) { Dialogs.SetError(err, "Enter an hourly rate like 85 or 37.50."); return false; } rateV = Math.Round(rr, 2); }
            if (!string.IsNullOrWhiteSpace(tax.Text)) { if (!Ui.TryDec(tax.Text, out var tt) || tt is < 0 or > 100) { Dialogs.SetError(err, "Tax % should be between 0 and 100."); return false; } taxV = tt; }
            if (!string.IsNullOrWhiteSpace(terms.Text)) { if (!int.TryParse(terms.Text, out var dd) || dd is < 0 or > 365) { Dialogs.SetError(err, "Due days should be a number from 0 to 365."); return false; } termsV = dd; }

            var c = editing ?? new Client();
            c.Name = name.Text.Trim(); c.Email = email.Text.Trim(); c.Address = address.Text.Trim(); c.TaxId = taxId.Text.Trim(); c.Notes = notes.Text.Trim();
            c.DefaultRate = rateV; c.TaxPercent = taxV; c.PaymentTermsDays = termsV; c.ReverseCharge = rc.IsChecked == true;
            c.Currency = (string)cur.SelectedItem == defaultCur ? "" : (string)cur.SelectedItem;
            if (editing == null) d.Clients.Add(c);
            // keep the text copy on each project (and its contact details) in step with the client record
            foreach (var p in d.Projects.Where(p => p.ClientId == c.Id)) { p.Client = c.Name; p.ClientEmail = c.Email; p.ClientAddress = c.Address; }
            State.Notify();
            State.Toast(editing == null ? Loc.F("Client “{0}” created", c.Name) : "Client updated");
            result = c;
            return true;
        }, 520);
        return result;
    }
}
