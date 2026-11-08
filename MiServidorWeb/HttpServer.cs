using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace MiServidorWeb
{
    public class HttpServer
    {
        private readonly ServerConfig _config;
        private readonly TcpListener _listener;
        private bool _estaEjecutando;

        public HttpServer(ServerConfig config)
        {
            _config = config;
            _listener = new TcpListener(IPAddress.Any, _config.Port);
        }

        public async Task IniciarAsync()
        {
            _listener.Start();
            _estaEjecutando = true;
            Console.WriteLine($"Carpeta Raíz configurada en: {_config.WebRoot}");

            while (_estaEjecutando)
            {
                try
                {
                    TcpClient cliente = await _listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => ProcesarClienteAsync(cliente));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR]: {ex.Message}");
                }
            }
        }

        private async Task ProcesarClienteAsync(TcpClient cliente)
        {
            using (cliente)
            {
                using NetworkStream stream = cliente.GetStream();
                string rutaRelativaSolicitada = "/estilos.css"; 
                string rutaFisica = Path.Combine(_config.WebRoot, rutaRelativaSolicitada.TrimStart('/', '\\'));
                
                Console.WriteLine($"Buscando archivo en: {rutaFisica}");
                
            }
        }
    }
}