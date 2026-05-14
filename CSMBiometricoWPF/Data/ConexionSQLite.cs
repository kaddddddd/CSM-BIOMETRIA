// ============================================================
// CSMBiometricoWPF.Data - Conexión a SQLite local
// Archivo: Data/ConexionSQLite.cs
// ============================================================
using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace CSMBiometricoWPF.Data
{
    public static class ConexionSQLite
    {
        public static readonly string DbPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "csm_local.db");

        private static readonly string _connectionString =
            $"Data Source={DbPath};Cache=Shared;";

        public static SqliteConnection ObtenerConexion()
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            return conn;
        }
    }
}
