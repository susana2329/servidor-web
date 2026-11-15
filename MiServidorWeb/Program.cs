
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
 
namespace MiServidorWeb
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            ServerConfig config;
            try
            {
                config = ServerConfig.Cargar("config.json");
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"[CONFIG] {ex.Message}");
                return 1;
            }
 
            // Ctrl+C no mata el programa de golpe: avisa al servidor para que cierre ordenadamente.
            using var cancelacion = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cancelacion.Cancel();
            };
 
            try
            {
                var servidor = new HttpServer(config);
                Console.WriteLine("Arrancando el servidor... (Ctrl+C para detenerlo)");
                await servidor.IniciarAsync(cancelacion.Token);
                return 0;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                Console.Error.WriteLine($"[ERROR] El puerto {config.Port} ya está en uso. Cerrá el otro programa o cambiá \"Port\" en config.json.");
                return 1;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied)
            {
                Console.Error.WriteLine($"[ERROR] Sin permiso para abrir el puerto {config.Port}. Usá un puerto mayor a 1024 en config.json.");
                return 1;
            }
        }
    }
}
 