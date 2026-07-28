using System.Text;
using LoliconSetuBot.Models;
using LoliconSetuBot.Services;

namespace LoliconSetuBot;

/// <summary>
/// 程序主类：控制台程序的入口点，处理命令解析和运行模式
/// </summary>
static class Program {
    // 取消令牌源，用于全局控制程序停止（Ctrl+C 时触发）
    private static readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// 程序入口点（主函数）：初始化控制台、加载配置、进入命令循环
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    static async Task Main(string[] args) {
        // 设置控制台输出编码为 UTF-8，确保中文字符正常显示
        Console.OutputEncoding = Encoding.UTF8;
        // 设置控制台窗口标题
        Console.Title = "Lolicon Bot";
        // 设置启动信息颜色为紫色
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("Lolicon Bot v1.0 (SkiaSharp + Retry + Async)");
        // 恢复默认颜色
        Console.ResetColor();
        // 打印可用指令说明
        Console.WriteLine("命令:");
        Console.WriteLine("  来张[标签]涩图   - 获取单张（标签可选）");
        Console.WriteLine("  无限涩图 / 循环  - 进入无限循环模式（Ctrl+C 停止）");
        Console.WriteLine("  exit             - 退出程序");
        Console.WriteLine();

        // 注册 Ctrl+C 事件：取消按键默认行为，触发取消令牌
        Console.CancelKeyPress += (s, e) => {
            e.Cancel = true;
            _cts.Cancel();
            Console.WriteLine("[STOP] 正在停止...");
        };

        // 从 config.json 加载配置
        var config = BotConfig.Load("config.json");
        // 创建 HTTP 客户端（超时 60 秒）
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        // 创建插画服务实例
        using var service = new LoliconService(httpClient);
        // 清理旧缓存
        service.CleanCache();

        // 获取群组 ID（从命令行参数获取，默认为 default）
        string groupId = args.Length > 0 ? args[0] : "default";
        // 冷却时间记录：按群组 ID 跟踪上次请求时间
        var cooldowns = new Dictionary<string, DateTimeOffset>();

        // 主循环：持续读取用户输入
        while (!_cts.Token.IsCancellationRequested) {
            Console.Write("> ");
            // 异步读取一行用户输入并去除首尾空格
            var input = (await Console.In.ReadLineAsync())?.Trim();
            // 如果输入为空则跳过
            if (string.IsNullOrEmpty(input)) continue;

            // 处理退出指令
            if (input is "exit" or "quit") {
                break;
            // 处理无限循环模式指令
            } else if (input is "无限涩图" or "循环") {
                await RunInfiniteMode(config, cooldowns, groupId, service);
            // 处理单张获取指令（如 "来张涩图" 或 "来张御坂美琴涩图"）
            } else if (input.StartsWith("来张") && input.EndsWith("涩图") && input.Length >= 4) {
                await RunSingleFetch(input, config, cooldowns, groupId, service);
            // 未知指令提示
            } else {
                Console.WriteLine("[ERROR] 未知指令，请输入: 来张[标签]涩图 / 无限涩图 / exit");
            }
        }

        // 程序退出提示
        Console.WriteLine("[INFO] 再见！");
    }

    /// <summary>
    /// 无限循环模式：持续获取并打印图片信息，直到用户取消
    /// </summary>
    /// <param name="config"></param>
    /// <param name="cooldowns"></param>
    /// <param name="groupId"></param>
    /// <param name="service"></param>
    /// <returns></returns>
    private static async Task RunInfiniteMode(BotConfig config, Dictionary<string, DateTimeOffset> cooldowns, string groupId, LoliconService service) {
        // 提示用户进入无限模式
        Console.WriteLine("[INFO] 进入无限涩图模式，按 Ctrl+C 停止...");
        // 错误计数器，用于检测连续失败
        int errorCount = 0;
        // 标记是否为首次输出
        bool first = true;

        while (!_cts.Token.IsCancellationRequested) {
            try {
                // 应用冷却时间限制
                await ApplyCooldown(cooldowns, groupId, config);

                // 在两次结果之间打印空行
                if (!first) {
                    Console.WriteLine();
                }
                first = false;

                // 获取图片（不传标签，使用默认随机）
                var result = await service.FetchAsync("", config, _cts.Token);
                // 如果有信息文本则打印
                if (!string.IsNullOrEmpty(result.InfoText)) {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine(result.InfoText);
                    Console.ResetColor();
                }
                // 打印图片大小
                Console.WriteLine("[IMAGE] 大小: " + (result.ImageBytes.Length / 1024) + " KB");
                // 更新该群组的冷却时间
                cooldowns[groupId] = DateTimeOffset.Now;
                errorCount = 0;

                // 如果启用了自动撤回功能
                if (config.AutoRevoke && config.RevokeDelay > 0) {
                    Console.WriteLine("[REVOKE] " + config.RevokeDelay + "ms 后撤回...");
                    await Task.Delay(config.RevokeDelay, _cts.Token);
                    Console.WriteLine("[REVOKE] 撤回指令已执行（模拟）");
                }
            } catch (TaskCanceledException) {
                // 任务被取消，退出循环
                break;
            } catch (OperationCanceledException) {
                // 操作被取消，退出循环
                break;
            } catch (Exception ex) {
                // 捕获其他异常并记录错误
                errorCount++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[ERROR] 错误 (" + errorCount + "): " + ex.Message);
                Console.ResetColor();
                // 连续错误超过 5 次则暂停 10 秒
                if (errorCount > 5) {
                    Console.WriteLine("[WARN] 连续错误过多，暂停 10 秒...");
                    await Task.Delay(TimeSpan.FromSeconds(10), _cts.Token);
                    errorCount = 0;
                }
            }

            // 每次请求后等待 1.5 秒再获取下一张
            if (!_cts.Token.IsCancellationRequested)
                await Task.Delay(1500, _cts.Token);
        }
        // 提示无限模式已停止
        Console.WriteLine("[INFO] 无限循环已停止。");
    }

    /// <summary>
    /// 单张获取模式：解析输入指令获取指定标签的图片，仅执行一次
    /// </summary>
    /// <param name="input"></param>
    /// <param name="config"></param>
    /// <param name="cooldowns"></param>
    /// <param name="groupId"></param>
    /// <param name="service"></param>
    /// <returns></returns>
    private static async Task RunSingleFetch(string input, BotConfig config, Dictionary<string, DateTimeOffset> cooldowns, string groupId, LoliconService service) {
        // 标签起始位置：跳过 "来张" 2 个字符
        var tagStart = 2;
        // 标签长度：总长度减去 "来张" 和 "涩图" 共 6 个字符
        var tagLen = input.Length - 6;
        // 提取中间部分的标签文本
        var tag = tagLen > 0 ? input.Substring(tagStart, tagLen).Trim() : "";

        try {
            // 应用冷却时间限制
            await ApplyCooldown(cooldowns, groupId, config);

            // 获取指定标签的图片
            var result = await service.FetchAsync(tag, config, _cts.Token);
            // 如果有信息文本则打印
            if (!string.IsNullOrEmpty(result.InfoText)) {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine(result.InfoText);
                Console.ResetColor();
            }
            // 打印图片大小
            Console.WriteLine("[IMAGE] 大小: " + (result.ImageBytes.Length / 1024) + " KB");
            Console.WriteLine();
            // 更新冷却时间
            cooldowns[groupId] = DateTimeOffset.Now;
            // 如果启用了自动撤回功能
            if (config.AutoRevoke && config.RevokeDelay > 0) {
                Console.WriteLine("[REVOKE] " + config.RevokeDelay + "ms 后撤回...");
                await Task.Delay(config.RevokeDelay, _cts.Token);
                Console.WriteLine("[REVOKE] 撤回指令已执行（模拟）");
            }
        } catch (TaskCanceledException) {
            // HTTP 客户端超时或用户手动取消
        } catch (OperationCanceledException) {
            // 用户取消操作
        } catch (Exception ex) {
            // 捕获异常并以红色打印错误信息
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[ERROR] " + ex.Message);
            Console.ResetColor();
        }
    }

    /// <summary>
    /// 应用冷却时间：检查并等待冷却时间，防止请求过于频繁
    /// </summary>
    /// <param name="cooldowns"></param>
    /// <param name="groupId"></param>
    /// <param name="config"></param>
    /// <returns></returns>
    private static async Task ApplyCooldown(Dictionary<string, DateTimeOffset> cooldowns, string groupId, BotConfig config) {
        // 如果冷却时间配置为 0 则不限制
        if (config.CoolDown <= 0) return;
        // 尝试获取该群组的上次请求时间
        if (cooldowns.TryGetValue(groupId, out var last)) {
            // 计算剩余冷却时间（秒）
            int remain = config.CoolDown - (int)(DateTimeOffset.Now - last).TotalSeconds;
            // 如果还有剩余冷却时间则等待
            if (remain > 0) {
                Console.WriteLine("[COOLDOWN] 冷却中，还需 " + remain + " 秒");
                try {
                    await Task.Delay(TimeSpan.FromSeconds(remain), _cts.Token);
                } catch (TaskCanceledException) {
                    // 取消时退出等待
                } catch (OperationCanceledException) {
                    // 操作取消时退出等待
                }
            }
        }
    }
}
