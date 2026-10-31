namespace OpenHwid.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(true, @"Local\OpenHwid.Desktop", out var first);
        if (!first)
        {
            // During elevation, the launching process still owns the mutex until its window closes.
            bool acquired;
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) { MessageBox.Show("OpenHwid jest już uruchomiony.", "OpenHwid"); return; }
        }
        var smoke = args.Length == 2 && args[0] == "--smoke-test" ? Path.GetFullPath(args[1]) : null;
        try { Application.Run(new MainForm(smoke)); }
        finally { mutex.ReleaseMutex(); }
    }
}
