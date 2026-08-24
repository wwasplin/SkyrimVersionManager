using System.Windows;
using System.Windows.Media;
using SkyrimVersionManager.Services;

namespace SkyrimVersionManager;

public partial class PreflightWindow : Window
{
    public bool Proceed { get; private set; }

    private readonly bool _hasBlockers;

    private record IssueRow(string ChipText, Brush ChipBrush, string Title, string Detail);

    public PreflightWindow(IReadOnlyList<PreflightIssue> issues)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowTheming.EnableDarkTitleBar(this);

        var blockerBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x6B, 0x6B));
        var warnBrush = (Brush)FindResource("WarnBrush");
        var infoBrush = (Brush)FindResource("MutedBrush");

        IssuesList.ItemsSource = issues
            .OrderByDescending(i => i.Severity)
            .Select(i => new IssueRow(
                i.Severity.ToString().ToUpperInvariant(),
                i.Severity switch
                {
                    PreflightSeverity.Blocker => blockerBrush,
                    PreflightSeverity.Warning => warnBrush,
                    _ => infoBrush
                },
                i.Title, i.Detail))
            .ToList();

        _hasBlockers = issues.Any(i => i.Severity == PreflightSeverity.Blocker);
        if (_hasBlockers)
        {
            HeaderText.Text = "Serious compatibility problems were found. Switching anyway can crash the game or corrupt saves:";
            AckCheck.Visibility = Visibility.Visible;
            ProceedBtn.IsEnabled = false;
        }
    }

    private void AckCheck_Click(object sender, RoutedEventArgs e) =>
        ProceedBtn.IsEnabled = !_hasBlockers || AckCheck.IsChecked == true;

    private void ProceedBtn_Click(object sender, RoutedEventArgs e)
    {
        Proceed = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => Close();
}
