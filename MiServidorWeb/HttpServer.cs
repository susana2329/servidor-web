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
                    // Punto 6: lee la solicitud COMPLETA (encabezados y, en un POST, el cuerpo)
                    HttpRequest? solicitud = await HttpRequest.LeerAsync(stream);
                    if (solicitud == null) return;   // conexión vacía del navegador

                    string metodo = solicitud.Metodo;
                    string destino = solicitud.Destino;
                    Console.WriteLine($"{metodo} {destino}");

                    string ipCliente = ((IPEndPoint)cliente.Client.RemoteEndPoint)?.Address.ToString() ?? "Desconocida";

                    // Punto 6: solo se aceptan GET y POST
                    if (metodo != "GET" && metodo != "POST")
                    {
                        RegistrarLog(ipCliente, metodo, destino);
                        await HttpResponse.EnviarAsync(stream, 405, "Method Not Allowed",
                            "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("405 - Solo se aceptan GET y POST"));
                        return;
                    }

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

                    // Punto 2: si no pide ningún archivo ("/"), se entrega index.html
                    if (ruta == "/") ruta = "/index.html";

                    // Punto 6: los datos del cuerpo (solo hay en un POST) se decodifican para el log
                    string datos = Uri.UnescapeDataString(solicitud.Cuerpo.Replace('+', ' '))
                                      .Replace("\r", " ").Replace("\n", " ");

                    // --- (Requisito 9): Registrar la solicitud en el log diario con IP ---
                    RegistrarLog(ipCliente, metodo, destino, datos);

                    // Punto 6: un POST solo se registra en el log; se responde una página simple
                    if (metodo == "POST")
                    {
                        string html = "<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\">" +
                            "<title>Datos recibidos</title><link rel=\"stylesheet\" href=\"/styles.css\"></head><body>" +
                            "<main><h1>Datos recibidos</h1><p>El servidor registr&oacute; en el log: <code>" +
                            WebUtility.HtmlEncode(datos) + "</code></p>" +
                            "<p><a class=\"btn\" href=\"/\">Volver al inicio</a></p></main></body></html>";

                        await HttpResponse.EnviarAsync(stream, 200, "OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
                        return;
                    }

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
        private void RegistrarLog(string ip, string metodo, string ruta, string datos = "")
        {
            string nombreArchivoLog = $"log_{DateTime.Now:yyyy-MM-dd}.txt";
            string registro = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | IP: {ip} | {metodo} {ruta}";

            // Solo en un POST: se agregan los datos recibidos
            if (datos != "") registro += $" | DATOS: {datos}";
            registro += "\n";

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