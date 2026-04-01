using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace CSMBiometricoWPF.Data
{
    public static class ConexionDB
    {
        private static string _connectionString;
        private static bool _conectado = false;

        public static bool EstaConectado => _conectado;

        static ConexionDB()
        {
            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "csm_biometrico.db");
            _connectionString = $"Data Source={dbPath};";
        }

        public static SqliteConnection ObtenerConexion()
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var pragma = new SqliteCommand("PRAGMA foreign_keys = ON;", conn);
            pragma.ExecuteNonQuery();
            _conectado = true;
            return conn;
        }

        public static bool VerificarConexion()
        {
            try
            {
                using var conn = new SqliteConnection(_connectionString);
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

        public static void ConfigurarConexion(string dbPath)
        {
            _connectionString = $"Data Source={dbPath};";
        }

        public static string ObtenerCadenaConexion() => _connectionString;
    }
}
