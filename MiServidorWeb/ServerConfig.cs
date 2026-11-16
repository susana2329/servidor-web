
using System;
using System.IO;
using System.Text.Json;
 
namespace MiServidorWeb
{
    public class ServerConfig
    {
        public int Port { get; set; } = 8080;
 
        public string WebRoot { get; set; } = "wwwroot";
 
        public int MaxConexiones { get; set; } = 100;
 
        public static ServerConfig Cargar(string rutaArchivo)
        {
            ServerConfig config;
 
            if (!File.Exists(rutaArchivo))
            {
                config = new ServerConfig();
                string jsonDefecto = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(rutaArchivo, jsonDefecto);
            }
            else
            {
                try
                {
                    string json = File.ReadAllText(rutaArchivo);
                    config = JsonSerializer.Deserialize<ServerConfig>(json) ?? new ServerConfig();
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException($"'{rutaArchivo}' no es un JSON válido: {ex.Message}", ex);
                }
            }
 
            config.Validar(rutaArchivo);
            return config;
        }
 
        private void Validar(string rutaArchivo)
        {
            if (Port < 1 || Port > 65535)
                throw new InvalidDataException($"Port inválido en '{rutaArchivo}': {Port}. Debe estar entre 1 y 65535.");
 
            if (MaxConexiones < 1)
                throw new InvalidDataException($"MaxConexiones inválido en '{rutaArchivo}': {MaxConexiones}. Debe ser 1 o más.");
 
            if (string.IsNullOrWhiteSpace(WebRoot) || !Directory.Exists(WebRoot))
                throw new DirectoryNotFoundException($"La carpeta WebRoot '{WebRoot}' indicada en '{rutaArchivo}' no existe.");
        }
    }
}
 