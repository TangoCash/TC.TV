using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Localization = TCTV.Core.Localization;

namespace TCTV.Views;

/// <summary>Kleiner Eingabedialog (Ersatz für die AutoIt-InputBox-Aufrufe).</summary>
public sealed class PromptWindow : Window
{
    private readonly TextBox _input;

    internal PromptWindow(string title, string prompt, string defaultValue)
    {
        Title = "TC.TV";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, "Brush.Window");

        Border card = new() { Margin = new Thickness(16), Padding = new Thickness(16) };
        card.SetResourceReference(StyleProperty, "Card");

        StackPanel content = new();

        StackPanel head = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        Border badge = new()
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(9),
        };
        badge.SetResourceReference(BackgroundProperty, "Brush.Accent");
        TextBlock badgeGlyph = new()
        {
            Text = "\uE70F",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        badgeGlyph.SetResourceReference(TextBlock.ForegroundProperty, "Brush.AccentText");
        badge.Child = badgeGlyph;
        head.Children.Add(badge);

        TextBlock header = new()
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        header.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        head.Children.Add(header);

        content.Children.Add(head);

        TextBlock label = new()
        {
            Text = prompt,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Muted");
        content.Children.Add(label);

        _input = new TextBox { Text = defaultValue };
        if (TryFindResource("InputBox") is Style inputStyle)
        {
            _input.Style = inputStyle;
        }

        content.Children.Add(_input);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };

        Button ok = new() { Content = "OK", Width = 90, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        if (TryFindResource("PrimaryButton") is Style primaryStyle)
        {
            ok.Style = primaryStyle;
        }

        ok.Click += (_, _) => DialogResult = true;

        Button cancel = new() { Content = Localization.T(272), Width = 90, IsCancel = true };
        cancel.Click += (_, _) => DialogResult = false;

        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        content.Children.Add(buttons);

        card.Child = content;
        Content = card;

        Loaded += (_, _) =>
        {
            _input.Focus();
            _input.SelectAll();
        };
    }

    /// <summary>Fragt einen Text ab; liefert null bei Abbruch.</summary>
    public static string? Ask(Window owner, string title, string prompt, string defaultValue)
    {
        PromptWindow window = new(title, prompt, defaultValue)
        {
            Owner = owner.IsVisible ? owner : null,
        };

        return window.ShowDialog() == true ? window._input.Text : null;
    }

    /// <summary>Erzeugt den Dialog nur zum Prüfen der Oberflaeche (siehe --selftest).</summary>
    internal static PromptWindow CreateForSelfTest() => new("TC.TV", "Selbsttest", "1");
}
