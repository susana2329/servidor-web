namespace MiServidorWeb;

// Punto 2: decide QUÉ archivo corresponde a la URL que pidió el navegador.
public static class ResolutorDeRutas
{
    // Archivo que se entrega cuando la URL no dice cuál.
    public const string ArchivoPorDefecto = "index.html";

    // Devuelve la ruta completa del archivo dentro de la carpeta raíz,
    // o null si la URL intenta salir de esa carpeta (o no es una ruta válida).
    //
    // Ejemplos (carpeta raíz = C:\sitio\wwwroot):
    //   "/"                -> C:\sitio\wwwroot\index.html
    //   "/styles.css"      -> C:\sitio\wwwroot\styles.css
    //   "/ayuda/"          -> C:\sitio\wwwroot\ayuda\index.html
    //   "/buscar?x=1"      -> C:\sitio\wwwroot\buscar          (lo que va después del "?" se ignora)
    //   "/../secreto.txt"  -> null                              (intento de salir de la carpeta)
    public static string? Resolver(string destino, string carpetaRaiz)
    {
        try
        {
            // 1) Quedarse solo con la ruta: sin los parámetros (?...) ni el fragmento (#...)
            string ruta = destino;
            int corte = ruta.IndexOfAny(new[] { '?', '#' });
            if (corte >= 0)
                ruta = ruta.Substring(0, corte);

            // 2) Convertir %20, %C3%A1, %2e%2e, etc. en los caracteres reales
            ruta = Uri.UnescapeDataString(ruta);

            // 3) Si no pidió ningún archivo ("/" o una carpeta que termina en "/"), usar index.html
            ruta = ruta.TrimStart('/', '\\');
            if (ruta.Length == 0 || ruta.EndsWith('/') || ruta.EndsWith('\\'))
                ruta += ArchivoPorDefecto;

            // 4) Armar la ruta completa y comprobar que quede DENTRO de la carpeta raíz
            string raiz = Path.GetFullPath(carpetaRaiz);
            string completa = Path.GetFullPath(Path.Combine(raiz, ruta));
            string raizConSeparador = raiz.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (!completa.StartsWith(raizConSeparador, StringComparison.OrdinalIgnoreCase))
                return null;

            return completa;
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
        {
            return null;    // la ruta tiene caracteres inválidos: se trata como "no existe"
        }
    }
}
