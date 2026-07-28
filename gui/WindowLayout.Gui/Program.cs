namespace WindowLayout.Gui;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Do not call SetCurrentProcessExplicitAppUserModelID — that makes
        // AppsFolder use an AUMID identity and drops "Pin to Start".
        if (args.Any(a =>
                string.Equals(a, "--progress", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "-progress", StringComparison.OrdinalIgnoreCase)))
        {
            return ProgressForm.Run(args);
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
