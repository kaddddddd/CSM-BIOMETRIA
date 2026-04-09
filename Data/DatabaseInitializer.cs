using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace CSMBiometricoWPF.Data
{
    public static class DatabaseInitializer
    {
        public static void InicializarSiNecesario()
        {
            using var conn = ConexionDB.ObtenerConexion();

            using var check = new SqliteCommand(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='roles'", conn);
            long existe = (long)check.ExecuteScalar();
            if (existe > 0)
            {
                // Asegurar que el hash del superadmin sea correcto
                using var fix = new SqliteCommand(
                    "UPDATE usuarios SET password_hash='3eb3fe66b31e3b4d10fa70b5cad49c7112294af6ae4e476a1c405155d45aa121' WHERE username='superadmin'", conn);
                fix.ExecuteNonQuery();
                // Migrar tablas nuevas si no existen aún
                AplicarMigraciones(conn);
                return;
            }

            foreach (string sql in GetSentencias())
            {
                if (string.IsNullOrWhiteSpace(sql)) continue;
                using var cmd = new SqliteCommand(sql, conn);
                cmd.ExecuteNonQuery();
            }
        }

        private static void AplicarMigraciones(SqliteConnection conn)
        {
            foreach (string sql in GetMigraciones())
            {
                if (string.IsNullOrWhiteSpace(sql)) continue;
                using var cmd = new SqliteCommand(sql, conn);
                cmd.ExecuteNonQuery();
            }
            // Programmatic migrations (need conditional logic)
            MigrarHorariosConGrado(conn);
            MigrarHorariosConGrupo(conn);
            MigrarExcepcionesConAlcance(conn);
            MigrarExcepcionesConUniqueGrado(conn);
            MigrarRegistrosConNombreFranja(conn);
        }

        private static bool ColumnaExiste(SqliteConnection conn, string tabla, string columna)
        {
            using var cmd = new SqliteCommand($"PRAGMA table_info({tabla})", conn);
            using var dr = cmd.ExecuteReader();
            while (dr.Read())
                if (dr["name"].ToString() == columna) return true;
            return false;
        }

        private static void AgregarColumna(SqliteConnection conn, string tabla, string columna, string tipo)
        {
            if (ColumnaExiste(conn, tabla, columna)) return;
            try { new SqliteCommand($"ALTER TABLE {tabla} ADD COLUMN {columna} {tipo}", conn).ExecuteNonQuery(); }
            catch { }
        }

        private static void MigrarHorariosConGrado(SqliteConnection conn)
        {
            if (ColumnaExiste(conn, "horarios", "id_grado")) return;

            new SqliteCommand("PRAGMA foreign_keys=OFF", conn).ExecuteNonQuery();
            try
            {
                new SqliteCommand(@"CREATE TABLE horarios_v2 (
                    id_horario INTEGER PRIMARY KEY,
                    id_sede INTEGER NOT NULL,
                    id_grado INTEGER,
                    dia_semana TEXT NOT NULL CHECK(dia_semana IN ('LUNES','MARTES','MIERCOLES','JUEVES','VIERNES','SABADO','DOMINGO')),
                    hora_inicio TEXT NOT NULL,
                    hora_limite_tarde TEXT NOT NULL,
                    hora_cierre_ingreso TEXT NOT NULL,
                    activo INTEGER DEFAULT 1,
                    fecha_creacion TEXT DEFAULT (datetime('now')),
                    FOREIGN KEY (id_sede) REFERENCES sedes(id_sede) ON DELETE CASCADE,
                    FOREIGN KEY (id_grado) REFERENCES grados(id_grado)
                )", conn).ExecuteNonQuery();

                new SqliteCommand(@"INSERT INTO horarios_v2
                    (id_horario, id_sede, id_grado, dia_semana, hora_inicio, hora_limite_tarde, hora_cierre_ingreso, activo, fecha_creacion)
                    SELECT id_horario, id_sede, NULL, dia_semana, hora_inicio, hora_limite_tarde, hora_cierre_ingreso, activo, fecha_creacion
                    FROM horarios", conn).ExecuteNonQuery();

                new SqliteCommand("DROP TABLE horarios", conn).ExecuteNonQuery();
                new SqliteCommand("ALTER TABLE horarios_v2 RENAME TO horarios", conn).ExecuteNonQuery();

                new SqliteCommand(@"CREATE UNIQUE INDEX IF NOT EXISTS uix_horario_sede_dia
                    ON horarios(id_sede, dia_semana) WHERE id_grado IS NULL", conn).ExecuteNonQuery();
                new SqliteCommand(@"CREATE UNIQUE INDEX IF NOT EXISTS uix_horario_sede_grado_dia
                    ON horarios(id_sede, id_grado, dia_semana) WHERE id_grado IS NOT NULL", conn).ExecuteNonQuery();
            }
            finally
            {
                new SqliteCommand("PRAGMA foreign_keys=ON", conn).ExecuteNonQuery();
            }
        }

        private static void MigrarHorariosConGrupo(SqliteConnection conn)
        {
            AgregarColumna(conn, "horarios", "id_grupo", "INTEGER REFERENCES grupos(id_grupo)");
            try
            {
                new SqliteCommand(
                    @"CREATE UNIQUE INDEX IF NOT EXISTS uix_horario_sede_grado_grupo_dia
                      ON horarios(id_sede, id_grado, id_grupo, dia_semana)
                      WHERE id_grado IS NOT NULL AND id_grupo IS NOT NULL",
                    conn).ExecuteNonQuery();
            }
            catch { }
        }

        private static void MigrarExcepcionesConAlcance(SqliteConnection conn)
        {
            AgregarColumna(conn, "horario_excepciones", "id_grado",       "INTEGER");
            AgregarColumna(conn, "horario_excepciones", "id_institucion",  "INTEGER");
            AgregarColumna(conn, "horario_excepciones", "alcance",         "TEXT DEFAULT 'SEDE'");
        }

        private static void MigrarExcepcionesConUniqueGrado(SqliteConnection conn)
        {
            // Ya migrado si existe el índice parcial por grado
            using var check = new SqliteCommand(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='uix_exc_sede_grado_fecha'", conn);
            if ((long)check.ExecuteScalar() > 0) return;

            new SqliteCommand("PRAGMA foreign_keys=OFF", conn).ExecuteNonQuery();
            try
            {
                new SqliteCommand(@"CREATE TABLE horario_excepciones_v2 (
                    id_excepcion    INTEGER PRIMARY KEY,
                    id_sede         INTEGER,
                    id_grado        INTEGER,
                    id_institucion  INTEGER,
                    alcance         TEXT DEFAULT 'SEDE',
                    fecha_excepcion TEXT NOT NULL,
                    descripcion     TEXT NOT NULL,
                    activo          INTEGER DEFAULT 1,
                    fecha_creacion  TEXT DEFAULT (datetime('now')),
                    FOREIGN KEY (id_sede)  REFERENCES sedes(id_sede)  ON DELETE CASCADE,
                    FOREIGN KEY (id_grado) REFERENCES grados(id_grado)
                )", conn).ExecuteNonQuery();

                new SqliteCommand(@"INSERT INTO horario_excepciones_v2
                    (id_excepcion, id_sede, id_grado, id_institucion, alcance,
                     fecha_excepcion, descripcion, activo, fecha_creacion)
                    SELECT id_excepcion,
                           NULLIF(id_sede, 0),
                           id_grado, id_institucion,
                           COALESCE(alcance, 'SEDE'),
                           fecha_excepcion, descripcion, activo, fecha_creacion
                    FROM horario_excepciones", conn).ExecuteNonQuery();

                new SqliteCommand("DROP TABLE horario_excepciones", conn).ExecuteNonQuery();
                new SqliteCommand("ALTER TABLE horario_excepciones_v2 RENAME TO horario_excepciones", conn).ExecuteNonQuery();

                new SqliteCommand(@"CREATE UNIQUE INDEX IF NOT EXISTS uix_exc_sede_fecha
                    ON horario_excepciones(id_sede, fecha_excepcion) WHERE id_grado IS NULL", conn).ExecuteNonQuery();
                new SqliteCommand(@"CREATE UNIQUE INDEX IF NOT EXISTS uix_exc_sede_grado_fecha
                    ON horario_excepciones(id_sede, id_grado, fecha_excepcion) WHERE id_grado IS NOT NULL", conn).ExecuteNonQuery();
            }
            finally
            {
                new SqliteCommand("PRAGMA foreign_keys=ON", conn).ExecuteNonQuery();
            }
        }

        private static void MigrarRegistrosConNombreFranja(SqliteConnection conn)
        {
            AgregarColumna(conn, "registros_ingreso", "nombre_franja", "TEXT");
        }

        private static IEnumerable<string> GetMigraciones() => new[]
        {
            // Recrear vista con fecha local (antes usaba date('now') UTC)
            "DROP VIEW IF EXISTS v_estadisticas_hoy",
            @"CREATE VIEW IF NOT EXISTS v_estadisticas_hoy AS
            SELECT
                s.id_sede,
                s.nombre_sede,
                i.nombre AS nombre_institucion,
                COUNT(DISTINCT e.id_estudiante) AS total_estudiantes,
                COUNT(DISTINCT ri.id_estudiante) AS presentes_hoy,
                COUNT(DISTINCT e.id_estudiante) - COUNT(DISTINCT ri.id_estudiante) AS ausentes_hoy,
                SUM(CASE WHEN ri.estado_ingreso = 'A_TIEMPO' THEN 1 ELSE 0 END) AS a_tiempo,
                SUM(CASE WHEN ri.estado_ingreso = 'TARDE' THEN 1 ELSE 0 END) AS tarde
            FROM sedes s
            INNER JOIN instituciones i ON s.id_institucion = i.id_institucion
            LEFT JOIN estudiantes e ON e.id_sede = s.id_sede AND e.estado = 'ACTIVO'
            LEFT JOIN registros_ingreso ri ON ri.id_sede = s.id_sede
                AND ri.fecha_ingreso = date('now','localtime')
                AND ri.id_estudiante = e.id_estudiante
            WHERE s.estado = 1
            GROUP BY s.id_sede, s.nombre_sede, i.nombre",
            @"CREATE TABLE IF NOT EXISTS franjas_horario (
                id_franja INTEGER PRIMARY KEY,
                id_horario INTEGER NOT NULL,
                nombre TEXT NOT NULL DEFAULT 'Franja',
                hora_inicio TEXT NOT NULL,
                hora_limite_tarde TEXT NOT NULL,
                hora_cierre_ingreso TEXT NOT NULL,
                orden INTEGER DEFAULT 0,
                activo INTEGER DEFAULT 1,
                FOREIGN KEY (id_horario) REFERENCES horarios(id_horario) ON DELETE CASCADE
            )",
            @"CREATE TABLE IF NOT EXISTS horario_excepciones (
                id_excepcion    INTEGER PRIMARY KEY,
                id_sede         INTEGER,
                id_grado        INTEGER,
                id_institucion  INTEGER,
                alcance         TEXT DEFAULT 'SEDE',
                fecha_excepcion TEXT NOT NULL,
                descripcion     TEXT NOT NULL,
                activo          INTEGER DEFAULT 1,
                fecha_creacion  TEXT DEFAULT (datetime('now')),
                FOREIGN KEY (id_sede)  REFERENCES sedes(id_sede)  ON DELETE CASCADE,
                FOREIGN KEY (id_grado) REFERENCES grados(id_grado)
            )",
            @"CREATE UNIQUE INDEX IF NOT EXISTS uix_exc_sede_fecha
                ON horario_excepciones(id_sede, fecha_excepcion) WHERE id_grado IS NULL",
            @"CREATE UNIQUE INDEX IF NOT EXISTS uix_exc_sede_grado_fecha
                ON horario_excepciones(id_sede, id_grado, fecha_excepcion) WHERE id_grado IS NOT NULL",
            @"CREATE TABLE IF NOT EXISTS franjas_excepcion (
                id_franja_exc INTEGER PRIMARY KEY,
                id_excepcion INTEGER NOT NULL,
                nombre TEXT NOT NULL DEFAULT 'Franja',
                hora_inicio TEXT NOT NULL,
                hora_limite_tarde TEXT NOT NULL,
                hora_cierre_ingreso TEXT NOT NULL,
                orden INTEGER DEFAULT 0,
                FOREIGN KEY (id_excepcion) REFERENCES horario_excepciones(id_excepcion) ON DELETE CASCADE
            )",
            @"CREATE TABLE IF NOT EXISTS periodos_academicos (
                id_periodo     INTEGER PRIMARY KEY,
                id_institucion INTEGER,
                nombre         TEXT NOT NULL,
                mes_inicio     INTEGER NOT NULL,
                dia_inicio     INTEGER NOT NULL,
                mes_fin        INTEGER NOT NULL,
                dia_fin        INTEGER NOT NULL,
                orden          INTEGER DEFAULT 0,
                FOREIGN KEY (id_institucion) REFERENCES instituciones(id_institucion) ON DELETE CASCADE
            )",
        };

        private static IEnumerable<string> GetSentencias()
        {
            return new[]
            {
                // roles
                @"CREATE TABLE IF NOT EXISTS roles (
                    id_rol INTEGER PRIMARY KEY,
                    nombre_rol TEXT NOT NULL UNIQUE,
                    descripcion TEXT,
                    activo INTEGER DEFAULT 1,
                    fecha_creacion TEXT DEFAULT (datetime('now'))
                )",
                "INSERT OR IGNORE INTO roles (id_rol, nombre_rol, descripcion) VALUES (1,'SUPERADMIN','Control total del sistema')",
                "INSERT OR IGNORE INTO roles (id_rol, nombre_rol, descripcion) VALUES (2,'ADMINISTRADOR','Gestión de estudiantes y asistencia')",
                "INSERT OR IGNORE INTO roles (id_rol, nombre_rol, descripcion) VALUES (3,'OPERADOR','Solo registro biométrico de ingreso')",

                // instituciones
                @"CREATE TABLE IF NOT EXISTS instituciones (
                    id_institucion INTEGER PRIMARY KEY,
                    nombre TEXT NOT NULL,
                    direccion TEXT,
                    telefono TEXT,
                    estado INTEGER DEFAULT 1,
                    fecha_creacion TEXT DEFAULT (datetime('now')),
                    fecha_modificacion TEXT
                )",

                // sedes
                @"CREATE TABLE IF NOT EXISTS sedes (
                    id_sede INTEGER PRIMARY KEY,
                    id_institucion INTEGER NOT NULL,
                    nombre_sede TEXT NOT NULL,
                    direccion TEXT,
                    estado INTEGER DEFAULT 1,
                    fecha_creacion TEXT DEFAULT (datetime('now')),
                    FOREIGN KEY (id_institucion) REFERENCES instituciones(id_institucion) ON DELETE RESTRICT ON UPDATE CASCADE
                )",

                // grados
                @"CREATE TABLE IF NOT EXISTS grados (
                    id_grado INTEGER PRIMARY KEY,
                    nombre_grado TEXT NOT NULL,
                    orden_grado INTEGER DEFAULT 0,
                    estado INTEGER DEFAULT 1,
                    fecha_creacion TEXT DEFAULT (datetime('now'))
                )",
                "INSERT OR IGNORE INTO grados (id_grado, nombre_grado, orden_grado) VALUES (1,'Preescolar',0)",
                "INSERT OR IGNORE INTO grados (id_grado, nombre_grado, orden_grado) VALUES (2,'Primero',1)",
                "INSERT OR IGNORE INTO grados (id_grado, nombre_grado, orden_grado) VALUES (3,'Segundo',2)",
                "INSERT OR IGNORE INTO grados (id_grado, nombre_grado, orden_grado) VALUES (4,'Tercero',3)",
                "INSERT OR IGNORE INTO grados (id_grado, nombre_grado, orden_grado) VALUES (5,'Cuarto',4)",
                "INSERT OR IGNORE INTO grados (id_grado, nombre_grado, orden_grado) VALUES (6,'Quinto',5)",
                "INSERT OR IGNORE INTO grados (id_grado, nombre_grado, orden_grado) VALUES (7,'Sexto',6)",
                "INSERT OR IGNORE INTO grados (id_grado, nombre_grado, orden_grado) VALUES (8,'Séptimo',7)",
                "INSERT OR IGNORE INTO grados (id_grado, nombre_grado, orden_grado) VALUES (9,'Octavo',8)",
                "INSERT OR IGNORE INTO grados (id_grado, nombre_grado, orden_grado) VALUES (10,'Noveno',9)",
                "INSERT OR IGNORE INTO grados (id_grado, nombre_grado, orden_grado) VALUES (11,'Décimo',10)",
                "INSERT OR IGNORE INTO grados (id_grado, nombre_grado, orden_grado) VALUES (12,'Undécimo',11)",

                // grupos
                @"CREATE TABLE IF NOT EXISTS grupos (
                    id_grupo INTEGER PRIMARY KEY,
                    nombre_grupo TEXT NOT NULL,
                    estado INTEGER DEFAULT 1,
                    fecha_creacion TEXT DEFAULT (datetime('now'))
                )",
                "INSERT OR IGNORE INTO grupos (id_grupo, nombre_grupo) VALUES (1,'A')",
                "INSERT OR IGNORE INTO grupos (id_grupo, nombre_grupo) VALUES (2,'B')",
                "INSERT OR IGNORE INTO grupos (id_grupo, nombre_grupo) VALUES (3,'C')",
                "INSERT OR IGNORE INTO grupos (id_grupo, nombre_grupo) VALUES (4,'D')",
                "INSERT OR IGNORE INTO grupos (id_grupo, nombre_grupo) VALUES (5,'E')",

                // usuarios
                @"CREATE TABLE IF NOT EXISTS usuarios (
                    id_usuario INTEGER PRIMARY KEY,
                    id_rol INTEGER NOT NULL,
                    id_institucion INTEGER,
                    username TEXT NOT NULL UNIQUE,
                    password_hash TEXT NOT NULL,
                    nombre_completo TEXT NOT NULL,
                    email TEXT,
                    estado INTEGER DEFAULT 1,
                    ultimo_login TEXT,
                    intentos_fallidos INTEGER DEFAULT 0,
                    bloqueado INTEGER DEFAULT 0,
                    fecha_creacion TEXT DEFAULT (datetime('now')),
                    fecha_modificacion TEXT,
                    FOREIGN KEY (id_rol) REFERENCES roles(id_rol) ON DELETE RESTRICT ON UPDATE CASCADE,
                    FOREIGN KEY (id_institucion) REFERENCES instituciones(id_institucion) ON DELETE SET NULL ON UPDATE CASCADE
                )",
                // password: Admin123!  SHA256 = 3eb3fe66b31e3b4d10fa70b5cad49c7112294af6ae4e476a1c405155d45aa121
                "INSERT OR IGNORE INTO usuarios (id_rol, username, password_hash, nombre_completo, email) VALUES (1,'superadmin','3eb3fe66b31e3b4d10fa70b5cad49c7112294af6ae4e476a1c405155d45aa121','Administrador del Sistema','admin@csm.edu.co')",

                // estudiantes
                @"CREATE TABLE IF NOT EXISTS estudiantes (
                    id_estudiante INTEGER PRIMARY KEY,
                    identificacion TEXT NOT NULL UNIQUE,
                    nombre TEXT NOT NULL,
                    apellidos TEXT NOT NULL,
                    id_institucion INTEGER NOT NULL,
                    id_sede INTEGER NOT NULL,
                    id_grado INTEGER NOT NULL,
                    id_grupo INTEGER NOT NULL,
                    telefono TEXT,
                    email TEXT,
                    foto BLOB,
                    estado TEXT DEFAULT 'ACTIVO' CHECK(estado IN ('ACTIVO','INACTIVO','RETIRADO')),
                    fecha_matricula TEXT,
                    fecha_creacion TEXT DEFAULT (datetime('now')),
                    fecha_modificacion TEXT,
                    FOREIGN KEY (id_institucion) REFERENCES instituciones(id_institucion) ON DELETE RESTRICT ON UPDATE CASCADE,
                    FOREIGN KEY (id_sede) REFERENCES sedes(id_sede) ON DELETE RESTRICT ON UPDATE CASCADE,
                    FOREIGN KEY (id_grado) REFERENCES grados(id_grado) ON DELETE RESTRICT ON UPDATE CASCADE,
                    FOREIGN KEY (id_grupo) REFERENCES grupos(id_grupo) ON DELETE RESTRICT ON UPDATE CASCADE
                )",
                "CREATE INDEX IF NOT EXISTS idx_identificacion ON estudiantes(identificacion)",
                "CREATE INDEX IF NOT EXISTS idx_sede_grado ON estudiantes(id_sede, id_grado)",

                // huellas_digitales
                @"CREATE TABLE IF NOT EXISTS huellas_digitales (
                    id_huella INTEGER PRIMARY KEY,
                    id_estudiante INTEGER NOT NULL,
                    template_biometrico BLOB NOT NULL,
                    dedo TEXT DEFAULT 'INDICE_D' CHECK(dedo IN ('PULGAR_D','INDICE_D','MEDIO_D','ANULAR_D','MENIQUE_D','PULGAR_I','INDICE_I','MEDIO_I','ANULAR_I','MENIQUE_I')),
                    calidad INTEGER DEFAULT 0,
                    activo INTEGER DEFAULT 1,
                    fecha_registro TEXT DEFAULT (datetime('now')),
                    registrado_por INTEGER,
                    FOREIGN KEY (id_estudiante) REFERENCES estudiantes(id_estudiante) ON DELETE CASCADE ON UPDATE CASCADE,
                    FOREIGN KEY (registrado_por) REFERENCES usuarios(id_usuario) ON DELETE SET NULL ON UPDATE CASCADE
                )",
                "CREATE INDEX IF NOT EXISTS idx_estudiante_activo ON huellas_digitales(id_estudiante, activo)",

                // horarios
                @"CREATE TABLE IF NOT EXISTS horarios (
                    id_horario INTEGER PRIMARY KEY,
                    id_sede INTEGER NOT NULL,
                    dia_semana TEXT NOT NULL CHECK(dia_semana IN ('LUNES','MARTES','MIERCOLES','JUEVES','VIERNES','SABADO','DOMINGO')),
                    hora_inicio TEXT NOT NULL,
                    hora_limite_tarde TEXT NOT NULL,
                    hora_cierre_ingreso TEXT NOT NULL,
                    activo INTEGER DEFAULT 1,
                    fecha_creacion TEXT DEFAULT (datetime('now')),
                    FOREIGN KEY (id_sede) REFERENCES sedes(id_sede) ON DELETE CASCADE ON UPDATE CASCADE,
                    UNIQUE (id_sede, dia_semana)
                )",

                // franjas_horario: ventanas adicionales de ingreso por día (jornada completa, etc.)
                @"CREATE TABLE IF NOT EXISTS franjas_horario (
                    id_franja INTEGER PRIMARY KEY,
                    id_horario INTEGER NOT NULL,
                    nombre TEXT NOT NULL DEFAULT 'Franja',
                    hora_inicio TEXT NOT NULL,
                    hora_limite_tarde TEXT NOT NULL,
                    hora_cierre_ingreso TEXT NOT NULL,
                    orden INTEGER DEFAULT 0,
                    activo INTEGER DEFAULT 1,
                    FOREIGN KEY (id_horario) REFERENCES horarios(id_horario) ON DELETE CASCADE
                )",

                // horario_excepciones: override para una fecha específica
                @"CREATE TABLE IF NOT EXISTS horario_excepciones (
                    id_excepcion    INTEGER PRIMARY KEY,
                    id_sede         INTEGER,
                    id_grado        INTEGER,
                    id_institucion  INTEGER,
                    alcance         TEXT DEFAULT 'SEDE',
                    fecha_excepcion TEXT NOT NULL,
                    descripcion     TEXT NOT NULL,
                    activo          INTEGER DEFAULT 1,
                    fecha_creacion  TEXT DEFAULT (datetime('now')),
                    FOREIGN KEY (id_sede)  REFERENCES sedes(id_sede)  ON DELETE CASCADE,
                    FOREIGN KEY (id_grado) REFERENCES grados(id_grado)
                )",
                @"CREATE UNIQUE INDEX IF NOT EXISTS uix_exc_sede_fecha
                    ON horario_excepciones(id_sede, fecha_excepcion) WHERE id_grado IS NULL",
                @"CREATE UNIQUE INDEX IF NOT EXISTS uix_exc_sede_grado_fecha
                    ON horario_excepciones(id_sede, id_grado, fecha_excepcion) WHERE id_grado IS NOT NULL",

                // franjas_excepcion: ventanas de ingreso de cada excepción
                @"CREATE TABLE IF NOT EXISTS franjas_excepcion (
                    id_franja_exc INTEGER PRIMARY KEY,
                    id_excepcion INTEGER NOT NULL,
                    nombre TEXT NOT NULL DEFAULT 'Franja',
                    hora_inicio TEXT NOT NULL,
                    hora_limite_tarde TEXT NOT NULL,
                    hora_cierre_ingreso TEXT NOT NULL,
                    orden INTEGER DEFAULT 0,
                    FOREIGN KEY (id_excepcion) REFERENCES horario_excepciones(id_excepcion) ON DELETE CASCADE
                )",

                // registros_ingreso
                @"CREATE TABLE IF NOT EXISTS registros_ingreso (
                    id_registro INTEGER PRIMARY KEY,
                    id_estudiante INTEGER NOT NULL,
                    id_sede INTEGER NOT NULL,
                    fecha_ingreso TEXT NOT NULL,
                    hora_ingreso TEXT NOT NULL,
                    estado_ingreso TEXT NOT NULL CHECK(estado_ingreso IN ('A_TIEMPO','TARDE','FUERA_DE_HORARIO')),
                    puntaje_biometrico REAL DEFAULT 0,
                    sincronizado INTEGER DEFAULT 1,
                    observaciones TEXT,
                    fecha_registro TEXT DEFAULT (datetime('now')),
                    FOREIGN KEY (id_estudiante) REFERENCES estudiantes(id_estudiante) ON DELETE RESTRICT ON UPDATE CASCADE,
                    FOREIGN KEY (id_sede) REFERENCES sedes(id_sede) ON DELETE RESTRICT ON UPDATE CASCADE
                )",
                "CREATE INDEX IF NOT EXISTS idx_fecha_sede ON registros_ingreso(fecha_ingreso, id_sede)",
                "CREATE INDEX IF NOT EXISTS idx_estudiante_fecha ON registros_ingreso(id_estudiante, fecha_ingreso)",

                // registros_offline
                @"CREATE TABLE IF NOT EXISTS registros_offline (
                    id_offline INTEGER PRIMARY KEY,
                    id_estudiante INTEGER NOT NULL,
                    id_sede INTEGER NOT NULL,
                    fecha_ingreso TEXT NOT NULL,
                    hora_ingreso TEXT NOT NULL,
                    estado_ingreso TEXT NOT NULL,
                    puntaje_biometrico REAL DEFAULT 0,
                    sincronizado INTEGER DEFAULT 0,
                    fecha_registro TEXT DEFAULT (datetime('now'))
                )",

                // logs_sistema
                @"CREATE TABLE IF NOT EXISTS logs_sistema (
                    id_log INTEGER PRIMARY KEY,
                    id_usuario INTEGER,
                    tipo_evento TEXT NOT NULL CHECK(tipo_evento IN ('LOGIN','LOGOUT','ERROR_DB','ERROR_LECTOR','REGISTRO_ASISTENCIA','ENROLAMIENTO','CRUD','SISTEMA','SEGURIDAD')),
                    descripcion TEXT NOT NULL,
                    ip_origen TEXT,
                    nivel TEXT DEFAULT 'INFO' CHECK(nivel IN ('INFO','ADVERTENCIA','ERROR','CRITICO')),
                    fecha_evento TEXT DEFAULT (datetime('now')),
                    FOREIGN KEY (id_usuario) REFERENCES usuarios(id_usuario) ON DELETE SET NULL ON UPDATE CASCADE
                )",
                "CREATE INDEX IF NOT EXISTS idx_tipo_fecha ON logs_sistema(tipo_evento, fecha_evento)",
                "CREATE INDEX IF NOT EXISTS idx_nivel_fecha ON logs_sistema(nivel, fecha_evento)",

                // vistas
                @"CREATE VIEW IF NOT EXISTS v_estadisticas_hoy AS
                SELECT
                    s.id_sede,
                    s.nombre_sede,
                    i.nombre AS nombre_institucion,
                    COUNT(DISTINCT e.id_estudiante) AS total_estudiantes,
                    COUNT(DISTINCT ri.id_estudiante) AS presentes_hoy,
                    COUNT(DISTINCT e.id_estudiante) - COUNT(DISTINCT ri.id_estudiante) AS ausentes_hoy,
                    SUM(CASE WHEN ri.estado_ingreso = 'A_TIEMPO' THEN 1 ELSE 0 END) AS a_tiempo,
                    SUM(CASE WHEN ri.estado_ingreso = 'TARDE' THEN 1 ELSE 0 END) AS tarde
                FROM sedes s
                INNER JOIN instituciones i ON s.id_institucion = i.id_institucion
                LEFT JOIN estudiantes e ON e.id_sede = s.id_sede AND e.estado = 'ACTIVO'
                LEFT JOIN registros_ingreso ri ON ri.id_sede = s.id_sede
                    AND ri.fecha_ingreso = date('now')
                    AND ri.id_estudiante = e.id_estudiante
                WHERE s.estado = 1
                GROUP BY s.id_sede, s.nombre_sede, i.nombre",

                @"CREATE VIEW IF NOT EXISTS v_registros_ingreso_detalle AS
                SELECT
                    ri.id_registro,
                    ri.fecha_ingreso,
                    ri.hora_ingreso,
                    ri.estado_ingreso,
                    ri.puntaje_biometrico,
                    e.identificacion,
                    (e.nombre || ' ' || e.apellidos) AS nombre_completo,
                    s.nombre_sede,
                    g.nombre_grado,
                    gr.nombre_grupo,
                    i.nombre AS nombre_institucion
                FROM registros_ingreso ri
                INNER JOIN estudiantes e ON ri.id_estudiante = e.id_estudiante
                INNER JOIN sedes s ON ri.id_sede = s.id_sede
                INNER JOIN instituciones i ON s.id_institucion = i.id_institucion
                INNER JOIN grados g ON e.id_grado = g.id_grado
                INNER JOIN grupos gr ON e.id_grupo = gr.id_grupo"
            };
        }
    }
}
