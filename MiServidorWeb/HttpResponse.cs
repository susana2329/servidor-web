
using System.IO;
using System.IO.Compression;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
 
namespace MiServidorWeb;
 
public static class HttpResponse
{
    private const int MinimoParaComprimir = 1024;
 
    public static async Task EnviarAsync(NetworkStream stream, int codigo, string motivo,
                                         string tipoContenido, byte[] cuerpo,
                                         bool aceptaGzip = false, string? encabezadoExtra = null)
    {
        bool comprimible = EsComprimible(tipoContenido);
        bool comprimir = aceptaGzip && comprimible && cuerpo.Length > MinimoParaComprimir;
        byte[] datosAEnviar = comprimir ? ComprimirDatos(cuerpo) : cuerpo;
 
        string encabezado = $"HTTP/1.1 {codigo} {motivo}\r\n" +
                            $"Content-Type: {tipoContenido}\r\n" +
                            $"Content-Length: {datosAEnviar.Length}\r\n";
 
        if (comprimir) encabezado += "Content-Encoding: gzip\r\n";
 
      
        if (comprimible) encabezado += "Vary: Accept-Encoding\r\n";
 
        if (encabezadoExtra != null) encabezado += encabezadoExtra + "\r\n";
 
        encabezado += "Connection: close\r\n\r\n";
 
        await stream.WriteAsync(Encoding.ASCII.GetBytes(encabezado));
        await stream.WriteAsync(datosAEnviar);
    }
 
    public static async Task EnviarNoEncontradoAsync(NetworkStream stream, string raiz, bool aceptaGzip = false)
    {
        string pagina = Path.Combine(raiz, "404.html");
        byte[] cuerpo = File.Exists(pagina)
            ? await File.ReadAllBytesAsync(pagina)
            : Encoding.UTF8.GetBytes("404 Not Found");
 
        await EnviarAsync(stream, 404, "Not Found", "text/html; charset=utf-8", cuerpo, aceptaGzip);
    }
 
    public static string ObtenerTipoContenido(string archivo) =>
        Path.GetExtension(archivo).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".txt" => "text/plain; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };
 
    private static bool EsComprimible(string tipoContenido) =>
        tipoContenido.StartsWith("text/") ||
        tipoContenido.Contains("json") ||
        tipoContenido.Contains("xml") ||
        tipoContenido.Contains("javascript");
 
    private static byte[] ComprimirDatos(byte[] datosOriginales)
    {
        using var memoryStream = new MemoryStream();
        using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress))
        {
            gzipStream.Write(datosOriginales, 0, datosOriginales.Length);
        }
        return memoryStream.ToArray();
    }
}
 