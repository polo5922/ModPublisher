using System.Windows;
using ModPublisher.Services;

namespace ModPublisher.Views;

public partial class DiffWindow : Window
{
    private readonly List<LineDiff.Line> _lines;

    public DiffWindow(string title, List<LineDiff.Line> lines)
    {
        InitializeComponent();
        Title = title;
        _lines = lines;
        var added = lines.Count(l => l.Kind == LineDiff.Kind.Added);
        var removed = lines.Count(l => l.Kind == LineDiff.Kind.Removed);
        Summary.Text = added + removed == 0 ? L.T("Aucune différence", "No differences") : L.T($"{removed} ligne(s) en ligne seulement, {added} ligne(s) locale(s) seulement", $"{removed} line(s) only online, {added} line(s) only local");
        Apply();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => Apply();

    private void Apply()
    {
        if (Lines is null || _lines is null) return;
        if (OnlyChanges.IsChecked != true)
        {
            Lines.ItemsSource = _lines;
            return;
        }
        // Lignes modifiées, avec une ligne de contexte autour
        var keep = new bool[_lines.Count];
        for (var i = 0; i < _lines.Count; i++)
            if (_lines[i].Kind != LineDiff.Kind.Same)
                for (var j = Math.Max(0, i - 1); j <= Math.Min(_lines.Count - 1, i + 1); j++) keep[j] = true;
        Lines.ItemsSource = _lines.Where((_, i) => keep[i]).ToList();
    }
}
