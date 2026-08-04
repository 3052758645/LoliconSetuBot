using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using Serilog;
using Spectre.Console;
using LoliconSetuBot.Models;
using LoliconSetuBot.Services;

namespace LoliconSetuBot;

static class Program {
    [DllImport("kernel32.dll")]
    static extern bool SetConsoleOutputCP(uint codePage);

    private static readonly CancellationTokenSource _cts = new();

    private static void PrintHelp() {
        AnsiConsole.MarkupLine("[bold]LoliconSetuBot v2.7[/]\n");
        AnsiConsole.MarkupLine("[bold]用法:[/]\n");
        // 不用 Markup 输出 --tag 等内容，避免 Markup 解析冲突
        Console.Write("  dotnet run -- --tag <tag> --count <n> --infinite --interval <ms> --quiet --help\n");
        AnsiConsole.MarkupLine("[bold]参数:[/]\n");
        AnsiConsole.MarkupLine("  [cyan]--tag <tag>[/]         获取指定标签的图片（不传则进入交互模式）");
        AnsiConsole.MarkupLine("  [cyan]--count <n>[/]          获取图片数量，默认 [dim]1[/]");
        AnsiConsole.MarkupLine("  [cyan]--infinite[/]          无限循环模式，Ctrl+C 停止");
        AnsiConsole.MarkupLine("  [cyan]--interval <ms>[/]     无限模式请求间隔（毫秒），默认 [dim]1500[/]");
        AnsiConsole.MarkupLine("  [cyan]--quiet[/]             不显示图片元数据（标题、作者等）");
        AnsiConsole.MarkupLine("  [cyan]--help[/]              显示此帮助信息\n");
    }

    static async Task Main(string[] args) {
        // Fix mojibake: Windows cmd defaults to GBK, .NET 6+ outputs UTF-8.
        Console.OutputEncoding = Encoding.UTF8;
        SetConsoleOutputCP(65001);

        if (args.Length >= 1 && args[0] is "--help" or "-h") {
            PrintHelp();
            return;
        }

        // Parse CLI args
        string? tag = null;
        int count = 1;
        bool infinite = false;
        int interval = 1500;
        bool quiet = false;

        for (int i = 0; i < args.Length; i++) {
            switch (args[i]) {
                case "--tag":
                    tag = args[++i];
                    break;
                case "--count":
                    count = int.Parse(args[++i]);
                    break;
                case "--infinite":
                    infinite = true;
                    break;
                case "--interval":
                    interval = int.Parse(args[++i]);
                    break;
                case "--quiet":
                    quiet = true;
                    break;
                default:
                    Log.Logger = new LoggerConfiguration()
                        .MinimumLevel.Warning()
                        .WriteTo.Console()
                        .CreateLogger();
                    Log.Warning("未知参数: {Arg}", args[i]);
                    PrintHelp();
                    return;
            }
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
            var config = BotConfig.Load("config.json");
            Log.Information("配置已加载: r18={R18}, size={Size}, proxy={Proxy}", config.R18, config.Size, config.Proxy);

            using var service = new LoliconService();
            service.CleanCache(keep: 50);

            string groupId = config.Enabled ? "default" : "disabled";
            var cooldowns = new Dictionary<string, DateTimeOffset>();

            Console.CancelKeyPress += (s, e) => {
                e.Cancel = true;
                _cts.Cancel();
                Log.Information("[STOP] 正在停止...");
            };

            DrawBanner();

            if (!config.Enabled) {
                AnsiConsole.MarkupLine("[yellow]  ⚠️ 已禁用（config.json 中 enabled=false）[/]");
                return;
            }

            if (infinite) {
                // 无限模式
                await RunInfiniteMode(tag, config, cooldowns, groupId, service, interval, quiet);
            } else if (tag != null) {
                // 单次/批量获取
                await RunBatchFetch(tag, count, config, cooldowns, groupId, service, quiet);
            } else {
                // 交互模式
                DrawCommands();
                await RunInteractiveMode(config, cooldowns, groupId, service);
            }

            AnsiConsole.MarkupLine("\n[dim]  再见！[/]");
            Log.Information("再见！");
        } catch (OperationCanceledException) {
            AnsiConsole.MarkupLine("[dim]  程序已取消。[/]");
            Log.Information("程序已取消。");
        } catch (Exception ex) {
            AnsiConsole.MarkupLine($"[red]  ❌ 程序异常终止:[/] [red1]{Escape(ex.Message)}[/]");
            Log.Fatal(ex, "程序异常终止");
        } finally {
            Log.CloseAndFlush();
        }
    }

    private static void DrawBanner() {
        Console.WriteLine();
        Console.WriteLine(@"   ____  _            _  ____          _                 ");
        Console.WriteLine(@"  / ___|| |_ _ __ __| || ___| _   _  | |_ ___  __ _    ");
        Console.WriteLine(@"  \___ \| __| '__/ _` ||___ \| | | | | __/ _ \/ _` |   ");
        Console.WriteLine(@"   ___) | |_| || (_| | ___) | |_| | | ||  __/ (_| |   ");
        Console.WriteLine(@"  |____/ \__|_| \__,_||____/ \__, |  \__\___|\__,_|   ");
        Console.WriteLine(@"                             |___/                    ");
        Console.WriteLine();
        Console.WriteLine("  v2.7 · 跨平台 · 两阶段请求 → 展示 → 下载");
        Console.WriteLine();
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

    private static async Task RunInteractiveMode(BotConfig config, Dictionary<string, DateTimeOffset> cooldowns, string groupId, LoliconService service) {
        while (!_cts.Token.IsCancellationRequested) {
            AnsiConsole.Markup("  [cyan]>[/] ");
            var input = Console.ReadLine()?.Trim() ?? "";
            if (string.IsNullOrEmpty(input)) continue;

            if (input is "exit" or "quit") {
                break;
            } else if (input is "无限涩图" or "循环") {
                await RunInfiniteMode(null, config, cooldowns, groupId, service, 1500, false);
            } else if (TryParseTag(input, out var tag)) {
                await RunSingleFetch(tag!, config, cooldowns, groupId, service, false);
            } else {
                Log.Warning("未知指令: {Input}", input);
                AnsiConsole.MarkupLine("[red]  ❌ 未知指令，请输入: 来张[标签]涩图 | 无限涩图 | exit[/]\n");
            }
        }
    }

    private static async Task RunBatchFetch(string tag, int count, BotConfig config, Dictionary<string, DateTimeOffset> cooldowns, string groupId, LoliconService service, bool quiet) {
        if (count < 1) {
            AnsiConsole.MarkupLine("[yellow]  ⚠️ 数量必须 >= 1[/]");
            return;
        }
        AnsiConsole.MarkupLine($"[bold]  📦 标签:[/] [bold]{Escape(tag)}[/]  [dim]x{count}[/]\n");
        for (int i = 0; i < count; i++) {
            if (_cts.Token.IsCancellationRequested) break;
            await RunSingleFetch(tag, config, cooldowns, groupId, service, quiet);
            if (i < count - 1) await Task.Delay(1000, _cts.Token);
        }
        AnsiConsole.MarkupLine("\n[dim]  ✅ 完成！[/]");
    }

    private static bool TryParseTag(string input, out string? tag) {
        if (input.Length >= 4 && input.StartsWith("来张", StringComparison.Ordinal) && input.EndsWith("涩图", StringComparison.Ordinal)) {
            tag = input[2..^2].Trim();
            return true;
        }
        tag = null;
        return false;
    }

    private static async Task RunInfiniteMode(string? tag, BotConfig config, Dictionary<string, DateTimeOffset> cooldowns, string groupId, LoliconService service, int intervalMs, bool quiet) {
        if (!quiet) {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold dim]━━━ 无限涩图模式 ━━━[/]\n");
            AnsiConsole.MarkupLine("[dim]  按 Ctrl+C 停止[/]\n");
        }

        int consecutiveErrors = 0;

        while (!_cts.Token.IsCancellationRequested) {
            try {
                await ApplyCooldown(cooldowns, groupId, config);

                AnsiConsole.MarkupLine("[cyan]  ⏳ 请求中…[/]");
                var result = await service.ResolveAsync(tag ?? "", config, _cts.Token);

                if (!quiet) DrawImageInfo(result.Data);
                else Console.WriteLine($"  ⬇️ {result.Data.Title} — {result.Data.Author}");

                await DownloadWithProgress(service, result.Data, config, _cts.Token);

                cooldowns[groupId] = DateTimeOffset.Now;
                consecutiveErrors = 0;

                if (config.AutoRevoke && config.RevokeDelay > 0) {
                    AnsiConsole.MarkupLine($"[yellow]  ⏳ {config.RevokeDelay}ms 后撤回(模拟)[/]\n");
                    Log.Information("[REVOKE] {0}ms 后撤回...", config.RevokeDelay);
                    await Task.Delay(config.RevokeDelay, _cts.Token);
                }
            } catch (OperationCanceledException) {
                if (!quiet) {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[dim]  ━━━ 无限模式已停止 ━━━[/]\n");
                }
                Log.Information("无限模式已取消。");
                break;
            } catch (Exception ex) {
                consecutiveErrors++;
                AnsiConsole.MarkupLine($"[red]  ❌ 错误 (第 {consecutiveErrors} 次): {Escape(ex.Message)}[/]\n");
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
                    await Task.Delay(intervalMs, _cts.Token);
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

    private static async Task RunSingleFetch(string tag, BotConfig config, Dictionary<string, DateTimeOffset> cooldowns, string groupId, LoliconService service, bool quiet) {
        try {
            await ApplyCooldown(cooldowns, groupId, config);

            if (!quiet) AnsiConsole.MarkupLine($"[cyan]  📦 标签:[/] [bold]{Escape(tag)}[/]\n");

            AnsiConsole.MarkupLine("[cyan]  ⏳ 请求中…[/]");
            var result = await service.ResolveAsync(tag, config, _cts.Token);

            if (!quiet) DrawImageInfo(result.Data);
            else Console.WriteLine($"  ⬇️ {result.Data.Title} — {result.Data.Author}");

            await DownloadWithProgress(service, result.Data, config, _cts.Token);

            cooldowns[groupId] = DateTimeOffset.Now;

            if (config.AutoRevoke && config.RevokeDelay > 0) {
                AnsiConsole.MarkupLine($"[yellow]  ⏳ {config.RevokeDelay}ms 后撤回(模拟)[/]\n");
                await Task.Delay(config.RevokeDelay, _cts.Token);
            }
        } catch (OperationCanceledException) {
            AnsiConsole.MarkupLine("[dim]  请求已取消。[/]");
        } catch (Exception ex) {
            AnsiConsole.MarkupLine($"[red]  ❌ 获取图片失败:[/][red1]{Escape(ex.Message)}[/]\n");
            Log.Error(ex, "获取图片失败");
        }
    }

    private static async Task DownloadWithProgress(LoliconService service, LoliconData data, BotConfig config, CancellationToken ct) {
        long total = 0;
        long loaded = 0;
        var lastPct = -1;
        var bytes = await service.DownloadImageAsync(data, config, ct, new Progress<(long loaded, long total)>(tuple => {
            loaded = tuple.loaded;
            total = tuple.total;
            if (total > 0) {
                var pct = (int)((double)loaded / total * 100);
                if (pct - lastPct >= 1) { lastPct = pct; RenderProgress(loaded, total); }
            }
        }));
        Console.Write("\r");
        if (!Console.IsOutputRedirected) { Console.Write(new string(' ', Console.WindowWidth)); }
        Console.Write("\r");
        var fmt = bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 ? "JPEG" :
                  bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 ? "PNG" : "未知";
        var kb = bytes.Length / 1024;
        AnsiConsole.MarkupLine($"[green]  ✅ 下载完成[/] [dim]({kb} KB) · {fmt}[/]");
    }

    private static void RenderProgress(long loaded, long total) {
        if (total <= 0) {
            Console.WriteLine("⬇️ 下载中… 0%");
            return;
        }
        var pct = Math.Min(100, (int)((double)loaded / total * 100));
        var mbLoaded = loaded / (1024.0 * 1024.0);
        var mbTotal = total / (1024.0 * 1024.0);
        var barWidth = 30;
        var filled = (int)(barWidth * pct / 100.0);
        var bar = new string('\u2588', filled) + new string('\u2591', barWidth - filled);
        Console.Write("⬇️");
        Console.Write($"{mbLoaded:F1}MB / {mbTotal:F1}MB ");
        Console.Write(bar);
        Console.Write($" {pct}%");
        Console.Out.Flush();
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
                }
            }
        }
    }

    private static string Escape(string s) {
        return s.Replace("[", "[[]").Replace("]", "[]]");
    }
}
