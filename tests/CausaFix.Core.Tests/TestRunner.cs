using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace CausaFix.Core.Tests
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestAttribute : Attribute { }

    public sealed class AssertException : Exception
    {
        public AssertException(string m) : base(m) { }
    }

    public static class Assert
    {
        public static void True(bool cond, string msg = "Expected true")
        {
            if (!cond) throw new AssertException(msg);
        }

        public static void False(bool cond, string msg = "Expected false") => True(!cond, msg);

        public static void Equal<T>(T expected, T actual, string msg = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new AssertException((msg != null ? msg + ": " : "") + "expected <" + expected + "> but was <" + actual + ">");
        }

        public static void Near(double expected, double actual, double tol, string msg = null)
        {
            if (Math.Abs(expected - actual) > tol)
                throw new AssertException((msg != null ? msg + ": " : "") + "expected " + expected.ToString("R") + " ± " + tol + " but was " + actual.ToString("R"));
        }

        public static void Contains(string needle, string hay, string msg = null)
        {
            if (hay == null || hay.IndexOf(needle, StringComparison.Ordinal) < 0)
                throw new AssertException((msg != null ? msg + ": " : "") + "'" + needle + "' not found in '" + hay + "'");
        }

        public static T Throws<T>(Action a) where T : Exception
        {
            try { a(); }
            catch (T ex) { return ex; }
            catch (Exception ex) { throw new AssertException("Expected " + typeof(T).Name + " but got " + ex.GetType().Name + ": " + ex.Message); }
            throw new AssertException("Expected " + typeof(T).Name + " but nothing was thrown");
        }
    }

    public static class Program
    {
        public static int Main(string[] args)
        {
            // Utilities for validating files with other spreadsheet applications:
            //   --sample <file.xlsx>   writes the Lighting Fixtures sample workbook
            //   --check  <file.xlsx>   imports it against the unchanged sample model and prints the result
            if (args.Length == 2 && args[0] == "--sample")
            {
                Workbook.WorkbookExporter.Save(TestData.LightingPackage(), args[1]);
                Console.WriteLine("Sample written: " + args[1]);
                return 0;
            }
            if (args.Length == 2 && args[0] == "--check")
            {
                var model = FakeModel.From(TestData.LightingPackage());
                var set = TestData.Import(Xlsx.XlsxReader.Load(args[1]), model);
                Console.WriteLine("Entries: " + set.Entries.Count + ", unchanged cells: " + set.UnchangedCells + ", empty ignored: " + set.EmptyCellsIgnored);
                foreach (var e in set.Entries) Console.WriteLine("  " + e.Status + " " + e.Column.Header + " @ " + e.SourceSummary + " : " + e.Original + " -> " + e.Proposed + " " + e.Reason);
                foreach (var i in set.Issues) Console.WriteLine("  " + i);
                return set.Entries.Count == 0 ? 0 : 2;
            }

            string filter = args.Length > 0 ? args[0] : null;
            var tests = typeof(Program).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
                .Where(m => filter == null || (m.DeclaringType.Name + "." + m.Name).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(m => m.DeclaringType.Name).ThenBy(m => m.Name)
                .ToList();

            int passed = 0, failed = 0;
            var total = Stopwatch.StartNew();
            foreach (var m in tests)
            {
                string name = m.DeclaringType.Name + "." + m.Name;
                var sw = Stopwatch.StartNew();
                try
                {
                    m.Invoke(null, null);
                    passed++;
                    Console.WriteLine("  PASS  " + name + "  (" + sw.ElapsedMilliseconds + " ms)");
                }
                catch (TargetInvocationException tie)
                {
                    failed++;
                    var ex = tie.InnerException;
                    Console.WriteLine("  FAIL  " + name + "\n        " + ex.GetType().Name + ": " + ex.Message);
                    if (!(ex is AssertException)) Console.WriteLine(ex.StackTrace);
                }
            }
            Console.WriteLine();
            Console.WriteLine("Runtime: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            Console.WriteLine(passed + " passed, " + failed + " failed, " + tests.Count + " total in " + total.ElapsedMilliseconds + " ms");
            return failed == 0 ? 0 : 1;
        }
    }
}
