using System.Collections.ObjectModel;
using System.Windows;
using SparseTypes;

namespace EfCoreSample.WinAppClient;

/// <summary>Lists the pending edits in a path/before/after table.</summary>
public partial class ChangesDialog : Window
{
    /// <summary>Creates a changes viewer.</summary>
    /// <param name="summary">Explains what the table shows.</param>
    /// <param name="rows">The projected change rows.</param>
    public ChangesDialog(string summary, IReadOnlyList<SparseChangeRow> rows)
    {
        Summary = summary;
        Rows = new ObservableCollection<SparseChangeRow>(rows);
        InitializeComponent();
        DataContext = this;
    }

    /// <summary>Gets the dialog explanation.</summary>
    public string Summary { get; }

    /// <summary>Gets the change rows.</summary>
    public ObservableCollection<SparseChangeRow> Rows { get; }
}
