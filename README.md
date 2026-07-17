# BonsaiWinUI — Launcher WinUI 3

App **WinUI 3** para:

- escolher modelo `.gguf`
- baixar modelos PrismML do HuggingFace
- iniciar `llama-server` (Vulkan) com **Built-in Tools**
- abrir a WebUI no browser

## Requisitos

- Windows 10/11
- .NET 8+ / 10 SDK
- Windows App SDK (restaurado via NuGet)
- `llama.cpp` compilado em `..\llama.cpp\build\bin`
- Python 3 (só para download HF)

## Build

```powershell
cd C:\Users\geron\OneDrive\Desktop\AI\BonsaiWinUI
dotnet build -c Release -p:Platform=x64
```

Executável (unpackaged):

```
bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\BonsaiWinUI.exe
```

Ou:

```powershell
dotnet run -c Release -p:Platform=x64
```

## Uso

1. Confirme pastas **llama-server** e **models**
2. Selecione **Bonsai-27B-Q1_0.gguf**
3. Deixe **Built-in Tools** ligado
4. **Iniciar llama + WebUI** → http://127.0.0.1:8080/

## Estrutura

```
BonsaiWinUI/
  MainWindow.xaml(.cs)     # UI
  Services/
    AppConfig.cs           # config JSON em %LocalAppData%\BonsaiWinUI
    ModelCatalog.cs        # modelos locais + catálogo HF
    LlamaServerService.cs  # start/stop + health
```
