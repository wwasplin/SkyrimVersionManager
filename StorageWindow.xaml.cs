using System.Windows;
using System.Windows.Controls;
using SkyrimVersionManager.Services;

namespace SkyrimVersionManager;

public partial class StorageWindow : Window
{
    /// <summary>True if anything was deleted, so the main window can refresh its status.</summary>
    public bool ChangedAnything { get; private set; }

    public StorageWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowTheming.EnableDarkTitleBar(this);
        Reload();
    }

    private void Reload()
    {
        var items = StorageService.GetItems();
        ItemsList.ItemsSource = items;
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DeleteAllBtn.IsEnabled = items.Count > 0;
        TotalText.Text = items.Count == 0
            ? ""
            : $"{items.Count} item(s), {StorageItem.FormatSize(items.Sum(i => i.SizeBytes))} total";
    }

    private bool TryDelete(StorageItem item)
    {
        try
        {
            StorageService.Delete(item);
            ChangedAnything = true;
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not delete {item.Name}:\n{ex.Message}", "Delete failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void DeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not StorageItem item) return;

        var answer = MessageBox.Show(this,
            $"Delete this item?\n\n{item.Description}\n\nThis cannot be undone.",
            "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Yes)
        {
            TryDelete(item);
            Reload();
        }
    }

    private void DeleteAllBtn_Click(object sender, RoutedEventArgs e)
    {
        // Per-item confirmation: Yes deletes, No skips, Cancel stops the sweep.
        foreach (var item in StorageService.GetItems())
        {
            var answer = MessageBox.Show(this,
                $"Delete this item?\n\n{item.Description}\n\nYes = delete, No = keep, Cancel = stop.",
                "Delete all - confirm each item", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.Cancel) break;
            if (answer == MessageBoxResult.Yes) TryDelete(item);
        }
        Reload();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}
