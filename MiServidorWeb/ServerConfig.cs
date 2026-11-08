using System;
using System.IO;
using System.Text.Json;

namespace MiServidorWeb
{
    public class ServerConfig
    {
        public int Port { get; set; } = 8080;
        
        public string WebRoot { get; set; } = "wwwroot";

        public static ServerConfig Cargar(string rutaArchivo)
        {
            if (!File.Exists(rutaArchivo))
            {
                var configDefecto = new ServerConfig();
                string jsonDefecto = JsonSerializer.Serialize(configDefecto, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(rutaArchivo, jsonDefecto);
                return configDefecto;
            }

            string json = File.ReadAllText(rutaArchivo);
            return JsonSerializer.Deserialize<ServerConfig>(json) ?? new ServerConfig();
        }
    }
}