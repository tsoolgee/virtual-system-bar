using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace VirtualSystemBar
{
    static class Program
    {
        public const string Title = "Virtual System Bar";
        public static string ExePath => Process.GetCurrentProcess().MainModule.FileName;

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--guard" && int.TryParse(args[1], out int pid))
                return Guard.Run(pid);

            using (var mutex = new Mutex(true, @"Local\VirtualSystemBar.SingleInstance", out bool first))
            {
                if (!first) return 0;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                // an error must never leave the kiosk without its bar: exit non-zero and the guard restarts us
                Application.ThreadException += (s, e) => Crash(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash(e.ExceptionObject as Exception);
                Application.Run(new BarForm(ConfigStore.Load()));
                return 0;
            }
        }

        static void Crash(Exception ex)
        {
            try { Trace.WriteLine(ex); } catch { }
            Environment.Exit(2);
        }
    }

    /// <summary>
    /// A second copy of the EXE that waits for the bar process. If the bar dies any way other than the
    /// admin's "exit" (exit code 0) it starts it again. The bar in turn restarts the guard if it is killed.
    /// </summary>
    static class Guard
    {
        public static int Run(int pid)
        {
            Process p;
            try { p = Process.GetProcessById(pid); } catch { return 0; }
            p.WaitForExit();
            int code;
            try { code = p.ExitCode; } catch { code = 1; }
            if (code == 0) return 0;
            Thread.Sleep(1500);
            try { Process.Start(new ProcessStartInfo(Program.ExePath) { UseShellExecute = false }); } catch { }
            return 0;
        }

        static Process _guard;

        public static void Ensure(bool wanted)
        {
            try
            {
                bool alive = _guard != null && !_guard.HasExited;
                if (wanted && !alive)
                    _guard = Process.Start(new ProcessStartInfo(Program.ExePath, "--guard " + Process.GetCurrentProcess().Id)
                    { UseShellExecute = false, CreateNoWindow = true });
                else if (!wanted && alive)
                {
                    _guard.Kill();
                    _guard = null;
                }
            }
            catch { }
        }
    }
}
