// ============================================================
// CSMBiometricoWPF.Data - Inicializador de esquema SQLite local
// Archivo: Data/SQLiteInitializer.cs
// ============================================================
namespace CSMBiometricoWPF.Data
{
    public static class SQLiteInitializer
    {
        public static void InicializarSiNecesario()
        {
            using var conn = ConexionSQLite.ObtenerConexion();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode = WAL;

                -- Cache de huellas digitales (sync desde MySQL)
                CREATE TABLE IF NOT EXISTS cache_huellas (
                    id_huella         INTEGER PRIMARY KEY,
                    id_estudiante     INTEGER NOT NULL,
                    template          BLOB    NOT NULL,
                    calidad           INTEGER DEFAULT 0,
                    nombre_estudiante TEXT    DEFAULT '',
                    id_sede           INTEGER DEFAULT 0,
                    id_institucion    INTEGER DEFAULT 0
                );

                -- Cache de estudiantes (sync desde MySQL)
                CREATE TABLE IF NOT EXISTS cache_estudiantes (
                    id_estudiante      INTEGER PRIMARY KEY,
                    identificacion     TEXT    DEFAULT '',
                    nombre             TEXT    DEFAULT '',
                    apellidos          TEXT    DEFAULT '',
                    id_institucion     INTEGER DEFAULT 0,
                    id_sede            INTEGER DEFAULT 0,
                    id_grado           INTEGER DEFAULT 0,
                    id_grupo           INTEGER DEFAULT 0,
                    nombre_institucion TEXT    DEFAULT '',
                    nombre_sede        TEXT    DEFAULT '',
                    nombre_grado       TEXT    DEFAULT '',
                    nombre_grupo       TEXT    DEFAULT '',
                    foto               BLOB
                );

                -- Cache de sedes (para mapear id_sede → id_institucion)
                CREATE TABLE IF NOT EXISTS cache_sedes (
                    id_sede        INTEGER PRIMARY KEY,
                    id_institucion INTEGER DEFAULT 0,
                    nombre_sede    TEXT    DEFAULT ''
                );

                -- Cache de horarios semanales (sync desde MySQL)
                CREATE TABLE IF NOT EXISTS cache_horarios (
                    id_horario          INTEGER PRIMARY KEY,
                    id_sede             INTEGER,
                    id_grado            INTEGER,
                    id_grupo            INTEGER,
                    dia_semana          TEXT    DEFAULT '',
                    hora_inicio         TEXT    DEFAULT '',
                    hora_limite_tarde   TEXT    DEFAULT '',
                    hora_cierre_ingreso TEXT    DEFAULT ''
                );

                -- Cache de franjas adicionales por horario
                CREATE TABLE IF NOT EXISTS cache_franjas (
                    id_franja           INTEGER PRIMARY KEY,
                    id_horario          INTEGER,
                    nombre              TEXT    DEFAULT '',
                    hora_inicio         TEXT    DEFAULT '',
                    hora_limite_tarde   TEXT    DEFAULT '',
                    hora_cierre_ingreso TEXT    DEFAULT '',
                    orden               INTEGER DEFAULT 0
                );

                -- Cache de excepciones de horario (sync desde MySQL)
                CREATE TABLE IF NOT EXISTS cache_excepciones (
                    id_excepcion    INTEGER PRIMARY KEY,
                    id_sede         INTEGER,
                    id_grado        INTEGER,
                    id_institucion  INTEGER,
                    fecha_excepcion TEXT DEFAULT ''
                );

                -- Franjas de excepciones
                CREATE TABLE IF NOT EXISTS cache_franjas_excepcion (
                    id_franja           INTEGER PRIMARY KEY,
                    id_excepcion        INTEGER,
                    nombre              TEXT    DEFAULT '',
                    hora_inicio         TEXT    DEFAULT '',
                    hora_limite_tarde   TEXT    DEFAULT '',
                    hora_cierre_ingreso TEXT    DEFAULT '',
                    orden               INTEGER DEFAULT 0
                );

                -- Cola de registros offline pendientes de sincronizar con MySQL
                CREATE TABLE IF NOT EXISTS registros_pendientes (
                    id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                    id_estudiante      INTEGER,
                    id_sede            INTEGER,
                    fecha_ingreso      TEXT    DEFAULT '',
                    hora_ingreso       TEXT    DEFAULT '',
                    estado_ingreso     TEXT    DEFAULT '',
                    puntaje_biometrico REAL    DEFAULT 0,
                    nombre_franja      TEXT,
                    sincronizado       INTEGER DEFAULT 0
                );
            ";
            cmd.ExecuteNonQuery();
        }
    }
}
