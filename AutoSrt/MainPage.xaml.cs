using AutoSrt.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using SrtAgent;
using System.Text;
using System.Text.Json;

namespace AutoSrt;

public partial class MainPage : ContentPage
{
    private readonly UiLogger _logger = new();
    private string? _selectedVideoPath;

    public MainPage()
    {
        InitializeComponent();
        BindingContext = _logger;
        _logger.LogAdded += OnLogAdded;
        LoadHtml();
    }

    private async void LoadHtml()
    {
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync("index.html");
            using var reader = new StreamReader(stream);
            var html = await reader.ReadToEndAsync();
            MainWebView.Source = new HtmlWebViewSource { Html = html };
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Failed to load UI: {ex.Message}", "OK");
        }
    }

    private void OnLogAdded(string message)
    {
        // Simple JS escaping
        var jsMessage = message.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "");
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await MainWebView.EvaluateJavaScriptAsync($"appendLog('{jsMessage}')");
            }
            catch { /* Ignore JS errors during navigation/loading */ }
        });
    }

    private async void OnWebViewNavigating(object sender, WebNavigatingEventArgs e)
    {
        if (e.Url.StartsWith("autosrt://"))
        {
            e.Cancel = true;
            var uri = new Uri(e.Url);
            var host = uri.Host; // action name

            if (host.ToLower() == "pickvideo")
            {
                // Defer to next UI cycle to ensure we're fully out of WebView navigation context
                Dispatcher.Dispatch(async () =>
                {
                    await Task.Delay(50); // Small delay to ensure WebView navigation is fully settled
                    await PickVideoAsync();
                });
            }
            else if (host == "run")
            {
                // autosrt://run?payload=...
                // Manual parsing to avoid System.Web dependency
                var queryIndex = e.Url.IndexOf("?payload=");
                if (queryIndex > 0)
                {
                    var payloadEncoded = e.Url.Substring(queryIndex + 9);
                    var payloadJson = Uri.UnescapeDataString(payloadEncoded);
                    if (!string.IsNullOrEmpty(payloadJson))
                    {
                        Dispatcher.Dispatch(async () =>
                        {
                            await Task.Delay(50);
                            await RunProcessAsync(payloadJson);
                        });
                    }
                }
            }
        }
    }

    private async Task PickVideoAsync()
    {
        try
        {
            _logger.Info("准备打开文件选择器...");

            // Force using WinUI FileOpenPicker on Windows (FilePicker.Default often fails in WebView context)
            var fullPath = await MainThread.InvokeOnMainThreadAsync(async () =>
            {
#if WINDOWS
                try
                {
                    _logger.Info("使用 WinUI FileOpenPicker...");
                    
                    var picker = new Windows.Storage.Pickers.FileOpenPicker
                    {
                        ViewMode = Windows.Storage.Pickers.PickerViewMode.List,
                        SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.VideosLibrary
                    };
                    
                    picker.FileTypeFilter.Add(".mp4");
                    picker.FileTypeFilter.Add(".mkv");
                    picker.FileTypeFilter.Add(".avi");
                    picker.FileTypeFilter.Add(".mov");
                    picker.FileTypeFilter.Add(".wmv");
                    picker.FileTypeFilter.Add(".flv");
                    picker.FileTypeFilter.Add(".m4v");
                    picker.FileTypeFilter.Add(".webm");

                    // Get HWND for window initialization
                    var window = Microsoft.Maui.Controls.Application.Current?.Windows?.FirstOrDefault();
                    if (window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
                    {
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
                        _logger.Info($"初始化 FileOpenPicker HWND: {hwnd}");
                        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                    }
                    else
                    {
                        _logger.Warn("无法获取窗口句柄，FileOpenPicker 可能无法正常显示");
                    }

                    _logger.Info("调用 PickSingleFileAsync...");
                    var file = await picker.PickSingleFileAsync();
                    
                    if (file != null)
                    {
                        _logger.Info($"FileOpenPicker 返回: {file.Path}");
                        return file.Path;
                    }
                    else
                    {
                        _logger.Info("FileOpenPicker 返回 null (用户取消)");
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "WinUI FileOpenPicker 失败");
                    return null;
                }
#else
                var results = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "选择视频文件",
                    FileTypes = FilePickerFileType.Videos
                });

                return results?.FullPath;
#endif
            });

            if (string.IsNullOrWhiteSpace(fullPath))
            {
                _logger.Info("未选择文件。");
                return;
            }

            _selectedVideoPath = fullPath;

            // Update UI
            var jsPath = _selectedVideoPath.Replace("\\", "\\\\").Replace("'", "\\'");
            await MainWebView.EvaluateJavaScriptAsync($"setVideoPath('{jsPath}')");

            _logger.Info($"已选择: {_selectedVideoPath}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "选择文件失败。");
            await DisplayAlert("错误", ex.Message, "OK");
        }
    }

    private async Task RunProcessAsync(string payloadJson)
    {
        string endpoint = "", apiKey = "", model = "", targetLanguage = "简体中文";
        try
        {
            var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("endpoint", out var epProp)) endpoint = epProp.GetString()?.Trim() ?? "";
            if (doc.RootElement.TryGetProperty("apiKey", out var akProp)) apiKey = akProp.GetString()?.Trim() ?? "";
            if (doc.RootElement.TryGetProperty("model", out var mdProp)) model = mdProp.GetString()?.Trim() ?? "";
            if (doc.RootElement.TryGetProperty("targetLanguage", out var tlProp)) targetLanguage = tlProp.GetString()?.Trim() ?? "简体中文";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "无法解析配置参数。");
            await DisplayAlert("错误", ex.ToString(), "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedVideoPath))
        {
            await DisplayAlert("提示", "请先选择视频文件。", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(model))
        {
            await DisplayAlert("提示", "请填写 VLLM 的 Endpoint / API Key / Model。", "OK");
            return;
        }

        await SetUiProcessing(true);
        _logger.Clear();

        try
        {
            _logger.Info("初始化 VLLM ChatClient...");
            var chatClient = VllmChatClientFactory.Create(endpoint, apiKey, model);

            var exportor = new SrtExportor();

            _logger.Info("读取字幕轨道列表...");
            var streams = await exportor.GetSubtitleStreamsAsync(_selectedVideoPath);
            if (streams.Count == 0)
            {
                await DisplayAlert("提示", "该视频未发现内嵌字幕轨道。", "OK");
                return;
            }

            foreach (var s in streams)
            {
                _logger.Info($"Subtitle stream index={s.Index}, lang={s.Language}, codec={s.Codec}, title={s.Title}");
            }

            _logger.Info($"让 LLM 判断最佳字幕轨道/是否已包含{targetLanguage}字幕...");
            var decision = await exportor.ChooseSubtitleTrackAsync(chatClient, streams, targetLanguage: targetLanguage);

            _logger.Info($"LLM decision: targetExists={decision.TargetLanguageAlreadyExists}, action={decision.Action}, index={decision.SelectedIndex}, reason={decision.Reason}");

            _logger.Info("开始提取内嵌字幕...");
            var srtText = await exportor.ExportEmbeddedSubtitlesAsSrtTextAsync(
                _selectedVideoPath,
                new SrtExportor.ExportOptions(SubtitleStreamIndex: decision.SelectedIndex));

            _logger.Info($"提取完成，长度: {srtText.Length} chars");

            string outputSrt;
            string outputPath;

            if (decision.Action == "use_existing_target")
            {
                _logger.Info($"检测到已存在{targetLanguage}字幕轨道，直接输出（不翻译）。");
                outputSrt = srtText;
                outputPath = GetOutputSrtPath(_selectedVideoPath, targetLanguage);
            }
            else
            {
                _logger.Info($"开始翻译为{targetLanguage}...");
                var translator = new SrtTranslator(chatClient, NullLogger.Instance);

                var options = new SrtTranslator.TranslationOptions(
                    TargetLanguage: targetLanguage,
                    BatchSize: 10,
                    ContextSize: 3,
                    Temperature: 0.2f,
                    MaxOutputTokens: 2000,
                    MaxRetries: 3,
                    RetryBaseDelayMs: 1000,
                    BatchDelayMs: 0);

                outputSrt = await translator.TranslateSrtAsync(
                    srtText,
                    options,
                    progress: (percent, stage) =>
                    {
                        _logger.Info($"翻译进度: {percent}%{(string.IsNullOrWhiteSpace(stage) ? string.Empty : $" ({stage})")}");
                    });

                _logger.Info($"翻译完成，长度: {outputSrt.Length} chars");

                outputPath = GetOutputSrtPath(_selectedVideoPath, targetLanguage);
            }

            await File.WriteAllTextAsync(outputPath, outputSrt, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            _logger.Info($"已输出字幕: {outputPath}");
            await DisplayAlert("完成", $"已生成 {targetLanguage} SRT:\n{outputPath}", "OK");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "处理失败。");
            await DisplayAlert("错误", ex.ToString(), "OK");
        }
        finally
        {
            await SetUiProcessing(false);
        }
    }

    private async Task SetUiProcessing(bool processing)
    {
        await MainWebView.EvaluateJavaScriptAsync($"setProcessing({(processing ? "true" : "false")})");
    }

    private static string GetOutputSrtPath(string videoPath)
    {
        return GetOutputSrtPath(videoPath, "简体中文");
    }

    private static string GetOutputSrtPath(string videoPath, string targetLanguage)
    {
        var dir = Path.GetDirectoryName(videoPath) ?? Environment.CurrentDirectory;
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(videoPath);

        var suffix = targetLanguage switch
        {
            "简体中文" => "zh",
            "繁体中文" => "zh-Hant",
            "日文" => "ja",
            "韩文" => "ko",
            _ => "out"
        };

        return Path.Combine(dir, $"{fileNameWithoutExt}.{suffix}.srt");
    }
}
