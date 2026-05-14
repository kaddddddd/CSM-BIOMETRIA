// ============================================================
// CSMBiometricoWPF.Services - Sincronización MySQL ↔ SQLite
// Archivo: Services/SyncService.cs
// ============================================================
using System;
using System.Collections.Generic;
using CSMBiometricoWPF.Data;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace CSMBiometricoWPF.Services
{
    /// <summary>
    /// Maneja la sincronización bidireccional entre MySQL (primario) y SQLite (local/offline).
    /// MySQL → SQLite : huellas, estudiantes, sedes, horarios, franjas, excepciones.
    /// SQLite → MySQL : registros de ingreso pendientes (guardados en modo offline).
    /// </summary>
    public static class SyncService
    {
        // ═══════════════════════════════════════════════════
        // SYNC COMPLETO MySQL → SQLite
        // ═══════════════════════════════════════════════════

        /// <summary>
        /// Sincroniza todos los datos necesarios para el modo offline desde MySQL a SQLite.
        /// Llamar al inicio cuando hay conexión disponible.
        /// </summary>
        public static void SincronizarDeMySQL(int? idInstitucion = null)
        {
            SincronizarSedes();
            SincronizarHuellas(idInstitucion);
            SincronizarEstudiantes(idInstitucion);
            SincronizarHorarios();
            SincronizarExcepciones();
        }

        // ── Sedes ────────────────────────────────────────────────────────
        private static void SincronizarSedes()
        {
            try
            {
                var sedes = new List<(int IdSede, int IdInst, string Nombre)>();
                using (var conn = ConexionDB.ObtenerConexion())
                using (var cmd = new MySqlCommand(
                    "SELECT id_sede, id_institucion, nombre_sede FROM sedes WHERE activo=1", conn))
                using (var dr = cmd.ExecuteReader())
                    while (dr.Read())
                        sedes.Add((
                            Convert.ToInt32(dr["id_sede"]),
                            Convert.ToInt32(dr["id_institucion"]),
                            dr["nombre_sede"].ToString()
                        ));

                using var lite = ConexionSQLite.ObtenerConexion();
                using var tx   = lite.BeginTransaction();
                Exec(lite, tx, "DELETE FROM cache_sedes");
                foreach (var s in sedes)
                {
                    var ins = lite.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = "INSERT INTO cache_sedes (id_sede, id_institucion, nombre_sede) VALUES (@s, @i, @n)";
                    ins.Parameters.AddWithValue("@s", s.IdSede);
                    ins.Parameters.AddWithValue("@i", s.IdInst);
                    ins.Parameters.AddWithValue("@n", s.Nombre);
                    ins.ExecuteNonQuery();
                }
                tx.Commit();
            }
            catch { }
        }

        // ── Huellas ──────────────────────────────────────────────────────
        private static void SincronizarHuellas(int? idInstitucion)
        {
            try
            {
                List<HuellaDigital> huellas = idInstitucion.HasValue
                    ? new HuellaRepository().ObtenerActivasPorInstitucion(idInstitucion.Value)
                    : new HuellaRepository().ObtenerTodasActivas();

                PersistirHuellas(huellas, idInstitucion);
            }
            catch { }
        }

        // ── Estudiantes ──────────────────────────────────────────────────
        private static void SincronizarEstudiantes(int? idInstitucion)
        {
            try
            {
                var estudiantes = new EstudianteRepository()
                    .ObtenerTodos(idInstitucion: idInstitucion, estado: "ACTIVO");

                using var lite = ConexionSQLite.ObtenerConexion();
                using var tx   = lite.BeginTransaction();

                if (idInstitucion.HasValue)
                {
                    var del = lite.CreateCommand();
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM cache_estudiantes WHERE id_institucion=@inst";
                    del.Parameters.AddWithValue("@inst", idInstitucion.Value);
                    del.ExecuteNonQuery();
                }
                else
                {
                    Exec(lite, tx, "DELETE FROM cache_estudiantes");
                }

                foreach (var e in estudiantes)
                {
                    var ins = lite.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = @"INSERT OR REPLACE INTO cache_estudiantes
                        (id_estudiante, identificacion, nombre, apellidos,
                         id_institucion, id_sede, id_grado, id_grupo,
                         nombre_institucion, nombre_sede, nombre_grado, nombre_grupo, foto)
                        VALUES (@id,@doc,@nom,@ap,@inst,@sede,@grado,@grupo,@ninst,@nsede,@ngrado,@ngrupo,@foto)";
                    ins.Parameters.AddWithValue("@id",     e.IdEstudiante);
                    ins.Parameters.AddWithValue("@doc",    e.Identificacion ?? "");
                    ins.Parameters.AddWithValue("@nom",    e.Nombre ?? "");
                    ins.Parameters.AddWithValue("@ap",     e.Apellidos ?? "");
                    ins.Parameters.AddWithValue("@inst",   e.IdInstitucion);
                    ins.Parameters.AddWithValue("@sede",   e.IdSede);
                    ins.Parameters.AddWithValue("@grado",  e.IdGrado);
                    ins.Parameters.AddWithValue("@grupo",  e.IdGrupo);
                    ins.Parameters.AddWithValue("@ninst",  e.NombreInstitucion ?? "");
                    ins.Parameters.AddWithValue("@nsede",  e.NombreSede ?? "");
                    ins.Parameters.AddWithValue("@ngrado", e.NombreGrado ?? "");
                    ins.Parameters.AddWithValue("@ngrupo", e.NombreGrupo ?? "");
                    ins.Parameters.AddWithValue("@foto",   (object?)e.Foto ?? DBNull.Value);
                    ins.ExecuteNonQuery();
                }
                tx.Commit();
            }
            catch { }
        }

        // ── Horarios y franjas ───────────────────────────────────────────
        private static void SincronizarHorarios()
        {
            try
            {
                var horarios = new List<(int Id, int Sede, int? Grado, int? Grupo, string Dia,
                                         string Ini, string Lim, string Cie)>();
                var franjas  = new List<(int Id, int IdHor, string Nom, string Ini,
                                         string Lim, string Cie, int Ord)>();

                using (var conn = ConexionDB.ObtenerConexion())
                {
                    using (var cmd = new MySqlCommand(
                        @"SELECT id_horario, id_sede, id_grado, id_grupo, dia_semana,
                                 hora_inicio, hora_limite_tarde, hora_cierre_ingreso
                          FROM horarios WHERE activo=1", conn))
                    using (var dr = cmd.ExecuteReader())
                        while (dr.Read())
                            horarios.Add((
                                Convert.ToInt32(dr["id_horario"]),
                                Convert.ToInt32(dr["id_sede"]),
                                dr["id_grado"]  == DBNull.Value ? (int?)null : Convert.ToInt32(dr["id_grado"]),
                                dr["id_grupo"]  == DBNull.Value ? (int?)null : Convert.ToInt32(dr["id_grupo"]),
                                dr["dia_semana"].ToString(),
                                dr["hora_inicio"].ToString(),
                                dr["hora_limite_tarde"].ToString(),
                                dr["hora_cierre_ingreso"].ToString()
                            ));

                    using (var cmd2 = new MySqlCommand(
                        @"SELECT id_franja, id_horario, nombre,
                                 hora_inicio, hora_limite_tarde, hora_cierre_ingreso, orden
                          FROM franjas_horario WHERE activo=1", conn))
                    using (var dr2 = cmd2.ExecuteReader())
                        while (dr2.Read())
                            franjas.Add((
                                Convert.ToInt32(dr2["id_franja"]),
                                Convert.ToInt32(dr2["id_horario"]),
                                dr2["nombre"].ToString(),
                                dr2["hora_inicio"].ToString(),
                                dr2["hora_limite_tarde"].ToString(),
                                dr2["hora_cierre_ingreso"].ToString(),
                                Convert.ToInt32(dr2["orden"])
                            ));
                }

                using var lite = ConexionSQLite.ObtenerConexion();
                using var tx   = lite.BeginTransaction();
                Exec(lite, tx, "DELETE FROM cache_horarios");
                Exec(lite, tx, "DELETE FROM cache_franjas");

                foreach (var h in horarios)
                {
                    var ins = lite.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = @"INSERT INTO cache_horarios
                        (id_horario, id_sede, id_grado, id_grupo, dia_semana,
                         hora_inicio, hora_limite_tarde, hora_cierre_ingreso)
                        VALUES (@id,@sede,@grado,@grupo,@dia,@ini,@lim,@cie)";
                    ins.Parameters.AddWithValue("@id",   h.Id);
                    ins.Parameters.AddWithValue("@sede", h.Sede);
                    ins.Parameters.AddWithValue("@grado",(object?)h.Grado ?? DBNull.Value);
                    ins.Parameters.AddWithValue("@grupo",(object?)h.Grupo ?? DBNull.Value);
                    ins.Parameters.AddWithValue("@dia",  h.Dia);
                    ins.Parameters.AddWithValue("@ini",  h.Ini);
                    ins.Parameters.AddWithValue("@lim",  h.Lim);
                    ins.Parameters.AddWithValue("@cie",  h.Cie);
                    ins.ExecuteNonQuery();
                }

                foreach (var f in franjas)
                {
                    var ins = lite.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = @"INSERT INTO cache_franjas
                        (id_franja, id_horario, nombre, hora_inicio,
                         hora_limite_tarde, hora_cierre_ingreso, orden)
                        VALUES (@id,@hor,@nom,@ini,@lim,@cie,@ord)";
                    ins.Parameters.AddWithValue("@id",  f.Id);
                    ins.Parameters.AddWithValue("@hor", f.IdHor);
                    ins.Parameters.AddWithValue("@nom", f.Nom);
                    ins.Parameters.AddWithValue("@ini", f.Ini);
                    ins.Parameters.AddWithValue("@lim", f.Lim);
                    ins.Parameters.AddWithValue("@cie", f.Cie);
                    ins.Parameters.AddWithValue("@ord", f.Ord);
                    ins.ExecuteNonQuery();
                }
                tx.Commit();
            }
            catch { }
        }

        // ── Excepciones de horario ───────────────────────────────────────
        private static void SincronizarExcepciones()
        {
            try
            {
                var excepciones = new List<(int Id, int? Sede, int? Grado, int? Inst, string Fecha)>();
                var franjas     = new List<(int Id, int IdExc, string Nom, string Ini,
                                            string Lim, string Cie, int Ord)>();

                using (var conn = ConexionDB.ObtenerConexion())
                {
                    // Traer excepciones desde hoy en adelante
                    using (var cmd = new MySqlCommand(
                        @"SELECT id_excepcion, id_sede, id_grado, id_institucion, fecha_excepcion
                          FROM horario_excepciones
                          WHERE activo=1 AND fecha_excepcion >= CURDATE()", conn))
                    using (var dr = cmd.ExecuteReader())
                        while (dr.Read())
                            excepciones.Add((
                                Convert.ToInt32(dr["id_excepcion"]),
                                dr["id_sede"]        == DBNull.Value ? (int?)null : Convert.ToInt32(dr["id_sede"]),
                                dr["id_grado"]       == DBNull.Value ? (int?)null : Convert.ToInt32(dr["id_grado"]),
                                dr["id_institucion"] == DBNull.Value ? (int?)null : Convert.ToInt32(dr["id_institucion"]),
                                Convert.ToDateTime(dr["fecha_excepcion"]).ToString("yyyy-MM-dd")
                            ));

                    if (excepciones.Count > 0)
                    {
                        var ids = string.Join(",", excepciones.ConvertAll(e => e.Id.ToString()));
                        using var cmd2 = new MySqlCommand(
                            $@"SELECT id_franja_exc, id_excepcion, nombre,
                                      hora_inicio, hora_limite_tarde, hora_cierre_ingreso, orden
                               FROM franjas_excepcion WHERE id_excepcion IN ({ids})", conn);
                        using var dr2 = cmd2.ExecuteReader();
                        while (dr2.Read())
                            franjas.Add((
                                Convert.ToInt32(dr2["id_franja_exc"]),
                                Convert.ToInt32(dr2["id_excepcion"]),
                                dr2["nombre"].ToString(),
                                dr2["hora_inicio"].ToString(),
                                dr2["hora_limite_tarde"].ToString(),
                                dr2["hora_cierre_ingreso"].ToString(),
                                Convert.ToInt32(dr2["orden"])
                            ));
                    }
                }

                using var lite = ConexionSQLite.ObtenerConexion();
                using var tx   = lite.BeginTransaction();
                Exec(lite, tx, "DELETE FROM cache_excepciones");
                Exec(lite, tx, "DELETE FROM cache_franjas_excepcion");

                foreach (var ex in excepciones)
                {
                    var ins = lite.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = @"INSERT INTO cache_excepciones
                        (id_excepcion, id_sede, id_grado, id_institucion, fecha_excepcion)
                        VALUES (@id,@sede,@grado,@inst,@fecha)";
                    ins.Parameters.AddWithValue("@id",    ex.Id);
                    ins.Parameters.AddWithValue("@sede",  (object?)ex.Sede ?? DBNull.Value);
                    ins.Parameters.AddWithValue("@grado", (object?)ex.Grado ?? DBNull.Value);
                    ins.Parameters.AddWithValue("@inst",  (object?)ex.Inst ?? DBNull.Value);
                    ins.Parameters.AddWithValue("@fecha", ex.Fecha);
                    ins.ExecuteNonQuery();
                }

                foreach (var f in franjas)
                {
                    var ins = lite.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = @"INSERT INTO cache_franjas_excepcion
                        (id_franja, id_excepcion, nombre, hora_inicio,
                         hora_limite_tarde, hora_cierre_ingreso, orden)
                        VALUES (@id,@exc,@nom,@ini,@lim,@cie,@ord)";
                    ins.Parameters.AddWithValue("@id",  f.Id);
                    ins.Parameters.AddWithValue("@exc", f.IdExc);
                    ins.Parameters.AddWithValue("@nom", f.Nom);
                    ins.Parameters.AddWithValue("@ini", f.Ini);
                    ins.Parameters.AddWithValue("@lim", f.Lim);
                    ins.Parameters.AddWithValue("@cie", f.Cie);
                    ins.Parameters.AddWithValue("@ord", f.Ord);
                    ins.ExecuteNonQuery();
                }
                tx.Commit();
            }
            catch { }
        }

        // ═══════════════════════════════════════════════════
        // SYNC SQLite → MySQL (registros pendientes)
        // ═══════════════════════════════════════════════════

        /// <summary>
        /// Envía los registros de ingreso guardados en modo offline a MySQL.
        /// Retorna la cantidad de registros sincronizados exitosamente.
        /// </summary>
        public static int SincronizarHaciaMySQL()
        {
            var pendientes = new List<(long Id, RegistroIngreso Reg)>();
            try
            {
                using var lite = ConexionSQLite.ObtenerConexion();
                using var cmd  = lite.CreateCommand();
                cmd.CommandText = "SELECT * FROM registros_pendientes WHERE sincronizado=0";
                using var dr = cmd.ExecuteReader();
                while (dr.Read())
                    pendientes.Add((
                        Convert.ToInt64(dr["id"]),
                        new RegistroIngreso
                        {
                            IdEstudiante      = Convert.ToInt32(dr["id_estudiante"]),
                            IdSede            = Convert.ToInt32(dr["id_sede"]),
                            FechaIngreso      = DateTime.Parse(dr["fecha_ingreso"].ToString()),
                            HoraIngreso       = TimeSpan.Parse(dr["hora_ingreso"].ToString()),
                            EstadoIngreso     = (EstadoIngreso)Enum.Parse(typeof(EstadoIngreso), dr["estado_ingreso"].ToString()),
                            PuntajeBiometrico = Convert.ToSingle(dr["puntaje_biometrico"]),
                            NombreFranja      = dr["nombre_franja"] == DBNull.Value ? null : dr["nombre_franja"].ToString()
                        }
                    ));
            }
            catch { return 0; }

            if (pendientes.Count == 0) return 0;

            var repo = new RegistroIngresoRepository();
            int sincronizados = 0;
            var idsOk = new List<long>();

            foreach (var (id, reg) in pendientes)
            {
                try
                {
                    repo.Guardar(reg);
                    idsOk.Add(id);
                    sincronizados++;
                }
                catch { }
            }

            if (idsOk.Count > 0)
            {
                try
                {
                    using var lite2 = ConexionSQLite.ObtenerConexion();
                    using var upd   = lite2.CreateCommand();
                    upd.CommandText = $"UPDATE registros_pendientes SET sincronizado=1 WHERE id IN ({string.Join(",", idsOk)})";
                    upd.ExecuteNonQuery();
                }
                catch { }
            }
            return sincronizados;
        }

        // ═══════════════════════════════════════════════════
        // HELPERS PARA PERSISTIR / LEER HUELLAS
        // ═══════════════════════════════════════════════════

        /// <summary>
        /// Persiste una lista de huellas en SQLite (reemplaza las existentes de esa institución o todas).
        /// </summary>
        public static void PersistirHuellas(List<HuellaDigital> huellas, int? idInstitucion = null)
        {
            try
            {
                using var lite = ConexionSQLite.ObtenerConexion();
                using var tx   = lite.BeginTransaction();

                if (idInstitucion.HasValue)
                {
                    var del = lite.CreateCommand();
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM cache_huellas WHERE id_institucion=@inst";
                    del.Parameters.AddWithValue("@inst", idInstitucion.Value);
                    del.ExecuteNonQuery();
                }
                else
                {
                    Exec(lite, tx, "DELETE FROM cache_huellas");
                }

                foreach (var h in huellas)
                {
                    var ins = lite.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = @"INSERT OR REPLACE INTO cache_huellas
                        (id_huella, id_estudiante, template, calidad,
                         nombre_estudiante, id_sede, id_institucion)
                        VALUES (@id,@est,@tmpl,@cal,@nom,@sede,@inst)";
                    ins.Parameters.AddWithValue("@id",   h.IdHuella);
                    ins.Parameters.AddWithValue("@est",  h.IdEstudiante);
                    ins.Parameters.AddWithValue("@tmpl", h.TemplateBiometrico);
                    ins.Parameters.AddWithValue("@cal",  h.Calidad);
                    ins.Parameters.AddWithValue("@nom",  h.NombreEstudiante ?? "");
                    ins.Parameters.AddWithValue("@sede", h.IdSede);
                    ins.Parameters.AddWithValue("@inst", h.IdInstitucion);
                    ins.ExecuteNonQuery();
                }
                tx.Commit();
            }
            catch { }
        }

        /// <summary>
        /// Carga huellas desde SQLite (usado cuando MySQL no está disponible).
        /// </summary>
        public static List<HuellaDigital> CargarHuellasDeSQLite(int? idInstitucion = null)
        {
            var lista = new List<HuellaDigital>();
            try
            {
                using var lite = ConexionSQLite.ObtenerConexion();
                using var cmd  = lite.CreateCommand();
                if (idInstitucion.HasValue)
                {
                    cmd.CommandText = "SELECT * FROM cache_huellas WHERE id_institucion=@inst";
                    cmd.Parameters.AddWithValue("@inst", idInstitucion.Value);
                }
                else
                {
                    cmd.CommandText = "SELECT * FROM cache_huellas";
                }
                using var dr = cmd.ExecuteReader();
                while (dr.Read())
                    lista.Add(new HuellaDigital
                    {
                        IdHuella           = Convert.ToInt32(dr["id_huella"]),
                        IdEstudiante       = Convert.ToInt32(dr["id_estudiante"]),
                        TemplateBiometrico = (byte[])dr["template"],
                        Calidad            = Convert.ToInt32(dr["calidad"]),
                        NombreEstudiante   = dr["nombre_estudiante"].ToString(),
                        IdSede             = Convert.ToInt32(dr["id_sede"]),
                        IdInstitucion      = Convert.ToInt32(dr["id_institucion"]),
                        Activo             = true
                    });
            }
            catch { }
            return lista;
        }

        // ═══════════════════════════════════════════════════
        // HELPER: id_institucion de una sede (sin MySQL)
        // ═══════════════════════════════════════════════════

        public static int? ObtenerInstitucionDeSede(int idSede)
        {
            try
            {
                using var lite = ConexionSQLite.ObtenerConexion();
                using var cmd  = lite.CreateCommand();
                cmd.CommandText = "SELECT id_institucion FROM cache_sedes WHERE id_sede=@s LIMIT 1";
                cmd.Parameters.AddWithValue("@s", idSede);
                var res = cmd.ExecuteScalar();
                return res == null || res == DBNull.Value ? (int?)null : Convert.ToInt32(res);
            }
            catch { return null; }
        }

        // ── Utilidad interna ─────────────────────────────────────────────
        private static void Exec(SqliteConnection conn, SqliteTransaction tx, string sql)
        {
            var cmd = conn.CreateCommand();
            cmd.Transaction  = tx;
            cmd.CommandText  = sql;
            cmd.ExecuteNonQuery();
        }
    }
}
