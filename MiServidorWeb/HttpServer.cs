using System;
using System.IO;
using System.IO.Compression;
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

        private static readonly object _lockObjeto = new object();

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

                    string ipCliente = ((IPEndPoint)cliente.Client.RemoteEndPoint)?.Address.ToString() ?? "Desconocida";

                    // De la ruta al archivo (sin querystring; "/" equivale a index.html)
                    string ruta = destino;
                    string queryString = "";
                    if (destino.Contains("?"))
                    {
                        string[] partesUrl = destino.Split('?');
                        ruta = partesUrl[0];
                        queryString = partesUrl[1];

                        // Opcional: Procesar o loguear los parámetros de consulta individualmente
                        ManejarQueryString(queryString);
                    }

                    if (ruta == "/") ruta = "/index.html";

                    // --- (Requisito 9): Registrar la solicitud en el log diario con IP ---
                    RegistrarLog(ipCliente, metodo, destino);

                    string raiz = Path.GetFullPath(_config.WebRoot);
                    string relativa = Uri.UnescapeDataString(ruta).TrimStart('/', '\\');
                    string rutaFisica = Path.GetFullPath(Path.Combine(raiz, relativa));
                    bool dentroDeRaiz = rutaFisica.StartsWith(raiz + Path.DirectorySeparatorChar);

                    // Punto 5: existe, 200; no existe, 404
                    if (dentroDeRaiz && File.Exists(rutaFisica))
                    {
                        byte[] contenido = await File.ReadAllBytesAsync(rutaFisica);

                        contenido = ComprimirDatos(contenido);

                        await HttpResponse.EnviarAsync(stream, 200, "OK",
                            HttpResponse.ObtenerTipoContenido(rutaFisica), contenido, true);
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
        private void ManejarQueryString(string queryString)
        {
            Console.WriteLine($"[Query] Parámetros detectados: {queryString}");
            string[] parametros = queryString.Split('&');
            foreach (var param in parametros)
            {
                string[] kv = param.Split('=');
                if (kv.Length == 2)
                {
                    Console.WriteLine($"  -> Clave: {kv[0]}, Valor: {kv[1]}");
                }
            }
        }

        // --- REQUISITO 9: Log diario con exclusión mutua (lock) ---
        private void RegistrarLog(string ip, string metodo, string ruta)
        {
            string nombreArchivoLog = $"log_{DateTime.Now:yyyy-MM-dd}.txt";
            string registro = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | IP: {ip} | {metodo} {ruta}\n";

            lock (_lockObjeto)
            {
                File.AppendAllText(nombreArchivoLog, registro);
            }
        }

        // --- REQUISITO 8: Compresión GZip (Patrón Decorador) ---
        private byte[] ComprimirDatos(byte[] datosOriginales)
        {
            using (var memoryStream = new MemoryStream())
            {
                using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress))
                {
                    gzipStream.Write(datosOriginales, 0, datosOriginales.Length);
                }
                return memoryStream.ToArray();
            }
        }
    }
}