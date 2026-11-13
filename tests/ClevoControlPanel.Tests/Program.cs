using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using ClevoControlPanel.Tests.Live;
using Xunit;

namespace ClevoControlPanel.Tests
{
    public class CliOptions
    {
        public bool IsLive { get; set; }
        public bool FullLive { get; set; }
        public string TierFilter { get; set; } = "all";
        public string NameFilter { get; set; } = string.Empty;
        public bool ListOnly { get; set; }
        public string JsonOutputPath { get; set; } = string.Empty;
        public bool ShowHelp { get; set; }

        public HashSet<string>? ParsedTiers
        {
            get
            {
                if (string.IsNullOrWhiteSpace(TierFilter) || TierFilter.Equals("all", StringComparison.OrdinalIgnoreCase))
                    return null;

                return new HashSet<string>(
                    TierFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public class TestExecutionResult
    {
        public string ClassName { get; set; } = string.Empty;
        public string MethodName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public List<string> Tiers { get; set; } = new();
        public bool Passed { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public string? ErrorMessage { get; set; }
        public string? StackTrace { get; set; }
    }

    public class TestSuiteSummary
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public bool IsLiveMode { get; set; }
        public int TotalDiscovered { get; set; }
        public int TotalExecuted { get; set; }
        public int TotalPassed { get; set; }
        public int TotalFailed { get; set; }
        public long TotalDurationMs { get; set; }
        public List<TestExecutionResult> Results { get; set; } = new();
    }

    public class Program
    {
        private const string ANSI_RESET = "\u001b[0m";
        private const string ANSI_GREEN = "\u001b[32m";
        private const string ANSI_RED = "\u001b[31m";
        private const string ANSI_YELLOW = "\u001b[33m";
        private const string ANSI_CYAN = "\u001b[36m";
        private const string ANSI_BOLD = "\u001b[1m";

        private static readonly HashSet<string> ValidTiers = new(StringComparer.OrdinalIgnoreCase)
        {
            "1", "2", "3", "4", "all"
        };

        public static bool ValidateTierArgument(string? tierArg)
        {
            if (string.IsNullOrWhiteSpace(tierArg))
                return false;

            var parts = tierArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
                return false;

            foreach (var part in parts)
            {
                if (!ValidTiers.Contains(part))
                    return false;
            }

            return true;
        }

        public static int Main(string[] args)
        {
            int parseExitCode = ParseArguments(args, out var options);
            if (parseExitCode != 0)
            {
                return parseExitCode;
            }

            if (options.ShowHelp)
            {
                PrintHelp();
                return 0;
            }

            Console.WriteLine($"{ANSI_BOLD}{ANSI_CYAN}======================================================================{ANSI_RESET}");
            Console.WriteLine($"{ANSI_BOLD}{ANSI_CYAN}       ClevoControlPanel Standalone Console Test Suite Runner        {ANSI_RESET}");
            Console.WriteLine($"{ANSI_BOLD}{ANSI_CYAN}======================================================================{ANSI_RESET}");
            Console.WriteLine($"Mode: {(options.IsLive ? $"{ANSI_YELLOW}Live Hardware (--live){ANSI_RESET}" : $"{ANSI_GREEN}Virtual Mock Simulator (Default CI){ANSI_RESET}")}");
            Console.WriteLine($"Tier Filter: {options.TierFilter}");
            if (!string.IsNullOrEmpty(options.NameFilter))
                Console.WriteLine($"Name Filter: {options.NameFilter}");
            Console.WriteLine();

            if (options.IsLive)
            {
                if (!OperatingSystem.IsWindows())
                {
                    Console.WriteLine($"{ANSI_RED}[ERROR] Live hardware execution is only supported on Windows.{ANSI_RESET}");
                    return 2;
                }

                if (!IsAdministrator())
                {
                    Console.WriteLine($"{ANSI_YELLOW}[WARN] 当前未以管理员身份运行；若 CCDCHUService 正常运行，驱动调用仍可成功。{ANSI_RESET}");
                }

                return LiveHardwareVerifier.Run(options.FullLive, options.JsonOutputPath);
            }

            var discoveredTests = DiscoverTests(options);
            Console.WriteLine($"Discovered {ANSI_BOLD}{discoveredTests.Count}{ANSI_RESET} matching test invocation(s).\n");

            if (options.ListOnly)
            {
                PrintTestList(discoveredTests);
                if (discoveredTests.Count == 0)
                {
                    Console.WriteLine($"{ANSI_RED}[ERROR] No matching tests found.{ANSI_RESET}");
                    return 1;
                }
                return 0;
            }

            var summary = RunTests(discoveredTests, options);
            PrintSummary(summary);

            if (!string.IsNullOrWhiteSpace(options.JsonOutputPath))
            {
                try
                {
                    var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(options.JsonOutputPath, json);
                    Console.WriteLine($"Results successfully exported to: {options.JsonOutputPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ANSI_RED}[ERROR] Failed to export JSON results: {ex.Message}{ANSI_RESET}");
                }
            }

            if (summary.TotalExecuted == 0)
            {
                Console.WriteLine($"{ANSI_RED}[ERROR] No matching tests were found or executed.{ANSI_RESET}");
                return 1;
            }

            return summary.TotalFailed == 0 ? 0 : 1;
        }

        public static int ParseArguments(string[] args, out CliOptions options)
        {
            options = new CliOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg.ToLowerInvariant())
                {
                    case "--live":
                    case "-l":
                        options.IsLive = true;
                        break;

                    case "--full":
                        options.FullLive = true;
                        break;

                    case "--tier":
                    case "-t":
                        if (i + 1 >= args.Length)
                        {
                            Console.WriteLine($"{ANSI_RED}[ERROR] Option '{arg}' expects a tier value (e.g. 1, 2, 3, 4, or all).{ANSI_RESET}");
                            return 2;
                        }
                        string tierArg = args[++i];
                        if (!ValidateTierArgument(tierArg))
                        {
                            Console.WriteLine($"{ANSI_RED}[ERROR] Invalid tier '{tierArg}'. Expected one or more of: 1, 2, 3, 4, all (e.g. --tier 1,2).{ANSI_RESET}");
                            return 2;
                        }
                        options.TierFilter = tierArg;
                        break;

                    case "--filter":
                    case "-f":
                        if (i + 1 >= args.Length)
                        {
                            Console.WriteLine($"{ANSI_RED}[ERROR] Option '{arg}' expects a filter string.{ANSI_RESET}");
                            return 2;
                        }
                        options.NameFilter = args[++i];
                        break;

                    case "--list":
                        options.ListOnly = true;
                        break;

                    case "--json":
                    case "-j":
                        if (i + 1 >= args.Length)
                        {
                            Console.WriteLine($"{ANSI_RED}[ERROR] Option '{arg}' expects a file path.{ANSI_RESET}");
                            return 2;
                        }
                        options.JsonOutputPath = args[++i];
                        break;

                    case "--help":
                    case "-h":
                    case "-?":
                    case "/?":
                        options.ShowHelp = true;
                        break;

                    default:
                        Console.WriteLine($"{ANSI_RED}[ERROR] Unknown CLI option: '{arg}'{ANSI_RESET}");
                        PrintHelp();
                        return 2;
                }
            }
            return 0;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Usage: dotnet run --project tests\\ClevoControlPanel.Tests [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --live, -l            Verify against live hardware via InsydeDCHU.dll (safe re-apply mode)");
            Console.WriteLine("  --full                With --live: sweep every profile + turbo/RGB, then restore the snapshot");
            Console.WriteLine("  --tier, -t <tiers>    Filter by tier: 1, 2, 3, 4, combinations (e.g. 1,2 or 3,4), or all (default)");
            Console.WriteLine("  --filter, -f <text>   Filter tests matching substring in class or method name");
            Console.WriteLine("  --list                Discover and list all matching test cases without running");
            Console.WriteLine("  --json, -j <file>     Export test execution report to a JSON file");
            Console.WriteLine("  --help, -h            Show this help documentation");
        }

        private static bool IsAdministrator()
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private record TestInvocation(
            Type ClassType,
            MethodInfo Method,
            object?[]? Parameters,
            string DisplayName,
            List<string> Tiers
        );

        private static List<string> GetTiers(MemberInfo member)
        {
            var tiers = new List<string>();
            foreach (var attr in member.CustomAttributes)
            {
                if (attr.AttributeType.Name == "TraitAttribute" &&
                    attr.ConstructorArguments.Count >= 2 &&
                    string.Equals(attr.ConstructorArguments[0].Value?.ToString(), "Tier", StringComparison.OrdinalIgnoreCase))
                {
                    var val = attr.ConstructorArguments[1].Value?.ToString();
                    if (!string.IsNullOrEmpty(val))
                        tiers.Add(val);
                }
            }
            return tiers;
        }

        private static List<TestInvocation> DiscoverTests(CliOptions options)
        {
            var invocations = new List<TestInvocation>();
            var assembly = typeof(Program).Assembly;
            var testClasses = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.Namespace == "ClevoControlPanel.Tests");

            var filterTiers = options.ParsedTiers;

            foreach (var testClass in testClasses)
            {
                // Class-level traits
                var classTiers = GetTiers(testClass);

                var methods = testClass.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                foreach (var method in methods)
                {
                    var isTheory = method.GetCustomAttribute<TheoryAttribute>() != null;
                    var isFact = !isTheory && method.GetCustomAttribute<FactAttribute>() != null;

                    if (!isFact && !isTheory) continue;

                    // Method-level traits
                    var methodTiers = GetTiers(method);

                    var combinedTiers = classTiers.Union(methodTiers).Distinct().ToList();
                    if (combinedTiers.Count == 0) combinedTiers.Add("1"); // Default to tier 1

                    // Apply tier filtering
                    if (filterTiers != null && !combinedTiers.Any(t => filterTiers.Contains(t)))
                        continue;

                    // Apply name filtering
                    string fullDisplayName = $"{testClass.Name}.{method.Name}";
                    if (!string.IsNullOrEmpty(options.NameFilter) &&
                        !fullDisplayName.Contains(options.NameFilter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (isFact)
                    {
                        invocations.Add(new TestInvocation(testClass, method, null, fullDisplayName, combinedTiers));
                    }
                    else if (isTheory)
                    {
                        var methodParams = method.GetParameters();
                        foreach (var attr in method.CustomAttributes)
                        {
                            if (attr.AttributeType.Name == "InlineDataAttribute")
                            {
                                var rawArgs = new List<object?>();
                                if (attr.ConstructorArguments.Count == 1 &&
                                    attr.ConstructorArguments[0].Value is System.Collections.ObjectModel.ReadOnlyCollection<CustomAttributeTypedArgument> collection)
                                {
                                    for (int pIdx = 0; pIdx < collection.Count; pIdx++)
                                    {
                                        var typedArg = collection[pIdx];
                                        object? val = typedArg.Value;
                                        if (pIdx < methodParams.Length)
                                        {
                                            var targetType = methodParams[pIdx].ParameterType;
                                            if (val != null && val.GetType() != targetType)
                                            {
                                                if (targetType.IsEnum)
                                                {
                                                    val = Enum.ToObject(targetType, val);
                                                }
                                                else
                                                {
                                                    val = Convert.ChangeType(val, targetType);
                                                }
                                            }
                                        }
                                        rawArgs.Add(val);
                                    }
                                }
                                else
                                {
                                    for (int pIdx = 0; pIdx < attr.ConstructorArguments.Count; pIdx++)
                                    {
                                        var typedArg = attr.ConstructorArguments[pIdx];
                                        object? val = typedArg.Value;
                                        if (pIdx < methodParams.Length)
                                        {
                                            var targetType = methodParams[pIdx].ParameterType;
                                            if (val != null && val.GetType() != targetType)
                                            {
                                                if (targetType.IsEnum)
                                                    val = Enum.ToObject(targetType, val);
                                                else
                                                    val = Convert.ChangeType(val, targetType);
                                            }
                                        }
                                        rawArgs.Add(val);
                                    }
                                }

                                var paramArray = rawArgs.ToArray();
                                string paramStr = string.Join(", ", paramArray.Select(p => p?.ToString() ?? "null"));
                                string theoryName = $"{fullDisplayName}({paramStr})";
                                invocations.Add(new TestInvocation(testClass, method, paramArray, theoryName, combinedTiers));
                            }
                        }
                    }
                }
            }

            return invocations;
        }

        private static void PrintTestList(List<TestInvocation> tests)
        {
            Console.WriteLine($"{ANSI_BOLD}Discovered Tests:{ANSI_RESET}");
            int i = 1;
            foreach (var test in tests)
            {
                string tierBadges = string.Join(", ", test.Tiers.Select(t => $"Tier {t}"));
                Console.WriteLine($"  {i++:D3}. [{tierBadges}] {test.DisplayName}");
            }
        }

        private static TestSuiteSummary RunTests(List<TestInvocation> tests, CliOptions options)
        {
            var summary = new TestSuiteSummary
            {
                IsLiveMode = options.IsLive,
                TotalDiscovered = tests.Count
            };

            var overallStopwatch = Stopwatch.StartNew();

            for (int i = 0; i < tests.Count; i++)
            {
                var test = tests[i];
                var testResult = new TestExecutionResult
                {
                    ClassName = test.ClassType.Name,
                    MethodName = test.Method.Name,
                    DisplayName = test.DisplayName,
                    Tiers = test.Tiers
                };

                Console.Write($"[{i + 1:D3}/{tests.Count:D3}] {test.DisplayName} ... ");

                var sw = Stopwatch.StartNew();
                object? instance = null;

                try
                {
                    instance = Activator.CreateInstance(test.ClassType);

                    // Execute
                    var result = test.Method.Invoke(instance, test.Parameters);
                    if (result is System.Threading.Tasks.Task task)
                    {
                        task.GetAwaiter().GetResult();
                    }

                    sw.Stop();
                    testResult.Passed = true;
                    testResult.ElapsedMilliseconds = sw.ElapsedMilliseconds;
                    summary.TotalPassed++;

                    Console.WriteLine($"{ANSI_GREEN}[PASS]{ANSI_RESET} ({sw.ElapsedMilliseconds}ms)");
                }
                catch (TargetInvocationException ex)
                {
                    sw.Stop();
                    testResult.Passed = false;
                    testResult.ElapsedMilliseconds = sw.ElapsedMilliseconds;
                    var inner = ex.InnerException ?? ex;
                    testResult.ErrorMessage = inner.Message;
                    testResult.StackTrace = inner.StackTrace;
                    summary.TotalFailed++;

                    Console.WriteLine($"{ANSI_RED}[FAIL]{ANSI_RESET} ({sw.ElapsedMilliseconds}ms)");
                    Console.WriteLine($"       {ANSI_RED}{inner.GetType().Name}: {inner.Message}{ANSI_RESET}");
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    testResult.Passed = false;
                    testResult.ElapsedMilliseconds = sw.ElapsedMilliseconds;
                    testResult.ErrorMessage = ex.Message;
                    testResult.StackTrace = ex.StackTrace;
                    summary.TotalFailed++;

                    Console.WriteLine($"{ANSI_RED}[FAIL]{ANSI_RESET} ({sw.ElapsedMilliseconds}ms)");
                    Console.WriteLine($"       {ANSI_RED}{ex.GetType().Name}: {ex.Message}{ANSI_RESET}");
                }
                finally
                {
                    if (instance is IDisposable disposable)
                    {
                        try { disposable.Dispose(); } catch { }
                    }
                }

                summary.TotalExecuted++;
                summary.Results.Add(testResult);
            }

            overallStopwatch.Stop();
            summary.TotalDurationMs = overallStopwatch.ElapsedMilliseconds;

            return summary;
        }

        private static void PrintSummary(TestSuiteSummary summary)
        {
            Console.WriteLine();
            Console.WriteLine($"{ANSI_BOLD}{ANSI_CYAN}======================================================================{ANSI_RESET}");
            Console.WriteLine($"{ANSI_BOLD}{ANSI_CYAN}                      Test Execution Summary                          {ANSI_RESET}");
            Console.WriteLine($"{ANSI_BOLD}{ANSI_CYAN}======================================================================{ANSI_RESET}");
            Console.WriteLine($"Total Discovered: {summary.TotalDiscovered}");
            Console.WriteLine($"Total Executed:   {summary.TotalExecuted}");
            Console.WriteLine($"Passed:           {ANSI_GREEN}{summary.TotalPassed}{ANSI_RESET}");
            if (summary.TotalFailed > 0)
                Console.WriteLine($"Failed:           {ANSI_RED}{summary.TotalFailed}{ANSI_RESET}");
            else
                Console.WriteLine($"Failed:           0");
            Console.WriteLine($"Total Duration:   {summary.TotalDurationMs} ms");

            if (summary.TotalExecuted == 0)
            {
                Console.WriteLine();
                Console.WriteLine($"{ANSI_BOLD}{ANSI_RED}>>> NO TESTS EXECUTED (0 MATCHING TESTS) <<<{ANSI_RESET}");
            }
            else if (summary.TotalFailed == 0)
            {
                Console.WriteLine();
                Console.WriteLine($"{ANSI_BOLD}{ANSI_GREEN}>>> ALL TESTS PASSED SUCCESSFULLY! (100% PASS RATE) <<<{ANSI_RESET}");
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine($"{ANSI_BOLD}{ANSI_RED}>>> TEST SUITE EXECUTION FAILED WITH {summary.TotalFailed} FAILURES <<<{ANSI_RESET}");
            }
            Console.WriteLine($"{ANSI_BOLD}{ANSI_CYAN}======================================================================{ANSI_RESET}\n");
        }
    }
}
