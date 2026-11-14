using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
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
    public string Metodo { get; private set; } = "";    // "GET" o "POST"
    public string Destino { get; private set; } = "";   // "/buscar?nombre=Juan"
    public string Cuerpo { get; private set; } = "";    // vacío si no es POST

    public static async Task<HttpRequest?> LeerAsync(NetworkStream stream)
    {
        var recibido = new List<byte>();
        var buffer = new byte[4096];
        int fin = -1;

        // PASO 1: leer hasta encontrar la línea vacía que termina los encabezados.
        // TCP entrega los datos en pedazos: una sola lectura puede no traer todo.
        while (fin < 0)
        {
            int n = await stream.ReadAsync(buffer);
            if (n == 0) return null;                       // el cliente cerró la conexión
            recibido.AddRange(buffer.Take(n));
            fin = BuscarFinDeEncabezados(recibido);
        }

        // PASO 2: separar la primera línea y buscar "Content-Length".
        string[] lineas = Encoding.UTF8.GetString(recibido.ToArray(), 0, fin).Split("\r\n");
        string[] partes = lineas[0].Split(' ');
        if (partes.Length < 3) return null;

        int largo = 0;
        foreach (string linea in lineas.Skip(1))
        {
            if (linea.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                int.TryParse(linea.Substring("Content-Length:".Length), out largo);
        }
        largo = Math.Clamp(largo, 0, 1_000_000);           // máximo 1 MB

        // PASO 3: leer el cuerpo. Parte pudo llegar junto con los encabezados; se lee lo que falta.
        int inicioCuerpo = fin + 4;                        // 4 = largo de "\r\n\r\n"
        while (recibido.Count - inicioCuerpo < largo)
        {
            int n = await stream.ReadAsync(buffer);
            if (n == 0) break;
            recibido.AddRange(buffer.Take(n));
        }

        int disponibles = Math.Min(recibido.Count - inicioCuerpo, largo);
        string cuerpo = Encoding.UTF8.GetString(recibido.ToArray(), inicioCuerpo, disponibles);

        return new HttpRequest { Metodo = partes[0], Destino = partes[1], Cuerpo = cuerpo };
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
}