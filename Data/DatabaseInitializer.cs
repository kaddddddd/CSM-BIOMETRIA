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
            }
            else
            {
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

                // En instalación nueva también sembrar horarios y grupos si ya hay sedes
                CorregirUniqueKeyHorarios(conn);
                SembrarHorariosPorGrado(conn);
                SembrarGruposPorSede(conn);
            }

            // Siempre: garantizar usuario Karenda
            CrearUsuarioKarenda(conn);
        }

        private static void CrearUsuarioKarenda(MySqlConnection conn)
        {
            // SHA256("123456") = 8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92
            const string hash = "8d969eef6ecad3c29a3a629280e686cf0c3f5d5a86aff3ca12020c923adc6c92";
            try
            {
                long existeUser = 0;
                using (var chk = new MySqlCommand(
                    "SELECT COUNT(*) FROM usuarios WHERE LOWER(username) = 'karenda'", conn))
                    existeUser = Convert.ToInt64(chk.ExecuteScalar());

                // Obtener la institución activa (única válida tras limpiar duplicados)
                long idInstActiva = 0;
                using (var ic = new MySqlCommand(
                    "SELECT id_institucion FROM instituciones WHERE estado=1 ORDER BY id_institucion LIMIT 1", conn))
                {
                    var iv = ic.ExecuteScalar();
                    if (iv != null && iv != DBNull.Value) idInstActiva = Convert.ToInt64(iv);
                }

                if (existeUser > 0)
                {
                    using var upd = new MySqlCommand(
                        @"UPDATE usuarios SET password_hash=@h, estado=1, bloqueado=0,
                          id_institucion=@inst WHERE LOWER(username)='karenda'", conn);
                    upd.Parameters.AddWithValue("@h",    hash);
                    upd.Parameters.AddWithValue("@inst", idInstActiva > 0 ? (object)idInstActiva : DBNull.Value);
                    upd.ExecuteNonQuery();
                }
                else
                {
                    long idRol = 0;
                    using (var rc = new MySqlCommand(
                        "SELECT id_rol FROM roles WHERE UPPER(nombre_rol) IN ('SUPERADMIN','ADMINISTRADOR') ORDER BY id_rol LIMIT 1", conn))
                    {
                        var rv = rc.ExecuteScalar();
                        if (rv != null && rv != DBNull.Value) idRol = Convert.ToInt64(rv);
                    }

                    if (idRol > 0)
                    {
                        using var ins = new MySqlCommand(
                            @"INSERT INTO usuarios (id_rol, id_institucion, username, password_hash, nombre_completo, estado, bloqueado)
                              VALUES (@rol, @inst, 'Karenda', @h, 'Karenda', 1, 0)", conn);
                        ins.Parameters.AddWithValue("@rol",  idRol);
                        ins.Parameters.AddWithValue("@inst", idInstActiva > 0 ? (object)idInstActiva : DBNull.Value);
                        ins.Parameters.AddWithValue("@h",    hash);
                        ins.ExecuteNonQuery();
                    }
                }
            }
            catch { }
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
            AgregarColumna(conn, "instituciones",       "logo_path",       "VARCHAR(255) NULL");
            AgregarColumna(conn, "grupos",              "id_sede",         "INT");
            AgregarColumna(conn, "grupos",              "id_grado",        "INT");

            foreach (string sql in GetMigraciones())
            {
                if (string.IsNullOrWhiteSpace(sql)) continue;
                try { using var cmd = new MySqlCommand(sql, conn); cmd.ExecuteNonQuery(); }
                catch { }
            }

            CorregirUniqueKeyHorarios(conn);

            // Obtener la única institución activa
            long idInstPrincipal = 0;
            try
            {
                using var cmd = new MySqlCommand(
                    "SELECT id_institucion FROM instituciones WHERE estado=1 ORDER BY id_institucion LIMIT 1", conn);
                var r = cmd.ExecuteScalar();
                if (r != null && r != DBNull.Value) idInstPrincipal = Convert.ToInt64(r);
            }
            catch { }

            // Actualizar nombre, dirección y teléfono de la institución
            if (idInstPrincipal > 0)
            {
                try
                {
                    using var cmd = new MySqlCommand(
                        @"UPDATE instituciones
                          SET nombre    = 'Institución Educativa Barrios Unidos',
                              direccion = 'Kr 15 # 1-25',
                              telefono  = '8334254-3142972815'
                          WHERE id_institucion = @id", conn);
                    cmd.Parameters.AddWithValue("@id", idInstPrincipal);
                    cmd.ExecuteNonQuery();
                }
                catch { }
            }

            // Agregar las 4 sedes bajo la institución principal
            if (idInstPrincipal > 0)
            {
                foreach (var nombreSede in new[] {
                    "BARRIOS UNIDOS", "GABRIEL GONZALEZ", "LA JAGUA", "SOLEDAD HERMOSA" })
                {
                    try
                    {
                        long existe = 0;
                        using (var chk = new MySqlCommand(
                            "SELECT COUNT(*) FROM sedes WHERE UPPER(TRIM(nombre_sede)) = UPPER(TRIM(@n))", conn))
                        {
                            chk.Parameters.AddWithValue("@n", nombreSede);
                            existe = Convert.ToInt64(chk.ExecuteScalar());
                        }
                        if (existe == 0)
                        {
                            using var ins = new MySqlCommand(
                                "INSERT INTO sedes (id_institucion, nombre_sede, estado) VALUES (@inst, @nombre, 1)", conn);
                            ins.Parameters.AddWithValue("@inst",   idInstPrincipal);
                            ins.Parameters.AddWithValue("@nombre", nombreSede);
                            ins.ExecuteNonQuery();
                        }
                    }
                    catch { }
                }
            }

            // Desactivar instituciones duplicadas: conservar activa solo la de menor id
            try
            {
                using var cmd = new MySqlCommand(
                    @"UPDATE instituciones
                      SET estado = 0
                      WHERE id_institucion NOT IN (
                          SELECT id FROM (
                              SELECT MIN(id_institucion) AS id
                              FROM instituciones
                              GROUP BY UPPER(TRIM(nombre))
                          ) t
                      )", conn);
                cmd.ExecuteNonQuery();
            }
            catch { }

            // Desactivar sede "fetcito" si aún existe
            try
            {
                using var cmd = new MySqlCommand(
                    "UPDATE sedes SET estado=0 WHERE UPPER(TRIM(nombre_sede)) LIKE '%FETCITO%'", conn);
                cmd.ExecuteNonQuery();
            }
            catch { }

            // Desactivar sede "triangulo" si existe
            try
            {
                using var cmd = new MySqlCommand(
                    "UPDATE sedes SET estado=0 WHERE UPPER(TRIM(nombre_sede)) LIKE '%TRIANGULO%'", conn);
                cmd.ExecuteNonQuery();
            }
            catch { }

            // Corregir nombre sede: SOLEDAD HERMIDA → SOLEDAD HERMOSA
            try
            {
                using var cmd = new MySqlCommand(
                    "UPDATE sedes SET nombre_sede='SOLEDAD HERMOSA' WHERE UPPER(TRIM(nombre_sede))='SOLEDAD HERMIDA'", conn);
                cmd.ExecuteNonQuery();
            }
            catch { }

            // Renombrar rol OPERADOR → DOCENTE si existe
            try
            {
                using var cmd = new MySqlCommand("UPDATE roles SET nombre_rol='DOCENTE' WHERE nombre_rol='OPERADOR'", conn);
                cmd.ExecuteNonQuery();
            }
            catch { }


            // Corregir horarios de primaria (1°–5°) a 14:00–17:30
            try
            {
                using var cmd = new MySqlCommand(
                    @"UPDATE horarios h
                      JOIN grados g ON g.id_grado = h.id_grado
                      SET h.hora_inicio          = '14:00',
                          h.hora_limite_tarde    = '14:15',
                          h.hora_cierre_ingreso  = '17:30'
                      WHERE g.orden_grado BETWEEN 1 AND 5
                        AND h.id_grupo IS NULL
                        AND h.activo = 1", conn);
                cmd.ExecuteNonQuery();
            }
            catch { }

            // Sembrar horarios DESPUÉS de que todas las sedes estén creadas
            SembrarHorariosPorGrado(conn);

            // Sembrar y migrar grupos con nomenclatura por sede
            MigrarNomenclaturaGrupos(conn);
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

        // ─────────────────────────────────────────────────────────────────────
        // Nomenclatura de grupos: 0{sede_num}{orden_grado:D2}
        //   Sede 1 = BARRIOS UNIDOS   (todos los grados)
        //   Sede 2 = GABRIEL GONZALEZ (grados 1–5)
        //   Sede 3 = LA JAGUA         (grados 1–5)
        //   Sede 4 = SOLEDAD HERMOSA  (grados 1–5)
        // Ejemplo: Segundo en Barrios Unidos → 0102
        // ─────────────────────────────────────────────────────────────────────
        private static void MigrarNomenclaturaGrupos(MySqlConnection conn)
        {
            // Crear los grupos por sede (idempotente)
            SembrarGruposPorSede(conn);

            // Actualizar estudiantes para que apunten al grupo correcto según su sede y grado
            try
            {
                using var cmd = new MySqlCommand(
                    @"UPDATE estudiantes e
                      JOIN grupos g ON g.id_sede = e.id_sede
                                   AND g.id_grado = e.id_grado
                                   AND g.estado = 1
                      SET e.id_grupo = g.id_grupo", conn);
                cmd.ExecuteNonQuery();
            }
            catch { }

            // Desactivar grupos antiguos (sin id_sede)
            try
            {
                using var cmd = new MySqlCommand(
                    "UPDATE grupos SET estado=0 WHERE id_sede IS NULL", conn);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        // Quita tildes y convierte a mayúsculas para comparación robusta de nombres de sede
        private static string NormalizarNombreSede(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.ToUpperInvariant().Trim()
                .Replace("\u00C1", "A").Replace("\u00E1", "A") // Á á
                .Replace("\u00C9", "E").Replace("\u00E9", "E") // É é
                .Replace("\u00CD", "I").Replace("\u00ED", "I") // Í í
                .Replace("\u00D3", "O").Replace("\u00F3", "O") // Ó ó
                .Replace("\u00DA", "U").Replace("\u00FA", "U") // Ú ú
                .Replace("\u00DC", "U").Replace("\u00FC", "U") // Ü ü
                .Replace("\u00D1", "N").Replace("\u00F1", "N"); // Ñ ñ
        }

        private static void SembrarGruposPorSede(MySqlConnection conn)
        {
            // Mapa nombre sede normalizado → número de sede
            var sedeNumeros = new Dictionary<string, int>
            {
                { "BARRIOS UNIDOS",    1 },
                { "GABRIEL GONZALEZ",  2 },
                { "LA JAGUA",          3 },
                { "SOLEDAD HERMOSA",   4 }
            };
            var sedesPrimaria = new HashSet<string>
                { "GABRIEL GONZALEZ", "LA JAGUA", "SOLEDAD HERMOSA" };

            // Obtener sedes activas con nombre (sin transformar en SQL para manejar tildes en C#)
            var sedes = new List<(int IdSede, string NombreNorm)>();
            using (var cmd = new MySqlCommand(
                "SELECT id_sede, nombre_sede FROM sedes WHERE estado=1", conn))
            using (var dr = cmd.ExecuteReader())
                while (dr.Read())
                {
                    string norm = NormalizarNombreSede(dr.GetString(1));
                    if (sedeNumeros.ContainsKey(norm))
                        sedes.Add((dr.GetInt32(0), norm));
                }

            if (sedes.Count == 0) return;

            // Obtener grados activos
            var grados = new List<(int IdGrado, int Orden)>();
            using (var cmd = new MySqlCommand(
                "SELECT id_grado, orden_grado FROM grados WHERE estado=1 ORDER BY orden_grado", conn))
            using (var dr = cmd.ExecuteReader())
                while (dr.Read()) grados.Add((dr.GetInt32(0), dr.GetInt32(1)));

            foreach (var (idSede, nombreNorm) in sedes)
            {
                int sedeNum       = sedeNumeros[nombreNorm];
                bool soloPrimaria = sedesPrimaria.Contains(nombreNorm);

                foreach (var (idGrado, orden) in grados)
                {
                    if (soloPrimaria && (orden < 1 || orden > 5)) continue;

                    string nombreGrupo = $"0{sedeNum}{orden:D2}";
                    try
                    {
                        long existe = 0;
                        using (var chk = new MySqlCommand(
                            "SELECT COUNT(*) FROM grupos WHERE id_sede=@s AND id_grado=@g", conn))
                        {
                            chk.Parameters.AddWithValue("@s", idSede);
                            chk.Parameters.AddWithValue("@g", idGrado);
                            existe = Convert.ToInt64(chk.ExecuteScalar());
                        }
                        if (existe == 0)
                        {
                            using var ins = new MySqlCommand(
                                "INSERT INTO grupos (nombre_grupo, id_sede, id_grado, estado) VALUES (@n,@s,@g,1)",
                                conn);
                            ins.Parameters.AddWithValue("@n", nombreGrupo);
                            ins.Parameters.AddWithValue("@s", idSede);
                            ins.Parameters.AddWithValue("@g", idGrado);
                            ins.ExecuteNonQuery();
                        }
                        else
                        {
                            // Actualizar nombre en caso de que haya cambiado la regla
                            using var upd = new MySqlCommand(
                                "UPDATE grupos SET nombre_grupo=@n, estado=1 WHERE id_sede=@s AND id_grado=@g",
                                conn);
                            upd.Parameters.AddWithValue("@n", nombreGrupo);
                            upd.Parameters.AddWithValue("@s", idSede);
                            upd.Parameters.AddWithValue("@g", idGrado);
                            upd.ExecuteNonQuery();
                        }
                    }
                    catch { }
                }
            }
        }

        // Tablas y vistas que pueden faltar en una DB MySQL ya inicializada
        // Reemplaza cualquier índice único restrictivo en horarios por uno que
        // incluye id_grado e id_grupo, permitiendo horarios distintos por grado.
        private static void CorregirUniqueKeyHorarios(MySqlConnection conn)
        {
            // Usar SHOW INDEX (no requiere permisos especiales) para obtener
            // todos los índices únicos en horarios excepto PRIMARY
            var indexNames = new List<string>();
            try
            {
                using var cmd = new MySqlCommand(
                    "SHOW INDEX FROM horarios WHERE Non_unique = 0 AND Key_name != 'PRIMARY'", conn);
                using var dr = cmd.ExecuteReader();
                while (dr.Read())
                {
                    var name = dr["Key_name"].ToString();
                    if (!indexNames.Contains(name)) indexNames.Add(name);
                }
            }
            catch { }

            // Eliminar todos los índices únicos encontrados
            foreach (var name in indexNames)
            {
                try { new MySqlCommand($"ALTER TABLE `horarios` DROP INDEX `{name}`", conn).ExecuteNonQuery(); }
                catch { }
            }

            // Intentar también eliminar nombres conocidos por si SHOW INDEX falló
            foreach (var name in new[] { "uk_sede_dia", "uk_sede_grado_grupo_dia", "uk_horario_dia" })
            {
                try { new MySqlCommand($"ALTER TABLE `horarios` DROP INDEX `{name}`", conn).ExecuteNonQuery(); }
                catch { }
            }

            // Crear el índice correcto que permite horarios distintos por grado/grupo
            try
            {
                new MySqlCommand(
                    "ALTER TABLE `horarios` ADD UNIQUE KEY `uk_sede_grado_grupo_dia` " +
                    "(id_sede, id_grado, id_grupo, dia_semana)", conn).ExecuteNonQuery();
            }
            catch { }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Siembra horarios por grado para todas las sedes activas.
        // Solo inserta si todavía no existe (idempotente).
        //
        // Reglas institucionales:
        //   Preescolar  (orden=0)   → Entrada 09:00  Tardanza 09:15  Cierre 13:00
        //   1° – 5°     (orden 1-5) → Entrada 14:00  Tardanza 14:15  Cierre 17:30
        //   6° – 9°     (orden 6-9) → Entrada 08:00  Tardanza 08:15  Cierre 14:00
        //   10° – 11°   (orden≥10)  → Entrada 08:00  Tardanza 08:15  Cierre 14:00
        //                             + franja "Media Técnica" 16:00 / 16:15 / 18:00
        //
        // El sistema ya permite registrar huella 1 hora antes de la entrada puntual.
        // ─────────────────────────────────────────────────────────────────────
        private static void SembrarHorariosPorGrado(MySqlConnection conn)
        {
            // 1. Obtener sedes activas
            var sedes = new List<int>();
            using (var cmd = new MySqlCommand("SELECT id_sede FROM sedes WHERE estado = 1", conn))
            using (var dr = cmd.ExecuteReader())
                while (dr.Read()) sedes.Add(dr.GetInt32(0));

            if (sedes.Count == 0) return;

            // Limpiar horarios generados por seed anterior (sin id_grupo, marcados por seed)
            // Solo borra los que tienen id_grado (per-grado) y no tienen franjas personalizadas
            // para evitar borrar configuraciones manuales.
            // Si el único horario por grado viene del seed, sus franjas son solo "Media Técnica".
            // Detectamos si el índice viejo bloqueó la siembra: si hay grados sin horario para
            // algún día, borramos TODOS los horarios per-grado y resembramos limpio.
            bool siembraIncompleta = false;
            try
            {
                using var chk = new MySqlCommand(
                    @"SELECT COUNT(*) FROM grados g
                      CROSS JOIN sedes s
                      WHERE g.estado=1 AND s.estado=1
                        AND NOT EXISTS (
                            SELECT 1 FROM horarios h
                            WHERE h.id_sede=s.id_sede AND h.id_grado=g.id_grado
                              AND h.dia_semana='LUNES' AND h.id_grupo IS NULL AND h.activo=1
                        )", conn);
                siembraIncompleta = Convert.ToInt64(chk.ExecuteScalar()) > 0;
            }
            catch { siembraIncompleta = true; }

            if (siembraIncompleta)
            {
                // Borrar horarios per-grado sin grupo (los del seed) para resembrar limpio
                try
                {
                    new MySqlCommand(
                        "DELETE FROM horarios WHERE id_grado IS NOT NULL AND id_grupo IS NULL",
                        conn).ExecuteNonQuery();
                }
                catch { }
            }

            // 2. Obtener grados activos con su orden
            var grados = new List<(int IdGrado, int Orden)>();
            using (var cmd = new MySqlCommand("SELECT id_grado, orden_grado FROM grados WHERE estado = 1", conn))
            using (var dr = cmd.ExecuteReader())
                while (dr.Read()) grados.Add((dr.GetInt32(0), dr.GetInt32(1)));

            // Sedes que solo tienen primaria (grados 1°–5°)
            // BARRIOS UNIDOS NO está aquí → recibe todos los grados
            var sedesPrimaria = new HashSet<string>
                { "GABRIEL GONZALEZ", "LA JAGUA", "SOLEDAD HERMOSA" };

            // Obtener nombre de cada sede (normalizado) para decidir qué grados sembrar
            var nombrePorSede = new Dictionary<int, string>();
            using (var cmd = new MySqlCommand("SELECT id_sede, nombre_sede FROM sedes WHERE estado=1", conn))
            using (var dr = cmd.ExecuteReader())
                while (dr.Read()) nombrePorSede[dr.GetInt32(0)] = NormalizarNombreSede(dr.GetString(1));

            string[] dias = { "LUNES", "MARTES", "MIERCOLES", "JUEVES", "VIERNES" };

            foreach (int idSede in sedes)
            {
                bool soloPrimaria = nombrePorSede.TryGetValue(idSede, out var nomSede)
                                    && sedesPrimaria.Contains(nomSede);

                foreach (var (idGrado, orden) in grados)
                {
                    // Sedes de primaria: solo grados 1°–5° (orden 1–5)
                    if (soloPrimaria && (orden < 1 || orden > 5)) continue;

                    // Determinar tiempos según rango de grado
                    string entrada, tarde, cierre;
                    if (orden == 0)
                    { entrada = "09:00"; tarde = "09:15"; cierre = "13:00"; }       // Preescolar
                    else if (orden <= 5)
                    { entrada = "14:00"; tarde = "14:15"; cierre = "17:30"; }       // 1° – 5°
                    else
                    { entrada = "08:00"; tarde = "08:15"; cierre = "14:00"; }       // 6° en adelante

                    bool esMedia = orden >= 10; // Décimo y Undécimo

                    foreach (string dia in dias)
                    {
                        try
                        {
                            // INSERT IGNORE: si ya existe la combinación, la omite sin error
                            using (var ins = new MySqlCommand(
                                @"INSERT IGNORE INTO horarios (id_sede, id_grado, id_grupo, dia_semana,
                                    hora_inicio, hora_limite_tarde, hora_cierre_ingreso, activo)
                                  VALUES (@s, @g, NULL, @d, @ent, @tar, @cie, 1)",
                                conn))
                            {
                                ins.Parameters.AddWithValue("@s",   idSede);
                                ins.Parameters.AddWithValue("@g",   idGrado);
                                ins.Parameters.AddWithValue("@d",   dia);
                                ins.Parameters.AddWithValue("@ent", entrada);
                                ins.Parameters.AddWithValue("@tar", tarde);
                                ins.Parameters.AddWithValue("@cie", cierre);
                                ins.ExecuteNonQuery();
                            }

                            // LAST_INSERT_ID() == 0 cuando INSERT IGNORE omitió la fila
                            long idHorario;
                            using (var lid = new MySqlCommand("SELECT LAST_INSERT_ID()", conn))
                                idHorario = Convert.ToInt64(lid.ExecuteScalar());

                            // Franja Media Técnica para 10° y 11° (solo si se insertó la fila)
                            if (esMedia && idHorario > 0)
                            {
                                using var fIns = new MySqlCommand(
                                    @"INSERT IGNORE INTO franjas_horario
                                        (id_horario, nombre, hora_inicio, hora_limite_tarde, hora_cierre_ingreso, orden, activo)
                                      VALUES (@h, 'Media Técnica', '16:00', '16:15', '18:00', 1, 1)",
                                    conn);
                                fIns.Parameters.AddWithValue("@h", idHorario);
                                fIns.ExecuteNonQuery();
                            }
                        }
                        catch { }
                    }
                }
            }
        }

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

            // grupos (los registros se crean dinámicamente en SembrarGruposPorSede)
            @"CREATE TABLE IF NOT EXISTS grupos (
                id_grupo INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                id_sede INT,
                id_grado INT,
                nombre_grupo VARCHAR(50) NOT NULL,
                estado TINYINT(1) DEFAULT 1,
                fecha_creacion DATETIME DEFAULT CURRENT_TIMESTAMP
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",

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
