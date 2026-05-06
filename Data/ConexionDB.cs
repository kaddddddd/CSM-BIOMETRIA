using System;
using System.Configuration;
using MySqlConnector;

namespace CSMBiometricoWPF.Data
{
    public static class ConexionDB
    {
        private static string _connectionString;
        private static bool _conectado = false;

        public static bool EstaConectado => _conectado;

        static ConexionDB()
        {
            _connectionString =
                ConfigurationManager.ConnectionStrings["MySqlConnection"]?.ConnectionString
                ?? "Server=127.0.0.1;Port=3306;Database=csm_biometrico;User ID=root;Password=admin;CharSet=utf8mb4;SslMode=None;";
        }

        public static MySqlConnection ObtenerConexion()
        {
            var conn = new MySqlConnection(_connectionString);
            conn.Open();
            _conectado = true;
            return conn;
        }

        public static bool VerificarConexion()
        {
            try
            {
                using var conn = new MySqlConnection(_connectionString);
                conn.Open();
                _conectado = true;
                return true;
            }
            catch
            {
                _conectado = false;
                return false;
            }
        }

        /// <summary>Reconfigura la conexión en tiempo de ejecución (útil para la pantalla de configuración).</summary>
        public static void ConfigurarConexion(string servidor, string baseDatos, string usuario, string password, int puerto = 3306)
        {
            _connectionString = $"Server={servidor};Port={puerto};Database={baseDatos};User ID={usuario};Password={password};CharSet=utf8mb4;SslMode=None;";
        }

        public static string ObtenerCadenaConexion() => _connectionString;
    }
}
