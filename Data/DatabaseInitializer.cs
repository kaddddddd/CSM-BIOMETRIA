using System;
using System.Collections.Generic;
using MySqlConnector;

namespace CSMBiometricoWPF.Data
{
    public static class DatabaseInitializer
    {
        public static void InicializarSiNecesario()
        {
            using var conn = ConexionDB.ObtenerConexion();

            using var check = new MySqlCommand(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='roles'", conn);
            long existe = Convert.ToInt64(check.ExecuteScalar());

            if (existe > 0)
            {
                using var fix = new MySqlCommand(
                    "UPDATE usuarios SET password_hash='3eb3fe66b31e3b4d10fa70b5cad49c7112294af6ae4e476a1c405155d45aa121' WHERE username='superadmin'", conn);
                fix.ExecuteNonQuery();
                AplicarMigraciones(conn);
                return;
            }

            foreach (string sql in GetSentencias())
            {
                if (string.IsNullOrWhiteSpace(sql)) continue;
                try
                {
                    using var cmd = new MySqlCommand(sql, conn);
                    cmd.ExecuteNonQuery();
                }
                catch { }
            }
        }

        // Agrega columnas o tablas que puedan faltar en instalaciones MySQL existentes
        private static void AplicarMigraciones(MySqlConnection conn)
        {
            AgregarColumna(conn, "registros_ingreso",   "nombre_franja",  "VARCHAR(100)");
            AgregarColumna(conn, "horario_excepciones", "id_grado",        "INT");
            AgregarColumna(conn, "horario_excepciones", "id_institucion",  "INT");
            AgregarColumna(conn, "horario_excepciones", "alcance",         "VARCHAR(20) DEFAULT 'SEDE'");
            AgregarColumna(conn, "horarios",            "id_grado",        "INT");
            AgregarColumna(conn, "horarios",            "id_grupo",        "INT");

            foreach (string sql in GetMigraciones())
            {
                if (string.IsNullOrWhiteSpace(sql)) continue;
                try { using var cmd = new MySqlCommand(sql, conn); cmd.ExecuteNonQuery(); }
                catch { }
            }

            // Renombrar rol OPERADOR → DOCENTE si existe
            try
            {
                using var cmd = new MySqlCommand("UPDATE roles SET nombre_rol='DOCENTE' WHERE nombre_rol='OPERADOR'", conn);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        private static bool ColumnaExiste(MySqlConnection conn, string tabla, string columna)
        {
            using var cmd = new MySqlCommand(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@t AND COLUMN_NAME=@c", conn);
            cmd.Parameters.AddWithValue("@t", tabla);
            cmd.Parameters.AddWithValue("@c", columna);
            return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        }

        private static void AgregarColumna(MySqlConnection conn, string tabla, string columna, string tipo)
        {
            if (ColumnaExiste(conn, tabla, columna)) return;
            try { new MySqlCommand($"ALTER TABLE `{tabla}` ADD COLUMN `{columna}` {tipo}", conn).ExecuteNonQuery(); }
            catch { }
        }

        // Tablas y vistas que pueden faltar en una DB MySQL ya inicializada
        private static IEnumerable<string> GetMigraciones() => new[]
        {
            @"CREATE TABLE IF NOT EXISTS franjas_horario (
                id_franja INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                id_horario INT NOT NULL,
                nombre VARCHAR(100) NOT NULL DEFAULT 'Franja',
                hora_inicio VARCHAR(8) NOT NULL,
                hora_limite_tarde VARCHAR(8) NOT NULL,
                hora_cierre_ingreso VARCHAR(8) NOT NULL,
                orden INT DEFAULT 0,
                activo TINYINT(1) DEFAULT 1,
                FOREIGN KEY (id_horario) REFERENCES horarios(id_horario) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",
            @"CREATE TABLE IF NOT EXISTS horario_excepciones (
                id_excepcion INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                id_sede INT,
                id_grado INT,
                id_institucion INT,
                alcance VARCHAR(20) DEFAULT 'SEDE',
                fecha_excepcion VARCHAR(10) NOT NULL,
                descripcion TEXT NOT NULL,
                activo TINYINT(1) DEFAULT 1,
                fecha_creacion DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (id_sede)  REFERENCES sedes(id_sede)  ON DELETE CASCADE,
                FOREIGN KEY (id_grado) REFERENCES grados(id_grado)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",
            @"CREATE TABLE IF NOT EXISTS franjas_excepcion (
                id_franja_exc INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                id_excepcion INT NOT NULL,
                nombre VARCHAR(100) NOT NULL DEFAULT 'Franja',
                hora_inicio VARCHAR(8) NOT NULL,
                hora_limite_tarde VARCHAR(8) NOT NULL,
                hora_cierre_ingreso VARCHAR(8) NOT NULL,
                orden INT DEFAULT 0,
                FOREIGN KEY (id_excepcion) REFERENCES horario_excepciones(id_excepcion) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",
            @"CREATE TABLE IF NOT EXISTS periodos_academicos (
                id_periodo INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                id_institucion INT,
                nombre VARCHAR(100) NOT NULL,
                mes_inicio INT NOT NULL,
                dia_inicio INT NOT NULL,
                mes_fin INT NOT NULL,
                dia_fin INT NOT NULL,
                orden INT DEFAULT 0,
                FOREIGN KEY (id_institucion) REFERENCES instituciones(id_institucion) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",
            "DROP VIEW IF EXISTS v_estadisticas_hoy",
            @"CREATE VIEW v_estadisticas_hoy AS
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
                AND ri.fecha_ingreso = CURDATE()
                AND ri.id_estudiante = e.id_estudiante
            WHERE s.estado = 1
            GROUP BY s.id_sede, s.nombre_sede, i.nombre",
        };

        private static IEnumerable<string> GetSentencias() => new[]
        {
            // roles
            @"CREATE TABLE IF NOT EXISTS roles (
                id_rol INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                nombre_rol VARCHAR(50) NOT NULL UNIQUE,
                descripcion TEXT,
                activo TINYINT(1) DEFAULT 1,
                fecha_creacion DATETIME DEFAULT CURRENT_TIMESTAMP
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",
            "INSERT IGNORE INTO roles (id_rol, nombre_rol, descripcion) VALUES (1,'SUPERADMIN','Control total del sistema')",
            "INSERT IGNORE INTO roles (id_rol, nombre_rol, descripcion) VALUES (2,'ADMINISTRADOR','Gestión de estudiantes y asistencia')",
            "INSERT IGNORE INTO roles (id_rol, nombre_rol, descripcion) VALUES (3,'DOCENTE','Solo registro biométrico de ingreso')",

            // instituciones
            @"CREATE TABLE IF NOT EXISTS instituciones (
                id_institucion INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                nombre VARCHAR(200) NOT NULL,
                direccion TEXT,
                telefono VARCHAR(20),
                estado TINYINT(1) DEFAULT 1,
                fecha_creacion DATETIME DEFAULT CURRENT_TIMESTAMP,
                fecha_modificacion DATETIME
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

            // sedes
            @"CREATE TABLE IF NOT EXISTS sedes (
                id_sede INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                id_institucion INT NOT NULL,
                nombre_sede VARCHAR(200) NOT NULL,
                direccion TEXT,
                estado TINYINT(1) DEFAULT 1,
                fecha_creacion DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (id_institucion) REFERENCES instituciones(id_institucion) ON DELETE RESTRICT ON UPDATE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

            // grados
            @"CREATE TABLE IF NOT EXISTS grados (
                id_grado INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                nombre_grado VARCHAR(100) NOT NULL,
                orden_grado INT DEFAULT 0,
                estado TINYINT(1) DEFAULT 1,
                fecha_creacion DATETIME DEFAULT CURRENT_TIMESTAMP
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",
            "INSERT IGNORE INTO grados (id_grado, nombre_grado, orden_grado) VALUES (1,'Preescolar',0)",
            "INSERT IGNORE INTO grados (id_grado, nombre_grado, orden_grado) VALUES (2,'Primero',1)",
            "INSERT IGNORE INTO grados (id_grado, nombre_grado, orden_grado) VALUES (3,'Segundo',2)",
            "INSERT IGNORE INTO grados (id_grado, nombre_grado, orden_grado) VALUES (4,'Tercero',3)",
            "INSERT IGNORE INTO grados (id_grado, nombre_grado, orden_grado) VALUES (5,'Cuarto',4)",
            "INSERT IGNORE INTO grados (id_grado, nombre_grado, orden_grado) VALUES (6,'Quinto',5)",
            "INSERT IGNORE INTO grados (id_grado, nombre_grado, orden_grado) VALUES (7,'Sexto',6)",
            "INSERT IGNORE INTO grados (id_grado, nombre_grado, orden_grado) VALUES (8,'Séptimo',7)",
            "INSERT IGNORE INTO grados (id_grado, nombre_grado, orden_grado) VALUES (9,'Octavo',8)",
            "INSERT IGNORE INTO grados (id_grado, nombre_grado, orden_grado) VALUES (10,'Noveno',9)",
            "INSERT IGNORE INTO grados (id_grado, nombre_grado, orden_grado) VALUES (11,'Décimo',10)",
            "INSERT IGNORE INTO grados (id_grado, nombre_grado, orden_grado) VALUES (12,'Undécimo',11)",

            // grupos
            @"CREATE TABLE IF NOT EXISTS grupos (
                id_grupo INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                nombre_grupo VARCHAR(50) NOT NULL,
                estado TINYINT(1) DEFAULT 1,
                fecha_creacion DATETIME DEFAULT CURRENT_TIMESTAMP
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",
            "INSERT IGNORE INTO grupos (id_grupo, nombre_grupo) VALUES (1,'001')",
            "INSERT IGNORE INTO grupos (id_grupo, nombre_grupo) VALUES (2,'101')",
            "INSERT IGNORE INTO grupos (id_grupo, nombre_grupo) VALUES (3,'201')",
            "INSERT IGNORE INTO grupos (id_grupo, nombre_grupo) VALUES (4,'301')",
            "INSERT IGNORE INTO grupos (id_grupo, nombre_grupo) VALUES (5,'401')",
            "INSERT IGNORE INTO grupos (id_grupo, nombre_grupo) VALUES (6,'501')",
            "INSERT IGNORE INTO grupos (id_grupo, nombre_grupo) VALUES (7,'601')",
            "INSERT IGNORE INTO grupos (id_grupo, nombre_grupo) VALUES (8,'701')",
            "INSERT IGNORE INTO grupos (id_grupo, nombre_grupo) VALUES (9,'801')",
            "INSERT IGNORE INTO grupos (id_grupo, nombre_grupo) VALUES (10,'901')",
            "INSERT IGNORE INTO grupos (id_grupo, nombre_grupo) VALUES (11,'1001')",
            "INSERT IGNORE INTO grupos (id_grupo, nombre_grupo) VALUES (12,'1101')",

            // usuarios
            @"CREATE TABLE IF NOT EXISTS usuarios (
                id_usuario INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                id_rol INT NOT NULL,
                id_institucion INT,
                username VARCHAR(100) NOT NULL UNIQUE,
                password_hash VARCHAR(64) NOT NULL,
                nombre_completo VARCHAR(200) NOT NULL,
                email VARCHAR(150),
                estado TINYINT(1) DEFAULT 1,
                ultimo_login DATETIME,
                intentos_fallidos INT DEFAULT 0,
                bloqueado TINYINT(1) DEFAULT 0,
                fecha_creacion DATETIME DEFAULT CURRENT_TIMESTAMP,
                fecha_modificacion DATETIME,
                FOREIGN KEY (id_rol) REFERENCES roles(id_rol) ON DELETE RESTRICT ON UPDATE CASCADE,
                FOREIGN KEY (id_institucion) REFERENCES instituciones(id_institucion) ON DELETE SET NULL ON UPDATE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",
            "INSERT IGNORE INTO usuarios (id_rol, username, password_hash, nombre_completo, email) VALUES (1,'superadmin','3eb3fe66b31e3b4d10fa70b5cad49c7112294af6ae4e476a1c405155d45aa121','Administrador del Sistema','admin@csm.edu.co')",

            // estudiantes
            @"CREATE TABLE IF NOT EXISTS estudiantes (
                id_estudiante INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                identificacion VARCHAR(30) NOT NULL UNIQUE,
                nombre VARCHAR(100) NOT NULL,
                apellidos VARCHAR(100) NOT NULL,
                id_institucion INT NOT NULL,
                id_sede INT NOT NULL,
                id_grado INT NOT NULL,
                id_grupo INT NOT NULL,
                telefono VARCHAR(20),
                email VARCHAR(150),
                foto LONGBLOB,
                estado VARCHAR(10) DEFAULT 'ACTIVO',
                fecha_matricula VARCHAR(10),
                fecha_creacion DATETIME DEFAULT CURRENT_TIMESTAMP,
                fecha_modificacion DATETIME,
                FOREIGN KEY (id_institucion) REFERENCES instituciones(id_institucion) ON DELETE RESTRICT ON UPDATE CASCADE,
                FOREIGN KEY (id_sede) REFERENCES sedes(id_sede) ON DELETE RESTRICT ON UPDATE CASCADE,
                FOREIGN KEY (id_grado) REFERENCES grados(id_grado) ON DELETE RESTRICT ON UPDATE CASCADE,
                FOREIGN KEY (id_grupo) REFERENCES grupos(id_grupo) ON DELETE RESTRICT ON UPDATE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",
            "CREATE INDEX IF NOT EXISTS idx_identificacion ON estudiantes(identificacion)",
            "CREATE INDEX IF NOT EXISTS idx_sede_grado ON estudiantes(id_sede, id_grado)",

            // huellas_digitales
            @"CREATE TABLE IF NOT EXISTS huellas_digitales (
                id_huella INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                id_estudiante INT NOT NULL,
                template_biometrico LONGBLOB NOT NULL,
                dedo VARCHAR(20) DEFAULT 'INDICE_D',
                calidad INT DEFAULT 0,
                activo TINYINT(1) DEFAULT 1,
                fecha_registro DATETIME DEFAULT CURRENT_TIMESTAMP,
                registrado_por INT,
                FOREIGN KEY (id_estudiante) REFERENCES estudiantes(id_estudiante) ON DELETE CASCADE ON UPDATE CASCADE,
                FOREIGN KEY (registrado_por) REFERENCES usuarios(id_usuario) ON DELETE SET NULL ON UPDATE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",
            "CREATE INDEX IF NOT EXISTS idx_estudiante_activo ON huellas_digitales(id_estudiante, activo)",

            // horarios (esquema final con id_grado e id_grupo)
            @"CREATE TABLE IF NOT EXISTS horarios (
                id_horario INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                id_sede INT NOT NULL,
                id_grado INT,
                id_grupo INT,
                dia_semana VARCHAR(10) NOT NULL,
                hora_inicio VARCHAR(8) NOT NULL,
                hora_limite_tarde VARCHAR(8) NOT NULL,
                hora_cierre_ingreso VARCHAR(8) NOT NULL,
                activo TINYINT(1) DEFAULT 1,
                fecha_creacion DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (id_sede)  REFERENCES sedes(id_sede)  ON DELETE CASCADE ON UPDATE CASCADE,
                FOREIGN KEY (id_grado) REFERENCES grados(id_grado),
                FOREIGN KEY (id_grupo) REFERENCES grupos(id_grupo)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

            // franjas_horario
            @"CREATE TABLE IF NOT EXISTS franjas_horario (
                id_franja INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                id_horario INT NOT NULL,
                nombre VARCHAR(100) NOT NULL DEFAULT 'Franja',
                hora_inicio VARCHAR(8) NOT NULL,
                hora_limite_tarde VARCHAR(8) NOT NULL,
                hora_cierre_ingreso VARCHAR(8) NOT NULL,
                orden INT DEFAULT 0,
                activo TINYINT(1) DEFAULT 1,
                FOREIGN KEY (id_horario) REFERENCES horarios(id_horario) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

            // horario_excepciones
            @"CREATE TABLE IF NOT EXISTS horario_excepciones (
                id_excepcion INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                id_sede INT,
                id_grado INT,
                id_institucion INT,
                alcance VARCHAR(20) DEFAULT 'SEDE',
                fecha_excepcion VARCHAR(10) NOT NULL,
                descripcion TEXT NOT NULL,
                activo TINYINT(1) DEFAULT 1,
                fecha_creacion DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (id_sede)  REFERENCES sedes(id_sede)  ON DELETE CASCADE,
                FOREIGN KEY (id_grado) REFERENCES grados(id_grado)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

            // franjas_excepcion
            @"CREATE TABLE IF NOT EXISTS franjas_excepcion (
                id_franja_exc INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                id_excepcion INT NOT NULL,
                nombre VARCHAR(100) NOT NULL DEFAULT 'Franja',
                hora_inicio VARCHAR(8) NOT NULL,
                hora_limite_tarde VARCHAR(8) NOT NULL,
                hora_cierre_ingreso VARCHAR(8) NOT NULL,
                orden INT DEFAULT 0,
                FOREIGN KEY (id_excepcion) REFERENCES horario_excepciones(id_excepcion) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

            // registros_ingreso
            @"CREATE TABLE IF NOT EXISTS registros_ingreso (
                id_registro INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                id_estudiante INT NOT NULL,
                id_sede INT NOT NULL,
                fecha_ingreso VARCHAR(10) NOT NULL,
                hora_ingreso VARCHAR(8) NOT NULL,
                estado_ingreso VARCHAR(20) NOT NULL,
                puntaje_biometrico DOUBLE DEFAULT 0,
                sincronizado TINYINT(1) DEFAULT 1,
                nombre_franja VARCHAR(100),
                observaciones TEXT,
                fecha_registro DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (id_estudiante) REFERENCES estudiantes(id_estudiante) ON DELETE RESTRICT ON UPDATE CASCADE,
                FOREIGN KEY (id_sede) REFERENCES sedes(id_sede) ON DELETE RESTRICT ON UPDATE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",
            "CREATE INDEX IF NOT EXISTS idx_fecha_sede ON registros_ingreso(fecha_ingreso, id_sede)",
            "CREATE INDEX IF NOT EXISTS idx_estudiante_fecha ON registros_ingreso(id_estudiante, fecha_ingreso)",

            // registros_offline
            @"CREATE TABLE IF NOT EXISTS registros_offline (
                id_offline INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                id_estudiante INT NOT NULL,
                id_sede INT NOT NULL,
                fecha_ingreso VARCHAR(10) NOT NULL,
                hora_ingreso VARCHAR(8) NOT NULL,
                estado_ingreso VARCHAR(20) NOT NULL,
                puntaje_biometrico DOUBLE DEFAULT 0,
                sincronizado TINYINT(1) DEFAULT 0,
                fecha_registro DATETIME DEFAULT CURRENT_TIMESTAMP
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

            // logs_sistema
            @"CREATE TABLE IF NOT EXISTS logs_sistema (
                id_log INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                id_usuario INT,
                tipo_evento VARCHAR(30) NOT NULL,
                descripcion TEXT NOT NULL,
                ip_origen VARCHAR(45),
                nivel VARCHAR(20) DEFAULT 'INFO',
                fecha_evento DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (id_usuario) REFERENCES usuarios(id_usuario) ON DELETE SET NULL ON UPDATE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",
            "CREATE INDEX IF NOT EXISTS idx_tipo_fecha ON logs_sistema(tipo_evento, fecha_evento)",
            "CREATE INDEX IF NOT EXISTS idx_nivel_fecha ON logs_sistema(nivel, fecha_evento)",

            // periodos_academicos
            @"CREATE TABLE IF NOT EXISTS periodos_academicos (
                id_periodo INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                id_institucion INT,
                nombre VARCHAR(100) NOT NULL,
                mes_inicio INT NOT NULL,
                dia_inicio INT NOT NULL,
                mes_fin INT NOT NULL,
                dia_fin INT NOT NULL,
                orden INT DEFAULT 0,
                FOREIGN KEY (id_institucion) REFERENCES instituciones(id_institucion) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

            // vistas
            "DROP VIEW IF EXISTS v_estadisticas_hoy",
            @"CREATE VIEW v_estadisticas_hoy AS
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
                AND ri.fecha_ingreso = CURDATE()
                AND ri.id_estudiante = e.id_estudiante
            WHERE s.estado = 1
            GROUP BY s.id_sede, s.nombre_sede, i.nombre",

            "DROP VIEW IF EXISTS v_registros_ingreso_detalle",
            @"CREATE VIEW v_registros_ingreso_detalle AS
            SELECT
                ri.id_registro,
                ri.fecha_ingreso,
                ri.hora_ingreso,
                ri.estado_ingreso,
                ri.puntaje_biometrico,
                e.identificacion,
                CONCAT(e.nombre, ' ', e.apellidos) AS nombre_completo,
                s.nombre_sede,
                g.nombre_grado,
                gr.nombre_grupo,
                i.nombre AS nombre_institucion
            FROM registros_ingreso ri
            INNER JOIN estudiantes e ON ri.id_estudiante = e.id_estudiante
            INNER JOIN sedes s ON ri.id_sede = s.id_sede
            INNER JOIN instituciones i ON s.id_institucion = i.id_institucion
            INNER JOIN grados g ON e.id_grado = g.id_grado
            INNER JOIN grupos gr ON e.id_grupo = gr.id_grupo",
        };
    }
}
