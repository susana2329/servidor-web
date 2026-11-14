using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace MiServidorWeb;

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
                HttpRequest? solicitud = await HttpRequest.LeerAsync(stream);
                if (solicitud == null) return;

                string metodo = solicitud.Metodo;
                string destino = solicitud.Destino;
                Console.WriteLine($"{metodo} {destino}");

                string ipCliente = ((IPEndPoint?)cliente.Client.RemoteEndPoint)?.Address.ToString() ?? "Desconocida";

                if (metodo != "GET" && metodo != "POST")
                {
                    Logger.Registrar(ipCliente, metodo, destino);
                    await HttpResponse.EnviarAsync(stream, 405, "Method Not Allowed",
                        "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("405 - Solo se aceptan GET y POST"));
                    return;
                }

                string ruta = destino;
                if (destino.Contains('?'))
                {
                    string[] partesUrl = destino.Split('?');
                    ruta = partesUrl[0];
                    ManejarQueryString(partesUrl[1]);
                }

                if (ruta == "/") ruta = "/index.html";

                string datos = Uri.UnescapeDataString(solicitud.Cuerpo.Replace('+', ' '))
                                  .Replace("\r", " ").Replace("\n", " ");

                Logger.Registrar(ipCliente, metodo, destino, datos);

                if (metodo == "POST")
                {
                    string html = "<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\">" +
                        "<title>Datos recibidos</title><link rel=\"stylesheet\" href=\"/styles.css\"></head><body>" +
                        "<main><h1>Datos recibidos</h1><p>El servidor registró en el log: <code>" +
                        WebUtility.HtmlEncode(datos) + "</code></p>" +
                        "<p><a class=\"btn\" href=\"/\">Volver al inicio</a></p></main></body></html>";

                    await HttpResponse.EnviarAsync(stream, 200, "OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
                    return;
                }

                string raiz = Path.GetFullPath(_config.WebRoot);
                string relativa = Uri.UnescapeDataString(ruta).TrimStart('/', '\\');
                string rutaFisica = Path.GetFullPath(Path.Combine(raiz, relativa));
                bool dentroDeRaiz = rutaFisica.StartsWith(raiz + Path.DirectorySeparatorChar);

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
}