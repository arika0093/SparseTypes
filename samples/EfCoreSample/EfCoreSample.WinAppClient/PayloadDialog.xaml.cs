using System.Windows;

namespace EfCoreSample.WinAppClient;

/// <summary>Shows the exact JSON body of the pending change payload.</summary>
public partial class PayloadDialog : Window
{
    /// <summary>Creates a payload viewer.</summary>
    /// <param name="json">The indented payload JSON.</param>
    public PayloadDialog(string json)
    {
        Json = json;
        InitializeComponent();
        DataContext = this;
    }

    /// <summary>Gets the payload JSON.</summary>
    public string Json { get; }
}
