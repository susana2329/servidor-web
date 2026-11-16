
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
 
namespace MiServidorWeb;
 
public class HttpServer
{
    private readonly ServerConfig _config;
    private readonly TcpListener _listener;
 
    private readonly SemaphoreSlim _limite;
 
    private readonly ConcurrentDictionary<Task, byte> _conexionesActivas = new();
 
    public HttpServer(ServerConfig config)
    {
        _config = config;
        _listener = new TcpListener(IPAddress.Any, _config.Port);
        _limite = new SemaphoreSlim(_config.MaxConexiones);
    }
 
    public async Task IniciarAsync(CancellationToken cancelacion)
    {
        _listener.Start();
        Console.WriteLine($"Escuchando en http://localhost:{_config.Port}");
        Console.WriteLine($"Carpeta Raíz configurada en: {_config.WebRoot}");
 
        try
        {
            while (!cancelacion.IsCancellationRequested)
            {
                await _limite.WaitAsync(cancelacion);  
                try
                {
                    TcpClient cliente = await _listener.AcceptTcpClientAsync(cancelacion);
 
                    Task tarea = Task.Run(() => ProcesarClienteAsync(cliente));
                    _conexionesActivas.TryAdd(tarea, 0);
                    _ = tarea.ContinueWith(t =>
                    {
                        _conexionesActivas.TryRemove(t, out _);
                        _limite.Release();           
                    });
                }
                catch (OperationCanceledException)
                {
                    _limite.Release();
                    break;
                }
                catch (Exception ex)
                {
                    _limite.Release();
                    Console.WriteLine($"[ERROR]: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            
        }
        finally
        {
            _listener.Stop();   
        }
 
        
        Console.WriteLine("Cerrando: esperando que terminen las conexiones en curso...");
        await Task.WhenAny(Task.WhenAll(_conexionesActivas.Keys), Task.Delay(TimeSpan.FromSeconds(5)));
        Console.WriteLine("Servidor detenido.");
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
                        "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("405 - Solo se aceptan GET y POST"),
                        solicitud.AceptaGzip, "Allow: GET, POST");
                    return;
                }
 
                string ruta = destino;
                string query = "";
                if (destino.Contains('?'))
                {
                    string[] partesUrl = destino.Split('?', 2);
                    ruta = partesUrl[0];
                    query = partesUrl[1];
                    MostrarParametros("[Query] Parámetros recibidos:", query);
                }
 
                if (ruta == "/") ruta = "/index.html";
 
                string datos = FormatearParametros(metodo == "POST" ? solicitud.Cuerpo : query);
 
                Logger.Registrar(ipCliente, metodo, destino, datos);
 
                if (metodo == "POST")
                {
                    MostrarParametros("[POST] Datos recibidos:", solicitud.Cuerpo);
 
                    var campos = ParsearParametros(solicitud.Cuerpo);
                    string lista = campos.Count == 0
                        ? "<p>No se recibieron datos.</p>"
                        : "<ul>" + string.Concat(campos.Select(campo =>
                            $"<li><strong>{WebUtility.HtmlEncode(campo.Clave)}:</strong> {WebUtility.HtmlEncode(campo.Valor)}</li>")) + "</ul>";
 
                    string html = "<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\">" +
                        "<title>Datos recibidos</title><link rel=\"stylesheet\" href=\"/styles.css\"></head><body>" +
                        "<main><h1>Datos recibidos</h1><p>El servidor registró en el log:</p>" + lista +
                        "<p><a class=\"btn\" href=\"/\">Volver al inicio</a></p></main></body></html>";
 
                    await HttpResponse.EnviarAsync(stream, 200, "OK", "text/html; charset=utf-8",
                        Encoding.UTF8.GetBytes(html), solicitud.AceptaGzip);
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
                        HttpResponse.ObtenerTipoContenido(rutaFisica), contenido, solicitud.AceptaGzip);
                }
                else
                {
                    await HttpResponse.EnviarNoEncontradoAsync(stream, raiz, solicitud.AceptaGzip);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR cliente]: {ex.Message}");
        }
    }
 
    private static string Decodificar(string texto) =>
        Uri.UnescapeDataString(texto.Replace('+', ' ')).Replace("\r", " ").Replace("\n", " ");
 
    private static List<(string Clave, string Valor)> ParsearParametros(string texto)
    {
        var lista = new List<(string Clave, string Valor)>();
        foreach (string par in texto.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] kv = par.Split('=', 2);
            lista.Add((Decodificar(kv[0]), kv.Length > 1 ? Decodificar(kv[1]) : ""));
        }
        return lista;
    }
 
    private static string FormatearParametros(string texto) =>
        string.Join(" | ", ParsearParametros(texto).Select(p => $"{p.Clave}={p.Valor}"));
 

    private static void MostrarParametros(string titulo, string texto)
    {
        Console.WriteLine(titulo);
        var campos = ParsearParametros(texto);
        if (campos.Count == 0)
        {
            Console.WriteLine("  (sin datos)");
            return;
        }
        foreach (var (clave, valor) in campos)
        {
            Console.WriteLine($"  -> {clave}: {valor}");
        }
    }
}