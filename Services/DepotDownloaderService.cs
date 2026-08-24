using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SkyrimVersionManager.Services;

/// <summary>
/// Wraps SteamRE's DepotDownloader CLI (MIT licensed, https://github.com/SteamRE/DepotDownloader).
/// The tool is fetched once from its official GitHub releases into data\tools and then reused.
///
/// Authentication is a normal Steam account login: the credentials are handed straight to
/// DepotDownloader (which talks to Steam's official auth servers) and are never written to disk
/// by this app. With -remember-password, Steam's login token is remembered so the password is
/// a one-time entry. If Steam Guard asks for a code (email or authenticator), the prompt is
/// surfaced in the UI via <c>requestInput</c> and the answer is piped back to the tool.
/// </summary>
public class DepotDownloaderService
{
    private static readonly Regex ProgressRegex = new(@"^\s*(\d{1,3}(?:[.,]\d+)?)\s*%", RegexOptions.Compiled);
    private static readonly Regex PromptRegex = new(
        @"(?i)(2\s*factor|authenticator|auth(entication)?\s+code|steam\s*guard|account password|password for)",
        RegexOptions.Compiled);

    public string ToolDir => Path.Combine(Paths.ToolsDir, "DepotDownloader");
    public string ExePath => Path.Combine(ToolDir, "DepotDownloader.exe");

    public bool IsInstalled => File.Exists(ExePath);

    public async Task EnsureInstalledAsync(Action<string> log, CancellationToken ct)
    {
        if (IsInstalled) return;

        log("DepotDownloader not found locally - downloading the official release from github.com/SteamRE/DepotDownloader ...");
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SkyrimVersionManager/1.0");

        var json = await http.GetStringAsync("https://api.github.com/repos/SteamRE/DepotDownloader/releases/latest", ct);
        using var doc = JsonDocument.Parse(json);

        string? assetUrl = null, assetName = null;
        foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (name.Contains("windows-x64", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                assetUrl = asset.GetProperty("browser_download_url").GetString();
                assetName = name;
                break;
            }
        }
        if (assetUrl == null)
            throw new InvalidOperationException("Could not find a windows-x64 DepotDownloader release asset on GitHub.");

        log($"Downloading {assetName} ...");
        var zipPath = Path.Combine(Paths.ToolsDir, assetName!);
        await using (var src = await http.GetStreamAsync(assetUrl, ct))
        await using (var dst = File.Create(zipPath))
        {
            await src.CopyToAsync(dst, ct);
        }

        if (Directory.Exists(ToolDir)) Directory.Delete(ToolDir, true);
        ZipFile.ExtractToDirectory(zipPath, ToolDir);
        File.Delete(zipPath);

        if (!IsInstalled)
        {
            // Some archives nest the files one folder deep; flatten if needed.
            var nested = Directory.GetFiles(ToolDir, "DepotDownloader.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (nested == null)
                throw new InvalidOperationException("DepotDownloader.exe missing from the extracted release.");
            var nestedDir = Path.GetDirectoryName(nested)!;
            foreach (var file in Directory.GetFiles(nestedDir))
                File.Move(file, Path.Combine(ToolDir, Path.GetFileName(file)), true);
        }
        log("DepotDownloader installed to " + ToolDir);
    }

    /// <summary>
    /// Downloads one depot (optionally pinned to a manifest) into targetDir.
    /// </summary>
    /// <param name="password">Steam password for the first login; null once a token is remembered.</param>
    /// <param name="requestInput">Called when the tool asks for input (Steam Guard code, or the
    /// password when the remembered token has expired). Second arg is true when the requested
    /// value is a password. Return null to abort.</param>
    public async Task DownloadDepotAsync(
        string appId,
        string depotId,
        string? manifestId,
        string targetDir,
        string username,
        string? password,
        Func<string, bool, Task<string?>> requestInput,
        Action<string> log,
        Action<double> progress,
        CancellationToken ct,
        bool manifestOnly = false)
    {
        var args = new List<string> { "-app", appId, "-depot", depotId };
        if (!string.IsNullOrEmpty(manifestId))
        {
            args.Add("-manifest");
            args.Add(manifestId!);
        }
        if (manifestOnly)
            args.Add("-manifest-only"); // login + ownership check without downloading the depot
        args.AddRange(new[] { "-dir", targetDir, "-remember-password", "-username", username });
        if (!string.IsNullOrEmpty(password))
        {
            args.Add("-password");
            args.Add(password!);
        }

        Directory.CreateDirectory(targetDir);

        var psi = new ProcessStartInfo
        {
            FileName = ExePath,
            WorkingDirectory = ToolDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) => exited.TrySetResult(process.ExitCode);

        if (!process.Start())
            throw new InvalidOperationException("Failed to start DepotDownloader.");

        var aborted = false;

        void EmitLine(string raw)
        {
            var clean = StripAnsi(raw).TrimEnd('\r');
            var pm = ProgressRegex.Match(clean);
            if (pm.Success && double.TryParse(pm.Groups[1].Value.Replace(',', '.'),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pct))
            {
                progress(pct);
                return; // per-file progress lines are too chatty for the log
            }
            if (clean.Trim().Length > 0)
                log("  " + clean.Trim());
        }

        async Task HandlePromptAsync(string promptText)
        {
            bool isPassword = promptText.Contains("password", StringComparison.OrdinalIgnoreCase);
            log("  " + promptText.Trim());
            var answer = await requestInput(promptText.Trim(), isPassword);
            if (answer == null)
            {
                aborted = true;
                try { if (!process.HasExited) process.Kill(true); } catch { }
                return;
            }
            await process.StandardInput.WriteLineAsync(answer);
            await process.StandardInput.FlushAsync();
        }

        // Prompts are written without a trailing newline (the tool then blocks on ReadLine),
        // so we read character-by-character and match the partial buffer against known prompts.
        async Task PumpAsync(StreamReader reader)
        {
            var sb = new StringBuilder();
            var buf = new char[256];
            int n;
            while ((n = await reader.ReadAsync(buf, 0, buf.Length)) > 0)
            {
                for (int i = 0; i < n; i++)
                {
                    char c = buf[i];
                    if (c == '\n')
                    {
                        EmitLine(sb.ToString());
                        sb.Clear();
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                // Only a buffer ending in ':' is a real prompt - matching on length alone can
                // misfire when an ordinary log line arrives split across read chunks.
                if (sb.Length > 0 && PromptRegex.IsMatch(sb.ToString()) &&
                    sb.ToString().TrimEnd().EndsWith(':'))
                {
                    var text = StripAnsi(sb.ToString());
                    sb.Clear();
                    await HandlePromptAsync(text);
                }
            }
            if (sb.Length > 0) EmitLine(sb.ToString());
        }

        var stdoutPump = PumpAsync(process.StandardOutput);
        var stderrPump = PumpAsync(process.StandardError);

        await using (ct.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } }))
        {
            var exitCode = await exited.Task;
            await Task.WhenAll(stdoutPump, stderrPump);
            ct.ThrowIfCancellationRequested();
            if (aborted)
                throw new OperationCanceledException("Login aborted.");
            if (exitCode != 0)
                throw new InvalidOperationException(
                    $"DepotDownloader exited with code {exitCode} for depot {depotId}. " +
                    "Check the log above (wrong password, network issue, or the account does not own Skyrim SE).");
        }
    }

    private static string StripAnsi(string s) => Regex.Replace(s, @"\x1B\[[0-9;]*[A-Za-z]", "");
}
