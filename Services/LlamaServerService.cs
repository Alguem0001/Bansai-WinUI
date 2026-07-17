using System.Diagnostics;
using System.Net.Http;
using System.Text;

namespace BonsaiWinUI.Services;

public sealed class LlamaServerService : IDisposable
{
    private Process? _process;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public bool IsRunning => _process is { HasExited: false };
    public int? ProcessId => IsRunning ? _process!.Id : null;

    public event Action<string>? LogReceived;
    public event Action? ProcessExited;

    public string BuildCommandLine(AppConfig cfg, string modelPath, string? mmprojPath)
    {
        var sb = new StringBuilder();
        sb.Append("-m \"").Append(modelPath).Append('"');
        sb.Append(" --host ").Append(cfg.Host);
        sb.Append(" --port ").Append(cfg.Port);
        sb.Append(" -c ").Append(cfg.ContextSize);
        sb.Append(" -ngl ").Append(cfg.GpuLayers);
        // Arc / Vulkan defaults — skip if user already set them in ExtraArgs
        var extra = cfg.ExtraArgs ?? "";
        if (!ContainsFlag(extra, "-fa") && !ContainsFlag(extra, "--flash-attn"))
            sb.Append(" -fa on");
        if (!ContainsFlag(extra, "-b") && !ContainsFlag(extra, "--batch-size"))
            sb.Append(" -b 512");
        if (!ContainsFlag(extra, "-ub") && !ContainsFlag(extra, "--ubatch-size"))
            sb.Append(" -ub 256");
        if (cfg.EnableJinja) sb.Append(" --jinja");
        if (cfg.EnableTools) sb.Append(" --tools all");
        if (cfg.EnableVision && !string.IsNullOrEmpty(mmprojPath))
            sb.Append(" --mmproj \"").Append(mmprojPath).Append('"');

        // Single-slot chat; no speculative/draft model is ever wired by the launcher.
        if (!ContainsFlag(extra, "-np") && !ContainsFlag(extra, "--parallel"))
            sb.Append(" -np 1");

        if (!string.IsNullOrWhiteSpace(extra))
            sb.Append(' ').Append(extra.Trim());
        return sb.ToString();
    }

    private static bool ContainsFlag(string args, string flag)
    {
        if (string.IsNullOrWhiteSpace(args)) return false;
        return args.Contains(flag + " ", StringComparison.OrdinalIgnoreCase)
            || args.Contains(flag + "=", StringComparison.OrdinalIgnoreCase)
            || args.EndsWith(flag, StringComparison.OrdinalIgnoreCase)
            || args.Contains(" " + flag + " ", StringComparison.OrdinalIgnoreCase);
    }

    public void Start(AppConfig cfg, string modelPath)
    {
        if (IsRunning)
            throw new InvalidOperationException("O servidor já está em execução.");

        var serverExe = Path.Combine(cfg.LlamaBin, "llama-server.exe");
        if (!File.Exists(serverExe))
            throw new FileNotFoundException($"llama-server.exe não encontrado em:\n{cfg.LlamaBin}");

        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Modelo não encontrado:\n{modelPath}");

        string? mmproj = cfg.EnableVision ? ModelCatalog.FindMmproj(modelPath, cfg.ModelsDir) : null;

        var args = BuildCommandLine(cfg, modelPath, mmproj);

        LogReceived?.Invoke($"bin: {cfg.LlamaBin}");
        LogReceived?.Invoke($"exe: {serverExe}");
        try
        {
            var verPsi = new ProcessStartInfo
            {
                FileName = serverExe,
                Arguments = "--version",
                WorkingDirectory = cfg.LlamaBin,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            verPsi.Environment["PATH"] = cfg.LlamaBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            using var verProc = Process.Start(verPsi);
            if (verProc is not null)
            {
                var so = verProc.StandardOutput.ReadToEnd();
                var se = verProc.StandardError.ReadToEnd();
                verProc.WaitForExit(5000);
                var line = (so + se).Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(line))
                    LogReceived?.Invoke($"llama.cpp: {line.Trim()}");
            }
        }
        catch (Exception ex)
        {
            LogReceived?.Invoke($"versão: (falha ao ler --version: {ex.Message})");
        }

        LogReceived?.Invoke($"Comando: llama-server.exe {args}");
        if (mmproj is not null)
            LogReceived?.Invoke($"mmproj: {Path.GetFileName(mmproj)}");
        LogReceived?.Invoke("WebUI embutida no llama-server → http://" + cfg.Host + ":" + cfg.Port + "/");

        var psi = new ProcessStartInfo
        {
            FileName = serverExe,
            Arguments = args,
            WorkingDirectory = cfg.LlamaBin,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.Environment["PATH"] = cfg.LlamaBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) LogReceived?.Invoke(e.Data);
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data)) LogReceived?.Invoke(e.Data);
        };
        _process.Exited += (_, _) =>
        {
            LogReceived?.Invoke($"Processo encerrado (exit {_process?.ExitCode}).");
            ProcessExited?.Invoke();
        };

        if (!_process.Start())
            throw new InvalidOperationException("Falha ao iniciar llama-server.");

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        LogReceived?.Invoke($"PID {_process.Id}");
    }

    public void Stop()
    {
        if (_process is null) return;
        try
        {
            if (!_process.HasExited)
            {
                LogReceived?.Invoke("Parando servidor…");
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(8000);
            }
        }
        catch (Exception ex)
        {
            LogReceived?.Invoke($"Erro ao parar: {ex.Message}");
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    public async Task<bool> WaitUntilHealthyAsync(AppConfig cfg, CancellationToken ct)
    {
        var url = $"http://{cfg.Host}:{cfg.Port}/health";
        for (var i = 0; i < 180; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (!IsRunning) return false;
            try
            {
                using var resp = await _http.GetAsync(url, ct);
                if (resp.IsSuccessStatusCode) return true;
            }
            catch
            {
                // still loading
            }
            await Task.Delay(1000, ct);
        }
        return false;
    }

    public async Task<bool> CheckHealthAsync(AppConfig cfg)
    {
        try
        {
            using var resp = await _http.GetAsync($"http://{cfg.Host}:{cfg.Port}/health");
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public string WebUiUrl(AppConfig cfg) => $"http://{cfg.Host}:{cfg.Port}/";

    public void Dispose()
    {
        Stop();
        _http.Dispose();
    }
}
