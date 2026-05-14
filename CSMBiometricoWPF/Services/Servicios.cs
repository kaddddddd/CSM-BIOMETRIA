// ============================================================
// CSMBiometricoWPF.Services - Capa de Servicios
// ============================================================
using CSMBiometricoWPF.Data;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CSMBiometricoWPF.Services
{
    // ═══════════════════════════════════════════════════
    // SERVICIO DE AUTENTICACIÓN
    // ═══════════════════════════════════════════════════
    public class AuthService
    {
        private readonly UsuarioRepository _usuarioRepo;
        private readonly LogRepository _logRepo;

        public AuthService()
        {
            _usuarioRepo = new UsuarioRepository();
            _logRepo = new LogRepository();
        }

        public (bool Exito, string Mensaje, Usuario? Usuario) Login(
            string username, string password, int? idInstitucion = null)
        {
            try
            {
                var usuario = _usuarioRepo.Autenticar(username, password);
                if (usuario == null)
                    return (false, "Credenciales incorrectas o usuario inactivo.", null);

                if (usuario.NombreRol != "SUPERADMIN")
                {
                    if (idInstitucion == null)
                        return (false, "Seleccione su institución para ingresar.", null);
                    if (usuario.IdInstitucion != idInstitucion)
                        return (false, "No tiene acceso a la institución seleccionada.", null);

                    SesionActiva.InstitucionActual = new InstitucionRepository().ObtenerPorId(idInstitucion.Value);
                }

                _usuarioRepo.RegistrarUltimoLogin(usuario.IdUsuario);
                SesionActiva.UsuarioActual = usuario;

                string instInfo = usuario.NombreInstitucion != null ? $" [{usuario.NombreInstitucion}]" : "";
                _logRepo.Registrar(TipoEvento.LOGIN, $"Login exitoso: {username}{instInfo}");
                return (true, "Bienvenido, " + usuario.NombreCompleto, usuario);
            }
            catch (Exception ex)
            {
                return (false, "Error de conexión: " + ex.Message, null);
            }
        }

        public void Logout()
        {
            if (SesionActiva.UsuarioActual != null)
                _logRepo.Registrar(TipoEvento.LOGOUT, $"Logout: {SesionActiva.UsuarioActual.Username}");
            SesionActiva.Cerrar();
        }

        public bool TienePermiso(string modulo)
        {
            if (SesionActiva.UsuarioActual == null) return false;
            string rol = (SesionActiva.UsuarioActual.NombreRol ?? "").ToUpper().Trim();
            bool esSuperAdmin    = rol == "SUPERADMIN";
            bool esAdministrador = rol == "ADMINISTRADOR";

            return modulo.ToUpper() switch
            {
                "USUARIOS"      => esSuperAdmin,
                "INSTITUCIONES" => esSuperAdmin || esAdministrador,
                "SEDES"         => esSuperAdmin,
                "GRADOS"        => esSuperAdmin || esAdministrador,
                "GRUPOS"        => esSuperAdmin || esAdministrador,
                "HORARIOS"      => esSuperAdmin || esAdministrador,
                "LOGS"          => esSuperAdmin || esAdministrador,
                "ESTUDIANTES"   => esSuperAdmin || esAdministrador,
                "ENROLAMIENTO"  => esSuperAdmin || esAdministrador,
                "REPORTES"      => esSuperAdmin || esAdministrador,
                "PERIODOS"      => esSuperAdmin || esAdministrador,
                "CONSULTA"      => true,
                "VERIFICACION"  => true,
                "DASHBOARD"     => true,
                "LECTOR"        => true,
                _               => esSuperAdmin
            };
        }
    }

    // ═══════════════════════════════════════════════════
    // SERVICIO DE ASISTENCIA
    // ═══════════════════════════════════════════════════
    public class AsistenciaService
    {
        /// <summary>
        /// Se dispara cada vez que se registra un nuevo ingreso (exitoso).
        /// El PanelEntradaWindow se suscribe para actualizar su lista en tiempo real.
        /// </summary>
        public static event EventHandler? IngresoRegistrado;
        private readonly RegistroIngresoRepository _registroRepo;
        private readonly HorarioRepository _horarioRepo;
        private readonly FranjaHorarioRepository _franjaRepo;
        private readonly HorarioExcepcionRepository _excepcionRepo;
        private readonly LogRepository _logRepo;
        private readonly OfflineService _offlineService;

        public AsistenciaService()
        {
            _registroRepo   = new RegistroIngresoRepository();
            _horarioRepo    = new HorarioRepository();
            _franjaRepo     = new FranjaHorarioRepository();
            _excepcionRepo  = new HorarioExcepcionRepository();
            _logRepo        = new LogRepository();
            _offlineService = new OfflineService();
        }

        public (EstadoIngreso Estado, string Mensaje, string? NombreFranja) RegistrarIngreso(
            Estudiante estudiante, float puntajeBiometrico)
        {
            try
            {
                var ahora   = DateTime.Now.TimeOfDay;
                var franjas = ObtenerFranjasVigentes(estudiante.IdSede, estudiante.IdGrado, estudiante.IdGrupo);

                // Franja activa en este momento
                var franjaActiva = franjas.FirstOrDefault(f => ahora >= f.Inicio && ahora <= f.Cierre);

                // Si llegó antes del inicio de la franja (llegada anticipada),
                // buscar la próxima franja del día y registrar como A_TIEMPO.
                bool esLlegadaAnticipada = false;
                if (franjaActiva == null)
                {
                    var proxima = franjas.Where(f => ahora < f.Inicio && (f.Inicio - ahora).TotalMinutes <= 60)
                                        .OrderBy(f => f.Inicio).FirstOrDefault();
                    if (proxima != null)
                    {
                        franjaActiva        = proxima;
                        esLlegadaAnticipada = true;
                    }
                }

                // Sin ninguna franja activa ni llegada anticipada → plataforma cerrada.
                // No guardar ningún registro; solo informar al operador.
                if (franjaActiva == null)
                    return (EstadoIngreso.FUERA_DE_HORARIO,
                        "🔒 Plataforma cerrada — el ingreso no está disponible en este momento.",
                        null);

                // Verificar duplicado: MySQL cuando hay conexión, SQLite cuando no.
                bool online = ConexionDB.EstaConectado;
                bool yaRegistrado;
                if (online)
                {
                    yaRegistrado = esLlegadaAnticipada
                        ? _registroRepo.YaRegistroHoy(estudiante.IdEstudiante)
                        : _registroRepo.YaRegistroEnFranja(estudiante.IdEstudiante, franjaActiva.Inicio, franjaActiva.Cierre);
                }
                else
                {
                    yaRegistrado = esLlegadaAnticipada
                        ? _registroRepo.YaRegistroHoyOffline(estudiante.IdEstudiante)
                        : _registroRepo.YaRegistroEnFranjaOffline(estudiante.IdEstudiante, franjaActiva.Inicio, franjaActiva.Cierre);
                }

                if (yaRegistrado)
                    return (EstadoIngreso.YA_REGISTRADO,
                        $"{estudiante.NombreCompleto} ya registró ingreso hoy.",
                        franjaActiva.EsFranja ? franjaActiva.Nombre : null);

                // Estado según hora de llegada dentro de la franja activa:
                //   A_TIEMPO  → ahora <= LimiteTarde  (o llegada anticipada)
                //   TARDE     → LimiteTarde < ahora <= CierreIngreso
                EstadoIngreso estado;
                if (esLlegadaAnticipada || ahora <= franjaActiva.Inicio)
                    estado = EstadoIngreso.A_TIEMPO;
                else if (ahora <= franjaActiva.LimiteTarde)
                    estado = EstadoIngreso.TARDE;
                else
                    estado = EstadoIngreso.TARDE;

                var registro = new RegistroIngreso
                {
                    IdEstudiante      = estudiante.IdEstudiante,
                    IdSede            = estudiante.IdSede,
                    FechaIngreso      = DateTime.Today,
                    HoraIngreso       = DateTime.Now.TimeOfDay,
                    EstadoIngreso     = estado,
                    PuntajeBiometrico = puntajeBiometrico,
                    NombreFranja      = franjaActiva?.EsFranja == true ? franjaActiva.Nombre : null
                };

                if (online)
                    _registroRepo.Guardar(registro);
                else
                    _offlineService.GuardarOffline(registro);

                IngresoRegistrado?.Invoke(this, EventArgs.Empty);

                string? nomFranja = franjaActiva?.EsFranja == true ? franjaActiva.Nombre : null;

                string msg = estado switch
                {
                    EstadoIngreso.A_TIEMPO         => $"✅ {estudiante.NombreCompleto} - A TIEMPO",
                    EstadoIngreso.TARDE            => $"⚠️ {estudiante.NombreCompleto} - TARDE",
                    EstadoIngreso.FUERA_DE_HORARIO => $"❌ {estudiante.NombreCompleto} - INASISTENCIA",
                    _                              => ""
                };

                try { _logRepo.Registrar(TipoEvento.REGISTRO_ASISTENCIA, $"Ingreso: {estudiante.Identificacion} - {estado}"); }
                catch { }
                return (estado, msg, nomFranja);
            }
            catch (Exception ex)
            {
                try { _logRepo.Registrar(TipoEvento.ERROR_DB, ex.Message, NivelLog.ERROR); } catch { }
                throw;
            }
        }

        private record FranjaVigente(TimeSpan Inicio, TimeSpan LimiteTarde, TimeSpan Cierre, string Nombre = "Horario habitual", bool EsFranja = false);

        private List<FranjaVigente> ObtenerFranjasVigentes(int idSede, int idGrado, int idGrupo)
        {
            var resultado = new List<FranjaVigente>();
            bool online = ConexionDB.EstaConectado;

            // Obtener institución del estudiante para excepciones a nivel institución
            int? idInstitucion = null;
            if (online)
            {
                try
                {
                    using var conn = ConexionDB.ObtenerConexion();
                    using var cmd = new MySqlConnector.MySqlCommand(
                        "SELECT id_institucion FROM sedes WHERE id_sede=@s LIMIT 1", conn);
                    cmd.Parameters.AddWithValue("@s", idSede);
                    var res = cmd.ExecuteScalar();
                    if (res != null) idInstitucion = Convert.ToInt32(res);
                }
                catch { online = false; }
            }
            if (!online)
                idInstitucion = SyncService.ObtenerInstitucionDeSede(idSede);

            // 1. ¿Hay excepción para hoy? (con prioridad sede+grado > sede > institución)
            HorarioExcepcion excepcion = null;
            if (online)
            {
                try { excepcion = _excepcionRepo.ObtenerHoy(idSede, idGrado, idInstitucion); }
                catch { online = false; }
            }
            if (!online)
                excepcion = _excepcionRepo.ObtenerHoyOffline(idSede, idGrado, idInstitucion);

            if (excepcion != null && excepcion.Franjas.Count > 0)
            {
                foreach (var f in excepcion.Franjas.OrderBy(x => x.Orden).ThenBy(x => x.HoraInicio))
                    resultado.Add(new FranjaVigente(f.HoraInicio, f.HoraLimiteTarde, f.HoraCierreIngreso, f.Nombre, true));
                return resultado;
            }

            // 2. Horario semanal normal (grupo+grado > grado > sede general)
            Horario horario = null;
            if (online)
            {
                try { horario = _horarioRepo.ObtenerHoy(idSede, idGrado, idGrupo); }
                catch { online = false; }
            }
            if (!online)
                horario = _horarioRepo.ObtenerHoyOffline(idSede, idGrado, idGrupo);

            if (horario == null) return resultado;

            resultado.Add(new FranjaVigente(horario.HoraInicio, horario.HoraLimiteTarde, horario.HoraCierreIngreso, "Horario habitual", false));

            // Las franjas adicionales se suman al horario base
            List<FranjaHorario> franjas;
            if (online)
            {
                try { franjas = _franjaRepo.ObtenerPorHorario(horario.IdHorario); }
                catch { franjas = _franjaRepo.ObtenerPorHorarioOffline(horario.IdHorario); }
            }
            else
            {
                franjas = _franjaRepo.ObtenerPorHorarioOffline(horario.IdHorario);
            }

            foreach (var f in franjas.OrderBy(x => x.Orden).ThenBy(x => x.HoraInicio))
                resultado.Add(new FranjaVigente(f.HoraInicio, f.HoraLimiteTarde, f.HoraCierreIngreso, f.Nombre, true));

            return resultado;
        }
    }

    // ═══════════════════════════════════════════════════
    // SERVICIO OFFLINE (SQLite local)
    // ═══════════════════════════════════════════════════
    public class OfflineService
    {
        public void GuardarOffline(RegistroIngreso registro)
        {
            try
            {
                using var conn = ConexionSQLite.ObtenerConexion();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO registros_pendientes
                    (id_estudiante, id_sede, fecha_ingreso, hora_ingreso,
                     estado_ingreso, puntaje_biometrico, nombre_franja, sincronizado)
                    VALUES (@est,@sede,@fecha,@hora,@estado,@puntaje,@franja,0)";
                cmd.Parameters.AddWithValue("@est",    registro.IdEstudiante);
                cmd.Parameters.AddWithValue("@sede",   registro.IdSede);
                cmd.Parameters.AddWithValue("@fecha",  registro.FechaIngreso.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@hora",   registro.HoraIngreso.ToString(@"hh\:mm\:ss"));
                cmd.Parameters.AddWithValue("@estado", registro.EstadoIngreso.ToString());
                cmd.Parameters.AddWithValue("@puntaje", registro.PuntajeBiometrico);
                cmd.Parameters.AddWithValue("@franja", (object?)registro.NombreFranja ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        public List<RegistroIngreso> CargarOffline()
        {
            var lista = new List<RegistroIngreso>();
            try
            {
                using var conn = ConexionSQLite.ObtenerConexion();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = "SELECT * FROM registros_pendientes WHERE sincronizado=0";
                using var dr = cmd.ExecuteReader();
                while (dr.Read())
                    lista.Add(MapearPendiente(dr));
            }
            catch { }
            return lista;
        }

        public int SincronizarConMySQL() => SyncService.SincronizarHaciaMySQL();

        public int ContarPendientes()
        {
            try
            {
                using var conn = ConexionSQLite.ObtenerConexion();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM registros_pendientes WHERE sincronizado=0";
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch { return 0; }
        }

        private static RegistroIngreso MapearPendiente(SqliteDataReader dr) => new RegistroIngreso
        {
            IdEstudiante      = Convert.ToInt32(dr["id_estudiante"]),
            IdSede            = Convert.ToInt32(dr["id_sede"]),
            FechaIngreso      = DateTime.Parse(dr["fecha_ingreso"].ToString()),
            HoraIngreso       = TimeSpan.Parse(dr["hora_ingreso"].ToString()),
            EstadoIngreso     = (EstadoIngreso)Enum.Parse(typeof(EstadoIngreso), dr["estado_ingreso"].ToString()),
            PuntajeBiometrico = Convert.ToSingle(dr["puntaje_biometrico"]),
            NombreFranja      = dr["nombre_franja"] == DBNull.Value ? null : dr["nombre_franja"].ToString(),
            Sincronizado      = false
        };
    }

    // ═══════════════════════════════════════════════════
    // CACHÉ DE HUELLAS EN MEMORIA
    // ═══════════════════════════════════════════════════
    public class CacheHuellas
    {
        // Caché global (SUPERADMIN sin filtro)
        private static List<HuellaDigital> _cacheTodas = new();
        private static DateTime _ultimaActualizacionTodas = DateTime.MinValue;

        // Caché por institución
        private static readonly Dictionary<int, List<HuellaDigital>> _cachePorInst = new();
        private static readonly Dictionary<int, DateTime> _ultimaActualizacionPorInst = new();

        private static readonly TimeSpan _tiempoExpiracion = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Devuelve el caché filtrado por institución (si se indica) o global.
        /// Cuando se proporciona idInstitucion solo se cargan y devuelven las
        /// huellas de esa institución — ni se consultan ni se almacenan las demás.
        /// </summary>
        public static List<HuellaDigital> ObtenerCache(int? idInstitucion = null)
        {
            if (idInstitucion.HasValue)
            {
                int id = idInstitucion.Value;
                if (!_cachePorInst.ContainsKey(id) ||
                    DateTime.Now - _ultimaActualizacionPorInst.GetValueOrDefault(id) > _tiempoExpiracion)
                    ActualizarInstitucion(id);
                return _cachePorInst.GetValueOrDefault(id, new());
            }

            if (_cacheTodas.Count == 0 || DateTime.Now - _ultimaActualizacionTodas > _tiempoExpiracion)
                Actualizar();
            return _cacheTodas;
        }

        public static void Actualizar()
        {
            try
            {
                _cacheTodas = new HuellaRepository().ObtenerTodasActivas();
                _ultimaActualizacionTodas = DateTime.Now;
                SyncService.PersistirHuellas(_cacheTodas);
            }
            catch
            {
                // MySQL no disponible: cargar desde SQLite
                if (_cacheTodas.Count == 0)
                {
                    _cacheTodas = SyncService.CargarHuellasDeSQLite();
                    _ultimaActualizacionTodas = DateTime.Now;
                }
            }
        }

        public static void ActualizarInstitucion(int idInstitucion)
        {
            try
            {
                _cachePorInst[idInstitucion] = new HuellaRepository().ObtenerActivasPorInstitucion(idInstitucion);
                _ultimaActualizacionPorInst[idInstitucion] = DateTime.Now;
                SyncService.PersistirHuellas(_cachePorInst[idInstitucion], idInstitucion);
            }
            catch
            {
                // MySQL no disponible: cargar desde SQLite
                if (!_cachePorInst.ContainsKey(idInstitucion) || _cachePorInst[idInstitucion].Count == 0)
                {
                    _cachePorInst[idInstitucion] = SyncService.CargarHuellasDeSQLite(idInstitucion);
                    _ultimaActualizacionPorInst[idInstitucion] = DateTime.Now;
                }
            }
        }

        public static void Invalidar()
        {
            _cacheTodas.Clear();
            _ultimaActualizacionTodas = DateTime.MinValue;
            _cachePorInst.Clear();
            _ultimaActualizacionPorInst.Clear();
        }

        public static void InvalidarInstitucion(int idInstitucion)
        {
            _cachePorInst.Remove(idInstitucion);
            _ultimaActualizacionPorInst.Remove(idInstitucion);
        }

        public static int Cantidad => _cacheTodas.Count;
    }
}
