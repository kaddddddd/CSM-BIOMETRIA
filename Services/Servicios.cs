// ============================================================
// CSMBiometricoWPF.Services - Capa de Servicios
// ============================================================
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Repositories;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

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
                    var proxima = franjas.Where(f => ahora < f.Inicio).OrderBy(f => f.Inicio).FirstOrDefault();
                    if (proxima != null)
                    {
                        franjaActiva      = proxima;
                        esLlegadaAnticipada = true;
                    }
                }

                // Verificar duplicado:
                // - Llegada anticipada o sin franja: revisar todo el día (evita doble registro
                //   si llega antes de la franja y luego vuelve a intentarlo dentro de ella).
                // - Dentro de franja activa: revisar solo esa ventana horaria.
                bool yaRegistrado = (franjaActiva == null || esLlegadaAnticipada)
                    ? _registroRepo.YaRegistroHoy(estudiante.IdEstudiante)
                    : _registroRepo.YaRegistroEnFranja(estudiante.IdEstudiante, franjaActiva.Inicio, franjaActiva.Cierre);

                if (yaRegistrado)
                    return (EstadoIngreso.YA_REGISTRADO,
                        $"{estudiante.NombreCompleto} ya registró ingreso hoy.",
                        franjaActiva?.EsFranja == true ? franjaActiva.Nombre : null);

                EstadoIngreso estado;
                if (franjaActiva == null)
                    estado = EstadoIngreso.FUERA_DE_HORARIO;
                else if (esLlegadaAnticipada)
                    estado = EstadoIngreso.A_TIEMPO;          // llegó antes de hora = puntual
                else
                    estado = ahora <= franjaActiva.LimiteTarde ? EstadoIngreso.A_TIEMPO : EstadoIngreso.TARDE;

                var registro = new RegistroIngreso
                {
                    IdEstudiante      = estudiante.IdEstudiante,
                    IdSede            = estudiante.IdSede,
                    EstadoIngreso     = estado,
                    PuntajeBiometrico = puntajeBiometrico,
                    NombreFranja      = franjaActiva?.EsFranja == true ? franjaActiva.Nombre : null
                };

                if (Data.ConexionDB.EstaConectado)
                    _registroRepo.Guardar(registro);
                else
                    _offlineService.GuardarOffline(registro);

                IngresoRegistrado?.Invoke(this, EventArgs.Empty);

                // Nombre de franja solo cuando es una franja extra (no el horario habitual)
                string? nomFranja = franjaActiva?.EsFranja == true ? franjaActiva.Nombre : null;

                string msg = estado switch
                {
                    EstadoIngreso.A_TIEMPO         => $"✅ {estudiante.NombreCompleto} - A TIEMPO",
                    EstadoIngreso.TARDE            => $"⚠️ {estudiante.NombreCompleto} - TARDE",
                    EstadoIngreso.FUERA_DE_HORARIO => $"❌ {estudiante.NombreCompleto} - INASISTENCIA",
                    _                              => ""
                };

                _logRepo.Registrar(TipoEvento.REGISTRO_ASISTENCIA,
                    $"Ingreso: {estudiante.Identificacion} - {estado}");
                return (estado, msg, nomFranja);
            }
            catch (Exception ex)
            {
                _logRepo.Registrar(TipoEvento.ERROR_DB, ex.Message, NivelLog.ERROR);
                throw;
            }
        }

        private record FranjaVigente(TimeSpan Inicio, TimeSpan LimiteTarde, TimeSpan Cierre, string Nombre = "Horario habitual", bool EsFranja = false);

        private List<FranjaVigente> ObtenerFranjasVigentes(int idSede, int idGrado, int idGrupo)
        {
            var resultado = new List<FranjaVigente>();

            // Obtener institución del estudiante para excepciones a nivel institución
            int? idInstitucion = null;
            try
            {
                using var conn = Data.ConexionDB.ObtenerConexion();
                using var cmd = new Microsoft.Data.Sqlite.SqliteCommand(
                    "SELECT id_institucion FROM sedes WHERE id_sede=@s LIMIT 1", conn);
                cmd.Parameters.AddWithValue("@s", idSede);
                var res = cmd.ExecuteScalar();
                if (res != null) idInstitucion = Convert.ToInt32(res);
            }
            catch { }

            // 1. ¿Hay excepción para hoy? (con prioridad sede+grado > sede > institución)
            var excepcion = _excepcionRepo.ObtenerHoy(idSede, idGrado, idInstitucion);
            if (excepcion != null && excepcion.Franjas.Count > 0)
            {
                foreach (var f in excepcion.Franjas.OrderBy(x => x.Orden).ThenBy(x => x.HoraInicio))
                    resultado.Add(new FranjaVigente(f.HoraInicio, f.HoraLimiteTarde, f.HoraCierreIngreso, f.Nombre, true));
                return resultado;
            }

            // 2. Horario semanal normal (grupo+grado > grado > sede general)
            var horario = _horarioRepo.ObtenerHoy(idSede, idGrado, idGrupo);
            if (horario == null) return resultado;

            var franjas = _franjaRepo.ObtenerPorHorario(horario.IdHorario);
            if (franjas.Count > 0)
            {
                // Cualquier FranjaHorario explícitamente configurada se marca como EsFranja=true
                foreach (var f in franjas.OrderBy(x => x.Orden).ThenBy(x => x.HoraInicio))
                    resultado.Add(new FranjaVigente(f.HoraInicio, f.HoraLimiteTarde, f.HoraCierreIngreso, f.Nombre, true));
            }
            else
            {
                // Sin franjas adicionales: usa el horario habitual directamente
                resultado.Add(new FranjaVigente(horario.HoraInicio, horario.HoraLimiteTarde, horario.HoraCierreIngreso, "Horario habitual", false));
            }

            return resultado;
        }
    }

    // ═══════════════════════════════════════════════════
    // SERVICIO OFFLINE (JSON local — usa System.Text.Json)
    // ═══════════════════════════════════════════════════
    public class OfflineService
    {
        private readonly string _archivoOffline;
        private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = false };

        public OfflineService()
        {
            _archivoOffline = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "offline_records.json");
        }

        public void GuardarOffline(RegistroIngreso registro)
        {
            var registros = CargarOffline();
            registros.Add(registro);
            File.WriteAllText(_archivoOffline, JsonSerializer.Serialize(registros, _jsonOpts));
        }

        public List<RegistroIngreso> CargarOffline()
        {
            if (!File.Exists(_archivoOffline)) return new List<RegistroIngreso>();
            try
            {
                string json = File.ReadAllText(_archivoOffline);
                return JsonSerializer.Deserialize<List<RegistroIngreso>>(json) ?? new List<RegistroIngreso>();
            }
            catch { return new List<RegistroIngreso>(); }
        }

        public int SincronizarConMySQL()
        {
            var registros = CargarOffline().Where(r => !r.Sincronizado).ToList();
            if (registros.Count == 0) return 0;

            var repo = new RegistroIngresoRepository();
            int sincronizados = 0;
            foreach (var r in registros)
            {
                try { r.Sincronizado = true; repo.Guardar(r); sincronizados++; }
                catch { }
            }
            var todos = CargarOffline();
            todos.ForEach(r => r.Sincronizado = true);
            File.WriteAllText(_archivoOffline, JsonSerializer.Serialize(todos, _jsonOpts));
            return sincronizados;
        }

        public int ContarPendientes() => CargarOffline().Count(r => !r.Sincronizado);
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
            }
            catch { }
        }

        public static void ActualizarInstitucion(int idInstitucion)
        {
            try
            {
                _cachePorInst[idInstitucion] = new HuellaRepository().ObtenerActivasPorInstitucion(idInstitucion);
                _ultimaActualizacionPorInst[idInstitucion] = DateTime.Now;
            }
            catch { }
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
