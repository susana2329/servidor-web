
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
 
namespace MiServidorWeb;
 
// Punto 6: una solicitud HTTP ya leída y separada en partes.
//
//   POST / HTTP/1.1                  <- primera línea: MÉTODO, DESTINO y VERSIÓN
//   Host: localhost:8080             <- encabezados (uno por línea)
//   Content-Length: 20               <- cuántos bytes tiene el cuerpo
//                                    <- LÍNEA VACÍA: acá terminan los encabezados
//   nombre=juan&edad=20              <- cuerpo (solo en POST)
public class HttpRequest
{
    // Defensas: un cliente no puede tenernos esperando para siempre ni mandarnos encabezados gigantes.
    private static readonly TimeSpan TiempoMaximo = TimeSpan.FromSeconds(10);
    private const int MaxEncabezados = 16 * 1024;   // 16 KB
 
    public string Metodo { get; private set; } = "";    // "GET" o "POST"
    public string Destino { get; private set; } = "";   // "/buscar?nombre=Juan"
    public string Cuerpo { get; private set; } = "";    // vacío si no es POST
 
    // Punto 8: true si el navegador avisó con "Accept-Encoding: gzip" que sabe descomprimir GZip.
    public bool AceptaGzip { get; private set; }
 
    // Devuelve null si el cliente cerró la conexión, mandó basura o tardó más de 10 segundos.
    public static async Task<HttpRequest?> LeerAsync(NetworkStream stream)
    {
        using var limiteDeTiempo = new CancellationTokenSource(TiempoMaximo);
        try
        {
            return await LeerInternoAsync(stream, limiteDeTiempo.Token);
        }
        catch (OperationCanceledException)
        {
            return null;   // se acabó el tiempo
        }
    }
 
    private static async Task<HttpRequest?> LeerInternoAsync(NetworkStream stream, CancellationToken token)
    {
        var recibido = new List<byte>();
        var buffer = new byte[4096];
        int fin = -1;
 
        // PASO 1: leer hasta encontrar la línea vacía que termina los encabezados.
        // TCP entrega los datos en pedazos: una sola lectura puede no traer todo.
        while (fin < 0)
        {
            int n = await stream.ReadAsync(buffer, token);
            if (n == 0) return null;                       // el cliente cerró la conexión
            recibido.AddRange(buffer.Take(n));
            fin = BuscarFinDeEncabezados(recibido);
            if (fin < 0 && recibido.Count > MaxEncabezados) return null;   // encabezados demasiado grandes
        }
 
        // PASO 2: separar la primera línea y buscar los encabezados que nos interesan.
        string[] lineas = Encoding.UTF8.GetString(recibido.ToArray(), 0, fin).Split("\r\n");
        string[] partes = lineas[0].Split(' ');
        if (partes.Length < 3) return null;
 
        int largo = 0;
        bool aceptaGzip = false;
        foreach (string linea in lineas.Skip(1))
        {
            if (linea.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                int.TryParse(linea.Substring("Content-Length:".Length), out largo);
            else if (linea.StartsWith("Accept-Encoding:", StringComparison.OrdinalIgnoreCase))
                aceptaGzip = ContieneGzip(linea.Substring("Accept-Encoding:".Length));
        }
        largo = Math.Clamp(largo, 0, 1_000_000);           // máximo 1 MB
 
        // PASO 3: leer el cuerpo. Parte pudo llegar junto con los encabezados; se lee lo que falta.
        int inicioCuerpo = fin + 4;                        // 4 = largo de "\r\n\r\n"
        while (recibido.Count - inicioCuerpo < largo)
        {
            int n = await stream.ReadAsync(buffer, token);
            if (n == 0) break;
            recibido.AddRange(buffer.Take(n));
        }
 
        int disponibles = Math.Min(recibido.Count - inicioCuerpo, largo);
        string cuerpo = Encoding.UTF8.GetString(recibido.ToArray(), inicioCuerpo, disponibles);
 
        return new HttpRequest { Metodo = partes[0], Destino = partes[1], Cuerpo = cuerpo, AceptaGzip = aceptaGzip };
    }
 
    // Posición donde empieza "\r\n\r\n" (los bytes 13, 10, 13, 10), o -1 si todavía no llegó.
    private static int BuscarFinDeEncabezados(List<byte> datos)
    {
        for (int i = 0; i + 3 < datos.Count; i++)
        {
            if (datos[i] == 13 && datos[i + 1] == 10 && datos[i + 2] == 13 && datos[i + 3] == 10)
                return i;
        }
        return -1;
    }
 
    // Interpreta valores como "gzip, deflate, br" o "gzip;q=0" (q=0 significa "no quiero gzip").
    private static bool ContieneGzip(string valor)
    {
        foreach (string opcion in valor.Split(','))
        {
            string[] partes = opcion.Split(';');
            if (!partes[0].Trim().Equals("gzip", StringComparison.OrdinalIgnoreCase)) continue;
 
            foreach (string parametro in partes.Skip(1))
            {
                string p = parametro.Trim();
                if (p.StartsWith("q=", StringComparison.OrdinalIgnoreCase) &&
                    double.TryParse(p.Substring(2), NumberStyles.Float, CultureInfo.InvariantCulture, out double q) &&
                    q == 0)
                    return false;
            }
            return true;
        }
        return false;
    }
}
 