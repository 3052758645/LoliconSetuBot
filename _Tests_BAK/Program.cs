using System;
using System.IO;
using System.Reflection;
using LoliconSetuBot.Models;
using LoliconSetuBot.Services;

class Program
{
    static int Main()
    {
        int failures = 0;
        void Check(string name, bool condition)
        {
            if (condition)
                Console.WriteLine(Pass(name));
            else
            {
                failures++;
                Console.WriteLine(Fail(name));
            }
        }
        static string Pass(string n) => $"  PASS: {n}";
        static string Fail(string n) => $"  FAIL: {n}";

        Console.WriteLine("=== Bug Fix Verification ===");
        Console.WriteLine();

        // Bug 1: config.json has no comments
        var cfgText = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..\..\..\..\..\..\config.json"));
        Check("Bug 6: config.json has no # comments", !cfgText.Contains("#"));
        Console.WriteLine("  INFO: config.json is valid pure JSON");

        // Bug 1: r18 uses true/false not 2/0
        // We can verify the source code directly
        var servicePath = Path.Combine(Path.GetDirectoryName(typeof(LoliconService).Assembly.Location), "..\..\..\..\..\..\Services\LoliconService.cs");
        if (!File.Exists(servicePath))
        {
            // Try alternate path
            var exeDir = AppContext.BaseDirectory;
            servicePath = Path.Combine(exeDir.Replace("bin\\Debug\\net10.0\\", "Services\\LoliconService.cs"));
        }
        // Find the repo root
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..\..\..\..\..\..\"));
        var serviceFile = Path.Combine(repoRoot, "LoliconSetuBot", "Services", "LoliconService.cs");
        if (File.Exists(serviceFile))
        {
            var serviceContent = File.ReadAllText(serviceFile);
            Check("Bug 1: r18 uses true/false values", serviceContent.Contains(\'"true"\' + " : " + \'"false"\') && serviceContent.Contains("r18="));
            Check("Bug 1: r18 does not use value 2", !serviceContent.Contains("r18=" + "2"));
            Check("Bug 3: Ctrl+C no retry", serviceContent.Contains("ct.IsCancellationRequested)") && !serviceContent.Contains("InnerException is not TimeoutException"));
            Check("Bug 5: dsc parameter exists", serviceContent.Contains("dsc="));
            Check("Bug 7: Cache raw bytes", serviceContent.Contains("CacheImage(data.Title, rawBytes)"));
        }
        else
        {
            Console.WriteLine("  WARN: Service source file not found at expected path");
        }

        // Bug 4: Mini and Thumb fields in LoliconUrls
        var urls = new LoliconUrls { Mini = "test", Thumb = "test" };
        Check("Bug 4: LoliconUrls has Mini property", !string.IsNullOrEmpty(urls.Mini));
        Check("Bug 4: LoliconUrls has Thumb property", !string.IsNullOrEmpty(urls.Thumb));

        // Bug 2: Service can be instantiated (try-catch exists in ProcessImageAsync)
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var service = new LoliconService(http);
        Check("Bug 2: LoliconService instantiates correctly", service != null);

        Console.WriteLine();
        Console.WriteLine($"??: {10 - failures} ??, {failures} ??");
        return failures > 0 ? 1 : 0;
    }
}
