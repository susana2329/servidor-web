using System.Net.Sockets;
using System.Text;

namespace MiServidorWeb;

// Una solicitud HTTP ya leída y separada en partes.
//
// El navegador manda un texto con esta forma:
//
//   POST /enviar HTTP/1.1            <- primera línea: MÉTODO, DESTINO y VERSIÓN
//   Host: localhost:8080             <- encabezados (uno por línea)
//   Content-Length: 20               <- cuántos bytes tiene el cuerpo
//                                    <- LÍNEA VACÍA: acá terminan los encabezados
//   nombre=Pedro&edad=20             <- cuerpo (solo en POST)
public class HttpRequest
{
    private const int TamanioMaximoEncabezados = 16 * 1024;   // 16 KB
    public const int TamanioMaximoCuerpo = 1024 * 1024;       // 1 MB
    private static readonly TimeSpan TiempoMaximoDeLectura = TimeSpan.FromSeconds(10);

    public string Metodo { get; private set; } = "";      // "GET" o "POST"
    public string Destino { get; private set; } = "";     // "/buscar?nombre=Juan"
    public string Version { get; private set; } = "";     // "HTTP/1.1"

    // Los nombres de los encabezados no distinguen mayúsculas ("content-length" = "Content-Length").
    public Dictionary<string, string> Encabezados { get; } = new(StringComparer.OrdinalIgnoreCase);

    public byte[] Cuerpo { get; private set; } = Array.Empty<byte>();

    // true si el cliente declaró un cuerpo más grande que el máximo permitido.
    public bool CuerpoDemasiadoGrande { get; private set; }

    // Lee del cliente la solicitud COMPLETA. Devuelve null si el cliente se desconectó
    // sin mandar nada, mandó algo mal formado o tardó demasiado.
    public static async Task<HttpRequest?> LeerAsync(NetworkStream stream)
    {
        // Si el cliente no manda datos en 10 segundos, se cancela la lectura.
        using var limiteDeTiempo = new CancellationTokenSource(TiempoMaximoDeLectura);
        CancellationToken token = limiteDeTiempo.Token;

        try
        {
            // ---------- PASO 1: leer hasta encontrar el fin de los encabezados ----------
            // TCP es un chorro de bytes: no hay garantía de recibir todo en una sola lectura.
            var recibido = new MemoryStream();
            var buffer = new byte[4096];
            int finDeEncabezados = -1;

            while (finDeEncabezados < 0)
            {
                int n = await stream.ReadAsync(buffer.AsMemory(), token);
                if (n == 0) return null;                       // el cliente cerró la conexión

                recibido.Write(buffer, 0, n);
                finDeEncabezados = BuscarFinDeEncabezados(recibido.GetBuffer(), (int)recibido.Length);

                if (finDeEncabezados < 0 && recibido.Length > TamanioMaximoEncabezados)
                    return null;                               // encabezados absurdamente grandes
            }

            byte[] todo = recibido.ToArray();

            // ---------- PASO 2: separar primera línea y encabezados ----------
            string textoEncabezados = Encoding.UTF8.GetString(todo, 0, finDeEncabezados);
            string[] lineas = textoEncabezados.Split("\r\n");

            string[] partes = lineas[0].Split(' ');            // "POST /enviar HTTP/1.1"
            if (partes.Length < 3) return null;

            var solicitud = new HttpRequest
            {
                Metodo = partes[0],
                Destino = partes[1],
                Version = partes[2]
            };

            for (int i = 1; i < lineas.Length; i++)            // "Content-Length: 20"
            {
                int dosPuntos = lineas[i].IndexOf(':');
                if (dosPuntos <= 0) continue;

                string nombre = lineas[i].Substring(0, dosPuntos).Trim();
                string valor = lineas[i].Substring(dosPuntos + 1).Trim();
                solicitud.Encabezados[nombre] = valor;
            }

            // ---------- PASO 3: leer el cuerpo usando Content-Length ----------
            int largo = 0;
            if (solicitud.Encabezados.TryGetValue("Content-Length", out string? textoLargo)
                && int.TryParse(textoLargo, out int largoLeido)
                && largoLeido > 0)
            {
                largo = largoLeido;
            }

            if (largo > TamanioMaximoCuerpo)
            {
                solicitud.CuerpoDemasiadoGrande = true;
                return solicitud;
            }

            // Parte del cuerpo ya pudo llegar junto con los encabezados: se aprovecha.
            int inicioCuerpo = finDeEncabezados + 4;           // 4 = largo de "\r\n\r\n"
            int yaRecibidos = Math.Min(todo.Length - inicioCuerpo, largo);

            var cuerpo = new byte[largo];
            Array.Copy(todo, inicioCuerpo, cuerpo, 0, yaRecibidos);

            // Lo que falta se sigue leyendo hasta completar EXACTAMENTE "largo" bytes.
            int leidos = yaRecibidos;
            while (leidos < largo)
            {
                int n = await stream.ReadAsync(cuerpo.AsMemory(leidos, largo - leidos), token);
                if (n == 0) break;                             // el cliente cortó antes de terminar
                leidos += n;
            }

            if (leidos < largo)
                Array.Resize(ref cuerpo, leidos);

            solicitud.Cuerpo = cuerpo;
            return solicitud;
        }
        catch (OperationCanceledException)
        {
            return null;                                       // se agotó el tiempo de espera
        }
    }

    // Devuelve la posición donde empieza la secuencia "\r\n\r\n" (13,10,13,10), o -1 si todavía no llegó.
    private static int BuscarFinDeEncabezados(byte[] datos, int largo)
    {
        for (int i = 0; i + 3 < largo; i++)
        {
            if (datos[i] == 13 && datos[i + 1] == 10 && datos[i + 2] == 13 && datos[i + 3] == 10)
                return i;
        }
        return -1;
    }

    // ¿El cuerpo viene de un formulario HTML? (nombre=Pedro&edad=20)
    public bool EsFormulario =>
        Encabezados.TryGetValue("Content-Type", out string? tipo)
        && tipo.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);

    // Convierte "nombre=Pedro&edad=20" en un diccionario: nombre -> Pedro, edad -> 20.
    public Dictionary<string, string> ObtenerDatosFormulario()
    {
        var datos = new Dictionary<string, string>();
        string texto = Encoding.UTF8.GetString(Cuerpo);

        foreach (string par in texto.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int igual = par.IndexOf('=');
            string clave = igual >= 0 ? par.Substring(0, igual) : par;
            string valor = igual >= 0 ? par.Substring(igual + 1) : "";
            datos[Decodificar(clave)] = Decodificar(valor);
        }
        return datos;
    }

    // Texto corto del cuerpo, listo para escribir en el log: "nombre=Pedro; edad=20".
    public string ResumenDelCuerpo()
    {
        if (Cuerpo.Length == 0) return "";

        string texto = EsFormulario
            ? string.Join("; ", ObtenerDatosFormulario().Select(d => $"{d.Key}={d.Value}"))
            : Encoding.UTF8.GetString(Cuerpo);

        // Sin saltos de línea: así nadie puede "inventar" renglones falsos en el log.
        texto = texto.Replace('\r', ' ').Replace('\n', ' ');
        return texto.Length > 500 ? texto.Substring(0, 500) + "..." : texto;
    }

    // En los formularios el espacio viaja como "+" y los acentos como %C3%A1.
    private static string Decodificar(string texto) => Uri.UnescapeDataString(texto.Replace('+', ' '));
}
