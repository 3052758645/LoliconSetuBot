using System.Net;
using System.Text;
using Serilog;
using LoliconSetuBot.Models;
using LoliconSetuBot.Services;

namespace LoliconSetuBot;

static class Program {
    private static readonly CancellationTokenSource _cts = new();

    static async Task Main(string[] args) {
        // 测试模式：dotnet run -- --test [tag]
        // 例: dotnet run -- --test         请求随机图（空标签）
        //     dotnet run -- --test 校园      请求指定标签
        if (args.Length >= 1 && args[0] is "--test" or "-t") {
            string tag = args.Length > 1 ? args[1] : "";
            await RunTestAsync(tag);
            return;
        }

        // 初始化日志：控制台 + 文件（带 7 天轮转）
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: "logs/log-.txt",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try {
            Console.OutputEncoding = Encoding.UTF8;
            // 跨平台：Console.Title 在 Linux/macOS terminal 中无效但不会报错，安全
            Console.Title = "Lolicon Bot";
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("Lolicon Bot v1.2 (Cross-Platform Fix)");
            Console.ResetColor();
            Console.WriteLine("命令:");
            Console.WriteLine("  来张[标签]涩图   - 获取单张（标签可选）");
            Console.WriteLine("  无限涩图 / 循环  - 进入无限循环模式（Ctrl+C 停止）");
            Console.WriteLine("  exit             - 退出程序");
            Console.WriteLine();

            Console.CancelKeyPress += (s, e) => {
                e.Cancel = true;
                _cts.Cancel();
                Log.Information("[STOP] 正在停止...");
            };

            // 配置
            var config = BotConfig.Load("config.json");
            Log.Information("配置已加载: r18={R18}, size={Size}, proxy={Proxy}", config.R18, config.Size, config.Proxy);

            // 创建插画服务实例（内部自管理 HttpClient 生命周期）
            using var service = new LoliconService();

            // 清理旧缓存（保留最近 50 张，不再全删）
            service.CleanCache(keep: 50);

            string groupId = args.Length > 0 ? args[0] : "default";
            var cooldowns = new Dictionary<string, DateTimeOffset>();

            // 主循环
            while (!_cts.Token.IsCancellationRequested) {
                Console.Write("> ");
                var input = (await Console.In.ReadLineAsync(_cts.Token))?.Trim();
                if (string.IsNullOrEmpty(input)) continue;

                if (input is "exit" or "quit") {
                    break;
                } else if (input is "无限涩图" or "循环") {
                    await RunInfiniteMode(config, cooldowns, groupId, service);
                } else if (TryParseTag(input, out var tag)) {
                    await RunSingleFetch(tag!, config, cooldowns, groupId, service);
                } else {
                    Log.Warning("未知指令: {Input}", input);
                    Console.WriteLine("[ERROR] 未知指令，请输入: 来张[标签]涩图 / 无限涩图 / exit");
                }
            }

            Log.Information("再见！");
        } catch (OperationCanceledException) {
            Log.Information("程序已取消。");
        } catch (Exception ex) {
            Log.Fatal(ex, "程序异常终止");
        } finally {
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// 命令解析：识别 "来张X涩图" 格式，提取标签
    /// 修复：用 Range 语法代替 Substring，自动处理空标签和边界情况
    /// </summary>
    private static bool TryParseTag(string input, out string? tag) {
        if (input.Length >= 4 && input.StartsWith("来张", StringComparison.Ordinal) && input.EndsWith("涩图", StringComparison.Ordinal)) {
            // "来张涩图" -> [2..^2] 是空范围，返回空字符串
            // "来张校园涩图" -> [2..^2] 是 "校园"
            tag = input[2..^2].Trim();
            return true;
        }
        tag = null;
        return false;
    }

    /// <summary>
    /// 无限循环模式：持续获取并打印图片信息，直到用户取消
    /// 两阶段：先请求元数据 → 展示信息 → 下载图片
    /// </summary>
    private static async Task RunInfiniteMode(BotConfig config, Dictionary<string, DateTimeOffset> cooldowns, string groupId, LoliconService service) {
        Log.Information("进入无限涩图模式，按 Ctrl+C 停止...");
        Console.WriteLine("[INFO] 进入无限涩图模式，按 Ctrl+C 停止...");
        int consecutiveErrors = 0;

        while (!_cts.Token.IsCancellationRequested) {
            try {
                await ApplyCooldown(cooldowns, groupId, config);

                // 阶段1：请求元数据
                Console.WriteLine("[API] 正在请求...");
                var result = await service.ResolveAsync("", config, _cts.Token);

                // 阶段2：展示信息
                if (!string.IsNullOrEmpty(result.InfoText)) {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine(result.InfoText);
                    Console.ResetColor();
                }

                // 阶段3：下载图片
                Console.WriteLine("[IMAGE] 正在下载...");
                var bytes = await service.DownloadImageAsync(result.Data, config, _cts.Token);
                Console.WriteLine("[IMAGE] 大小: {0} KB", bytes.Length / 1024);

                cooldowns[groupId] = DateTimeOffset.Now;
                consecutiveErrors = 0;

                if (config.AutoRevoke && config.RevokeDelay > 0) {
                    Log.Information("[REVOKE] {0}ms 后撤回...", config.RevokeDelay);
                    Console.WriteLine("[REVOKE] {0}ms 后撤回...", config.RevokeDelay);
                    await Task.Delay(config.RevokeDelay, _cts.Token);
                    Log.Information("[REVOKE] 撤回指令已执行（模拟）");
                    Console.WriteLine("[REVOKE] 撤回指令已执行（模拟）");
                }
            } catch (OperationCanceledException) {
                Log.Information("无限模式已取消。");
                Console.WriteLine("[INFO] 无限循环已停止。");
                break;
            } catch (Exception ex) {
                consecutiveErrors++;
                Log.Error(ex, "错误 (第 {Errors} 次)", consecutiveErrors);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[ERROR] 错误 ({0}): {1}", consecutiveErrors, ex.Message);
                Console.ResetColor();

                if (consecutiveErrors > 5) {
                    Log.Warning("连续错误过多，暂停 10 秒...");
                    Console.WriteLine("[WARN] 连续错误过多，暂停 10 秒...");
                    await Task.Delay(TimeSpan.FromSeconds(10), _cts.Token);
                    consecutiveErrors = 0;
                }
            }

            if (!_cts.Token.IsCancellationRequested) {
                try {
                    await Task.Delay(1500, _cts.Token);
                } catch (OperationCanceledException) {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 单张获取模式：解析输入指令获取指定标签的图片，仅执行一次
    /// 两阶段：先请求元数据 → 展示信息 → 下载图片
    /// </summary>
    private static async Task RunSingleFetch(string tag, BotConfig config, Dictionary<string, DateTimeOffset> cooldowns, string groupId, LoliconService service) {
        Log.Information("获取图片，群组={Group}, 标签={Tag}", groupId, tag);

        try {
            await ApplyCooldown(cooldowns, groupId, config);

            // 阶段1：请求元数据
            Console.WriteLine("[API] 正在请求...");
            var result = await service.ResolveAsync(tag, config, _cts.Token);

            // 阶段2：展示信息
            if (!string.IsNullOrEmpty(result.InfoText)) {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine(result.InfoText);
                Console.ResetColor();
            }

            // 阶段3：下载图片
            Console.WriteLine("[IMAGE] 正在下载...");
            var bytes = await service.DownloadImageAsync(result.Data, config, _cts.Token);
            Console.WriteLine("[IMAGE] 大小: {0} KB", bytes.Length / 1024);
            Console.WriteLine();
            cooldowns[groupId] = DateTimeOffset.Now;

            if (config.AutoRevoke && config.RevokeDelay > 0) {
                Log.Information("[REVOKE] {0}ms 后撤回...", config.RevokeDelay);
                Console.WriteLine("[REVOKE] " + config.RevokeDelay + "ms 后撤回...");
                await Task.Delay(config.RevokeDelay, _cts.Token);
                Log.Information("[REVOKE] 撤回指令已执行（模拟）");
                Console.WriteLine("[REVOKE] 撤回指令已执行（模拟）");
            }
        } catch (OperationCanceledException) {
            Log.Information("请求已取消。");
        } catch (Exception ex) {
            Log.Error(ex, "获取图片失败");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[ERROR] " + ex.Message);
            Console.ResetColor();
        }
    }

    /// <summary>
    /// 应用冷却时间：检查并等待冷却时间，防止请求过于频繁
    /// </summary>
    private static async Task ApplyCooldown(Dictionary<string, DateTimeOffset> cooldowns, string groupId, BotConfig config) {
        if (config.CoolDown <= 0) return;
        if (cooldowns.TryGetValue(groupId, out var last)) {
            int remain = config.CoolDown - (int)(DateTimeOffset.Now - last).TotalSeconds;
            if (remain > 0) {
                Log.Information("[COOLDOWN] {Group} 冷却中，还需 {Remain}s", groupId, remain);
                Console.WriteLine("[COOLDOWN] 冷却中，还需 " + remain + " 秒");
                try {
                    await Task.Delay(TimeSpan.FromSeconds(remain), _cts.Token);
                } catch (OperationCanceledException) {
                    // 取消时退出等待
                }
            }
        }
    }

    /// <summary>
    /// 测试模式：执行一次 API 请求，验证输出和缓存
    /// 用法: dotnet run -- --test [tag]
    /// </summary>
    private static async Task RunTestAsync(string tag) {
        // 初始化日志（仅控制台，避免文件写入影响）
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("=== 功能测试模式 ===");
        Console.WriteLine($"标签: '{tag}'");
        Console.WriteLine();

        try {
            // 加载配置
            var config = BotConfig.Load("config.json");
            Log.Information("配置已加载: r18={R18}, size={Size}, proxy={Proxy}", config.R18, config.Size, config.Proxy);

            // 清理旧缓存
            using var service = new LoliconService();
            service.CleanCache(keep: 50);

            var cacheDir = Path.Combine(AppContext.BaseDirectory, "cache");
            Console.WriteLine($"缓存目录: {cacheDir}");
            Console.WriteLine($"缓存目录内容: {(Directory.Exists(cacheDir) ? Directory.GetFiles(cacheDir).Length.ToString() + " 个文件" : "不存在")}");
            Console.WriteLine();

            // 阶段1：请求元数据
            Console.WriteLine("[API] 正在请求...");
            var resolve = await service.ResolveAsync(tag, config, CancellationToken.None);

            // 阶段2：展示信息
            Console.WriteLine();
            Console.WriteLine("=== API 响应 ===");
            if (!string.IsNullOrEmpty(resolve.InfoText)) {
                Console.WriteLine($"[INFO] {resolve.InfoText}");
            }
            Console.WriteLine($"[IMAGE] 标题: {resolve.Data.Title}");
            Console.WriteLine($"[IMAGE] 作者: {resolve.Data.Author}");
            Console.WriteLine($"[IMAGE] PID: {resolve.Data.Pid}");
            Console.WriteLine($"[IMAGE] 尺寸: {resolve.Data.Width}x{resolve.Data.Height}");
            Console.WriteLine($"[IMAGE] 标签: {string.Join(", ", resolve.Data.Tags)}");

            // 阶段3：下载图片
            Console.WriteLine();
            Console.WriteLine("[IMAGE] 正在下载...");
            var bytes = await service.DownloadImageAsync(resolve.Data, config, CancellationToken.None);
            Console.WriteLine($"[IMAGE] 已下载: {bytes.Length} bytes ({bytes.Length / 1024} KB)");
            Console.WriteLine($"[IMAGE] 格式判断: {(bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 ? "JPEG" : "PNG/其他")}");

            // 检查缓存
            Console.WriteLine();
            Console.WriteLine("=== 缓存检查 ===");
            if (Directory.Exists(cacheDir)) {
                var files = Directory.GetFiles(cacheDir);
                Console.WriteLine($"缓存文件数量: {files.Length}");
                foreach (var f in files) {
                    var fi = new FileInfo(f);
                    Console.WriteLine($"  - {fi.Name} ({fi.Length} bytes, 创建时间: {fi.CreationTime:HH:mm:ss})");

                    // 验证文件魔数
                    var fileBytes = await File.ReadAllBytesAsync(f);
                    string detected = fileBytes.Length >= 3 && fileBytes[0] == 0xFF && fileBytes[1] == 0xD8 ? "JPEG" :
                                      fileBytes.Length >= 8 && fileBytes[0] == 0x89 && fileBytes[1] == 0x50 && fileBytes[2] == 0x4E && fileBytes[3] == 0x47 ? "PNG" : "未知";
                    Console.WriteLine($"    [魔数检测] {detected}");
                }
            } else {
                Console.WriteLine("[WARN] 缓存目录不存在");
            }

            Console.WriteLine();
            Console.WriteLine("=== 测试完成 ===");
            Log.Information("测试完成");
        } catch (Exception ex) {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
            Console.ResetColor();
            if (ex.InnerException != null) {
                Console.WriteLine($"  内部异常: {ex.InnerException.Message}");
            }
            Log.Error(ex, "测试失败");
            Environment.Exit(1);
        } finally {
            Log.CloseAndFlush();
        }
    }
}
