using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SkyrimVersionManager.Models;
using SkyrimVersionManager.Services;

namespace SkyrimVersionManager;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private readonly DepotDownloaderService _depotDownloader = new();
    private readonly SteamConsoleService _steamConsole = new();
    private AppSettings _settings = new();
    private VersionCatalog _catalog = null!;
    private DowngradeService _downgrade = null!;

    private string? _installedVersion;
    private bool _initializing = true;
    private bool _busy;
    private CancellationTokenSource? _cts;
    private TaskCompletionSource<string?>? _authInputTcs;
    private readonly DispatcherTimer _settingsSaveTimer;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        SourceInitialized += (_, _) => WindowTheming.EnableDarkTitleBar(this);

        // Text fields save through this debounce timer instead of on every keystroke.
        _settingsSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _settingsSaveTimer.Tick += (_, _) => { _settingsSaveTimer.Stop(); SaveSettings(); };
        Closing += (_, _) =>
        {
            if (_settingsSaveTimer.IsEnabled) { _settingsSaveTimer.Stop(); SaveSettings(); }
        };
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (v != null) Title = $"Skyrim Version Manager v{v.Major}.{v.Minor}.{v.Build}";

            RotateLogFile();
            _settings = _settingsService.Load();
            _catalog = VersionCatalog.Load();
            _downgrade = new DowngradeService(_catalog);

            VersionCombo.ItemsSource = _catalog.Versions;

            GamePathBox.Text = _settings.GamePathOverride ?? SteamLocator.FindSkyrimDir() ?? "";

            FullGameRadio.IsChecked = _settings.FullGameScope;
            ExeOnlyRadio.IsChecked = !_settings.FullGameScope;
            AccountAuthRadio.IsChecked = _settings.AuthMethod == "account";
            ConsoleAuthRadio.IsChecked = _settings.AuthMethod != "account";
            LockUpdatesCheck.IsChecked = _settings.LockUpdates;
            BackupCheck.IsChecked = _settings.BackupBeforeSwitch;
            SkseCheckBox.IsChecked = _settings.SkseCheck;
            ManageSavesCheck.IsChecked = _settings.ManageSaves;
            UsernameBox.Text = _settings.SteamUsername ?? "";
            AccountPanel.Visibility = _settings.AuthMethod == "account" ? Visibility.Visible : Visibility.Collapsed;

            if (_settings.DesiredVersion != null)
                VersionCombo.SelectedItem = _catalog.Find(_settings.DesiredVersion);

            Log($"{Title} started. Data folder: {Paths.DataDir}");
            if (string.IsNullOrEmpty(GamePathBox.Text))
                Log("Could not auto-detect a Skyrim Special Edition install - set the folder manually.");
        }
        finally
        {
            _initializing = false;
        }
        RefreshStatus();
    }

    // ----- UI event handlers -------------------------------------------------

    private void DetectBtn_Click(object sender, RoutedEventArgs e)
    {
        var found = SteamLocator.FindSkyrimDir();
        if (found != null)
        {
            GamePathBox.Text = found;
            _settings.GamePathOverride = null;
            SaveSettings();
            Log("Auto-detected: " + found);
        }
        else
        {
            Log("Auto-detect failed: no Skyrim Special Edition folder found in any Steam library.");
        }
        RefreshStatus();
    }

    private void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select the Skyrim Special Edition folder (contains SkyrimSE.exe)"
        };
        if (dialog.ShowDialog(this) == true)
        {
            GamePathBox.Text = dialog.FolderName;
            _settings.GamePathOverride = dialog.FolderName;
            SaveSettings();
            RefreshStatus();
        }
    }

    private void GamePathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initializing) return;
        _settings.GamePathOverride = string.IsNullOrWhiteSpace(GamePathBox.Text) ? null : GamePathBox.Text.Trim();
        SaveSettingsDeferred();
        RefreshStatus();
    }

    private void UsernameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initializing) return;
        _settings.SteamUsername = string.IsNullOrWhiteSpace(UsernameBox.Text) ? null : UsernameBox.Text.Trim();
        SaveSettingsDeferred();
    }

    private void VersionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        if (VersionCombo.SelectedItem is GameVersion v)
        {
            _settings.DesiredVersion = v.Version;
            SaveSettings();
        }
        RefreshStatus();
    }

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _settings.FullGameScope = FullGameRadio.IsChecked == true;
        _settings.AuthMethod = AccountAuthRadio.IsChecked == true ? "account" : "console";
        _settings.LockUpdates = LockUpdatesCheck.IsChecked == true;
        _settings.BackupBeforeSwitch = BackupCheck.IsChecked == true;
        _settings.SkseCheck = SkseCheckBox.IsChecked == true;
        _settings.ManageSaves = ManageSavesCheck.IsChecked == true;
        AccountPanel.Visibility = _settings.AuthMethod == "account" ? Visibility.Visible : Visibility.Collapsed;
        SaveSettings();
        RefreshStatus();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _authInputTcs?.TrySetResult(null);
        Log("Cancelling...");
    }

    private void UnlockBtn_Click(object sender, RoutedEventArgs e)
    {
        var gameDir = GamePathBox.Text.Trim();
        if (gameDir.Length == 0) { Log("Set the game folder first."); return; }
        DowngradeService.UnlockSteamUpdates(gameDir, Log);
    }

    /// <summary>
    /// Authenticates with Steam ahead of time: fetches only a depot manifest listing (no game
    /// files), which validates the credentials and Skyrim ownership and stores the login token
    /// so later downloads run without any auth interruptions.
    /// </summary>
    private async void LoginBtn_Click(object sender, RoutedEventArgs e)
    {
        var username = UsernameBox.Text.Trim();
        if (username.Length == 0) { Log("Enter your Steam username first."); return; }

        SetBusy(true);
        _cts = new CancellationTokenSource();
        var testDir = Path.Combine(Paths.DataDir, ".login-test");
        try
        {
            await _depotDownloader.EnsureInstalledAsync(Log, _cts.Token);
            Log($"Logging in to Steam as '{username}' (no game files are downloaded for this check) ...");
            var password = SteamPasswordBox.Password;
            await _depotDownloader.DownloadDepotAsync(
                _catalog.AppId, _catalog.ExeDepot, null, testDir,
                username,
                string.IsNullOrEmpty(password) ? null : password,
                RequestAuthInputAsync,
                line => Dispatcher.Invoke(() => Log(line)),
                _ => { },
                _cts.Token,
                manifestOnly: true);
            SteamPasswordBox.Password = "";
            Log("Login successful - Steam token remembered. Downloads will now run without asking again.");
        }
        catch (OperationCanceledException)
        {
            Log("Login cancelled.");
        }
        catch (Exception ex)
        {
            Log("Login failed: " + ex.Message);
        }
        finally
        {
            try { if (Directory.Exists(testDir)) Directory.Delete(testDir, true); } catch { }
            AuthPromptPanel.Visibility = Visibility.Collapsed;
            SetBusy(false);
            _cts.Dispose();
            _cts = null;
        }
    }

    private void StorageBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) { Log("Finish or cancel the current operation before managing storage."); return; }
        var window = new StorageWindow { Owner = this };
        window.ShowDialog();
        if (window.ChangedAnything)
        {
            Log("Stored data changed - refreshing status.");
            RefreshStatus();
        }
    }

    private void OpenDataBtn_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = Paths.DataDir,
            UseShellExecute = true
        });
    }

    private void CopyCommandsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ConsoleCommandsBox.Text.Length > 0)
        {
            if (TrySetClipboard(ConsoleCommandsBox.Text))
                Log("Commands copied to clipboard.");
            else
                Log("Could not access the clipboard (another app is holding it) - select and copy the text manually.");
        }
    }

    private void ReopenConsoleBtn_Click(object sender, RoutedEventArgs e) => SteamConsoleService.OpenConsole();

    // ----- Steam Guard / login prompt ---------------------------------------

    private Task<string?> RequestAuthInputAsync(string prompt, bool isPassword)
    {
        return Dispatcher.Invoke(() =>
        {
            _authInputTcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            AuthPromptText.Text = prompt;
            AuthCodeBox.Text = "";
            AuthPwBox.Password = "";
            AuthCodeBox.Visibility = isPassword ? Visibility.Collapsed : Visibility.Visible;
            AuthPwBox.Visibility = isPassword ? Visibility.Visible : Visibility.Collapsed;
            AuthPromptPanel.Visibility = Visibility.Visible;
            (isPassword ? (UIElement)AuthPwBox : AuthCodeBox).Focus();
            return _authInputTcs.Task;
        });
    }

    private void SubmitAuthInput()
    {
        var value = AuthPwBox.Visibility == Visibility.Visible ? AuthPwBox.Password : AuthCodeBox.Text.Trim();
        AuthPromptPanel.Visibility = Visibility.Collapsed;
        AuthPwBox.Password = "";
        _authInputTcs?.TrySetResult(value.Length == 0 ? null : value);
    }

    private void AuthOkBtn_Click(object sender, RoutedEventArgs e) => SubmitAuthInput();

    private void AuthInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SubmitAuthInput();
    }

    private void AuthCancelBtn_Click(object sender, RoutedEventArgs e)
    {
        AuthPromptPanel.Visibility = Visibility.Collapsed;
        _authInputTcs?.TrySetResult(null);
    }

    // ----- Status / detection ------------------------------------------------

    private void RefreshStatus()
    {
        if (_busy) return;

        var gameDir = GamePathBox.Text.Trim();
        var desired = VersionCombo.SelectedItem as GameVersion;
        VersionNotesText.Text = desired?.Notes ?? "";

        _installedVersion = gameDir.Length > 0 ? VersionDetector.DetectGameVersion(gameDir) : null;
        InstalledVersionText.Text = _installedVersion ?? "not found";

        var skse = gameDir.Length > 0 ? VersionDetector.DetectSkseVersion(gameDir) : null;
        SkseVersionText.Text = skse != null ? $"SKSE loader: {skse}" : "SKSE loader: not installed";

        if (_installedVersion == null)
        {
            SetStatus("No SkyrimSE.exe found at the selected folder. Pick the folder that contains SkyrimSE.exe.",
                (SolidColorBrush)FindResource("WarnBrush"));
            ApplyBtn.IsEnabled = false;
            return;
        }

        if (desired == null)
        {
            SetStatus($"Installed version detected: {_installedVersion}. Select a desired version to compare.",
                (SolidColorBrush)FindResource("MutedBrush"));
            ApplyBtn.IsEnabled = false;
            return;
        }

        bool cached = _downgrade.IsCached(desired, _settings.FullGameScope);
        string availability = cached ? " Files are cached locally - no download needed."
                            : _downgrade.ShouldUseBackupAsSource(desired, _settings.FullGameScope) ? " A local backup exists - no download needed."
                            : _downgrade.CanDownload(desired, _settings.FullGameScope) ? " Files will be downloaded once, then cached for future switches."
                            : " Only the executable manifest is known for this version - use 'Executables only' (or restore it from a full backup).";

        if (_installedVersion == desired.Version)
        {
            SetStatus($"MATCH - installed version {_installedVersion} is the desired version. Nothing to do.",
                (SolidColorBrush)FindResource("OkBrush"));
            ApplyBtn.IsEnabled = false;
        }
        else
        {
            string direction = VersionMath.Compare(_installedVersion, desired.Version) > 0 ? "Downgrade" : "Upgrade";
            SetStatus($"MISMATCH - installed {_installedVersion}, desired {desired.Version}. " +
                      $"Click Apply to {direction.ToLower()}.{availability}",
                (SolidColorBrush)FindResource("WarnBrush"));
            ApplyBtn.Content = $"{direction} to {desired.Version}";
            ApplyBtn.IsEnabled = true;
        }

        if (_settings.SkseCheck && skse != null && !string.IsNullOrEmpty(desired.ExpectedSkse) &&
            VersionMath.Compare(skse, desired.ExpectedSkse) != 0)
        {
            StatusText.Text += $"\nSKSE note: installed loader is {skse}, but {desired.Version} expects SKSE {desired.ExpectedSkse}. " +
                               "Update SKSE from skse.silverlock.org after switching or SKSE mods will not load.";
        }

        // Surface this before Apply is ever clicked - the pre-flight blocker repeats it.
        if (FirstRunService.PendingFirstRunMarkers(gameDir).Count > 0)
        {
            StatusText.Text += "\nFIRST RUN NEEDED: this install has never been launched from Steam. Launch Skyrim once " +
                               "(Steam Play, reach the main menu, quit) before switching versions, or Steam may treat " +
                               "the install as broken and repair or re-download it.";
        }
    }

    private void SetStatus(string text, SolidColorBrush brush)
    {
        StatusText.Text = text;
        StatusText.Foreground = brush;
    }

    // ----- Apply flow --------------------------------------------------------

    private async void ApplyBtn_Click(object sender, RoutedEventArgs e)
    {
        var gameDir = GamePathBox.Text.Trim();
        if (VersionCombo.SelectedItem is not GameVersion target || gameDir.Length == 0) return;

        // Replacing files under a running game means locked-file errors mid-copy and a
        // half-updated install - refuse outright rather than fail partway through.
        if (IsGameRunning())
        {
            Log("Skyrim is currently running - close the game (and any SKSE loader) before switching versions.");
            MessageBox.Show(this,
                "Skyrim is currently running.\n\nClose the game (and any SKSE loader) before switching versions.",
                "Game is running", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool fullGame = FullGameRadio.IsChecked == true;
        bool consoleMode = AccountAuthRadio.IsChecked != true;
        var request = new ApplyRequest(
            gameDir, target, fullGame,
            BackupCheck.IsChecked == true,
            LockUpdatesCheck.IsChecked == true,
            _installedVersion);

        // Pre-flight compatibility checks (blockers need explicit acknowledgement).
        var issues = await Task.Run(() =>
            PreflightService.Run(gameDir, _installedVersion, target, fullGame, _settings.ManageSaves));
        if (issues.Count > 0)
        {
            var preflight = new PreflightWindow(issues) { Owner = this };
            preflight.ShowDialog();
            if (!preflight.Proceed)
            {
                Log("Switch cancelled at pre-flight checks.");
                return;
            }
            foreach (var issue in issues.Where(i => i.Severity != PreflightSeverity.Info))
                Log($"Pre-flight {issue.Severity}: {issue.Title}");
        }
        else
        {
            Log("Pre-flight checks: no issues found.");
        }

        SetBusy(true);
        _cts = new CancellationTokenSource();
        Progress.Value = 0;

        try
        {
            Log($"=== Switching {_installedVersion ?? "?"} -> {target.Version} ({(fullGame ? "full game" : "executables only")}) ===");

            if (!_downgrade.ShouldUseBackupAsSource(target, fullGame))
            {
                var missing = _downgrade.MissingDepots(target, fullGame);
                if (missing.Length > 0)
                {
                    if (consoleMode)
                        await AcquireViaSteamConsoleAsync(target, missing, _cts.Token);
                    else
                        await AcquireViaAccountLoginAsync(target, missing, _cts.Token);
                    var mismatch = _downgrade.VerifyCachedExe(target, Log);
                    if (mismatch != null)
                    {
                        var answer = MessageBox.Show(this,
                            $"The downloaded SkyrimSE.exe reports version {mismatch}, but {target.Version} was expected.\n\n" +
                            "The wrong manifest may have been downloaded. Apply it anyway?",
                            "Version mismatch", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (answer != MessageBoxResult.Yes)
                            throw new OperationCanceledException("Aborted: downloaded version did not match.");
                    }
                }
            }

            await Task.Run(() => _downgrade.ApplyCachedAsync(
                request,
                log: line => Dispatcher.Invoke(() => Log(line)),
                progress: pct => Dispatcher.Invoke(() => Progress.Value = pct),
                _cts.Token), _cts.Token);

            if (_settings.ManageSaves && _installedVersion != null && _installedVersion != target.Version)
            {
                await Task.Run(() => SavesService.SwitchSaves(_installedVersion!, target.Version,
                    line => Dispatcher.Invoke(() => Log(line))));
            }

            // Always on: a Creations catalog from a newer build crashes older executables.
            if (_installedVersion != null && _installedVersion != target.Version)
            {
                try
                {
                    await Task.Run(() => CreationsCatalogService.Switch(_installedVersion!, target.Version,
                        line => Dispatcher.Invoke(() => Log(line))));
                }
                catch (Exception ex)
                {
                    Log("WARNING: could not adjust the Creations catalog: " + ex.Message +
                        " - if the game crashes ~30 s into startup, move %LOCALAPPDATA%\\Skyrim Special Edition\\ContentCatalog.txt aside.");
                }
            }

            if (!fullGame)
                Log("Scope note: only the executable depot (489833, ~35 MB) was applied - that is what SKSE " +
                    "compatibility needs. Community guides list three download commands; the other two are the " +
                    "large core/asset depots, which map to the 'Full game' scope. If the game misbehaves after " +
                    "this switch, select 'Full game' and Apply again.");
            Log("Done.");
        }
        catch (OperationCanceledException)
        {
            Log("Cancelled.");
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
        }
        finally
        {
            ConsolePanel.Visibility = Visibility.Collapsed;
            AuthPromptPanel.Visibility = Visibility.Collapsed;
            SetBusy(false);
            _cts.Dispose();
            _cts = null;
            RefreshStatus();
        }
    }

    /// <summary>
    /// Zero-credential path: the logged-in desktop Steam client downloads the depots via its
    /// console; this app supplies the commands and waits for the files.
    /// </summary>
    private async Task AcquireViaSteamConsoleAsync(GameVersion target, string[] missing, CancellationToken ct)
    {
        var contentRoot = SteamConsoleService.ContentRoot()
            ?? throw new InvalidOperationException("Steam installation not found - cannot use the Steam console method.");

        var jobs = missing.Select(depot => (depot, manifest: GetManifest(target, depot))).ToList();
        var commands = jobs.Select(j => SteamConsoleService.BuildCommand(_catalog.AppId, j.depot, j.manifest)).ToList();
        var commandText = string.Join(Environment.NewLine, commands);

        // Stale leftovers in Steam's content folder would fool the completion detector.
        foreach (var (depot, _) in jobs)
        {
            var dir = Path.Combine(contentRoot, "depot_" + depot);
            if (Directory.Exists(dir))
            {
                Log($"Removing stale Steam download folder for depot {depot} ...");
                Directory.Delete(dir, true);
            }
        }

        bool copied = TrySetClipboard(commandText);
        ConsoleCommandsBox.Text = commandText;
        ConsolePanel.Visibility = Visibility.Visible;
        SteamConsoleService.OpenConsole();
        Log(copied
            ? "Steam console opened. Paste the command(s) from your clipboard into it and press Enter."
            : "Steam console opened. The clipboard was not available - copy the command(s) from the box below, paste them into the console and press Enter.");
        if (commands.Count > 1)
            Log("If pasting all lines at once doesn't run them all, run them one at a time.");

        foreach (var (depot, _) in jobs)
        {
            ct.ThrowIfCancellationRequested();
            var dir = Path.Combine(contentRoot, "depot_" + depot);
            Log($"Waiting for Steam to download depot {depot} ({_downgrade.DepotLabel(depot)}) ...");
            var expected = depot == _catalog.ExeDepot ? "SkyrimSE.exe" : null;
            await _steamConsole.WaitForDepotAsync(dir, expected,
                line => Dispatcher.Invoke(() => Log(line)), ct);
            await Task.Run(() => _downgrade.CacheFromContentDir(target, depot, dir,
                line => Dispatcher.Invoke(() => Log(line))), ct);
        }
        ConsolePanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Account path: DepotDownloader logs in with the user's Steam credentials (passed straight
    /// to Steam, never stored here) and downloads the depots automatically.
    /// </summary>
    private async Task AcquireViaAccountLoginAsync(GameVersion target, string[] missing, CancellationToken ct)
    {
        var username = UsernameBox.Text.Trim();
        if (username.Length == 0)
            throw new InvalidOperationException("Enter your Steam username (Steam access > Steam account login), or switch to the Desktop Steam client method.");

        await _depotDownloader.EnsureInstalledAsync(Log, ct);

        var password = SteamPasswordBox.Password;
        foreach (var depot in missing)
        {
            ct.ThrowIfCancellationRequested();
            Log($"Downloading depot {depot} ({_downgrade.DepotLabel(depot)}) for {target.Version} ...");
            await _depotDownloader.DownloadDepotAsync(
                _catalog.AppId, depot, GetManifest(target, depot),
                _downgrade.CacheDirFor(target, depot),
                username,
                string.IsNullOrEmpty(password) ? null : password,
                RequestAuthInputAsync,
                line => Dispatcher.Invoke(() => Log(line)),
                pct => Dispatcher.Invoke(() => Progress.Value = pct),
                ct);
            _downgrade.MarkCached(target, depot);
            Log($"Depot {depot} cached.");
        }

        // Login token is now remembered by DepotDownloader; the password is no longer needed.
        SteamPasswordBox.Password = "";
    }

    /// <summary>Pinned manifest when known; null (= Steam's current build) only for the latest version.</summary>
    private string? GetManifest(GameVersion target, string depot)
    {
        var manifest = target.ManifestFor(depot);
        if (manifest != null || target.IsLatest) return manifest;
        throw new InvalidOperationException(
            $"No manifest is known for depot {depot} ({_downgrade.DepotLabel(depot)}) of version {target.Version}. " +
            "Switch to 'Executables only', or add the manifest ID to data\\versions.json.");
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ApplyBtn.IsEnabled = !busy;
        CancelBtn.IsEnabled = busy;
        VersionCombo.IsEnabled = !busy;
        GamePathBox.IsEnabled = !busy;
        DetectBtn.IsEnabled = !busy;
        BrowseBtn.IsEnabled = !busy;
        ExeOnlyRadio.IsEnabled = !busy;
        FullGameRadio.IsEnabled = !busy;
        ConsoleAuthRadio.IsEnabled = !busy;
        AccountAuthRadio.IsEnabled = !busy;
        UsernameBox.IsEnabled = !busy;
        SteamPasswordBox.IsEnabled = !busy;
        LoginBtn.IsEnabled = !busy;
        StorageBtn.IsEnabled = !busy;
        UnlockBtn.IsEnabled = !busy;
    }

    private void SaveSettings()
    {
        try { _settingsService.Save(_settings); }
        catch (Exception ex) { Log("Could not save settings: " + ex.Message); }
    }

    /// <summary>Restarts the debounce timer; the actual save runs once typing pauses.</summary>
    private void SaveSettingsDeferred()
    {
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    private static bool IsGameRunning()
    {
        var procs = new[] { "SkyrimSE", "skse64_loader" }
            .SelectMany(Process.GetProcessesByName).ToList();
        bool running = procs.Count > 0;
        procs.ForEach(p => p.Dispose());
        return running;
    }

    /// <summary>
    /// WPF's clipboard fails with CLIPBRD_E_CANT_OPEN whenever another process briefly holds
    /// the clipboard (remote desktop, clipboard managers) - retry, and never treat it as fatal.
    /// </summary>
    private static bool TrySetClipboard(string text)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                return true;
            }
            catch
            {
                Thread.Sleep(100);
            }
        }
        return false;
    }

    private static void RotateLogFile()
    {
        try
        {
            var info = new FileInfo(Paths.LogFile);
            if (info.Exists && info.Length > 1024 * 1024)
                info.MoveTo(Path.Combine(Paths.DataDir, "log.old.txt"), overwrite: true);
            File.AppendAllText(Paths.LogFile, $"===== Session {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====\r\n");
        }
        catch
        {
            // The in-app log still works without a log file.
        }
    }

    private void Log(string line)
    {
        var stamped = $"[{DateTime.Now:HH:mm:ss}] {line}";
        LogBox.AppendText(stamped + "\r\n");
        LogBox.ScrollToEnd();
        try { File.AppendAllText(Paths.LogFile, stamped + "\r\n"); } catch { }
    }
}
