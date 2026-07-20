// Displays the hotkeys/help window (modal). Port of ui.ShowHelp in
// ui/dialogs_windows.go.
using System.Windows;

namespace WordBombTool.Views;

public partial class HelpWindow : Window
{
    public HelpWindow(string text)
    {
        InitializeComponent();
        HelpText.Text = text.Trim();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    public static void Show(Window? owner, string text)
    {
        var win = new HelpWindow(text);
        if (owner != null && owner.IsVisible) win.Owner = owner;
        win.ShowDialog();
    }
}
