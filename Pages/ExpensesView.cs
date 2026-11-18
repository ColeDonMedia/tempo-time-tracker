using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Tempo;

/// <summary>Out-of-pocket costs per project, with receipts, billable on the next invoice.</summary>
public static class ExpensesView
{
    static string projectFilter = "";
    static string status = "All";   // All | Unbilled | Invoiced

    public static StackPanel Build(Func<UIElement> tabs, Action rebuild)
    {
        var d = State.Data;
        var root = new StackPanel();
        root.Children.Add(Ui.Header("Time entries", "Track out-of-pocket costs for a project, attach the receipt, and bill them on the next invoice.",
            Ui.Btn("Add expense", () => Edit(null), "PrimaryButton", Icons.Plus)));
        root.Children.Add(tabs());

        var projects = new List<Project> { new() { Id = "", Name = Loc.T("All projects") } }; projects.AddRange(d.Projects);
        var pc = Ui.Combo(projects, projects.FirstOrDefault(p => p.Id == projectFilter), "Name"); pc.MinWidth = 220;
        pc.SelectionChanged += (_, _) => { projectFilter = (pc.SelectedItem as Project)?.Id ?? ""; rebuild(); };
        var sc = Ui.Combo(new[] { "All", "Unbilled", "Invoiced" }, status); sc.MinWidth = 140;
        sc.SelectionChanged += (_, _) => { status = (string)sc.SelectedItem; rebuild(); };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        pc.Margin = new Thickness(0, 0, 10, 0);
        bar.Children.Add(pc); bar.Children.Add(sc);
        root.Children.Add(bar);

        var list = d.Expenses.AsEnumerable();
        if (projectFilter != "") list = list.Where(e => e.ProjectId == projectFilter);
        if (status == "Unbilled") list = list.Where(e => e.InvoiceId == null);
        if (status == "Invoiced") list = list.Where(e => e.InvoiceId != null);
        var rows = list.OrderByDescending(e => e.Date).ToList();

        var card = new StackPanel();
        if (rows.Count == 0)
            card.Children.Add(Ui.Empty(Icons.Receipt, "No expenses yet", "Track out-of-pocket costs for a project, attach the receipt, and bill them on the next invoice.",
                Ui.Btn("Add expense", () => Edit(null), "PrimaryButton", Icons.Plus)));
        else
        {
            var sum = Ui.Text(Loc.N(rows.Count, "{0} expense  ·  {1}", "{0} expenses  ·  {1}", Fmt.Money(rows.Sum(e => e.Amount))), 13, "MutedBrush");
            sum.Margin = new Thickness(10, 4, 0, 8);
            card.Children.Add(sum);
            foreach (var e in rows) card.Children.Add(Row(e));
        }
        root.Children.Add(Ui.Card(card, 14));
        return root;
    }

    static Border Row(Expense e)
    {
        var p = State.Proj(e.ProjectId);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = Ui.Dot(p?.Color ?? "gold", 10); dot.Margin = new Thickness(0, 0, 14, 0); g.Children.Add(dot);
        var mid = new StackPanel();
        mid.Children.Add(Ui.Text(string.IsNullOrWhiteSpace(e.Description) ? Loc.T("Expense") : e.Description, 14.5, "TextBrush", FontWeights.SemiBold));
        mid.Children.Add(Ui.Text($"{Fmt.Date(e.Date)}  ·  {p?.Name ?? Loc.T("Deleted project")}{(p != null && State.ClientName(p).Length > 0 ? " · " + State.ClientName(p) : "")}", 12.5, "MutedBrush"));
        if (!e.Billable) mid.Children.Add(Ui.Pill("Non-billable", "WarnBrush", "WarnSoftBrush").HorizontalLeft().WithMargin(0, 5, 0, 0));
        Grid.SetColumn(mid, 1); g.Children.Add(mid);

        var amt = Ui.Text(Fmt.Money(e.Amount), 14, "TextBrush", FontWeights.SemiBold); amt.Margin = new Thickness(12, 0, 12, 0); amt.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(amt, 2); g.Children.Add(amt);

        var acts = new StackPanel { Orientation = Orientation.Horizontal };
        if (!string.IsNullOrEmpty(e.ReceiptPath) && File.Exists(e.ReceiptPath))
            acts.Children.Add(Ui.IconBtn(Icons.Receipt, "Open receipt", () => { try { Process.Start(new ProcessStartInfo(e.ReceiptPath) { UseShellExecute = true }); } catch (Exception ex) { State.Toast(ex.Message, true); } }));
        if (e.InvoiceId != null)
        {
            var inv = State.Data.Invoices.FirstOrDefault(i => i.Id == e.InvoiceId);
            acts.Children.Add(Ui.Pill(inv?.Number ?? "Invoiced", "GoodBrush", "GoodSoftBrush"));
        }
        else acts.Children.Add(Ui.IconBtn(Icons.Edit, "Edit expense", () => Edit(e)));
        acts.Children.Add(Ui.IconBtn(Icons.Trash, "Delete expense", () =>
        {
            var t = TrashBin.Add("Expense", e, (string.IsNullOrWhiteSpace(e.Description) ? Loc.T("Expense") : e.Description) + " · " + Fmt.Money(e.Amount));
            State.Data.Expenses.Remove(e); State.Notify(); State.ToastUndo("Expense deleted", t);
        }, true));
        Grid.SetColumn(acts, 3); g.Children.Add(acts);

        var b = new Border { Padding = new Thickness(10, 10, 6, 10), CornerRadius = new CornerRadius(12), Child = g, Background = System.Windows.Media.Brushes.Transparent };
        b.MouseEnter += (_, _) => b.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
        b.MouseLeave += (_, _) => b.Background = System.Windows.Media.Brushes.Transparent;
        return b;
    }

    public static void Edit(Expense? editing)
    {
        var d = State.Data;
        var projects = State.ActiveProjects.ToList();
        if (editing != null && State.Proj(editing.ProjectId) is Project cur && !projects.Contains(cur)) projects.Add(cur);
        if (projects.Count == 0) { State.Toast("Create a project first", true); return; }

        var pc = Ui.Combo(projects, editing != null ? State.Proj(editing.ProjectId) : projects[0], "Label");
        var desc = Ui.Input(editing?.Description ?? "", "What was it for?");
        var date = Ui.Input((editing?.Date ?? State.TodayLocal).ToString("yyyy-MM-dd"), "yyyy-mm-dd");
        var amount = Ui.Input(editing != null ? Fmt.Dec(editing.Amount) : "", "0.00");
        var bill = Ui.Check("Billable", editing?.Billable ?? true);
        string receipt = editing?.ReceiptPath ?? "";
        var rl = Ui.Text(receipt.Length > 0 ? Path.GetFileName(receipt) : Loc.T("No receipt"), 13, "MutedBrush");
        rl.VerticalAlignment = VerticalAlignment.Center; rl.Margin = new Thickness(12, 0, 0, 0);
        var pick = Ui.Btn("Choose file…", () =>
        {
            var dlg = new OpenFileDialog { Title = Loc.T("RECEIPT (OPTIONAL)"), Filter = "Receipts (*.pdf;*.png;*.jpg;*.jpeg)|*.pdf;*.png;*.jpg;*.jpeg|All files (*.*)|*.*" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var dir = Path.Combine(State.Dir, "receipts"); Directory.CreateDirectory(dir);
                var dest = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Path.GetFileName(dlg.FileName)}");
                File.Copy(dlg.FileName, dest, true);
                receipt = dest; rl.Text = Path.GetFileName(dest);
            }
            catch (Exception ex) { State.Toast(ex.Message, true); }
        }, "GlassButton");
        var clear = Ui.Btn("Remove", () => { receipt = ""; rl.Text = Loc.T("No receipt"); }, "GhostButton");
        var rrow = new StackPanel { Orientation = Orientation.Horizontal };
        rrow.Children.Add(pick); rrow.Children.Add(clear); rrow.Children.Add(rl);
        var err = Dialogs.ErrorLabel();

        var two = new Grid();
        two.ColumnDefinitions.Add(new ColumnDefinition()); two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); two.ColumnDefinitions.Add(new ColumnDefinition());
        var f1 = Ui.Field("DATE", date); var f2 = Ui.Field(Loc.T("AMOUNT") + " (" + d.Settings.CurrencyCode + ")", amount); Grid.SetColumn(f2, 2);
        two.Children.Add(f1); two.Children.Add(f2);

        var body = new StackPanel();
        body.Children.Add(Ui.Field("PROJECT", pc));
        body.Children.Add(Ui.Field("DESCRIPTION", desc));
        body.Children.Add(two);
        body.Children.Add(bill.WithMargin(0, 0, 0, 14));
        body.Children.Add(Ui.Field("RECEIPT (OPTIONAL)", rrow));
        body.Children.Add(err);

        Dialogs.Show(editing == null ? "Add expense" : "Edit expense", body, editing == null ? "Add expense" : "Save changes", () =>
        {
            if (pc.SelectedItem is not Project p) { Dialogs.SetError(err, "Choose a project."); return false; }
            if (!Ui.TryDate(date.Text, out var dt)) { Dialogs.SetError(err, "Date should look like 2026-11-30."); return false; }
            if (!Ui.TryDec(amount.Text, out var a) || a <= 0) { Dialogs.SetError(err, "Enter an amount like 24.50."); return false; }
            var e = editing ?? new Expense();
            e.ProjectId = p.Id; e.Description = desc.Text.Trim(); e.Date = dt.Date; e.Amount = Math.Round(a, 2);
            e.Billable = bill.IsChecked == true; e.ReceiptPath = receipt;
            if (editing == null) d.Expenses.Add(e);
            State.Notify(); State.Toast(editing == null ? "Expense added" : "Expense updated");
            return true;
        }, 500);
    }
}
