using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
            try
            {
                using (cliente)
                using (NetworkStream stream = cliente.GetStream())
                {
                    byte[] buffer = new byte[8192];
                    int n = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (n == 0) return;   // conexión vacía del navegador

                    // Primera línea: "GET /index.html HTTP/1.1"
                    string solicitud = Encoding.UTF8.GetString(buffer, 0, n);
                    string[] partes = solicitud.Split("\r\n")[0].Split(' ');
                    if (partes.Length < 3) return;

                    string metodo = partes[0];
                    string destino = partes[1];
                    Console.WriteLine($"{metodo} {destino}");

                    // De la ruta al archivo (sin querystring; "/" equivale a index.html)
                    string ruta = destino.Split('?')[0];
                    if (ruta == "/") ruta = "/index.html";

                    string raiz = Path.GetFullPath(_config.WebRoot);
                    string relativa = Uri.UnescapeDataString(ruta).TrimStart('/', '\\');
                    string rutaFisica = Path.GetFullPath(Path.Combine(raiz, relativa));
                    bool dentroDeRaiz = rutaFisica.StartsWith(raiz + Path.DirectorySeparatorChar);

                    // Punto 5: existe, 200; no existe, 404
                    if (dentroDeRaiz && File.Exists(rutaFisica))
                    {
                        byte[] contenido = await File.ReadAllBytesAsync(rutaFisica);
                        await HttpResponse.EnviarAsync(stream, 200, "OK",
                            HttpResponse.ObtenerTipoContenido(rutaFisica), contenido);
                    }
                    else
                    {
                        await HttpResponse.EnviarNoEncontradoAsync(stream, raiz);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR cliente]: {ex.Message}");
            }
        }
    }
}