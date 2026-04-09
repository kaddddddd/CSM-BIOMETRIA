// ============================================================
// CSMBiometrico.Models - Todas las entidades del sistema
// Archivo: Models/Entidades.cs
// ============================================================

using System;
using System.Collections.Generic;

namespace CSMBiometricoWPF.Models
{
    // ─────────────────────────────────────────
    // ROL
    // ─────────────────────────────────────────
    public class Rol
    {
        public int IdRol { get; set; }
        public string NombreRol { get; set; }
        public string Descripcion { get; set; }
        public bool Activo { get; set; } = true;
    }

    // ─────────────────────────────────────────
    // USUARIO
    // ─────────────────────────────────────────
    public class Usuario
    {
        public int IdUsuario { get; set; }
        public int IdRol { get; set; }
        public int? IdInstitucion { get; set; }
        public string Username { get; set; }
        public string PasswordHash { get; set; }
        public string NombreCompleto { get; set; }
        public string Email { get; set; }
        public bool Estado { get; set; } = true;
        public DateTime? UltimoLogin { get; set; }
        public int IntentosFallidos { get; set; }
        public bool Bloqueado { get; set; }
        public DateTime FechaCreacion { get; set; }
        // Navegación
        public string NombreRol { get; set; }
        public string NombreInstitucion { get; set; }
    }

    // ─────────────────────────────────────────
    // INSTITUCIÓN
    // ─────────────────────────────────────────
    public class Institucion
    {
        public int IdInstitucion { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string Telefono { get; set; }
        public bool Estado { get; set; } = true;
        public DateTime FechaCreacion { get; set; }
        public List<Sede> Sedes { get; set; } = new List<Sede>();
        public override string ToString() => Nombre;
    }

    // ─────────────────────────────────────────
    // SEDE
    // ─────────────────────────────────────────
    public class Sede
    {
        public int IdSede { get; set; }
        public int IdInstitucion { get; set; }
        public string NombreSede { get; set; }
        public string Direccion { get; set; }
        public bool Estado { get; set; } = true;
        // Navegación
        public string NombreInstitucion { get; set; }
        public override string ToString() => NombreSede;
    }

    // ─────────────────────────────────────────
    // GRADO
    // ─────────────────────────────────────────
    public class Grado
    {
        public int IdGrado { get; set; }
        public string NombreGrado { get; set; }
        public int OrdenGrado { get; set; }
        public bool Estado { get; set; } = true;
        public override string ToString() => NombreGrado;
    }

    // ─────────────────────────────────────────
    // GRUPO
    // ─────────────────────────────────────────
    public class Grupo
    {
        public int IdGrupo { get; set; }
        public string NombreGrupo { get; set; }
        public bool Estado { get; set; } = true;
        public override string ToString() => NombreGrupo;
    }

    // ─────────────────────────────────────────
    // ESTUDIANTE
    // ─────────────────────────────────────────
    public class Estudiante
    {
        public int IdEstudiante { get; set; }
        public string Identificacion { get; set; }
        public string Nombre { get; set; }
        public string Apellidos { get; set; }
        public string NombreCompleto => $"{Nombre} {Apellidos}";
        public int IdInstitucion { get; set; }
        public int IdSede { get; set; }
        public int IdGrado { get; set; }
        public int IdGrupo { get; set; }
        public string Telefono { get; set; }
        public string Email { get; set; }
        public byte[] Foto { get; set; }
        public string Estado { get; set; } = "ACTIVO";
        public DateTime? FechaMatricula { get; set; }
        public DateTime FechaCreacion { get; set; }
        // Navegación (para grids)
        public string NombreInstitucion { get; set; }
        public string NombreSede { get; set; }
        public string NombreGrado { get; set; }
        public string NombreGrupo { get; set; }
        public bool TieneHuella { get; set; }
    }

    // ─────────────────────────────────────────
    // HUELLA DIGITAL
    // ─────────────────────────────────────────
    public enum TipoDedo
    {
        PULGAR_D, INDICE_D, MEDIO_D, ANULAR_D, MENIQUE_D,
        PULGAR_I, INDICE_I, MEDIO_I, ANULAR_I, MENIQUE_I
    }

    public class HuellaDigital
    {
        public int IdHuella { get; set; }
        public int IdEstudiante { get; set; }
        public byte[] TemplateBiometrico { get; set; }
        public TipoDedo Dedo { get; set; } = TipoDedo.INDICE_D;
        public int Calidad { get; set; }
        public bool Activo { get; set; } = true;
        public DateTime FechaRegistro { get; set; }
        public int? RegistradoPor { get; set; }
        // Para comparación en memoria (se carga al inicio)
        public string NombreEstudiante { get; set; }
        public int IdSede { get; set; }
        public int IdInstitucion { get; set; }
    }

    // ─────────────────────────────────────────
    // HORARIO
    // ─────────────────────────────────────────
    public enum DiaSemana
    {
        LUNES, MARTES, MIERCOLES, JUEVES, VIERNES, SABADO, DOMINGO
    }

    public class Horario
    {
        public int IdHorario { get; set; }
        public int IdSede { get; set; }
        public DiaSemana DiaSemana { get; set; }
        public TimeSpan HoraInicio { get; set; }
        public TimeSpan HoraLimiteTarde { get; set; }
        public TimeSpan HoraCierreIngreso { get; set; }
        public bool Activo { get; set; } = true;
        public string NombreSede { get; set; }
        public int? IdGrado { get; set; }
        public string NombreGrado { get; set; }
        public int? IdGrupo { get; set; }
        public string NombreGrupo { get; set; }
    }

    // ─────────────────────────────────────────
    // FRANJA DE HORARIO (ventana adicional por día)
    // ─────────────────────────────────────────
    public class FranjaHorario
    {
        public int IdFranja { get; set; }
        public int IdHorario { get; set; }
        public string Nombre { get; set; } = "Franja";
        public TimeSpan HoraInicio { get; set; }
        public TimeSpan HoraLimiteTarde { get; set; }
        public TimeSpan HoraCierreIngreso { get; set; }
        public int Orden { get; set; }
        public bool Activo { get; set; } = true;
        public string ResumenStr =>
            $"{Nombre}: {HoraInicio:hh\\:mm} → {HoraLimiteTarde:hh\\:mm} → {HoraCierreIngreso:hh\\:mm}";
    }

    // ─────────────────────────────────────────
    // EXCEPCIÓN DE HORARIO (override por fecha)
    // ─────────────────────────────────────────
    public class HorarioExcepcion
    {
        public int IdExcepcion { get; set; }
        public int IdSede { get; set; }
        public DateTime FechaExcepcion { get; set; }
        public string Descripcion { get; set; }
        public bool Activo { get; set; } = true;
        public List<FranjaExcepcion> Franjas { get; set; } = new();
        public string NombreSede { get; set; }
        public string Alcance { get; set; } = "SEDE";   // SEDE | GRADO | INSTITUCION
        public int? IdGrado { get; set; }
        public int? IdInstitucion { get; set; }
        public string NombreGrado { get; set; }
        public string FechaStr => FechaExcepcion.ToString("dd/MM/yyyy");
        public string FranjasCount => Franjas.Count == 1 ? "1 franja" : $"{Franjas.Count} franjas";
        public string AlcanceLabel => Alcance switch
        {
            "INSTITUCION" => "Todas las sedes",
            "GRADO"       => $"Grado: {NombreGrado}",
            _             => "Esta sede"
        };
        public override string ToString() => $"{FechaStr} — {Descripcion}";
    }

    public class FranjaExcepcion
    {
        public int IdFranjaExc { get; set; }
        public int IdExcepcion { get; set; }
        public string Nombre { get; set; } = "Franja";
        public TimeSpan HoraInicio { get; set; }
        public TimeSpan HoraLimiteTarde { get; set; }
        public TimeSpan HoraCierreIngreso { get; set; }
        public int Orden { get; set; }
        public string ResumenStr =>
            $"{Nombre}: {HoraInicio:hh\\:mm} → {HoraLimiteTarde:hh\\:mm} → {HoraCierreIngreso:hh\\:mm}";
    }

    // ─────────────────────────────────────────
    // REGISTRO DE INGRESO
    // ─────────────────────────────────────────
    public enum EstadoIngreso { A_TIEMPO, TARDE, FUERA_DE_HORARIO, YA_REGISTRADO }

    public class RegistroIngreso
    {
        public int IdRegistro { get; set; }
        public int IdEstudiante { get; set; }
        public int IdSede { get; set; }
        public DateTime FechaIngreso { get; set; }
        public TimeSpan HoraIngreso { get; set; }
        public EstadoIngreso EstadoIngreso { get; set; }
        public float PuntajeBiometrico { get; set; }
        public bool Sincronizado { get; set; } = true;
        public string Observaciones { get; set; }
        public string NombreFranja { get; set; }
        public bool TieneFranja => !string.IsNullOrWhiteSpace(NombreFranja);
        public bool   EsJustificado => !string.IsNullOrWhiteSpace(Observaciones);
        // Navegación
        public string NombreEstudiante { get; set; }
        public string Identificacion { get; set; }
        public string NombreSede { get; set; }
        public string NombreGrado { get; set; }
        public string NombreGrupo { get; set; }
        // Propiedades calculadas para el grid
        public string HoraIngresoStr => HoraIngreso == default ? "—" : HoraIngreso.ToString(@"hh\:mm");
        public string EstadoStr => EstadoIngreso switch
        {
            EstadoIngreso.A_TIEMPO       => "A tiempo",
            EstadoIngreso.TARDE          => "Tarde",
            EstadoIngreso.FUERA_DE_HORARIO => "Inasistencia",
            EstadoIngreso.YA_REGISTRADO  => "Ya registrado",
            _                            => EstadoIngreso.ToString()
        };
        public bool EsTarde => EstadoIngreso == EstadoIngreso.TARDE;
        public bool EsAusente => EstadoIngreso == EstadoIngreso.FUERA_DE_HORARIO;
    }

    // ─────────────────────────────────────────
    // LOG DEL SISTEMA
    // ─────────────────────────────────────────
    public enum TipoEvento
    {
        LOGIN, LOGOUT, ERROR_DB, ERROR_LECTOR, REGISTRO_ASISTENCIA,
        ENROLAMIENTO, CRUD, SISTEMA, SEGURIDAD
    }

    public enum NivelLog { INFO, ADVERTENCIA, ERROR, CRITICO }

    public class LogSistema
    {
        public int IdLog { get; set; }
        public int? IdUsuario { get; set; }
        public TipoEvento TipoEvento { get; set; }
        public string Descripcion { get; set; }
        public string IpOrigen { get; set; }
        public NivelLog Nivel { get; set; } = NivelLog.INFO;
        public DateTime FechaEvento { get; set; }
        public string NombreUsuario { get; set; }
    }

    // ─────────────────────────────────────────
    // ESTADÍSTICAS DASHBOARD
    // ─────────────────────────────────────────
    public class EstadisticasDia
    {
        public int TotalEstudiantes { get; set; }
        public int PresentesHoy { get; set; }
        public int AusentesHoy => TotalEstudiantes - PresentesHoy;
        public int ATiempo { get; set; }
        public int Tardanzas { get; set; }
        public string NombreSede { get; set; }
        public string NombreInstitucion { get; set; }
        public double PorcentajeAsistencia =>
            TotalEstudiantes > 0 ? (double)PresentesHoy / TotalEstudiantes * 100 : 0;
    }

    // ─────────────────────────────────────────
    // PERÍODO ACADÉMICO
    // ─────────────────────────────────────────
    public class PeriodoAcademico
    {
        public int    IdPeriodo     { get; set; }
        public int?   IdInstitucion { get; set; }
        public string Nombre        { get; set; } = "";
        public int    MesInicio     { get; set; }
        public int    DiaInicio     { get; set; }
        public int    MesFin        { get; set; }
        public int    DiaFin        { get; set; }
        public int    Orden         { get; set; }

        public string RangoStr =>
            $"{NombreMes(MesInicio)} {DiaInicio} – {NombreMes(MesFin)} {DiaFin}";

        private static string NombreMes(int m) => m switch
        {
            1=>"Ene", 2=>"Feb", 3=>"Mar", 4=>"Abr",
            5=>"May", 6=>"Jun", 7=>"Jul", 8=>"Ago",
            9=>"Sep", 10=>"Oct", 11=>"Nov", 12=>"Dic", _=>""
        };
    }

    // ─────────────────────────────────────────
    // SESIÓN ACTIVA (Singleton)
    // ─────────────────────────────────────────
    public static class SesionActiva
    {
        public static Usuario UsuarioActual { get; set; }
        public static Institucion InstitucionActual { get; set; }
        public static bool EsSuperAdmin => UsuarioActual?.NombreRol == "SUPERADMIN";
        public static bool EsAdministrador => UsuarioActual?.NombreRol == "ADMINISTRADOR";
        public static bool EsOperador => UsuarioActual?.NombreRol == "OPERADOR";

        public static void Cerrar()
        {
            UsuarioActual = null;
            InstitucionActual = null;
        }
    }

}
