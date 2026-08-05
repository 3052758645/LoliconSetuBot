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
        AnsiConsole.MarkupLine("[bold]LoliconSetuBot v1.2[/]\n");
        AnsiConsole.MarkupLine("[bold]用法:[/]\n");
        // 不用 Markup 输出 --tag 等内容，避免 Markup 解析冲突
        Console.Write("  dotnet run -- --tag <tag> --count <n> --infinite --interval <ms> --quiet --help --save --output <dir>\n");
        AnsiConsole.MarkupLine("[bold]参数:[/]\n");
        AnsiConsole.MarkupLine("  [cyan]--tag <tag>[/]         获取指定标签的图片（不传则进入交互模式）");
        AnsiConsole.MarkupLine("  [cyan]--count <n>[/]          获取图片数量，默认 [dim]1[/]");
        AnsiConsole.MarkupLine("  [cyan]--infinite[/]          无限循环模式，Ctrl+C 停止");
        AnsiConsole.MarkupLine("  [cyan]--interval <ms>[/]     无限模式请求间隔（毫秒），默认 [dim]1500[/]");
        AnsiConsole.MarkupLine("  [cyan]--quiet[/]             不显示图片元数据（标题、作者等）");
        AnsiConsole.MarkupLine("  [cyan]--save[/]              修改配置后保存到 config.json");
        AnsiConsole.MarkupLine("  [cyan]--output <dir>[/]      图片输出目录（覆盖 config.json 中的 outputDir）");
        AnsiConsole.MarkupLine("  [cyan]--r18[/]               启用 R18 模式");
        AnsiConsole.MarkupLine("  [cyan]--no-r18[/]             禁用 R18");
        AnsiConsole.MarkupLine("  [cyan]--flip-h[/]             水平翻转图片");
        AnsiConsole.MarkupLine("  [cyan]--flip-v[/]             垂直翻转图片");
        AnsiConsole.MarkupLine("  [cyan]--exclude-ai[/]         排除 AI 生成图");
        AnsiConsole.MarkupLine("  [cyan]--no-exclude-ai[/]      不排除 AI 生成图");
        AnsiConsole.MarkupLine("  [cyan]--size <size>[/]        图片尺寸：original/regular/small/mini/thumb");
        AnsiConsole.MarkupLine("  [cyan]--proxy <proxy>[/]       代理域名（默认 i.pixiv.re）");
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
        bool save = false;
        string? outputDirOverride = null;

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
                case "--save":
                    save = true;
                    break;
                case "--output":
                    outputDirOverride = args[++i];
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
            config.ValidateAndFix();
            Log.Information("配置已加载: r18={R18}, size={Size}, proxy={Proxy}", config.R18, config.Size, config.Proxy);

            // 输出目录：CLI --output 优先级高于 config.json
            var effectiveOutputDir = !string.IsNullOrWhiteSpace(outputDirOverride) ? outputDirOverride : config.OutputDir;
            Log.Information("输出目录: {Dir}", string.IsNullOrWhiteSpace(effectiveOutputDir) ? "cache/" : effectiveOutputDir);

            using var service = new LoliconService(
                outputDir: effectiveOutputDir,
                fallbackUrls: config.FallbackUrls
            );
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

            // 保存配置
            if (save) {
                config.Save("config.json");
                AnsiConsole.MarkupLine("[green]  ✅ 配置已保存到 config.json[/]");
                Log.Information("配置已保存。");
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
        var lines = new[] {
            "___        ________   ___        ___   ________   ________   ________    ________   _______   _________   ___  ___   ________   ________   _________   ",
            "|\\  \\      |\\   __  \\ |\\  \\      |\\  \\ |\\   ____\\ |\\   __  \\ |\\   ___  \\ |\\   ____\\ |\\  ___ \\ |\\___   ___\\|\\  \\|\\  \\ |\\   __  \\ |\\   __  \\ |\\___   ___\\ ",
            "\\ \\  \\     \\ \\  \\|\\  \\ \\ \\  \\     \\ \\  \\ \\ \\  \\___| \\ \\  \\|\\  \\ \\ \\  \\ \\ \\  \\ \\ \\  \\___|_ \\ \\   __/|\\|___ \\  \\_|\\ \\  \\ \\  \\|\\ /_\\ \\  \\|\\  \\|___ \\  \\_| ",
            " \\ \\  \\     \\ \\  \\ \\  \\ \\ \\  \\     \\ \\  \\ \\ \\  \\     \\ \\  \\ \\  \\ \\ \\  \\ \\ \\  \\ \\_____  \\ \\ \\  \\_|/__   \\ \\  \\  \\ \\  \\ \\  \\ \\   __  \\ \\ \\  \\ \\ \\    \\ \\  \\  ",
            "  \\ \\  \\____ \\ \\  \\ \\  \\ \\ \\  \\____ \\ \\  \\ \\ \\  \\____ \\ \\  \\ \\  \\ \\ \\  \\ \\ \\  \\|_____|\\  \\ \\ \\  |_|\\ \\   \\ \\  \\  \\ \\  \\ \\  \\|\\  \\ \\ \\  \\ \\ \\    \\ \\  \\ ",
            "   \\ \\_______\\ \\_______\\ \\ \\_______\\ \\__\\ \\ \\_______\\ \\_______\\ \\__\\ \\ \\__\\ ____\\_\\ \\ \\_______\\   \\ \\__\\  \\ \\_______\\ \\ \\_______\\ \\ \\_______\\    \\ \\__\\",
            "    \\|_______| \\|_______| \\|_______| \\|__| \\|_______| \\|_______| \\|__| \\|__||\\_________|\\|_______|    \\|__|   \\|_______| \\|_______| \\|_______|     \\|__|",
            "                                                                            \\|_________|",
        };
        foreach (var line in lines) Console.WriteLine(line);
        Console.WriteLine();
        Console.WriteLine("  v1.1 · 跨平台 · 两阶段请求 → 展示 → 下载");
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
                else AnsiConsole.MarkupLine($"  ⬇️ {Escape(result.Data.Title)} — {Escape(result.Data.Author)}");

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
            else AnsiConsole.MarkupLine($"  ⬇️ {Escape(result.Data.Title)} — {Escape(result.Data.Author)}");

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
        byte[] resultBytes;
        try {
            resultBytes = await AnsiConsole.Progress().StartAsync(async ctx => {
                var task = ctx.AddTask("⬇️ 下载中...");
                task.MaxValue = 100;
                task.StartTask();

                var downloadedBytes = 0L;
                var totalBytes = 0L;

                var imageBytes = await service.DownloadImageAsync(data, config, ct, new Progress<(long loaded, long total)>(tuple => {
                    downloadedBytes = tuple.loaded;
                    totalBytes = tuple.total;
                    if (totalBytes > 0) {
                        var pct = (int)((double)downloadedBytes / totalBytes * 100);
                        task.Value = Math.Min(100, pct);
                        task.Description = $"⬇️ {downloadedBytes / (1024.0 * 1024.0):F1}MB / {totalBytes / (1024.0 * 1024.0):F1}MB ({Math.Min(100, pct)}%)";
                    } else {
                        task.Description = $"⬇️ 下载中... {downloadedBytes / (1024.0 * 1024.0):F1}MB";
                    }
                }));

                task.Value = 100;
                task.Description = $"✅ 下载完成 ({downloadedBytes / (1024.0 * 1024.0):F1}MB)";
                task.StopTask();
                return imageBytes;
            });
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            throw new InvalidOperationException($"下载失败: {ex.Message}", ex);
        }
        var fmt = resultBytes.Length >= 3 && resultBytes[0] == 0xFF && resultBytes[1] == 0xD8 ? "JPEG" :
                  resultBytes.Length >= 8 && resultBytes[0] == 0x89 && resultBytes[1] == 0x50 && resultBytes[2] == 0x4E && resultBytes[3] == 0x47 ? "PNG" : "未知";
        var kb = resultBytes.Length / 1024;
        AnsiConsole.MarkupLine($"[green]  ✅ 下载完成[/] [dim]({kb} KB) · {fmt}[/]");
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
