using AutoSrt.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using SrtAgent;
using System.Text;

namespace AutoSrt;

public partial class MainPage : ContentPage
{
    private readonly UiLogger _logger = new();
    private string? _selectedVideoPath;

    public MainPage()
    {
        InitializeComponent();
        BindingContext = _logger;
    }

    private async void OnPickVideoClicked(object? sender, EventArgs e)
    {
        try
        {
            var results = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择视频文件",
                FileTypes = FilePickerFileType.Videos
            });

            if (results is null)
            {
                _logger.Info("未选择文件。");
                return;
            }

            _selectedVideoPath = results.FullPath;
            SelectedVideoLabel.Text = _selectedVideoPath;
            _logger.Info($"已选择: {_selectedVideoPath}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "选择文件失败。");
            await DisplayAlert("错误", ex.Message, "OK");
        }
    }

    private async void OnRunClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedVideoPath))
        {
            await DisplayAlert("提示", "请先选择视频文件。", "OK");
            return;
        }

        var endpoint = EndpointEntry.Text?.Trim();
        var apiKey = ApiKeyEntry.Text?.Trim();
        var model = ModelEntry.Text?.Trim();

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(model))
        {
            await DisplayAlert("提示", "请填写 VLLM 的 Endpoint / API Key / Model。", "OK");
            return;
        }

        RunButton.IsEnabled = false;
        _logger.Clear();

        try
        {
            _logger.Info("初始化 VLLM ChatClient...");
            var chatClient = new VllmQwen3NextChatClient(endpoint, apiKey, model);

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

            _logger.Info("让 LLM 判断最佳字幕轨道/是否已包含中文字幕...");
            var decision = await exportor.ChooseSubtitleTrackAsync(chatClient, streams, targetLanguage: "中文");

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
                _logger.Info("检测到已存在中文字幕轨道，直接输出（不翻译）。");
                outputSrt = srtText;
                outputPath = GetOutputSrtPath(_selectedVideoPath);
            }
            else
            {
                _logger.Info("开始翻译为中文...");
                var translator = new SrtTranslator(chatClient, NullLogger.Instance);

                var options = new SrtTranslator.TranslationOptions(
                    TargetLanguage: "中文",
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
                        // Ensure UI-safe updates.
                        MainThread.BeginInvokeOnMainThread(() =>
                            _logger.Info($"翻译进度: {percent}%{(string.IsNullOrWhiteSpace(stage) ? string.Empty : $" ({stage})")}"));
                    });

                _logger.Info($"翻译完成，长度: {outputSrt.Length} chars");

                outputPath = GetOutputSrtPath(_selectedVideoPath);
            }

            await File.WriteAllTextAsync(outputPath, outputSrt, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            _logger.Info($"已输出字幕: {outputPath}");
            await DisplayAlert("完成", $"已生成中文 SRT:\n{outputPath}", "OK");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "处理失败。");
            await DisplayAlert("错误", ex.Message, "OK");
        }
        finally
        {
            RunButton.IsEnabled = true;
        }
    }

    private static string GetOutputSrtPath(string videoPath)
    {
        var dir = Path.GetDirectoryName(videoPath) ?? Environment.CurrentDirectory;
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(videoPath);
        return Path.Combine(dir, $"{fileNameWithoutExt}.zh.srt");
    }
}
