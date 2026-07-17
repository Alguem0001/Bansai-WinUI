using System.Diagnostics;
using BonsaiWinUI.Services;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace BonsaiWinUI;

public sealed partial class MainWindow : Window
{
    private readonly AppConfig _cfg;
    private readonly LlamaServerService _server = new();
    private readonly DispatcherQueue _dq;
    private List<LocalModel> _localModels = [];
    private CancellationTokenSource? _healthCts;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1120, 780));

        _dq = DispatcherQueue.GetForCurrentThread();
        _cfg = AppConfig.Load();
        LoadUiFromConfig();

        CatalogCombo.ItemsSource = ModelCatalog.Known.Select(k => k.Label).ToList();
        if (CatalogCombo.Items.Count > 0)
            CatalogCombo.SelectedIndex = 0;

        _server.LogReceived += line => AppendLog(line);
        _server.ProcessExited += () => _dq.TryEnqueue(() =>
        {
            SetRunningUi(false);
            SetStatus("idle", "Parado", "Servidor encerrado.");
        });

        RefreshLocalModels();
        Closed += (_, _) =>
        {
            _healthCts?.Cancel();
            _server.Dispose();
            SaveConfigFromUi();
            _cfg.Save();
        };
    }

    private void LoadUiFromConfig()
    {
        LlamaBinBox.Text = _cfg.LlamaBin;
        ModelsDirBox.Text = _cfg.ModelsDir;
        HostBox.Text = _cfg.Host;
        PortBox.Value = _cfg.Port;
        CtxBox.Value = _cfg.ContextSize;
        NglBox.Value = _cfg.GpuLayers;
        ToolsToggle.IsOn = _cfg.EnableTools;
        JinjaToggle.IsOn = _cfg.EnableJinja;
        VisionToggle.IsOn = _cfg.EnableVision;
        ExtraArgsBox.Text = _cfg.ExtraArgs;
    }

    private void SaveConfigFromUi()
    {
        _cfg.LlamaBin = LlamaBinBox.Text.Trim();
        _cfg.ModelsDir = ModelsDirBox.Text.Trim();
        _cfg.Host = string.IsNullOrWhiteSpace(HostBox.Text) ? "127.0.0.1" : HostBox.Text.Trim();
        _cfg.Port = (int)(PortBox.Value is double.NaN ? 8080 : PortBox.Value);
        _cfg.ContextSize = (int)(CtxBox.Value is double.NaN ? 8192 : CtxBox.Value);
        _cfg.GpuLayers = (int)(NglBox.Value is double.NaN ? 99 : NglBox.Value);
        _cfg.EnableTools = ToolsToggle.IsOn;
        _cfg.EnableJinja = JinjaToggle.IsOn;
        _cfg.EnableVision = VisionToggle.IsOn;
        _cfg.ExtraArgs = ExtraArgsBox.Text?.Trim() ?? "";
        if (LocalModelCombo.SelectedItem is LocalModel m)
            _cfg.LastModelPath = m.Path;
    }

    private void RefreshLocalModels()
    {
        SaveConfigFromUi();
        _localModels = ModelCatalog.ScanLocal(_cfg.ModelsDir);
        LocalModelCombo.ItemsSource = _localModels;
        LocalModelCombo.DisplayMemberPath = nameof(LocalModel.Display);

        if (_localModels.Count == 0)
        {
            LocalModelCombo.SelectedItem = null;
            AppendLog($"Nenhum .gguf em {_cfg.ModelsDir}");
            return;
        }

        LocalModel? preferred = null;
        if (!string.IsNullOrEmpty(_cfg.LastModelPath))
            preferred = _localModels.FirstOrDefault(m =>
                string.Equals(m.Path, _cfg.LastModelPath, StringComparison.OrdinalIgnoreCase));

        preferred ??= _localModels.FirstOrDefault(m =>
            m.Name.Equals("Bonsai-27B-Q1_0.gguf", StringComparison.OrdinalIgnoreCase));

        LocalModelCombo.SelectedItem = preferred ?? _localModels[0];
    }

    private void AppendLog(string line)
    {
        _dq.TryEnqueue(() =>
        {
            LogBox.Text += line + Environment.NewLine;
            // keep log from growing unbounded
            if (LogBox.Text.Length > 200_000)
                LogBox.Text = LogBox.Text[^150_000..];
        });
    }

    private void SetStatus(string kind, string title, string message)
    {
        StatusTitle.Text = title;
        StatusMessage.Text = message;
        StatusDot.Background = new SolidColorBrush(kind switch
        {
            "ok" => ColorHelper.FromArgb(0xFF, 0x7A, 0x8F, 0x6E),      // moss
            "warn" => ColorHelper.FromArgb(0xFF, 0xC4, 0xA5, 0x74),    // copper
            "error" => ColorHelper.FromArgb(0xFF, 0xB0, 0x7A, 0x5C),   // clay
            "busy" => ColorHelper.FromArgb(0xFF, 0xC4, 0xA5, 0x74),
            _ => ColorHelper.FromArgb(0xFF, 0x6B, 0x67, 0x5F),         // ash dim
        });
    }

    private void SetRunningUi(bool running)
    {
        StartButton.IsEnabled = !running;
        StopButton.IsEnabled = running;
        LocalModelCombo.IsEnabled = !running;
    }

    // ---- Browse ----
    private async void BrowseLlamaBin_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync();
        if (path is not null)
        {
            LlamaBinBox.Text = path;
            SaveConfigFromUi();
        }
    }

    private async void BrowseModelsDir_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync();
        if (path is not null)
        {
            ModelsDirBox.Text = path;
            SaveConfigFromUi();
            RefreshLocalModels();
        }
    }

    private async void BrowseGguf_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickGgufAsync();
        if (path is null) return;

        ModelsDirBox.Text = Path.GetDirectoryName(path) ?? ModelsDirBox.Text;
        SaveConfigFromUi();
        RefreshLocalModels();
        var match = _localModels.FirstOrDefault(m =>
            string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            LocalModelCombo.SelectedItem = match;
    }

    /// <summary>Copy a .gguf into the models folder and select it.</summary>
    private async void ImportModel_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickGgufAsync();
        if (path is null) return;

        SaveConfigFromUi();
        try
        {
            Directory.CreateDirectory(_cfg.ModelsDir);
            var dest = Path.Combine(_cfg.ModelsDir, Path.GetFileName(path));

            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            {
                AppendLog($"Já está em models: {Path.GetFileName(path)}");
                RefreshLocalModels();
                SelectLocalByPath(dest);
                SetStatus("ok", "Modelo", Path.GetFileName(path));
                return;
            }

            if (File.Exists(dest))
            {
                var overwrite = await ShowConfirm(
                    $"Já existe {Path.GetFileName(dest)} na pasta de models.\nSubstituir?");
                if (!overwrite) return;
            }

            DownloadProgress.Visibility = Visibility.Visible;
            DownloadStatus.Text = $"Importando {Path.GetFileName(path)}…";
            AppendLog($"Import → {_cfg.ModelsDir}: {Path.GetFileName(path)}");

            await Task.Run(() => File.Copy(path, dest, overwrite: true));

            DownloadStatus.Text = "Importado.";
            AppendLog("Import OK: " + dest);
            RefreshLocalModels();
            SelectLocalByPath(dest);
            SetStatus("ok", "Modelo adicionado", Path.GetFileName(dest));
        }
        catch (Exception ex)
        {
            DownloadStatus.Text = "Falha no import.";
            AppendLog("ERRO import: " + ex.Message);
            await ShowInfo(ex.Message);
        }
        finally
        {
            DownloadProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void OpenModelsFolder_Click(object sender, RoutedEventArgs e)
    {
        SaveConfigFromUi();
        try
        {
            Directory.CreateDirectory(_cfg.ModelsDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = _cfg.ModelsDir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppendLog("ERRO: " + ex.Message);
        }
    }

    private void SelectLocalByPath(string path)
    {
        var match = _localModels.FirstOrDefault(m =>
            string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            LocalModelCombo.SelectedItem = match;
    }

    private async Task<string?> PickGgufAsync()
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add(".gguf");
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private async void DownloadCustom_Click(object sender, RoutedEventArgs e)
    {
        var repo = HfRepoBox.Text?.Trim() ?? "";
        var file = HfFileBox.Text?.Trim() ?? "";
        var mmproj = HfMmprojBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(repo) || string.IsNullOrWhiteSpace(file))
        {
            await ShowInfo("Preencha repo (org/name) e file.gguf.");
            return;
        }

        await DownloadHfAsync(repo, file, string.IsNullOrWhiteSpace(mmproj) ? null : mmproj);
    }

    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add("*");
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private void RefreshModels_Click(object sender, RoutedEventArgs e) => RefreshLocalModels();

    private void SaveConfig_Click(object sender, RoutedEventArgs e)
    {
        SaveConfigFromUi();
        _cfg.Save();
        AppendLog("Config salva.");
        SetStatus("ok", "Config", "Preferências salvas.");
    }

    // ---- Download ----
    private async void DownloadCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (CatalogCombo.SelectedIndex < 0 || CatalogCombo.SelectedIndex >= ModelCatalog.Known.Count)
        {
            await ShowInfo("Selecione um modelo do catálogo.");
            return;
        }

        var known = ModelCatalog.Known[CatalogCombo.SelectedIndex];
        await DownloadHfAsync(known.Repo, known.File, known.Mmproj);
    }

    private async Task DownloadHfAsync(string repo, string file, string? mmproj)
    {
        SaveConfigFromUi();
        Directory.CreateDirectory(_cfg.ModelsDir);

        DownloadProgress.Visibility = Visibility.Visible;
        DownloadStatus.Text = $"Baixando {file}…";
        AppendLog($"HF download: {repo} / {file}");

        try
        {
            await Task.Run(() =>
            {
                var modelsDir = _cfg.ModelsDir;
                var py = FindPython();
                if (py is null)
                    throw new InvalidOperationException("Python não encontrado (necessário para baixar do HuggingFace).");

                var script =
                    "from huggingface_hub import hf_hub_download; " +
                    $"p=hf_hub_download(repo_id='{repo}', filename='{file}', local_dir=r'{modelsDir}'); print(p)";
                RunProcess(py, $"-c \"{script}\"");

                if (!string.IsNullOrEmpty(mmproj))
                {
                    var script2 =
                        "from huggingface_hub import hf_hub_download; " +
                        $"p=hf_hub_download(repo_id='{repo}', filename='{mmproj}', local_dir=r'{modelsDir}'); print(p)";
                    RunProcess(py, $"-c \"{script2}\"");
                }
            });

            DownloadStatus.Text = "Download concluído.";
            AppendLog("Download OK.");
            RefreshLocalModels();
            SelectLocalByPath(Path.Combine(_cfg.ModelsDir, file));
            var local = _localModels.FirstOrDefault(m => m.Name.Equals(file, StringComparison.OrdinalIgnoreCase));
            if (local is not null) LocalModelCombo.SelectedItem = local;
            SetStatus("ok", "Modelo adicionado", file);
        }
        catch (Exception ex)
        {
            DownloadStatus.Text = "Erro no download.";
            AppendLog("ERRO: " + ex.Message);
            await ShowInfo("Falha no download:\n" + ex.Message);
        }
        finally
        {
            DownloadProgress.Visibility = Visibility.Collapsed;
        }
    }

    private static string? FindPython()
    {
        foreach (var name in new[] { "python", "py" })
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = name,
                    Arguments = name == "py" ? "-3 -c \"import sys; print(sys.executable)\"" : "-c \"import sys; print(sys.executable)\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p is null) continue;
                var output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(5000);
                if (p.ExitCode == 0 && File.Exists(output)) return output;
            }
            catch { /* try next */ }
        }
        return null;
    }

    private void RunProcess(string fileName, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Falha ao iniciar processo.");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (!string.IsNullOrWhiteSpace(stdout)) AppendLog(stdout.Trim());
        if (p.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? $"exit {p.ExitCode}" : stderr.Trim());
    }

    // ---- Server ----
    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        SaveConfigFromUi();
        _cfg.Save();

        if (LocalModelCombo.SelectedItem is not LocalModel model)
        {
            await ShowInfo("Selecione um modelo local (.gguf).");
            return;
        }

        try
        {
            _server.Start(_cfg, model.Path);
            SetRunningUi(true);
            SetStatus("busy", "Iniciando…",
                $"Carregando {model.Name} (PID {_server.ProcessId})");

            _healthCts?.Cancel();
            _healthCts = new CancellationTokenSource();
            var ct = _healthCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    var ok = await _server.WaitUntilHealthyAsync(_cfg, ct);
                    _dq.TryEnqueue(() =>
                    {
                        if (ok)
                        {
                            SetStatus("ok", "Rodando", _server.WebUiUrl(_cfg));
                            OpenBrowser(_server.WebUiUrl(_cfg));
                        }
                        else if (_server.IsRunning)
                        {
                            SetStatus("warn", "Aguardando",
                                "Servidor ainda carregando — abra a WebUI manualmente.");
                        }
                    });
                }
                catch (OperationCanceledException) { }
            }, ct);
        }
        catch (Exception ex)
        {
            AppendLog("ERRO: " + ex.Message);
            SetStatus("error", "Erro", ex.Message);
            SetRunningUi(false);
            await ShowInfo(ex.Message);
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _healthCts?.Cancel();
        _server.Stop();
        SetRunningUi(false);
        SetStatus("idle", "Parado", "Servidor parado.");
    }

    private void OpenWebUi_Click(object sender, RoutedEventArgs e)
    {
        SaveConfigFromUi();
        OpenBrowser(_server.WebUiUrl(_cfg));
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }

    private async Task ShowInfo(string message)
    {
        var dlg = new ContentDialog
        {
            Title = "Bansai",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot,
        };
        await dlg.ShowAsync();
    }

    private async Task<bool> ShowConfirm(string message)
    {
        var dlg = new ContentDialog
        {
            Title = "Bansai",
            Content = message,
            PrimaryButtonText = "Sim",
            CloseButtonText = "Não",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        var result = await dlg.ShowAsync();
        return result == ContentDialogResult.Primary;
    }
}
