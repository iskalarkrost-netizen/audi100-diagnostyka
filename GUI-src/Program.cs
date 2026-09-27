using System.Text;

namespace Audi100Diag;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // kw1281test pisze na konsolę w stronie kodowej OEM (852) - potrzebny dostawca stron kodowych.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        ApplicationConfiguration.Initialize();

        // --zrzut KATALOG: zapisuje obraz każdej zakładki do PNG i kończy (podgląd/dokumentacja).
        if (args.Length == 2 && args[0] == "--zrzut")
        {
            using var form = new MainForm();
            form.SaveTabScreenshots(args[1]);
            return;
        }

        Application.Run(new MainForm());
    }
}
