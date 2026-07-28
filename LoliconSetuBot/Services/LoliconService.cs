using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text;
using SkiaSharp;
using LoliconSetuBot.Models;

namespace LoliconSetuBot.Services;

/// <summary>
/// 插画获取服务：负责调用 lolicon.app API、处理图片、缓存图片等核心功能
/// </summary>
public sealed class LoliconService : IDisposable {
    // HTTP 客户端，用于发起网络请求
    private readonly HttpClient _http;
    // 本地图片缓存目录路径
    private readonly string _cacheDir;
    // lolicon.app API 接口地址
    private const string ApiUrl = "https://api.lolicon.app/setu/v2";
    // JSON 序列化/反序列化选项：使用驼峰命名 + 允许字符串解析数字
    private static readonly JsonSerializerOptions JsonOpts = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    /// <summary>
    /// 构造函数：初始化服务，设置 HTTP 超时、User-Agent，并创建缓存目录
    /// </summary>
    /// <param name="http"></param>
    public LoliconService(HttpClient http) {
        _http = http;
        // 设置 HTTP 请求超时时间为 45 秒
        _http.Timeout = TimeSpan.FromSeconds(45);
        // 设置 User-Agent 标识
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("LoliconSetuBot/2.6");
        // 获取程序运行目录下的 cache 文件夹路径
        _cacheDir = Path.Combine(AppContext.BaseDirectory, "cache");
        // 如果 cache 目录不存在则创建
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>
    /// 获取插画（核心方法）：根据标签和配置从 API 获取插画，支持自动重试（最多 3 次）
    /// </summary>
    /// <param name="tag"></param>
    /// <param name="config"></param>
    /// <param name="ct"></param>
    /// <param name="retry"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public async Task<SetuResult> FetchAsync(string tag, BotConfig config, CancellationToken ct = default, int retry = 0) {
        const int maxRetries = 3;
        try {
            // 构建带参数的 API 请求 URL
            var urlBuilder = BuildUrl(tag, config);
            // 发起 GET 请求获取 JSON 响应
            var json = await _http.GetStringAsync(urlBuilder, ct);
            // 将 JSON 反序列化为 LoliconResponse 对象
            var resp = JsonSerializer.Deserialize<LoliconResponse>(json, JsonOpts);

            // 如果 API 返回了错误信息，则抛出异常
            if (!string.IsNullOrEmpty(resp?.Error))
                throw new InvalidOperationException("API error: " + resp.Error);
            // 如果没有返回任何图片数据，则抛出异常
            if (resp?.Data == null || resp.Data.Count == 0)
                throw new InvalidOperationException("No images matched.");

            // 取返回结果的第一张图片数据
            var data = resp.Data[0];
            // 根据配置中的尺寸参数获取对应的图片 URL
            var imageUrl = GetImageUrl(data, config.Size) ?? data.Urls.Original;
            // 如果图片 URL 为空，则抛出异常
            if (string.IsNullOrEmpty(imageUrl))
                throw new InvalidOperationException("Image URL is empty.");

            // 下载图片的原始字节数据
            var rawBytes = await _http.GetByteArrayAsync(imageUrl, ct);
            // 对图片进行处理（翻转等）
            var processed = await ProcessImageAsync(rawBytes, config);
            // 将处理后的图片保存到本地缓存
            CacheImage(data.Title, processed);

            // 根据配置决定是否生成图片信息文本
            string info = config.ShowInfo ? FormatInfo(data) : string.Empty;

            // 返回封装好的结果对象
            return new SetuResult {
                Data = data,
                ImageBytes = processed,
                InfoText = info
            };
        } catch (OperationCanceledException) when (retry < maxRetries) {
            // 取消异常但重试次数未超限：指数退避重试
            int delay = (int)Math.Pow(2, retry) * 1000;
            Console.WriteLine("Retry " + (retry + 1) + "/" + maxRetries + ", waiting " + delay + "ms... (cancellation)");
            await Task.Delay(TimeSpan.FromMilliseconds(delay), ct);
            return await FetchAsync(tag, config, ct, retry + 1);
        } catch (Exception ex) when (retry < maxRetries) {
            // 其他异常但重试次数未超限：指数退避重试
            int delay = (int)Math.Pow(2, retry) * 1000;
            Console.WriteLine("Retry " + (retry + 1) + "/" + maxRetries + ", waiting " + delay + "ms... (" + ex.Message + ")");
            await Task.Delay(TimeSpan.FromMilliseconds(delay), ct);
            return await FetchAsync(tag, config, ct, retry + 1);
        }
    }

    /// <summary>
    /// 构建 API 请求 URL：根据标签和配置拼接完整的 API 请求参数
    /// </summary>
    /// <param name="tag"></param>
    /// <param name="config"></param>
    /// <returns></returns>
    private string BuildUrl(string tag, BotConfig config) {
        // 基础参数：r18（0=排除、1=仅成人、2=包含成人）
        var q = new StringBuilder("?r18=" + (config.R18 ? 2 : 0));
        // 图片尺寸参数
        q.Append("&size=" + Uri.EscapeDataString(config.Size));
        // 图片代理参数
        q.Append("&proxy=" + Uri.EscapeDataString(config.Proxy));
        // 是否排除 AI 生成图片
        q.Append("&excludeAI=" + (config.ExcludeAI ? "true" : "false"));

        // 如果提供了标签参数，则逐个添加 tag 参数
        if (!string.IsNullOrEmpty(tag)) {
            // 支持空格、&、| 分隔的多个标签
            var separators = new[] { ' ', '&', '|' };
            var tags = tag.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            foreach (var t in tags)
                q.Append("&tag=" + Uri.EscapeDataString(t.Trim()));
        }
        return ApiUrl + q;
    }

    /// <summary>
    /// 获取指定尺寸的图片 URL：根据尺寸参数返回对应的 URL，默认为原始尺寸
    /// </summary>
    /// <param name="data"></param>
    /// <param name="size"></param>
    /// <returns></returns>
    private string? GetImageUrl(LoliconData data, string? size) {
        return size?.ToLowerInvariant() switch {
            "regular" => data.Urls.Regular,
            "small" => data.Urls.Small,
            _ => data.Urls.Original
        };
    }

    /// <summary>
    /// 处理图片（翻转）：根据配置对图片进行水平/垂直翻转处理
    /// </summary>
    /// <param name="bytes"></param>
    /// <param name="config"></param>
    /// <returns></returns>
    private static async Task<byte[]> ProcessImageAsync(byte[] bytes, BotConfig config) {
        // 如果不需要翻转，直接返回原始数据
        if (!config.FlipHorizontal && !config.FlipVertical)
            return bytes;

        // 将字节数组转为内存流
        using var inputStream = new MemoryStream(bytes);
        // 使用 SkiaSharp 解码图片
        using var original = SKBitmap.Decode(inputStream);
        if (original == null) return bytes;

        // 创建输出画布
        using var output = new SKBitmap(original.Width, original.Height);
        using var canvas = new SKCanvas(output);

        // 创建单位矩阵，并计算翻转中心点
        var matrix = SKMatrix.CreateIdentity();
        float cx = original.Width / 2f;
        float cy = original.Height / 2f;

        // 如果启用水平翻转，则应用水平翻转矩阵
        if (config.FlipHorizontal)
            matrix = SKMatrix.Concat(matrix, SKMatrix.CreateScale(-1, 1, cx, cy));
        // 如果启用垂直翻转，则应用垂直翻转矩阵
        if (config.FlipVertical)
            matrix = SKMatrix.Concat(matrix, SKMatrix.CreateScale(1, -1, cx, cy));

        // 设置画布变换矩阵并绘制翻转后的图片
        canvas.SetMatrix(matrix);
        canvas.DrawBitmap(original, new SKPoint(0, 0), SKSamplingOptions.Default);
        canvas.Flush();

        // 将处理后的图片编码为 JPEG（质量 90）
        using var image = SKImage.FromBitmap(output);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        using var outStream = new MemoryStream();
        encoded.SaveTo(outStream);
        return outStream.ToArray();
    }

    /// <summary>
    /// 缓存图片到本地：将图片保存到 cache 目录，文件名包含标题和时间戳
    /// </summary>
    /// <param name="title"></param>
    /// <param name="bytes"></param>
    private void CacheImage(string title, byte[] bytes) {
        // 移除文件名中的非法字符
        var safeName = string.Join("_", title.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        // 拼接缓存文件路径（标题_时间戳.jpg）
        var path = Path.Combine(_cacheDir, safeName + "_" + DateTimeOffset.Now.ToUnixTimeMilliseconds() + ".jpg");
        // 写入文件
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>
    /// 格式化图片信息文本：将插画数据格式化为可读的信息字符串（标题、作者、PID）
    /// </summary>
    /// <param name="d"></param>
    /// <returns></returns>
    private static string FormatInfo(LoliconData d) {
        var sb = new StringBuilder();
        sb.AppendLine("Title: " + d.Title);
        sb.AppendLine("Author: " + d.Author);
        sb.Append("PID: " + d.Pid);
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 清理缓存：删除 cache 目录中的所有缓存图片文件
    /// </summary>
    public void CleanCache() {
        if (!Directory.Exists(_cacheDir)) return;
        foreach (var f in Directory.GetFiles(_cacheDir))
            try { File.Delete(f); } catch { }
    }

    /// <summary>
    /// 资源释放：_http 由调用方管理，此处不释放
    /// </summary>
    public void Dispose() {
        // _http is owned by the caller, do not dispose
    }
}
