namespace VolumeKeeper;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(true, @"Local\VolumeKeeper.SingleInstance", out var created);
        if (!created)
        {
            MessageBox.Show("VolumeKeeper は既に起動しています。", "VolumeKeeper",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new KeeperContext());
    }
}
