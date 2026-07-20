// Displays a word's definitions (modal). Port of ui.ShowDefinition in
// ui/dialogs_windows.go.
using System.Text;
using System.Windows;

namespace WordBombTool.Views;

public partial class DefinitionWindow : Window
{
    public DefinitionWindow(string word, List<string> definitions)
    {
        InitializeComponent();
        Title = $"Definition of '{word}'";
        var sb = new StringBuilder();
        for (var i = 0; i < definitions.Count; i++)
            sb.Append(i + 1).Append(". ").Append(definitions[i].Trim()).Append("\n\n");
        DefinitionText.Text = sb.ToString();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>No-op when there are no definitions, matching the original.</summary>
    public static void Show(Window? owner, string word, List<string> definitions)
    {
        if (definitions.Count == 0) return;
        var win = new DefinitionWindow(word, definitions);
        if (owner != null && owner.IsVisible) win.Owner = owner;
        win.ShowDialog();
    }
}
