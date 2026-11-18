using System.Windows;
using System.Windows.Controls;

namespace Tempo;

/// <summary>Editable list of invoice lines (description, quantity, price) used for quotes, recurring invoices and extra lines.</summary>
public static class LinesEditor
{
    public static FrameworkElement Create(List<InvoiceLine> lines, Action changed)
    {
        var host = new StackPanel();

        void Render()
        {
            host.Children.Clear();
            foreach (var line in lines.ToList()) host.Children.Add(Row(line));
            host.Children.Add(Ui.Btn("Add line", () => { lines.Add(new InvoiceLine { Kind = "Custom", Hours = 1 }); Render(); changed(); }, "GlassButton", Icons.Plus).HorizontalLeft().WithMargin(0, 6, 0, 0));
        }

        FrameworkElement Row(InvoiceLine line)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var desc = Ui.Input(line.Description, "Description"); desc.Padding = new Thickness(10, 7, 10, 7);
            var qty = Ui.Input(line.Hours == 0 ? "" : Fmt.Dec(line.Hours), "Qty"); qty.Padding = new Thickness(8, 7, 8, 7); qty.Margin = new Thickness(6, 0, 0, 0);
            var price = Ui.Input(line.Rate == 0 ? "" : Fmt.Dec(line.Rate), "Price"); price.Padding = new Thickness(8, 7, 8, 7); price.Margin = new Thickness(6, 0, 0, 0);
            var amt = Ui.Text(Fmt.Money(line.Amount), 13, "TextBrush", FontWeights.SemiBold); amt.TextAlignment = TextAlignment.Right; amt.VerticalAlignment = VerticalAlignment.Center; amt.Margin = new Thickness(6, 0, 4, 0);
            System.Windows.Automation.AutomationProperties.SetName(desc, Loc.T("DESCRIPTION"));

            void Update()
            {
                line.Description = desc.Text;
                Ui.TryDec(qty.Text.Length == 0 ? "0" : qty.Text, out var q);
                Ui.TryDec(price.Text.Length == 0 ? "0" : price.Text, out var pr);
                line.Hours = q; line.Rate = pr; line.Kind = "Custom";
                line.Amount = Math.Round(q * pr, 2);
                amt.Text = Fmt.Money(line.Amount);
                changed();
            }
            desc.TextChanged += (_, _) => Update();
            qty.TextChanged += (_, _) => Update();
            price.TextChanged += (_, _) => Update();

            g.Children.Add(desc);
            Grid.SetColumn(qty, 1); g.Children.Add(qty);
            Grid.SetColumn(price, 2); g.Children.Add(price);
            Grid.SetColumn(amt, 3); g.Children.Add(amt);
            var rm = Ui.IconBtn(Icons.Trash, "Remove", () => { lines.Remove(line); Render(); changed(); }, true);
            Grid.SetColumn(rm, 4); g.Children.Add(rm);
            return g;
        }

        Render();
        return host;
    }
}
