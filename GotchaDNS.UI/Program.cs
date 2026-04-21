using Photino.NET;
using System;
using System.IO; // Certifica-te de que esta linha existe para o Path funcionar

namespace GotchaDNS.UI;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            // 1. ESTA É A PARTE QUE ALTERAS:
            // Obtém a pasta onde o .exe está (ex: bin/Debug/net8.0/)
            string basePath = AppContext.BaseDirectory;

            // Monta o caminho completo até ao index.html
            string indexPath = Path.Combine(basePath, "frontend", "dist", "index.html");

            var window = new PhotinoWindow()
                .SetTitle("GotchaDNS Dashboard")
                .SetUseOsDefaultSize(false)
                .SetSize(900, 600)
                .Load(indexPath); // 2. USA A VARIÁVEL AQUI

            window.WaitForClose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro: {ex.Message}");
            Console.ReadLine();
        }
    }
}