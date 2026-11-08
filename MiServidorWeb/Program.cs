using System;
using System.Threading.Tasks;

namespace MiServidorWeb
{
    class Program
    {
        static async Task Main(string[] args)
        {
            ServerConfig config = ServerConfig.Cargar("config.json");
            var servidor = new HttpServer(config);
            Console.WriteLine("Arrancando el servidor...");
            await servidor.IniciarAsync();
        }
    }
}