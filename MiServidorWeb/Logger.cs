using System;
using System.IO;

namespace MiServidorWeb;

public static class Logger
{
    private static readonly object _lockObjeto = new object();

    public static void Registrar(string ip, string metodo, string ruta, string datos = "")
    {
        string nombreArchivoLog = $"log_{DateTime.Now:yyyy-MM-dd}.txt";
        string registro = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | IP: {ip} | {metodo} {ruta}";

        if (!string.IsNullOrEmpty(datos))
        {
            registro += $" | DATOS: {datos}";
        }
        registro += "\n";

        lock (_lockObjeto)
        {
            File.AppendAllText(nombreArchivoLog, registro);
        }
    }
}