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
                    // --- Punto 6: lee la solicitud COMPLETA (primera línea, encabezados y, si hay, el cuerpo) ---
                    HttpRequest? solicitud = await HttpRequest.LeerAsync(stream);
                    if (solicitud == null) return;   // conexión vacía, mal formada o demasiado lenta

                    string metodo = solicitud.Metodo;
                    string destino = solicitud.Destino;
                    Console.WriteLine($"{metodo} {destino}");

                    string ipCliente = ((IPEndPoint)cliente.Client.RemoteEndPoint)?.Address.ToString() ?? "Desconocida";
                    string raiz = Path.GetFullPath(_config.WebRoot);

                    // --- Punto 6: solo se aceptan GET y POST ---
                    if (metodo != "GET" && metodo != "POST")
                    {
                        RegistrarLog(ipCliente, metodo, destino);
                        await HttpResponse.EnviarHtmlAsync(stream, 405, "Method Not Allowed",
                            "<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body>" +
                            "<h1>405 - M&eacute;todo no permitido</h1><p>Este servidor solo acepta GET y POST.</p></body></html>",
                            "Allow: GET, POST\r\n");
                        return;
                    }

                    if (solicitud.CuerpoDemasiadoGrande)
                    {
                        RegistrarLog(ipCliente, metodo, destino);
                        await HttpResponse.EnviarHtmlAsync(stream, 413, "Payload Too Large",
                            "<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body>" +
                            "<h1>413 - Cuerpo demasiado grande</h1></body></html>");
                        return;
                    }

                    // Parámetros de consulta (?clave=valor), sin el querystring ("/buscar?x=1" -> "/buscar")
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

                    // --- Punto 6: POST. Los datos recibidos solo se registran en el log ---
                    if (metodo == "POST")
                    {
                        string datos = solicitud.ResumenDelCuerpo();
                        RegistrarLog(ipCliente, metodo, destino, datos);

                        // La página de respuesta usa los mismos estilos (styles.css) que el sitio de Nicolas.
                        await HttpResponse.EnviarHtmlAsync(stream, 200, "OK",
                            "<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\">" +
                            "<title>Datos recibidos</title><link rel=\"stylesheet\" href=\"/styles.css\"></head><body>" +
                            "<header class=\"top\"><span class=\"brand\">MiServidorWeb</span></header>" +
                            "<main><h1>Datos recibidos</h1>" +
                            "<p>El servidor registr&oacute; en el log: <code>" + WebUtility.HtmlEncode(datos) + "</code></p>" +
                            "<p><a class=\"btn\" href=\"/\">Volver al inicio</a></p></main></body></html>");
                        return;
                    }

                    // --- (Requisito 9): Registrar la solicitud en el log diario con IP ---
                    RegistrarLog(ipCliente, metodo, destino);

                    // --- Punto 2: de la ruta al archivo ("/" y las carpetas equivalen a index.html) ---
                    string? rutaFisica = ResolutorDeRutas.Resolver(ruta, raiz);

                    // Punto 5: existe, 200; no existe, 404
                    if (rutaFisica != null && File.Exists(rutaFisica))
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

            // Solo en un POST: se agregan los datos recibidos en el cuerpo
            if (datos != "")
            {
                registro += $" | DATOS: {datos}";
            }
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