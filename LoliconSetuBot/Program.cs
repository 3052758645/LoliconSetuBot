using System.Net;
using System.Text;
using Serilog;
using Spectre.Console;
using LoliconSetuBot.Models;
using LoliconSetuBot.Services;

namespace LoliconSetuBot;

static class Program {
    private static readonly CancellationTokenSource _cts = new();

    static async Task Main(string[] args) {
        if (args.Length >= 1 && args[0] is "--test" or "-t") {
            string tag = args.Length > 1 ? args[1] : "";
            await RunTestAsync(tag);
            return;
        }

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

            DrawBanner();
            DrawCommands();

            Console.CancelKeyPress += (s, e) => {
                e.Cancel = true;
                _cts.Cancel();
                Log.Information("[STOP] 正在停止...");
            };

            var config = BotConfig.Load("config.json");
            Log.Information("配置已加载: r18={R18}, size={Size}, proxy={Proxy}", config.R18, config.Size, config.Proxy);

            using var service = new LoliconService();
            service.CleanCache(keep: 50);

            string groupId = args.Length > 0 ? args[0] : "default";
            var cooldowns = new Dictionary<string, DateTimeOffset>();

            while (!_cts.Token.IsCancellationRequested) {
                AnsiConsole.Markup("  [cyan]>[/] ");
                var input = Console.ReadLine()?.Trim() ?? "";
                if (string.IsNullOrEmpty(input)) continue;

                if (input is "exit" or "quit") {
                    break;
                } else if (input is "无限涩图" or "循环") {
                    await RunInfiniteMode(config, cooldowns, groupId, service);
                } else if (TryParseTag(input, out var tag)) {
                    await RunSingleFetch(tag!, config, cooldowns, groupId, service);
                } else {
                    Log.Warning("未知指令: {Input}", input);
                    AnsiConsole.MarkupLine("[red]  ❌ 未知指令，请输入: 来张[标签]涩图 | 无限涩图 | exit[/]\n");
                }
            }

            AnsiConsole.MarkupLine("\n[dim]  再见！[/]");
            Log.Information("再见！");
        } catch (OperationCanceledException) {
            AnsiConsole.MarkupLine("[dim]  程序已取消。[/]");
            Log.Information("程序已取消。");
        } catch (Exception ex) {
            AnsiConsole.MarkupLine($"[red]  ❌ 程序异常终止:[/][red1]{ex.Message}[/]");
            Log.Fatal(ex, "程序异常终止");
        } finally {
            Log.CloseAndFlush();
        }
    }

    private static void DrawBanner() {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold magenta]  🖼️ Lolicon Bot[/]");
        AnsiConsole.MarkupLine($"  [dim]v2.7 · 跨平台 · 两阶段请求 → 展示 → 下载[/]\n");
    }

    private static void DrawCommands() {
        var table = new Table();
        table.AddColumn("[bold]命令[/]");
        table.AddColumn("[dim]说明[/]");
        table.AddRow("来张[blue]标签[/]涩图", "获取单张涩图");
        table.AddRow("无限涩图 / 循环", "进入无限循环模式");
        table.AddRow("exit", "退出程序");
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static bool TryParseTag(string input, out string? tag) {
        if (input.Length >= 4 && input.StartsWith("来张", StringComparison.Ordinal) && input.EndsWith("涩图", StringComparison.Ordinal)) {
            tag = input[2..^2].Trim();
            return true;
        }
        tag = null;
        return false;
    }

    private static async Task RunInfiniteMode(BotConfig config, Dictionary<string, DateTimeOffset> cooldowns, string groupId, LoliconService service) {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold dim]━━━ 无限涩图模式 ━━━[/]\n");
        AnsiConsole.MarkupLine("[dim]  按 Ctrl+C 停止[/]\n");

        int consecutiveErrors = 0;

        while (!_cts.Token.IsCancellationRequested) {
            try {
                await ApplyCooldown(cooldowns, groupId, config);

                AnsiConsole.MarkupLine("[cyan]  ⏳ 请求中…[/]");
                var result = await service.ResolveAsync("", config, _cts.Token);

                DrawImageInfo(result.Data);

                AnsiConsole.MarkupLine("[magenta]  ⬇️ 下载中…[/]");
                var bytes = await service.DownloadImageAsync(result.Data, config, _cts.Token);

                AnsiConsole.MarkupLine($"  [green]✅ 下载完成[/] [dim]({bytes.Length / 1024} KB)[/]\n");

                cooldowns[groupId] = DateTimeOffset.Now;
                consecutiveErrors = 0;

                if (config.AutoRevoke && config.RevokeDelay > 0) {
                    AnsiConsole.MarkupLine($"[yellow]  ⏳ {config.RevokeDelay}ms 后撤回(模拟)[/]\n");
                    Log.Information("[REVOKE] {0}ms 后撤回...", config.RevokeDelay);
                    await Task.Delay(config.RevokeDelay, _cts.Token);
                }
            } catch (OperationCanceledException) {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[dim]  ━━━ 无限模式已停止 ━━━[/]\n");
                Log.Information("无限模式已取消。");
                break;
            } catch (Exception ex) {
                consecutiveErrors++;
                AnsiConsole.MarkupLine($"[red]  ❌ 错误 (第 {consecutiveErrors} 次): {ex.Message}[/]\n");
                Log.Error(ex, "错误 (第 {Errors} 次)", consecutiveErrors);

                if (consecutiveErrors > 5) {
                    AnsiConsole.MarkupLine("[yellow]  ⚠️ 连续错误过多，暂停 10 秒…[/]\n");
                    Log.Warning("连续错误过多，暂停 10 秒...");
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

    private static void DrawImageInfo(LoliconData data) {
        var panel = new Panel(
            new Markup(
                $"[bold]标题:[/] {Escape(data.Title)}\n" +
                $"[bold]作者:[/] {Escape(data.Author)}\n" +
                $"[bold]PID:[/] {data.Pid}\n" +
                $"[bold]尺寸:[/] {data.Width}x{data.Height}"
            )
        );
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();

        if (data.Tags.Count > 0) {
            AnsiConsole.Markup("[dim]  标签:[/] ");
            // 标签内容不能用 Markup（可能含 [ 和 ] 导致解析错误），用 Text 纯文本安全输出
            AnsiConsole.Write(new Text(string.Join(", ", data.Tags)));
            AnsiConsole.WriteLine();
        }
    }

    private static async Task RunSingleFetch(string tag, BotConfig config, Dictionary<string, DateTimeOffset> cooldowns, string groupId, LoliconService service) {
        try {
            await ApplyCooldown(cooldowns, groupId, config);

            AnsiConsole.MarkupLine($"[cyan]  📦 标签:[/] [bold]{Escape(tag)}[/]\n");

            AnsiConsole.MarkupLine("[cyan]  ⏳ 请求中…[/]");
            var result = await service.ResolveAsync(tag, config, _cts.Token);

            DrawImageInfo(result.Data);

            AnsiConsole.MarkupLine("[magenta]  ⬇️ 下载中…[/]");
            var bytes = await service.DownloadImageAsync(result.Data, config, _cts.Token);

            AnsiConsole.MarkupLine($"[green]  ✅ 下载完成[/] [dim]({bytes.Length / 1024} KB)\n[/]");

            cooldowns[groupId] = DateTimeOffset.Now;

            if (config.AutoRevoke && config.RevokeDelay > 0) {
                AnsiConsole.MarkupLine($"[yellow]  ⏳ {config.RevokeDelay}ms 后撤回(模拟)[/]\n");
                await Task.Delay(config.RevokeDelay, _cts.Token);
            }
        } catch (OperationCanceledException) {
            AnsiConsole.MarkupLine("[dim]  请求已取消。[/]");
        } catch (Exception ex) {
            AnsiConsole.MarkupLine($"[red]  ❌ 获取图片失败:[/][red1]{ex.Message}[/]\n");
            Log.Error(ex, "获取图片失败");
        }
    }

    private static async Task ApplyCooldown(Dictionary<string, DateTimeOffset> cooldowns, string groupId, BotConfig config) {
        if (config.CoolDown <= 0) return;
        if (cooldowns.TryGetValue(groupId, out var last)) {
            int remain = config.CoolDown - (int)(DateTimeOffset.Now - last).TotalSeconds;
            if (remain > 0) {
                AnsiConsole.MarkupLine($"[yellow]  ⏳ 冷却中，还需 {remain}s[/]\n");
                Log.Information("[COOLDOWN] {Group} 冷却中，还需 {Remain}s", groupId, remain);
                try {
                    await Task.Delay(TimeSpan.FromSeconds(remain), _cts.Token);
                } catch (OperationCanceledException) {
                    // 取消时退出等待
                }
            }
        }
    }

    private static async Task RunTestAsync(string tag) {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Console.OutputEncoding = Encoding.UTF8;

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold magenta]🧪 功能测试模式[/]");
        AnsiConsole.MarkupLine($"[dim]  标签:[/] '{tag}'\n");

        try {
            var config = BotConfig.Load("config.json");
            Log.Information("配置已加载: r18={R18}, size={Size}, proxy={Proxy}", config.R18, config.Size, config.Proxy);

            using var service = new LoliconService();
            service.CleanCache(keep: 50);

            var cacheDir = Path.Combine(AppContext.BaseDirectory, "cache");

            AnsiConsole.MarkupLine("[cyan]  📡 请求元数据…[/]\n");
            var resolve = await service.ResolveAsync(tag, config, CancellationToken.None);

            DrawImageInfo(resolve.Data);

            AnsiConsole.MarkupLine("[magenta]  ⬇️ 下载图片…[/]\n");
            var bytes = await service.DownloadImageAsync(resolve.Data, config, CancellationToken.None);
            AnsiConsole.MarkupLine($"[green]  ✅ 已下载[/] [dim]({bytes.Length / 1024} KB)[/]");

            var fmt = bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 ? "JPEG" :
                      bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 ? "PNG" : "未知";
            AnsiConsole.MarkupLine($"[dim]  格式:[/] {fmt}\n");

            AnsiConsole.MarkupLine("\n[dim]  📂 缓存检查:[/]\n");
            if (Directory.Exists(cacheDir)) {
                var files = Directory.GetFiles(cacheDir);
                AnsiConsole.MarkupLine($"[dim]  缓存文件:[/] {files.Length}\n");

                var cacheTable = new Table();
                cacheTable.AddColumn("[dim]文件名[/]");
                cacheTable.AddColumn("[dim]大小[/]");
                cacheTable.AddColumn("[dim]魔数[/]");

                foreach (var f in files) {
                    var fi = new FileInfo(f);
                    var raw = await File.ReadAllBytesAsync(f);
                    var detected = raw.Length >= 3 && raw[0] == 0xFF && raw[1] == 0xD8 ? "JPEG" :
                                   raw.Length >= 8 && raw[0] == 0x89 && raw[1] == 0x50 && raw[2] == 0x4E && raw[3] == 0x47 ? "PNG" : "未知";
                    cacheTable.AddRow(fi.Name, $"{fi.Length} bytes", detected);
                }

                AnsiConsole.Write(cacheTable);
            } else {
                AnsiConsole.MarkupLine("[yellow]  ⚠️ 缓存目录不存在[/]\n");
            }

            AnsiConsole.MarkupLine("\n[green]  ✅ 测试完成[/]");
            Log.Information("测试完成");
        } catch (Exception ex) {
            AnsiConsole.MarkupLine($"\n[red]  ❌ {ex.GetType().Name}: {ex.Message}[/]");
            if (ex.InnerException != null) {
                AnsiConsole.MarkupLine($"  [dim]内部异常:[/] {ex.InnerException.Message}[/]");
            }
            Log.Error(ex, "测试失败");
            Environment.Exit(1);
        } finally {
            Log.CloseAndFlush();
        }
    }

    private static string Escape(string s) {
        return s.Replace("[", "[[]").Replace("]", "[]]");
    }
}
