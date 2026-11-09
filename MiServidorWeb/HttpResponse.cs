using System.Net.Sockets;
using System.Text;

namespace MiServidorWeb;

public static class HttpResponse
{
    public static async Task EnviarAsync(NetworkStream stream, int codigo, string motivo,
                                         string tipoContenido, byte[] cuerpo, bool esGzip = false)
    {
        string encabezado = $"HTTP/1.1 {codigo} {motivo}\r\n" +
                            $"Content-Type: {tipoContenido}\r\n" +
                            $"Content-Length: {cuerpo.Length}\r\n";

        if (esGzip)
        {
            encabezado += "Content-Encoding: gzip\r\n";
        }

        encabezado += "Connection: close\r\n\r\n";

        await stream.WriteAsync(Encoding.ASCII.GetBytes(encabezado));
        await stream.WriteAsync(cuerpo);
    }

    public static async Task EnviarNoEncontradoAsync(NetworkStream stream, string raiz)
    {
        string pagina = Path.Combine(raiz, "404.html");
        byte[] cuerpo = File.Exists(pagina)
            ? await File.ReadAllBytesAsync(pagina)
            : Encoding.UTF8.GetBytes("404 Not Found");

        await EnviarAsync(stream, 404, "Not Found", "text/html; charset=utf-8", cuerpo);
    }

    public static string ObtenerTipoContenido(string archivo) =>
        Path.GetExtension(archivo).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css"  => "text/css; charset=utf-8",
            ".js"   => "text/javascript; charset=utf-8",
            ".png"  => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".ico"  => "image/x-icon",
            _       => "application/octet-stream"
        };
}