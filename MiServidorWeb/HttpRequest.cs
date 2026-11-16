
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
 
namespace MiServidorWeb;
 
public class HttpRequest
{
    private static readonly TimeSpan TiempoMaximo = TimeSpan.FromSeconds(10);
    private const int MaxEncabezados = 16 * 1024;   
 
    public string Metodo { get; private set; } = "";    
    public string Destino { get; private set; } = "";  
    public string Cuerpo { get; private set; } = "";    
 
    
    public bool AceptaGzip { get; private set; }
 
    public static async Task<HttpRequest?> LeerAsync(NetworkStream stream)
    {
        using var limiteDeTiempo = new CancellationTokenSource(TiempoMaximo);
        try
        {
            return await LeerInternoAsync(stream, limiteDeTiempo.Token);
        }
        catch (OperationCanceledException)
        {
            return null;   
        }
    }
 
    private static async Task<HttpRequest?> LeerInternoAsync(NetworkStream stream, CancellationToken token)
    {
        var recibido = new List<byte>();
        var buffer = new byte[4096];
        int fin = -1;
 
        while (fin < 0)
        {
            int n = await stream.ReadAsync(buffer, token);
            if (n == 0) return null;                      
            recibido.AddRange(buffer.Take(n));
            fin = BuscarFinDeEncabezados(recibido);
            if (fin < 0 && recibido.Count > MaxEncabezados) return null; 
        }
 
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
        largo = Math.Clamp(largo, 0, 1_000_000);           
        int inicioCuerpo = fin + 4;                       
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
 
    private static int BuscarFinDeEncabezados(List<byte> datos)
    {
        for (int i = 0; i + 3 < datos.Count; i++)
        {
            if (datos[i] == 13 && datos[i + 1] == 10 && datos[i + 2] == 13 && datos[i + 3] == 10)
                return i;
        }
        return -1;
    }
 
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
 