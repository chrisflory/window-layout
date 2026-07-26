namespace WindowLayout.Gui;

static class Program
{
    [STAThread]
    static void Main()
    {
        // Do not call SetCurrentProcessExplicitAppUserModelID — that makes
        // AppsFolder use an AUMID identity and drops "Pin to Start".
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
