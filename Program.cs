namespace NicPad;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var mainForm = new MainForm();

        // If files were passed as arguments, open them
        if (args.Length > 0)
        {
            foreach (var file in args)
            {
                try
                {
                    // Resolve to full path and validate
                    var fullPath = Path.GetFullPath(file);
                    if (File.Exists(fullPath))
                    {
                        mainForm.OpenFile(fullPath);
                    }
                }
                catch (Exception)
                {
                    // Skip invalid paths silently
                }
            }
        }

        Application.Run(mainForm);
    }
}
