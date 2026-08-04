using System.Net;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text;
using SkiaSharp;
using Serilog;
using LoliconSetuBot.Models;

namespace LoliconSetuBot.Services;

/// <summary>
/// 插画获取服务：负责调用 lolicon.app API、处理图片、缓存图片等核心功能
/// 修复：自管理 HttpClient、区分可重试/不可重试异常、保留原图格式、缓存清理
/// </summary>
public sealed class LoliconService : IDisposable {
    // HTTP 客户端：改为 private readonly + 服务内部创建（长生命周期）
    private readonly HttpClient _http;
    // 本地图片缓存目录路径
    private readonly string _cacheDir;
    // lolicon.app API 接口地址
    private const string ApiUrl = "https://api.lolicon.app/setu/v2";
    // 最大重试次数
    private const int MaxRetries = 3;
    // JSON 序列化/反序列化选项：驼峰命名 + 允许字符串解析数字
    private static readonly JsonSerializerOptions JsonOpts = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    /// <summary>
    /// 构造函数：服务内部管理 HttpClient 生命周期
    /// 修复：HttpClient 不再由外部传入（避免 using 语义错误），由本类负责创建和释放
    /// </summary>
    public LoliconService() {
        // 跨平台：用 SocketsHttpHandler 替代 ServicePointManager（已在 .NET 10 标记过时，Linux 行为不一致）
        var handler = new SocketsHttpHandler {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            MaxConnectionsPerServer = 50
        };
        _http = new HttpClient(handler, disposeHandler: true) {
            Timeout = TimeSpan.FromSeconds(60),
            DefaultRequestHeaders = {
                UserAgent = { new("LoliconSetuBot", "2.7") }
            }
        };

        _cacheDir = Path.Combine(AppContext.BaseDirectory, "cache");
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>
    /// 获取插画（核心方法）：根据标签和配置从 API 获取插画，支持自动重试（最多 3 次）
    /// 修复：区分可重试/不可重试异常；移除 unreachable 的 TimeoutException catch
    /// </summary>
    public async Task<SetuResult> FetchAsync(string tag, BotConfig config, CancellationToken ct = default) {
        for (int retry = 0; retry <= MaxRetries; retry++) {
            try {
                return await ExecuteFetch(tag, config, ct);
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                // 用户主动取消，不重试，直接抛出
                Log.Information("请求被用户取消（标签: {Tag}）", tag);
                throw;
            } catch (OperationCanceledException) when (retry < MaxRetries) {
                // HTTP 超时或网络问题导致的取消（非用户主动），指数退避重试
                await LogAndDelayAsync(retry, "网络超时/取消，正在重试...");
            } catch (HttpRequestException ex) when (retry < MaxRetries) {
                // 修复：检查 HTTP 状态码，4xx（400/403/404/429）不应重试
                if (ex.StatusCode is >= System.Net.HttpStatusCode.BadRequest and < System.Net.HttpStatusCode.InternalServerError) {
                    Log.Warning("API 返回 HTTP {StatusCode}，不再重试: {Message}", ex.StatusCode, ex.Message);
                    throw;
                }
                await LogAndDelayAsync(retry, $"HTTP 错误 ({ex.StatusCode}): {ex.Message}");
            } catch (InvalidOperationException ex) when (retry < MaxRetries) {
                // 修复：API 返回的无效响应（如 JSON 格式变化）不应重试
                // 但如果是"空标签"导致的 API 报错，可以重试
                if (ex.Message.StartsWith("API error:") || ex.Message.Contains("No images")) {
                    Log.Warning("API 返回无效响应，不再重试: {Message}", ex.Message);
                    throw;
                }
                // 其他InvalidOperationException（如 URL 构建问题）不重试
                Log.Warning("InvalidOperation 异常，不再重试: {Message}", ex.Message);
                throw;
            } catch (JsonException ex) when (retry < MaxRetries) {
                // 修复：JSON 解析失败不应重试——说明 API 返回格式变了
                Log.Warning("JSON 解析失败，API 可能已变更，不再重试: {Message}", ex.Message);
                throw;
            } catch (Exception ex) when (retry < MaxRetries) {
                // 其他未知异常，指数退避重试
                await LogAndDelayAsync(retry, $"未知错误: {ex.Message}");
            }
        }

        // 不应到达这里
        throw new InvalidOperationException("重试次数已达上限。");
    }

    /// <summary>
    /// 执行单次获取逻辑（不含重试）
    /// </summary>
    private async Task<SetuResult> ExecuteFetch(string tag, BotConfig config, CancellationToken ct) {
        var url = BuildUrl(tag, config);
        Log.Debug("请求 API: {Url}", url);

        var json = await _http.GetStringAsync(url, ct);
        var resp = JsonSerializer.Deserialize<LoliconResponse>(json, JsonOpts);

        if (!string.IsNullOrEmpty(resp?.Error))
            throw new InvalidOperationException("API error: " + resp.Error);

        if (resp?.Data == null || resp.Data.Count == 0)
            throw new InvalidOperationException("No images matched.");

        var data = resp.Data[0];
        var imageUrl = GetImageUrl(data, config.Size) ?? data.Urls.Original;
        if (string.IsNullOrEmpty(imageUrl))
            throw new InvalidOperationException("Image URL is empty.");

        Log.Debug("下载图片: {Url}", imageUrl);
        var rawBytes = await _http.GetByteArrayAsync(imageUrl, ct);

        // 原始图片缓存（不翻转，保留原始格式）
        CacheImage(data.Title, data.Pid, rawBytes);

        // 翻转处理（保留原图格式）
        var processed = await ProcessImageAsync(rawBytes, config);

        string info = config.ShowInfo ? FormatInfo(data) : string.Empty;

        return new SetuResult {
            Data = data,
            ImageBytes = processed,
            InfoText = info
        };
    }

    /// <summary>
    /// 记录日志并重试等待
    /// </summary>
    private static async Task LogAndDelayAsync(int retry, string reason) {
        int delay = (int)Math.Pow(2, retry) * 1000;
        Log.Warning("重试 {Retry}/{Max} ({Reason})", retry + 1, MaxRetries, reason);
        await Task.Delay(TimeSpan.FromMilliseconds(delay));
    }

    /// <summary>
    /// 构建 API 请求 URL：手动拼接参数，避免依赖 System.Web（.NET Core 无此程序集）
    /// 修复：更符合 C# 规范，自动处理编码
    /// </summary>
    private static string BuildUrl(string tag, BotConfig config) {
        var parts = new List<string> {
            "r18=" + (config.R18 ? "true" : "false"),
            "size=" + Uri.EscapeDataString(config.Size),
            "proxy=" + Uri.EscapeDataString(config.Proxy),
            "excludeAI=" + (config.ExcludeAI ? "true" : "false"),
            "dsc=false"
        };

        // 支持空格、&、| 分隔的多个标签
        if (!string.IsNullOrEmpty(tag)) {
            var tags = tag.Split(new[] { ' ', '&', '|' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var t in tags) {
                parts.Add("tag=" + Uri.EscapeDataString(t.Trim()));
            }
        }

        return ApiUrl + "?" + string.Join("&", parts);
    }

    /// <summary>
    /// 获取指定尺寸的图片 URL：根据尺寸参数返回对应的 URL，默认为原始尺寸
    /// </summary>
    private static string? GetImageUrl(LoliconData data, string? size) {
        return size?.ToLowerInvariant() switch {
            "regular" => data.Urls.Regular,
            "small" => data.Urls.Small,
            "mini" => data.Urls.Mini,
            "thumb" => data.Urls.Thumb,
            _ => data.Urls.Original
        };
    }

    /// <summary>
    /// 处理图片（翻转）：保留原图格式（PNG→PNG, JPG→JPG）
    /// 修复：不再强制转 JPEG，避免透明通道丢失
    /// </summary>
    private static async Task<byte[]> ProcessImageAsync(byte[] bytes, BotConfig config) {
        if (!config.FlipHorizontal && !config.FlipVertical)
            return bytes;

        try {
            using var inputStream = new MemoryStream(bytes);
            using var original = SKBitmap.Decode(inputStream);
            if (original == null) {
                Log.Warning("SkiaSharp 无法解码图片，返回原图");
                return bytes;
            }

            using var output = new SKBitmap(original.Width, original.Height);
            using var canvas = new SKCanvas(output);

            var matrix = SKMatrix.CreateIdentity();
            float cx = original.Width / 2f;
            float cy = original.Height / 2f;

            if (config.FlipHorizontal)
                matrix = SKMatrix.Concat(matrix, SKMatrix.CreateScale(-1, 1, cx, cy));
            if (config.FlipVertical)
                matrix = SKMatrix.Concat(matrix, SKMatrix.CreateScale(1, -1, cx, cy));

            canvas.SetMatrix(matrix);
            canvas.DrawBitmap(original, new SKPoint(0, 0), SKSamplingOptions.Default);
            canvas.Flush();

            using var image = SKImage.FromBitmap(output);

            // 修复：根据原始数据推断格式，保持原格式输出
            // PNG 文件以 89 50 4E 47 开头，JPG 以 FF D8 FF 开头
            var format = DetectImageFormat(bytes);
            // 修复：SKImage.Encode 总是需要 quality 参数（没有无参重载）
            var quality = format == SKEncodedImageFormat.Jpeg ? 90 : 100;
            using var encoded = image.Encode(format, quality);

            using var outStream = new MemoryStream();
            encoded.SaveTo(outStream);
            return outStream.ToArray();
        } catch (Exception ex) {
            Log.Warning(ex, "SkiaSharp 图片处理失败，使用原图: {Message}", ex.Message);
            return bytes;
        }
    }

    /// <summary>
    /// 检测原始图片格式（PNG vs JPEG vs 其他）
    /// 修复：根据魔数判断，而非依赖文件名
    /// </summary>
    private static SKEncodedImageFormat DetectImageFormat(byte[] bytes) {
        if (bytes.Length >= 3) {
            // JPEG 魔数: FF D8 FF
            if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) {
                return SKEncodedImageFormat.Jpeg;
            }
        }
        if (bytes.Length >= 8) {
            // PNG 魔数: 89 50 4E 47 0D 0A 1A 0A
            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
                && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A) {
                return SKEncodedImageFormat.Png;
            }
        }
        // 默认 JPEG（兼容旧行为）
        return SKEncodedImageFormat.Jpeg;
    }

    /// <summary>
    /// 缓存图片到本地：保留原始格式（不强制 .jpg 后缀）
    /// 修复：文件扩展名与实际格式匹配
    /// </summary>
    private void CacheImage(string title, long pid, byte[] bytes) {
        var safeName = string.Join("_", title.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        var ext = GetImageExtension(bytes);
        var path = Path.Combine(_cacheDir, $"{safeName}_{pid}{ext}");
        File.WriteAllBytes(path, bytes);
    }

    private static string GetImageExtension(byte[] bytes) {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) {
            return ".jpg";
        }
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) {
            return ".png";
        }
        return ".jpg";
    }

    /// <summary>
    /// 格式化图片信息文本
    /// </summary>
    private static string FormatInfo(LoliconData d) {
        return $"Title: {d.Title}\nAuthor: {d.Author}\nPID: {d.Pid}";
    }

    /// <summary>
    /// 清理缓存：保留最近 keep 个文件，删除最老的
    /// 修复：不再全部清空，只删除超出保留数量的旧文件
    /// </summary>
    public void CleanCache(int keep = 50) {
        if (!Directory.Exists(_cacheDir)) return;

        var files = Directory.GetFiles(_cacheDir)
            .Select(f => new { Path = f, Info = new FileInfo(f) })
            .OrderBy(x => x.Info.CreationTime)
            .ToList();

        int toDelete = files.Count - keep;
        if (toDelete <= 0) return;

        Log.Information("清理 {Count} 个旧缓存文件（保留最近 {Keep} 个）", toDelete, keep);
        for (int i = 0; i < toDelete; i++) {
            try {
                File.Delete(files[i].Path);
            } catch (IOException ex) {
                Log.Warning(ex, "删除缓存文件失败: {Path}", files[i].Path);
            }
        }
    }

    /// <summary>
    /// 释放资源
    /// 修复：内部创建的 HttpClient 需要在此释放
    /// </summary>
    public void Dispose() {
        _http?.Dispose();
    }
}
