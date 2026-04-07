// ============================================================
// CSMBiometrico.Repositories - Capa de Repositorios
// Archivo: Repositories/Repositorios.cs
// ============================================================

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using CSMBiometricoWPF.Data;
using CSMBiometricoWPF.Models;

namespace CSMBiometricoWPF.Repositories
{
    // ═══════════════════════════════════════════════════
    // REPOSITORIO DE USUARIOS
    // ═══════════════════════════════════════════════════
    public class UsuarioRepository
    {
        public Usuario Autenticar(string username, string password)
        {
            string hash = HashPassword(password);
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string sql = @"SELECT u.*, r.nombre_rol, i.nombre as nombre_institucion
                               FROM usuarios u
                               INNER JOIN roles r ON u.id_rol = r.id_rol
                               LEFT JOIN instituciones i ON u.id_institucion = i.id_institucion
                               WHERE u.username = @user AND u.password_hash = @pwd
                               AND u.estado = 1 AND u.bloqueado = 0";
                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@user", username);
                    cmd.Parameters.AddWithValue("@pwd", hash);
                    using (var dr = cmd.ExecuteReader())
                    {
                        if (dr.Read()) return MapearUsuario(dr);
                    }
                }
            }
            return null;
        }

        public Usuario BuscarPorUsername(string username)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string sql = @"SELECT u.*, r.nombre_rol, i.nombre as nombre_institucion
                               FROM usuarios u
                               INNER JOIN roles r ON u.id_rol = r.id_rol
                               LEFT JOIN instituciones i ON u.id_institucion = i.id_institucion
                               WHERE u.username = @user AND u.estado = 1";
                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@user", username);
                    using (var dr = cmd.ExecuteReader())
                        if (dr.Read()) return MapearUsuario(dr);
                }
            }
            return null;
        }

        public List<Usuario> ObtenerTodos()
        {
            var lista = new List<Usuario>();
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string sql = @"SELECT u.*, r.nombre_rol, i.nombre as nombre_institucion
                               FROM usuarios u
                               INNER JOIN roles r ON u.id_rol = r.id_rol
                               LEFT JOIN instituciones i ON u.id_institucion = i.id_institucion
                               ORDER BY u.nombre_completo";
                using (var cmd = new SqliteCommand(sql, conn))
                using (var dr = cmd.ExecuteReader())
                    while (dr.Read()) lista.Add(MapearUsuario(dr));
            }
            return lista;
        }

        public bool Guardar(Usuario u, string password = null)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            {
                if (u.IdUsuario == 0)
                {
                    string sql = @"INSERT INTO usuarios 
                        (id_rol, id_institucion, username, password_hash, nombre_completo, email, estado)
                        VALUES (@rol, @inst, @user, @pwd, @nombre, @email, @estado)";
                    using (var cmd = new SqliteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@rol", u.IdRol);
                        cmd.Parameters.AddWithValue("@inst", (object)u.IdInstitucion ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@user", u.Username);
                        cmd.Parameters.AddWithValue("@pwd", HashPassword(password ?? "Temporal123!"));
                        cmd.Parameters.AddWithValue("@nombre", u.NombreCompleto);
                        cmd.Parameters.AddWithValue("@email", (object)u.Email ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@estado", u.Estado);
                        cmd.ExecuteNonQuery();
                    }
                }
                else
                {
                    string sql = @"UPDATE usuarios SET id_rol=@rol, id_institucion=@inst,
                        username=@user, nombre_completo=@nombre, email=@email, estado=@estado
                        WHERE id_usuario=@id";
                    using (var cmd = new SqliteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", u.IdUsuario);
                        cmd.Parameters.AddWithValue("@rol", u.IdRol);
                        cmd.Parameters.AddWithValue("@inst", (object)u.IdInstitucion ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@user", u.Username);
                        cmd.Parameters.AddWithValue("@nombre", u.NombreCompleto);
                        cmd.Parameters.AddWithValue("@email", (object)u.Email ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@estado", u.Estado);
                        cmd.ExecuteNonQuery();
                    }
                    if (!string.IsNullOrEmpty(password))
                        CambiarPassword(u.IdUsuario, password);
                }
            }
            return true;
        }

        public bool CambiarPassword(int idUsuario, string nuevaPassword)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string sql = "UPDATE usuarios SET password_hash=@pwd WHERE id_usuario=@id";
                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@pwd", HashPassword(nuevaPassword));
                    cmd.Parameters.AddWithValue("@id", idUsuario);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        public bool Eliminar(int idUsuario)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string sql = "UPDATE usuarios SET estado=0 WHERE id_usuario=@id";
                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", idUsuario);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        public void RegistrarUltimoLogin(int idUsuario)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string sql = "UPDATE usuarios SET ultimo_login=datetime('now'), intentos_fallidos=0 WHERE id_usuario=@id";
                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", idUsuario);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public List<Rol> ObtenerRoles()
        {
            var lista = new List<Rol>();
            using (var conn = ConexionDB.ObtenerConexion())
            using (var cmd = new SqliteCommand("SELECT * FROM roles WHERE activo=1", conn))
            using (var dr = cmd.ExecuteReader())
                while (dr.Read())
                    lista.Add(new Rol
                    {
                        IdRol = Convert.ToInt32(dr["id_rol"]),
                        NombreRol = dr["nombre_rol"].ToString(),
                        Descripcion = dr["descripcion"].ToString()
                    });
            return lista;
        }

        private string HashPassword(string password)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(password);
                byte[] hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }

        private Usuario MapearUsuario(SqliteDataReader dr)
        {
            return new Usuario
            {
                IdUsuario = Convert.ToInt32(dr["id_usuario"]),
                IdRol = Convert.ToInt32(dr["id_rol"]),
                IdInstitucion = dr["id_institucion"] == DBNull.Value ? (int?)null : Convert.ToInt32(dr["id_institucion"]),
                Username = dr["username"].ToString(),
                NombreCompleto = dr["nombre_completo"].ToString(),
                Email = dr["email"]?.ToString(),
                Estado = Convert.ToBoolean(dr["estado"]),
                Bloqueado = Convert.ToBoolean(dr["bloqueado"]),
                NombreRol = dr["nombre_rol"].ToString(),
                NombreInstitucion = dr["nombre_institucion"]?.ToString(),
                UltimoLogin = dr["ultimo_login"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(dr["ultimo_login"])
            };
        }
    }

    // ═══════════════════════════════════════════════════
    // REPOSITORIO DE INSTITUCIONES
    // ═══════════════════════════════════════════════════
    public class InstitucionRepository
    {
        public List<Institucion> ObtenerTodas(bool soloActivas = true)
        {
            var lista = new List<Institucion>();
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string filtro = soloActivas ? "WHERE estado=1" : "";
                string sql = $"SELECT * FROM instituciones {filtro} ORDER BY nombre";
                using (var cmd = new SqliteCommand(sql, conn))
                using (var dr = cmd.ExecuteReader())
                    while (dr.Read())
                        lista.Add(new Institucion
                        {
                            IdInstitucion = Convert.ToInt32(dr["id_institucion"]),
                            Nombre = dr["nombre"].ToString(),
                            Direccion = dr["direccion"]?.ToString(),
                            Telefono = dr["telefono"]?.ToString(),
                            Estado = Convert.ToBoolean(dr["estado"]),
                            FechaCreacion = Convert.ToDateTime(dr["fecha_creacion"])
                        });
            }
            return lista;
        }

        public Institucion ObtenerPorId(int id)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            using (var cmd = new SqliteCommand("SELECT * FROM instituciones WHERE id_institucion=@id", conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                using (var dr = cmd.ExecuteReader())
                    if (dr.Read())
                        return new Institucion
                        {
                            IdInstitucion = Convert.ToInt32(dr["id_institucion"]),
                            Nombre = dr["nombre"].ToString(),
                            Direccion = dr["direccion"]?.ToString(),
                            Telefono = dr["telefono"]?.ToString(),
                            Estado = Convert.ToBoolean(dr["estado"])
                        };
            }
            return null;
        }

        public bool Guardar(Institucion inst)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            {
                if (inst.IdInstitucion == 0)
                {
                    string sql = "INSERT INTO instituciones (nombre, direccion, telefono, estado) VALUES (@n,@d,@t,@e)";
                    using (var cmd = new SqliteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@n", inst.Nombre);
                        cmd.Parameters.AddWithValue("@d", (object)inst.Direccion ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@t", (object)inst.Telefono ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@e", inst.Estado);
                        cmd.ExecuteNonQuery();
                    }
                }
                else
                {
                    string sql = "UPDATE instituciones SET nombre=@n, direccion=@d, telefono=@t, estado=@e WHERE id_institucion=@id";
                    using (var cmd = new SqliteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", inst.IdInstitucion);
                        cmd.Parameters.AddWithValue("@n", inst.Nombre);
                        cmd.Parameters.AddWithValue("@d", (object)inst.Direccion ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@t", (object)inst.Telefono ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@e", inst.Estado);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            return true;
        }

        public bool Eliminar(int id)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            using (var cmd = new SqliteCommand("UPDATE instituciones SET estado=0 WHERE id_institucion=@id", conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                return cmd.ExecuteNonQuery() > 0;
            }
        }
    }

    // ═══════════════════════════════════════════════════
    // REPOSITORIO DE SEDES
    // ═══════════════════════════════════════════════════
    public class SedeRepository
    {
        public List<Sede> ObtenerPorInstitucion(int idInstitucion, bool soloActivas = true)
        {
            var lista = new List<Sede>();
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string sql = @"SELECT s.*, i.nombre as nombre_institucion
                               FROM sedes s
                               INNER JOIN instituciones i ON s.id_institucion = i.id_institucion
                               WHERE s.id_institucion=@id"
                           + (soloActivas ? " AND s.estado=1" : "")
                           + " ORDER BY s.nombre_sede";
                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", idInstitucion);
                    using (var dr = cmd.ExecuteReader())
                        while (dr.Read())
                            lista.Add(MapearSede(dr));
                }
            }
            return lista;
        }

        public List<Sede> ObtenerTodas(bool soloActivas = true)
        {
            var lista = new List<Sede>();
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string sql = @"SELECT s.*, i.nombre as nombre_institucion
                               FROM sedes s
                               INNER JOIN instituciones i ON s.id_institucion = i.id_institucion"
                           + (soloActivas ? " WHERE s.estado=1" : "")
                           + " ORDER BY i.nombre, s.nombre_sede";
                using (var cmd = new SqliteCommand(sql, conn))
                using (var dr = cmd.ExecuteReader())
                    while (dr.Read()) lista.Add(MapearSede(dr));
            }
            return lista;
        }

        public bool Activar(int id)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            using (var cmd = new SqliteCommand("UPDATE sedes SET estado=1 WHERE id_sede=@id", conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        public bool Guardar(Sede sede)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            {
                if (sede.IdSede == 0)
                {
                    string sql = "INSERT INTO sedes (id_institucion, nombre_sede, direccion, estado) VALUES (@i,@n,@d,@e)";
                    using (var cmd = new SqliteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@i", sede.IdInstitucion);
                        cmd.Parameters.AddWithValue("@n", sede.NombreSede);
                        cmd.Parameters.AddWithValue("@d", (object)sede.Direccion ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@e", sede.Estado);
                        cmd.ExecuteNonQuery();
                    }
                }
                else
                {
                    string sql = "UPDATE sedes SET id_institucion=@i, nombre_sede=@n, direccion=@d, estado=@e WHERE id_sede=@id";
                    using (var cmd = new SqliteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", sede.IdSede);
                        cmd.Parameters.AddWithValue("@i", sede.IdInstitucion);
                        cmd.Parameters.AddWithValue("@n", sede.NombreSede);
                        cmd.Parameters.AddWithValue("@d", (object)sede.Direccion ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@e", sede.Estado);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            return true;
        }

        public bool Eliminar(int id)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            using (var cmd = new SqliteCommand("UPDATE sedes SET estado=0 WHERE id_sede=@id", conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        private Sede MapearSede(SqliteDataReader dr) => new Sede
        {
            IdSede = Convert.ToInt32(dr["id_sede"]),
            IdInstitucion = Convert.ToInt32(dr["id_institucion"]),
            NombreSede = dr["nombre_sede"].ToString(),
            Direccion = dr["direccion"]?.ToString(),
            Estado = Convert.ToBoolean(dr["estado"]),
            NombreInstitucion = dr["nombre_institucion"].ToString()
        };
    }

    // ═══════════════════════════════════════════════════
    // REPOSITORIO DE GRADOS
    // ═══════════════════════════════════════════════════
    public class GradoRepository
    {
        public List<Grado> ObtenerTodos()
        {
            var lista = new List<Grado>();
            using (var conn = ConexionDB.ObtenerConexion())
            using (var cmd = new SqliteCommand("SELECT * FROM grados WHERE estado=1 ORDER BY orden_grado", conn))
            using (var dr = cmd.ExecuteReader())
                while (dr.Read())
                    lista.Add(new Grado
                    {
                        IdGrado = Convert.ToInt32(dr["id_grado"]),
                        NombreGrado = dr["nombre_grado"].ToString(),
                        OrdenGrado = Convert.ToInt32(dr["orden_grado"])
                    });
            return lista;
        }

        public bool Guardar(Grado grado)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            {
                if (grado.IdGrado == 0)
                {
                    string sql = "INSERT INTO grados (nombre_grado, orden_grado, estado) VALUES (@n,@o,1)";
                    using (var cmd = new SqliteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@n", grado.NombreGrado);
                        cmd.Parameters.AddWithValue("@o", grado.OrdenGrado);
                        cmd.ExecuteNonQuery();
                    }
                }
                else
                {
                    string sql = "UPDATE grados SET nombre_grado=@n, orden_grado=@o WHERE id_grado=@id";
                    using (var cmd = new SqliteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", grado.IdGrado);
                        cmd.Parameters.AddWithValue("@n", grado.NombreGrado);
                        cmd.Parameters.AddWithValue("@o", grado.OrdenGrado);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            return true;
        }

        public bool Eliminar(int id)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            using (var cmd = new SqliteCommand("UPDATE grados SET estado=0 WHERE id_grado=@id", conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                return cmd.ExecuteNonQuery() > 0;
            }
        }
    }

    // ═══════════════════════════════════════════════════
    // REPOSITORIO DE GRUPOS
    // ═══════════════════════════════════════════════════
    public class GrupoRepository
    {
        public List<Grupo> ObtenerTodos()
        {
            var lista = new List<Grupo>();
            using (var conn = ConexionDB.ObtenerConexion())
            using (var cmd = new SqliteCommand("SELECT * FROM grupos WHERE estado=1 ORDER BY nombre_grupo", conn))
            using (var dr = cmd.ExecuteReader())
                while (dr.Read())
                    lista.Add(new Grupo
                    {
                        IdGrupo = Convert.ToInt32(dr["id_grupo"]),
                        NombreGrupo = dr["nombre_grupo"].ToString()
                    });
            return lista;
        }

        public bool Guardar(Grupo grupo)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            {
                if (grupo.IdGrupo == 0)
                {
                    using (var cmd = new SqliteCommand("INSERT INTO grupos (nombre_grupo, estado) VALUES (@n,1)", conn))
                    {
                        cmd.Parameters.AddWithValue("@n", grupo.NombreGrupo);
                        cmd.ExecuteNonQuery();
                    }
                }
                else
                {
                    using (var cmd = new SqliteCommand("UPDATE grupos SET nombre_grupo=@n WHERE id_grupo=@id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", grupo.IdGrupo);
                        cmd.Parameters.AddWithValue("@n", grupo.NombreGrupo);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            return true;
        }

        public bool Eliminar(int id)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            using (var cmd = new SqliteCommand("UPDATE grupos SET estado=0 WHERE id_grupo=@id", conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                return cmd.ExecuteNonQuery() > 0;
            }
        }
    }

    // ═══════════════════════════════════════════════════
    // REPOSITORIO DE ESTUDIANTES
    // ═══════════════════════════════════════════════════
    public class EstudianteRepository
    {
        public List<Estudiante> ObtenerTodos(int? idSede = null, int? idGrado = null, string estado = null, int? idInstitucion = null)
        {
            var lista = new List<Estudiante>();
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string sql = @"SELECT e.*, i.nombre as nombre_inst, s.nombre_sede,
                                      g.nombre_grado, gr.nombre_grupo,
                                      (SELECT COUNT(*) FROM huellas_digitales h WHERE h.id_estudiante=e.id_estudiante AND h.activo=1) > 0 as tiene_huella
                               FROM estudiantes e
                               INNER JOIN instituciones i ON e.id_institucion = i.id_institucion
                               INNER JOIN sedes s ON e.id_sede = s.id_sede
                               INNER JOIN grados g ON e.id_grado = g.id_grado
                               INNER JOIN grupos gr ON e.id_grupo = gr.id_grupo
                               WHERE 1=1";
                if (idInstitucion.HasValue) sql += " AND e.id_institucion=@inst";
                if (idSede.HasValue)        sql += " AND e.id_sede=@sede";
                if (idGrado.HasValue)       sql += " AND e.id_grado=@grado";
                if (!string.IsNullOrEmpty(estado)) sql += " AND e.estado=@estado";
                sql += " ORDER BY e.apellidos, e.nombre";

                using (var cmd = new SqliteCommand(sql, conn))
                {
                    if (idInstitucion.HasValue) cmd.Parameters.AddWithValue("@inst", idInstitucion.Value);
                    if (idSede.HasValue)        cmd.Parameters.AddWithValue("@sede", idSede.Value);
                    if (idGrado.HasValue)       cmd.Parameters.AddWithValue("@grado", idGrado.Value);
                    if (!string.IsNullOrEmpty(estado)) cmd.Parameters.AddWithValue("@estado", estado);
                    using (var dr = cmd.ExecuteReader())
                        while (dr.Read()) lista.Add(MapearEstudiante(dr));
                }
            }
            return lista;
        }

        public Estudiante ObtenerPorId(int id)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string sql = @"SELECT e.*, i.nombre as nombre_inst, s.nombre_sede, 
                                      g.nombre_grado, gr.nombre_grupo,
                                      0 as tiene_huella
                               FROM estudiantes e
                               INNER JOIN instituciones i ON e.id_institucion = i.id_institucion
                               INNER JOIN sedes s ON e.id_sede = s.id_sede
                               INNER JOIN grados g ON e.id_grado = g.id_grado
                               INNER JOIN grupos gr ON e.id_grupo = gr.id_grupo
                               WHERE e.id_estudiante=@id";
                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    using (var dr = cmd.ExecuteReader())
                        if (dr.Read()) return MapearEstudiante(dr);
                }
            }
            return null;
        }

        public bool Guardar(Estudiante e)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            {
                if (e.IdEstudiante == 0)
                {
                    string sql = @"INSERT INTO estudiantes 
                        (identificacion, nombre, apellidos, id_institucion, id_sede, id_grado, id_grupo, 
                         telefono, email, foto, estado, fecha_matricula)
                        VALUES (@id,@n,@ap,@inst,@sede,@grado,@grupo,@tel,@email,@foto,@estado,@fmat)";
                    using (var cmd = new SqliteCommand(sql, conn))
                    {
                        AgregarParametrosEstudiante(cmd, e);
                        cmd.ExecuteNonQuery();
                    }
                }
                else
                {
                    string sql = @"UPDATE estudiantes SET identificacion=@id, nombre=@n, apellidos=@ap,
                        id_institucion=@inst, id_sede=@sede, id_grado=@grado, id_grupo=@grupo,
                        telefono=@tel, email=@email, foto=@foto, estado=@estado, fecha_matricula=@fmat
                        WHERE id_estudiante=@eid";
                    using (var cmd = new SqliteCommand(sql, conn))
                    {
                        AgregarParametrosEstudiante(cmd, e);
                        cmd.Parameters.AddWithValue("@eid", e.IdEstudiante);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            return true;
        }

        public bool Eliminar(int id)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            using (var cmd = new SqliteCommand("UPDATE estudiantes SET estado='RETIRADO' WHERE id_estudiante=@id", conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        public List<Estudiante> Buscar(string texto, int? idInstitucion = null)
        {
            var lista = new List<Estudiante>();
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string sql = @"SELECT e.*, i.nombre as nombre_inst, s.nombre_sede,
                                      g.nombre_grado, gr.nombre_grupo,
                                      (SELECT COUNT(*) FROM huellas_digitales h WHERE h.id_estudiante=e.id_estudiante AND h.activo=1) > 0 as tiene_huella
                               FROM estudiantes e
                               INNER JOIN instituciones i ON e.id_institucion = i.id_institucion
                               INNER JOIN sedes s ON e.id_sede = s.id_sede
                               INNER JOIN grados g ON e.id_grado = g.id_grado
                               INNER JOIN grupos gr ON e.id_grupo = gr.id_grupo
                               WHERE (e.identificacion LIKE @texto OR e.nombre LIKE @texto OR e.apellidos LIKE @texto)";
                if (idInstitucion.HasValue) sql += " AND e.id_institucion=@inst";
                sql += " ORDER BY e.apellidos, e.nombre LIMIT 100";
                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@texto", $"%{texto}%");
                    if (idInstitucion.HasValue) cmd.Parameters.AddWithValue("@inst", idInstitucion.Value);
                    using (var dr = cmd.ExecuteReader())
                        while (dr.Read()) lista.Add(MapearEstudiante(dr));
                }
            }
            return lista;
        }

        private void AgregarParametrosEstudiante(SqliteCommand cmd, Estudiante e)
        {
            cmd.Parameters.AddWithValue("@id", e.Identificacion);
            cmd.Parameters.AddWithValue("@n", e.Nombre);
            cmd.Parameters.AddWithValue("@ap", e.Apellidos);
            cmd.Parameters.AddWithValue("@inst", e.IdInstitucion);
            cmd.Parameters.AddWithValue("@sede", e.IdSede);
            cmd.Parameters.AddWithValue("@grado", e.IdGrado);
            cmd.Parameters.AddWithValue("@grupo", e.IdGrupo);
            cmd.Parameters.AddWithValue("@tel", (object)e.Telefono ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@email", (object)e.Email ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@foto", (object)e.Foto ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@estado", e.Estado);
            cmd.Parameters.AddWithValue("@fmat", (object)e.FechaMatricula ?? DBNull.Value);
        }

        private Estudiante MapearEstudiante(SqliteDataReader dr) => new Estudiante
        {
            IdEstudiante = Convert.ToInt32(dr["id_estudiante"]),
            Identificacion = dr["identificacion"].ToString(),
            Nombre = dr["nombre"].ToString(),
            Apellidos = dr["apellidos"].ToString(),
            IdInstitucion = Convert.ToInt32(dr["id_institucion"]),
            IdSede = Convert.ToInt32(dr["id_sede"]),
            IdGrado = Convert.ToInt32(dr["id_grado"]),
            IdGrupo = Convert.ToInt32(dr["id_grupo"]),
            Telefono = dr["telefono"]?.ToString(),
            Email = dr["email"]?.ToString(),
            Foto = dr["foto"] as byte[],
            Estado = dr["estado"].ToString(),
            FechaMatricula = dr["fecha_matricula"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(dr["fecha_matricula"]),
            NombreInstitucion = dr["nombre_inst"].ToString(),
            NombreSede = dr["nombre_sede"].ToString(),
            NombreGrado = dr["nombre_grado"].ToString(),
            NombreGrupo = dr["nombre_grupo"].ToString(),
            TieneHuella = Convert.ToBoolean(dr["tiene_huella"])
        };
    }

    // ═══════════════════════════════════════════════════
    // REPOSITORIO DE HUELLAS
    // ═══════════════════════════════════════════════════
    public class HuellaRepository
    {
        public List<HuellaDigital> ObtenerTodasActivas()
        {
            return ObtenerActivas(null);
        }

        public List<HuellaDigital> ObtenerActivasPorInstitucion(int idInstitucion)
        {
            return ObtenerActivas(idInstitucion);
        }

        private List<HuellaDigital> ObtenerActivas(int? idInstitucion)
        {
            var lista = new List<HuellaDigital>();
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string sql = @"SELECT h.*, e.id_sede, s.id_institucion,
                                (e.nombre || ' ' || e.apellidos) as nombre_est
                               FROM huellas_digitales h
                               INNER JOIN estudiantes e ON h.id_estudiante = e.id_estudiante
                               INNER JOIN sedes s ON e.id_sede = s.id_sede
                               WHERE h.activo=1 AND e.estado='ACTIVO'";
                if (idInstitucion.HasValue)
                    sql += " AND s.id_institucion=@inst";

                using (var cmd = new SqliteCommand(sql, conn))
                {
                    if (idInstitucion.HasValue)
                        cmd.Parameters.AddWithValue("@inst", idInstitucion.Value);
                    using (var dr = cmd.ExecuteReader())
                        while (dr.Read())
                            lista.Add(new HuellaDigital
                            {
                                IdHuella = Convert.ToInt32(dr["id_huella"]),
                                IdEstudiante = Convert.ToInt32(dr["id_estudiante"]),
                                TemplateBiometrico = (byte[])dr["template_biometrico"],
                                Calidad = Convert.ToInt32(dr["calidad"]),
                                Activo = true,
                                NombreEstudiante = dr["nombre_est"].ToString(),
                                IdSede = Convert.ToInt32(dr["id_sede"]),
                                IdInstitucion = Convert.ToInt32(dr["id_institucion"])
                            });
                }
            }
            return lista;
        }

        public List<HuellaDigital> ObtenerPorEstudiante(int idEstudiante)
        {
            var lista = new List<HuellaDigital>();
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string sql = "SELECT * FROM huellas_digitales WHERE id_estudiante=@id AND activo=1";
                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", idEstudiante);
                    using (var dr = cmd.ExecuteReader())
                        while (dr.Read())
                            lista.Add(new HuellaDigital
                            {
                                IdHuella = Convert.ToInt32(dr["id_huella"]),
                                IdEstudiante = Convert.ToInt32(dr["id_estudiante"]),
                                TemplateBiometrico = (byte[])dr["template_biometrico"],
                                Calidad = Convert.ToInt32(dr["calidad"]),
                                Dedo = (TipoDedo)Enum.Parse(typeof(TipoDedo), dr["dedo"].ToString())
                            });
                }
            }
            return lista;
        }

        public bool Guardar(HuellaDigital h)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string sql = @"INSERT INTO huellas_digitales 
                    (id_estudiante, template_biometrico, dedo, calidad, activo, registrado_por)
                    VALUES (@est, @tmpl, @dedo, @cal, 1, @usr)";
                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@est", h.IdEstudiante);
                    cmd.Parameters.AddWithValue("@tmpl", h.TemplateBiometrico);
                    cmd.Parameters.AddWithValue("@dedo", h.Dedo.ToString());
                    cmd.Parameters.AddWithValue("@cal", h.Calidad);
                    cmd.Parameters.AddWithValue("@usr", (object)h.RegistradoPor ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                    return true;
                }
            }
        }

        public bool EliminarPorEstudiante(int idEstudiante)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            using (var cmd = new SqliteCommand("UPDATE huellas_digitales SET activo=0 WHERE id_estudiante=@id", conn))
            {
                cmd.Parameters.AddWithValue("@id", idEstudiante);
                return cmd.ExecuteNonQuery() > 0;
            }
        }

        public int ContarHuellasPorEstudiante(int idEstudiante)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            using (var cmd = new SqliteCommand(
                "SELECT COUNT(*) FROM huellas_digitales WHERE id_estudiante=@id AND activo=1", conn))
            {
                cmd.Parameters.AddWithValue("@id", idEstudiante);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }
    }

    // ═══════════════════════════════════════════════════
    // REPOSITORIO DE REGISTROS DE INGRESO
    // ═══════════════════════════════════════════════════
    public class RegistroIngresoRepository
    {
        public bool Guardar(RegistroIngreso r)
        {
            // Usamos hora local explícita para evitar desfase UTC vs. hora local del servidor.
            string fechaLocal = DateTime.Now.ToString("yyyy-MM-dd");
            string horaLocal  = DateTime.Now.ToString("HH:mm:ss");
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string sql = @"INSERT INTO registros_ingreso
                    (id_estudiante, id_sede, fecha_ingreso, hora_ingreso, estado_ingreso, puntaje_biometrico)
                    VALUES (@est,@sede,@fecha,@hora,@estado,@puntaje)";
                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@est",    r.IdEstudiante);
                    cmd.Parameters.AddWithValue("@sede",   r.IdSede);
                    cmd.Parameters.AddWithValue("@fecha",  fechaLocal);
                    cmd.Parameters.AddWithValue("@hora",   horaLocal);
                    cmd.Parameters.AddWithValue("@estado", r.EstadoIngreso.ToString());
                    cmd.Parameters.AddWithValue("@puntaje", r.PuntajeBiometrico);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        public bool YaRegistroHoy(int idEstudiante)
        {
            string hoy = DateTime.Today.ToString("yyyy-MM-dd");
            using (var conn = ConexionDB.ObtenerConexion())
            using (var cmd = new SqliteCommand(
                "SELECT COUNT(*) FROM registros_ingreso WHERE id_estudiante=@id AND fecha_ingreso=@hoy", conn))
            {
                cmd.Parameters.AddWithValue("@id",  idEstudiante);
                cmd.Parameters.AddWithValue("@hoy", hoy);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        public List<RegistroIngreso> ObtenerPorFecha(DateTime fecha, int? idSede = null, int? idInstitucion = null)
        {
            var lista = new List<RegistroIngreso>();
            using (var conn = ConexionDB.ObtenerConexion())
            {
                // JOIN con registros_ingreso y sedes para poder filtrar por sede e institución
                string sql = @"
                    SELECT v.*, ri.id_estudiante
                    FROM v_registros_ingreso_detalle v
                    INNER JOIN registros_ingreso ri ON v.id_registro = ri.id_registro
                    INNER JOIN sedes s ON ri.id_sede = s.id_sede
                    WHERE v.fecha_ingreso = @fecha";
                if (idSede.HasValue)       sql += " AND ri.id_sede = @sede";
                if (idInstitucion.HasValue) sql += " AND s.id_institucion = @inst";
                sql += " ORDER BY v.hora_ingreso DESC";
                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@fecha", fecha.Date.ToString("yyyy-MM-dd"));
                    if (idSede.HasValue)       cmd.Parameters.AddWithValue("@sede", idSede.Value);
                    if (idInstitucion.HasValue) cmd.Parameters.AddWithValue("@inst", idInstitucion.Value);
                    using (var dr = cmd.ExecuteReader())
                        while (dr.Read())
                            lista.Add(new RegistroIngreso
                            {
                                IdRegistro = Convert.ToInt32(dr["id_registro"]),
                                IdEstudiante = Convert.ToInt32(dr["id_estudiante"]),
                                FechaIngreso = Convert.ToDateTime(dr["fecha_ingreso"]),
                                HoraIngreso = TimeSpan.Parse(dr["hora_ingreso"].ToString()),
                                EstadoIngreso = (EstadoIngreso)Enum.Parse(typeof(EstadoIngreso), dr["estado_ingreso"].ToString()),
                                NombreEstudiante = dr["nombre_completo"].ToString(),
                                Identificacion = dr["identificacion"].ToString(),
                                NombreSede = dr["nombre_sede"].ToString(),
                                NombreGrado = dr["nombre_grado"].ToString(),
                                NombreGrupo = dr["nombre_grupo"].ToString()
                            });
                }
            }
            return lista;
        }

        public List<RegistroIngreso> ObtenerPorRango(DateTime desde, DateTime hasta, int? idSede = null, int? idInstitucion = null)
        {
            var lista = new List<RegistroIngreso>();
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string sql = @"
                    SELECT v.*, ri.id_estudiante, ri.observaciones
                    FROM v_registros_ingreso_detalle v
                    INNER JOIN registros_ingreso ri ON v.id_registro = ri.id_registro
                    INNER JOIN sedes s ON ri.id_sede = s.id_sede
                    WHERE DATE(v.fecha_ingreso) BETWEEN @desde AND @hasta";
                if (idSede.HasValue)       sql += " AND ri.id_sede = @sede";
                if (idInstitucion.HasValue) sql += " AND s.id_institucion = @inst";
                sql += " ORDER BY v.fecha_ingreso DESC, v.hora_ingreso DESC";
                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@desde", desde.Date.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("@hasta", hasta.Date.ToString("yyyy-MM-dd"));
                    if (idSede.HasValue)       cmd.Parameters.AddWithValue("@sede", idSede.Value);
                    if (idInstitucion.HasValue) cmd.Parameters.AddWithValue("@inst", idInstitucion.Value);
                    using (var dr = cmd.ExecuteReader())
                        while (dr.Read())
                            lista.Add(new RegistroIngreso
                            {
                                IdRegistro       = Convert.ToInt32(dr["id_registro"]),
                                IdEstudiante     = Convert.ToInt32(dr["id_estudiante"]),
                                FechaIngreso     = Convert.ToDateTime(dr["fecha_ingreso"]),
                                HoraIngreso      = TimeSpan.Parse(dr["hora_ingreso"].ToString()),
                                EstadoIngreso    = (EstadoIngreso)Enum.Parse(typeof(EstadoIngreso), dr["estado_ingreso"].ToString()),
                                NombreEstudiante = dr["nombre_completo"].ToString(),
                                Identificacion   = dr["identificacion"].ToString(),
                                NombreSede       = dr["nombre_sede"].ToString(),
                                NombreGrado      = dr["nombre_grado"].ToString(),
                                NombreGrupo      = dr["nombre_grupo"].ToString(),
                                Observaciones    = dr["observaciones"] as string
                            });
                }
            }
            return lista;
        }

        public void ActualizarObservaciones(int idRegistro, string observaciones)
        {
            using var conn = ConexionDB.ObtenerConexion();
            using var cmd  = new SqliteCommand(
                "UPDATE registros_ingreso SET observaciones = @obs WHERE id_registro = @id", conn);
            cmd.Parameters.AddWithValue("@obs", string.IsNullOrWhiteSpace(observaciones)
                ? DBNull.Value : (object)observaciones.Trim());
            cmd.Parameters.AddWithValue("@id", idRegistro);
            cmd.ExecuteNonQuery();
        }

        public List<RegistroIngreso> ObtenerPorEstudianteYRango(int idEstudiante, DateTime desde, DateTime hasta)
        {
            var lista = new List<RegistroIngreso>();
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string sql = @"SELECT v.*, ri.id_estudiante, ri.observaciones
                               FROM v_registros_ingreso_detalle v
                               INNER JOIN registros_ingreso ri ON v.id_registro = ri.id_registro
                               WHERE DATE(v.fecha_ingreso) BETWEEN @desde AND @hasta
                                 AND ri.id_estudiante = @est
                               ORDER BY v.fecha_ingreso ASC, v.hora_ingreso ASC";
                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@desde", desde.Date.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("@hasta", hasta.Date.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("@est", idEstudiante);
                    using (var dr = cmd.ExecuteReader())
                        while (dr.Read())
                            lista.Add(new RegistroIngreso
                            {
                                IdRegistro   = Convert.ToInt32(dr["id_registro"]),
                                IdEstudiante = Convert.ToInt32(dr["id_estudiante"]),
                                FechaIngreso = Convert.ToDateTime(dr["fecha_ingreso"]),
                                HoraIngreso  = TimeSpan.Parse(dr["hora_ingreso"].ToString()),
                                EstadoIngreso = (EstadoIngreso)Enum.Parse(typeof(EstadoIngreso), dr["estado_ingreso"].ToString()),
                                NombreEstudiante = dr["nombre_completo"].ToString(),
                                Identificacion   = dr["identificacion"].ToString(),
                                NombreSede    = dr["nombre_sede"].ToString(),
                                NombreGrado   = dr["nombre_grado"].ToString(),
                                NombreGrupo   = dr["nombre_grupo"].ToString(),
                                Observaciones = dr["observaciones"] as string
                            });
                }
            }
            return lista;
        }

        public EstadisticasDia ObtenerEstadisticasHoy(int idSede)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string sql = "SELECT * FROM v_estadisticas_hoy WHERE id_sede=@sede";
                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@sede", idSede);
                    using (var dr = cmd.ExecuteReader())
                    {
                        if (dr.Read())
                            return new EstadisticasDia
                            {
                                TotalEstudiantes = Convert.ToInt32(dr["total_estudiantes"]),
                                PresentesHoy = Convert.ToInt32(dr["presentes_hoy"]),
                                ATiempo = Convert.ToInt32(dr["a_tiempo"]),
                                Tardanzas = Convert.ToInt32(dr["tarde"]),
                                NombreSede = dr["nombre_sede"].ToString(),
                                NombreInstitucion = dr["nombre_institucion"].ToString()
                            };
                    }
                }
            }
            return new EstadisticasDia();
        }
    }

    // ═══════════════════════════════════════════════════
    // REPOSITORIO DE HORARIOS
    // ═══════════════════════════════════════════════════
    public class HorarioRepository
    {
        /// <summary>
        /// Devuelve los horarios de una sede. Si idGrado es null retorna los de
        /// alcance "todos los grados" (id_grado IS NULL); si se indica un grado
        /// devuelve los específicos de ese grado.
        /// </summary>
        public List<Horario> ObtenerPorSede(int idSede, int? idGrado = null)
        {
            var lista = new List<Horario>();
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string filtroGrado = idGrado.HasValue
                    ? "AND h.id_grado=@grado"
                    : "AND h.id_grado IS NULL";
                string sql = $@"SELECT h.*, s.nombre_sede,
                                       COALESCE(g.nombre_grado,'') AS nombre_grado
                               FROM horarios h
                               INNER JOIN sedes s ON h.id_sede=s.id_sede
                               LEFT  JOIN grados g ON h.id_grado=g.id_grado
                               WHERE h.id_sede=@id AND h.activo=1 {filtroGrado}";
                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", idSede);
                    if (idGrado.HasValue) cmd.Parameters.AddWithValue("@grado", idGrado.Value);
                    using (var dr = cmd.ExecuteReader())
                        while (dr.Read())
                            lista.Add(MapearHorario(dr));
                }
            }
            return lista;
        }

        /// <summary>
        /// Devuelve el horario vigente hoy para una sede y, opcionalmente, un grado.
        /// Prioridad: grado específico > sede genérico (id_grado IS NULL).
        /// </summary>
        public Horario ObtenerHoy(int idSede, int? idGrado = null)
        {
            string diaActual = DateTime.Now.DayOfWeek.ToString().ToUpper();
            var mapa = new Dictionary<string, string>
            {
                {"MONDAY","LUNES"},{"TUESDAY","MARTES"},{"WEDNESDAY","MIERCOLES"},
                {"THURSDAY","JUEVES"},{"FRIDAY","VIERNES"},{"SATURDAY","SABADO"},{"SUNDAY","DOMINGO"}
            };
            string diaEsp = mapa.ContainsKey(diaActual) ? mapa[diaActual] : diaActual;

            using (var conn = ConexionDB.ObtenerConexion())
            {
                // 1. Horario grado-específico
                if (idGrado.HasValue)
                {
                    string sql1 = "SELECT * FROM horarios WHERE id_sede=@sede AND id_grado=@grado AND dia_semana=@dia AND activo=1 LIMIT 1";
                    using (var cmd = new SqliteCommand(sql1, conn))
                    {
                        cmd.Parameters.AddWithValue("@sede", idSede);
                        cmd.Parameters.AddWithValue("@grado", idGrado.Value);
                        cmd.Parameters.AddWithValue("@dia", diaEsp);
                        using (var dr = cmd.ExecuteReader())
                            if (dr.Read()) return MapearHorario(dr);
                    }
                }
                // 2. Horario sede genérico
                string sql2 = "SELECT * FROM horarios WHERE id_sede=@sede AND id_grado IS NULL AND dia_semana=@dia AND activo=1 LIMIT 1";
                using (var cmd = new SqliteCommand(sql2, conn))
                {
                    cmd.Parameters.AddWithValue("@sede", idSede);
                    cmd.Parameters.AddWithValue("@dia", diaEsp);
                    using (var dr = cmd.ExecuteReader())
                        if (dr.Read()) return MapearHorario(dr);
                }
            }
            return null;
        }

        public bool Guardar(Horario h)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            {
                // Verificar si ya existe
                string chkSql = h.IdGrado.HasValue
                    ? "SELECT id_horario FROM horarios WHERE id_sede=@sede AND id_grado=@grado AND dia_semana=@dia LIMIT 1"
                    : "SELECT id_horario FROM horarios WHERE id_sede=@sede AND id_grado IS NULL AND dia_semana=@dia LIMIT 1";
                int existeId = 0;
                using (var chk = new SqliteCommand(chkSql, conn))
                {
                    chk.Parameters.AddWithValue("@sede", h.IdSede);
                    chk.Parameters.AddWithValue("@dia", h.DiaSemana.ToString());
                    if (h.IdGrado.HasValue) chk.Parameters.AddWithValue("@grado", h.IdGrado.Value);
                    var res = chk.ExecuteScalar();
                    if (res != null) existeId = Convert.ToInt32(res);
                }

                string sql = existeId > 0
                    ? "UPDATE horarios SET hora_inicio=@hi, hora_limite_tarde=@hlt, hora_cierre_ingreso=@hci, activo=1 WHERE id_horario=@id"
                    : "INSERT INTO horarios (id_sede, id_grado, dia_semana, hora_inicio, hora_limite_tarde, hora_cierre_ingreso, activo) VALUES (@sede,@grado,@dia,@hi,@hlt,@hci,1)";

                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@hi",  h.HoraInicio.ToString(@"hh\:mm"));
                    cmd.Parameters.AddWithValue("@hlt", h.HoraLimiteTarde.ToString(@"hh\:mm"));
                    cmd.Parameters.AddWithValue("@hci", h.HoraCierreIngreso.ToString(@"hh\:mm"));
                    if (existeId > 0)
                    {
                        cmd.Parameters.AddWithValue("@id", existeId);
                    }
                    else
                    {
                        cmd.Parameters.AddWithValue("@sede",  h.IdSede);
                        cmd.Parameters.AddWithValue("@grado", h.IdGrado.HasValue ? (object)h.IdGrado.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@dia",   h.DiaSemana.ToString());
                    }
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        public int? ObtenerIdHorario(int idSede, string diaSemana, int? idGrado = null)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string filtro = idGrado.HasValue ? "AND id_grado=@grado" : "AND id_grado IS NULL";
                string sql = $"SELECT id_horario FROM horarios WHERE id_sede=@sede AND dia_semana=@dia AND activo=1 {filtro} LIMIT 1";
                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@sede", idSede);
                    cmd.Parameters.AddWithValue("@dia", diaSemana);
                    if (idGrado.HasValue) cmd.Parameters.AddWithValue("@grado", idGrado.Value);
                    var res = cmd.ExecuteScalar();
                    return res == null ? null : (int?)Convert.ToInt32(res);
                }
            }
        }

        private static bool TieneColumna(SqliteDataReader dr, string nombre)
        {
            for (int i = 0; i < dr.FieldCount; i++)
                if (dr.GetName(i).Equals(nombre, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static Horario MapearHorario(SqliteDataReader dr) => new Horario
        {
            IdHorario         = Convert.ToInt32(dr["id_horario"]),
            IdSede            = Convert.ToInt32(dr["id_sede"]),
            IdGrado           = dr["id_grado"] == DBNull.Value ? (int?)null : Convert.ToInt32(dr["id_grado"]),
            DiaSemana         = dr["dia_semana"] != DBNull.Value
                                    ? (DiaSemana)Enum.Parse(typeof(DiaSemana), dr["dia_semana"].ToString())
                                    : default,
            HoraInicio        = TimeSpan.Parse(dr["hora_inicio"].ToString()),
            HoraLimiteTarde   = TimeSpan.Parse(dr["hora_limite_tarde"].ToString()),
            HoraCierreIngreso = TimeSpan.Parse(dr["hora_cierre_ingreso"].ToString()),
            Activo            = Convert.ToInt32(dr["activo"]) == 1,
            NombreSede        = TieneColumna(dr, "nombre_sede")  ? dr["nombre_sede"].ToString()  : "",
            NombreGrado       = TieneColumna(dr, "nombre_grado") && dr["nombre_grado"] != DBNull.Value
                                    ? dr["nombre_grado"].ToString() : null
        };
    }

    // ═══════════════════════════════════════════════════
    // REPOSITORIO DE FRANJAS DE HORARIO
    // ═══════════════════════════════════════════════════
    public class FranjaHorarioRepository
    {
        public List<FranjaHorario> ObtenerPorHorario(int idHorario)
        {
            var lista = new List<FranjaHorario>();
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string sql = "SELECT * FROM franjas_horario WHERE id_horario=@id AND activo=1 ORDER BY orden, id_franja";
                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", idHorario);
                    using (var dr = cmd.ExecuteReader())
                        while (dr.Read())
                            lista.Add(MapearFranja(dr));
                }
            }
            return lista;
        }

        public bool Guardar(FranjaHorario f)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string sql;
                if (f.IdFranja == 0)
                    sql = @"INSERT INTO franjas_horario (id_horario, nombre, hora_inicio, hora_limite_tarde, hora_cierre_ingreso, orden)
                            VALUES (@idh, @nom, @hi, @hlt, @hci, @ord)";
                else
                    sql = @"UPDATE franjas_horario SET nombre=@nom, hora_inicio=@hi, hora_limite_tarde=@hlt,
                            hora_cierre_ingreso=@hci, orden=@ord WHERE id_franja=@id";
                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@idh", f.IdHorario);
                    cmd.Parameters.AddWithValue("@nom", f.Nombre);
                    cmd.Parameters.AddWithValue("@hi", f.HoraInicio);
                    cmd.Parameters.AddWithValue("@hlt", f.HoraLimiteTarde);
                    cmd.Parameters.AddWithValue("@hci", f.HoraCierreIngreso);
                    cmd.Parameters.AddWithValue("@ord", f.Orden);
                    if (f.IdFranja != 0) cmd.Parameters.AddWithValue("@id", f.IdFranja);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        public bool Eliminar(int idFranja)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            {
                using (var cmd = new SqliteCommand("DELETE FROM franjas_horario WHERE id_franja=@id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", idFranja);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        private static FranjaHorario MapearFranja(SqliteDataReader dr) => new FranjaHorario
        {
            IdFranja          = Convert.ToInt32(dr["id_franja"]),
            IdHorario         = Convert.ToInt32(dr["id_horario"]),
            Nombre            = dr["nombre"].ToString(),
            HoraInicio        = TimeSpan.Parse(dr["hora_inicio"].ToString()),
            HoraLimiteTarde   = TimeSpan.Parse(dr["hora_limite_tarde"].ToString()),
            HoraCierreIngreso = TimeSpan.Parse(dr["hora_cierre_ingreso"].ToString()),
            Orden             = Convert.ToInt32(dr["orden"]),
            Activo            = Convert.ToInt32(dr["activo"]) == 1
        };
    }

    // ═══════════════════════════════════════════════════
    // REPOSITORIO DE EXCEPCIONES DE HORARIO
    // ═══════════════════════════════════════════════════
    public class HorarioExcepcionRepository
    {
        /// <summary>
        /// Devuelve todas las excepciones visibles para una sede:
        /// - Excepciones propias de la sede
        /// - Excepciones de la institución (todas las sedes)
        /// - Excepciones de grado específico vinculadas a esa sede
        /// </summary>
        public List<HorarioExcepcion> ObtenerPorSede(int idSede, int? idInstitucion = null)
        {
            var lista = new List<HorarioExcepcion>();
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string sql = @"SELECT e.*,
                                      COALESCE(s.nombre_sede,'(Todas las sedes)') AS nombre_sede,
                                      COALESCE(g.nombre_grado,'') AS nombre_grado
                               FROM horario_excepciones e
                               LEFT JOIN sedes s ON e.id_sede=s.id_sede
                               LEFT JOIN grados g ON e.id_grado=g.id_grado
                               WHERE e.activo=1
                                 AND (
                                     e.id_sede=@sede
                                     OR (e.id_sede IS NULL AND @instId IS NOT NULL AND e.id_institucion=@instId)
                                 )
                               ORDER BY e.fecha_excepcion DESC";
                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@sede", idSede);
                    cmd.Parameters.AddWithValue("@instId", idInstitucion.HasValue ? (object)idInstitucion.Value : DBNull.Value);
                    using (var dr = cmd.ExecuteReader())
                        while (dr.Read())
                            lista.Add(MapearExcepcion(dr));
                }
                foreach (var exc in lista)
                    exc.Franjas = ObtenerFranjas(conn, exc.IdExcepcion);
            }
            return lista;
        }

        /// <summary>
        /// Devuelve la excepción más específica para hoy, en orden de prioridad:
        /// sede+grado > sede > grado (todas las sedes de la institución) > institución completa
        /// </summary>
        public HorarioExcepcion ObtenerHoy(int idSede, int? idGrado = null, int? idInstitucion = null)
        {
            string hoy = DateTime.Today.ToString("yyyy-MM-dd");
            using (var conn = ConexionDB.ObtenerConexion())
            {
                // 1. sede + grado específico
                if (idGrado.HasValue)
                {
                    var exc = BuscarExcepcion(conn, hoy, idSede, idGrado.Value, null);
                    if (exc != null) return exc;
                }
                // 2. sede genérica
                {
                    var exc = BuscarExcepcion(conn, hoy, idSede, null, null);
                    if (exc != null) return exc;
                }
                // 3. grado (todas las sedes) y 4. institución completa
                if (idInstitucion.HasValue)
                {
                    if (idGrado.HasValue)
                    {
                        var exc = BuscarExcepcionInstitucion(conn, hoy, idInstitucion.Value, idGrado.Value);
                        if (exc != null) return exc;
                    }
                    {
                        var exc = BuscarExcepcionInstitucion(conn, hoy, idInstitucion.Value, null);
                        if (exc != null) return exc;
                    }
                }
            }
            return null;
        }

        private HorarioExcepcion BuscarExcepcion(SqliteConnection conn, string fecha, int idSede, int? idGrado, int? idInstitucion)
        {
            string filtroGrado = idGrado.HasValue ? "AND e.id_grado=@grado" : "AND e.id_grado IS NULL";
            string sql = $@"SELECT e.*,
                                   COALESCE(s.nombre_sede,'(Todas las sedes)') AS nombre_sede,
                                   COALESCE(g.nombre_grado,'') AS nombre_grado
                            FROM horario_excepciones e
                            LEFT JOIN sedes s ON e.id_sede=s.id_sede
                            LEFT JOIN grados g ON e.id_grado=g.id_grado
                            WHERE e.id_sede=@sede AND e.fecha_excepcion=@fecha AND e.activo=1 {filtroGrado} LIMIT 1";
            using (var cmd = new SqliteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@sede",  idSede);
                cmd.Parameters.AddWithValue("@fecha", fecha);
                if (idGrado.HasValue) cmd.Parameters.AddWithValue("@grado", idGrado.Value);
                using (var dr = cmd.ExecuteReader())
                {
                    if (!dr.Read()) return null;
                    var exc = MapearExcepcion(dr);
                    exc.Franjas = ObtenerFranjas(conn, exc.IdExcepcion);
                    return exc;
                }
            }
        }

        private HorarioExcepcion BuscarExcepcionInstitucion(SqliteConnection conn, string fecha, int idInstitucion, int? idGrado)
        {
            string filtroGrado = idGrado.HasValue ? "AND e.id_grado=@grado" : "AND e.id_grado IS NULL";
            string sql = $@"SELECT e.*,
                                   '(Todas las sedes)' AS nombre_sede,
                                   COALESCE(g.nombre_grado,'') AS nombre_grado
                            FROM horario_excepciones e
                            LEFT JOIN grados g ON e.id_grado=g.id_grado
                            WHERE e.id_sede IS NULL AND e.id_institucion=@inst
                              AND e.fecha_excepcion=@fecha AND e.activo=1 {filtroGrado} LIMIT 1";
            using (var cmd = new SqliteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@inst",  idInstitucion);
                cmd.Parameters.AddWithValue("@fecha", fecha);
                if (idGrado.HasValue) cmd.Parameters.AddWithValue("@grado", idGrado.Value);
                using (var dr = cmd.ExecuteReader())
                {
                    if (!dr.Read()) return null;
                    var exc = MapearExcepcion(dr);
                    exc.Franjas = ObtenerFranjas(conn, exc.IdExcepcion);
                    return exc;
                }
            }
        }

        public int Guardar(HorarioExcepcion e)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string fecha = e.FechaExcepcion.ToString("yyyy-MM-dd");
                if (e.IdExcepcion == 0)
                {
                    string sql = @"INSERT INTO horario_excepciones
                                   (id_sede, id_grado, id_institucion, alcance, fecha_excepcion, descripcion)
                                   VALUES (@sede, @grado, @inst, @alcance, @fecha, @desc);
                                   SELECT last_insert_rowid()";
                    using (var cmd = new SqliteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@sede",    e.IdSede > 0 ? (object)e.IdSede : DBNull.Value);
                        cmd.Parameters.AddWithValue("@grado",   e.IdGrado.HasValue ? (object)e.IdGrado.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@inst",    e.IdInstitucion.HasValue ? (object)e.IdInstitucion.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@alcance", e.Alcance ?? "SEDE");
                        cmd.Parameters.AddWithValue("@fecha",   fecha);
                        cmd.Parameters.AddWithValue("@desc",    e.Descripcion);
                        return Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
                else
                {
                    string sql = @"UPDATE horario_excepciones
                                   SET descripcion=@desc, fecha_excepcion=@fecha,
                                       id_grado=@grado, alcance=@alcance
                                   WHERE id_excepcion=@id";
                    using (var cmd = new SqliteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@desc",    e.Descripcion);
                        cmd.Parameters.AddWithValue("@fecha",   fecha);
                        cmd.Parameters.AddWithValue("@grado",   e.IdGrado.HasValue ? (object)e.IdGrado.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@alcance", e.Alcance ?? "SEDE");
                        cmd.Parameters.AddWithValue("@id",      e.IdExcepcion);
                        cmd.ExecuteNonQuery();
                        return e.IdExcepcion;
                    }
                }
            }
        }

        public bool Eliminar(int idExcepcion)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            {
                using (var cmd = new SqliteCommand("DELETE FROM horario_excepciones WHERE id_excepcion=@id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", idExcepcion);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        public bool GuardarFranja(FranjaExcepcion f)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string sql;
                if (f.IdFranjaExc == 0)
                    sql = @"INSERT INTO franjas_excepcion (id_excepcion, nombre, hora_inicio, hora_limite_tarde, hora_cierre_ingreso, orden)
                            VALUES (@ide, @nom, @hi, @hlt, @hci, @ord)";
                else
                    sql = @"UPDATE franjas_excepcion SET nombre=@nom, hora_inicio=@hi, hora_limite_tarde=@hlt,
                            hora_cierre_ingreso=@hci, orden=@ord WHERE id_franja_exc=@id";
                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@ide", f.IdExcepcion);
                    cmd.Parameters.AddWithValue("@nom", f.Nombre);
                    cmd.Parameters.AddWithValue("@hi", f.HoraInicio);
                    cmd.Parameters.AddWithValue("@hlt", f.HoraLimiteTarde);
                    cmd.Parameters.AddWithValue("@hci", f.HoraCierreIngreso);
                    cmd.Parameters.AddWithValue("@ord", f.Orden);
                    if (f.IdFranjaExc != 0) cmd.Parameters.AddWithValue("@id", f.IdFranjaExc);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        public bool EliminarFranja(int idFranjaExc)
        {
            using (var conn = ConexionDB.ObtenerConexion())
            {
                using (var cmd = new SqliteCommand("DELETE FROM franjas_excepcion WHERE id_franja_exc=@id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", idFranjaExc);
                    return cmd.ExecuteNonQuery() > 0;
                }
            }
        }

        private static List<FranjaExcepcion> ObtenerFranjas(SqliteConnection conn, int idExcepcion)
        {
            var lista = new List<FranjaExcepcion>();
            string sql = "SELECT * FROM franjas_excepcion WHERE id_excepcion=@id ORDER BY orden, id_franja_exc";
            using (var cmd = new SqliteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@id", idExcepcion);
                using (var dr = cmd.ExecuteReader())
                    while (dr.Read())
                        lista.Add(new FranjaExcepcion
                        {
                            IdFranjaExc       = Convert.ToInt32(dr["id_franja_exc"]),
                            IdExcepcion       = Convert.ToInt32(dr["id_excepcion"]),
                            Nombre            = dr["nombre"].ToString(),
                            HoraInicio        = TimeSpan.Parse(dr["hora_inicio"].ToString()),
                            HoraLimiteTarde   = TimeSpan.Parse(dr["hora_limite_tarde"].ToString()),
                            HoraCierreIngreso = TimeSpan.Parse(dr["hora_cierre_ingreso"].ToString()),
                            Orden             = Convert.ToInt32(dr["orden"])
                        });
            }
            return lista;
        }

        private static bool TieneColumna(SqliteDataReader dr, string nombre)
        {
            for (int i = 0; i < dr.FieldCount; i++)
                if (dr.GetName(i).Equals(nombre, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static HorarioExcepcion MapearExcepcion(SqliteDataReader dr)
        {
            int? idSede = dr["id_sede"] == DBNull.Value ? (int?)null : Convert.ToInt32(dr["id_sede"]);
            return new HorarioExcepcion
            {
                IdExcepcion    = Convert.ToInt32(dr["id_excepcion"]),
                IdSede         = idSede ?? 0,
                FechaExcepcion = DateTime.Parse(dr["fecha_excepcion"].ToString()),
                Descripcion    = dr["descripcion"].ToString(),
                Activo         = Convert.ToInt32(dr["activo"]) == 1,
                NombreSede     = TieneColumna(dr, "nombre_sede")  ? dr["nombre_sede"].ToString()  : "",
                Alcance        = TieneColumna(dr, "alcance") && dr["alcance"] != DBNull.Value
                                     ? dr["alcance"].ToString() : "SEDE",
                IdGrado        = TieneColumna(dr, "id_grado") && dr["id_grado"] != DBNull.Value
                                     ? Convert.ToInt32(dr["id_grado"]) : (int?)null,
                IdInstitucion  = TieneColumna(dr, "id_institucion") && dr["id_institucion"] != DBNull.Value
                                     ? Convert.ToInt32(dr["id_institucion"]) : (int?)null,
                NombreGrado    = TieneColumna(dr, "nombre_grado") && dr["nombre_grado"] != DBNull.Value
                                     ? dr["nombre_grado"].ToString() : null
            };
        }
    }

    // ═══════════════════════════════════════════════════
    // REPOSITORIO DE LOGS
    // ═══════════════════════════════════════════════════
    public class LogRepository
    {
        public void Registrar(TipoEvento tipo, string descripcion, NivelLog nivel = NivelLog.INFO)
        {
            try
            {
                using (var conn = ConexionDB.ObtenerConexion())
                {
                    string sql = @"INSERT INTO logs_sistema (id_usuario, tipo_evento, descripcion, nivel)
                                   VALUES (@usr, @tipo, @desc, @nivel)";
                    using (var cmd = new SqliteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@usr", (object)SesionActiva.UsuarioActual?.IdUsuario ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@tipo", tipo.ToString());
                        cmd.Parameters.AddWithValue("@desc", descripcion);
                        cmd.Parameters.AddWithValue("@nivel", nivel.ToString());
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch { /* Los logs no deben interrumpir el flujo */ }
        }

        public List<LogSistema> ObtenerRecientes(int cantidad = 100, int? idInstitucion = null)
        {
            var lista = new List<LogSistema>();
            using (var conn = ConexionDB.ObtenerConexion())
            {
                string sql = @"SELECT l.*, u.nombre_completo FROM logs_sistema l
                               LEFT JOIN usuarios u ON l.id_usuario = u.id_usuario";
                if (idInstitucion.HasValue)
                    sql += " WHERE u.id_institucion = @inst";
                sql += " ORDER BY l.fecha_evento DESC LIMIT @cant";
                using (var cmd = new SqliteCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@cant", cantidad);
                    if (idInstitucion.HasValue)
                        cmd.Parameters.AddWithValue("@inst", idInstitucion.Value);
                    using (var dr = cmd.ExecuteReader())
                        while (dr.Read())
                            lista.Add(new LogSistema
                            {
                                IdLog = Convert.ToInt32(dr["id_log"]),
                                TipoEvento = (TipoEvento)Enum.Parse(typeof(TipoEvento), dr["tipo_evento"].ToString()),
                                Descripcion = dr["descripcion"].ToString(),
                                Nivel = (NivelLog)Enum.Parse(typeof(NivelLog), dr["nivel"].ToString()),
                                FechaEvento = Convert.ToDateTime(dr["fecha_evento"]),
                                NombreUsuario = dr["nombre_completo"]?.ToString()
                            });
                }
            }
            return lista;
        }
    }
}
