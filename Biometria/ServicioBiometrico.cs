// ============================================================
// CSMBiometrico.Biometria - Servicio DigitalPersona U.are.U 5300
// Archivo: Biometria/ServicioBiometrico.cs
// 
// DEPENDENCIAS REQUERIDAS (agregar referencias al proyecto):
//   - DPUruNet.dll
//   - DPXUru.dll
//   - DPCtlUruNet.dll
//   - DPCtlXUru.dll
// ============================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using DPUruNet;
using CSMBiometricoWPF.Models;
using CSMBiometricoWPF.Services;

namespace CSMBiometricoWPF.Biometria
{
    /// <summary>
    /// Estados posibles del lector biométrico.
    /// </summary>
    public enum EstadoLector { Desconectado, Inicializando, Listo, Capturando, Error }

    /// <summary>
    /// Resultado de una captura biométrica.
    /// </summary>
    public class ResultadoCaptura
    {
        public bool Exitosa { get; set; }
        public byte[] Template { get; set; }
        public int Calidad { get; set; }
        public Bitmap ImagenHuella { get; set; }
        public string Mensaje { get; set; }
    }

    /// <summary>
    /// Resultado de una identificación biométrica.
    /// </summary>
    public class ResultadoIdentificacion
    {
        public bool Identificado { get; set; }
        public Estudiante Estudiante { get; set; }
        public float Puntaje { get; set; }
        public string Mensaje { get; set; }
    }

    /// <summary>
    /// Servicio principal para el manejo del lector DigitalPersona U.are.U 5300.
    /// Implementa captura asincrónica para no bloquear la UI de Windows Forms.
    /// </summary>
    public class ServicioBiometrico : IDisposable
    {
        // ── Instancia compartida (singleton) ───────────────
        // Todas las páginas usan esta instancia para no soltar el handle USB
        // entre navegaciones. Solo se dispone al cerrar la aplicación.
        private static readonly Lazy<ServicioBiometrico> _compartido =
            new Lazy<ServicioBiometrico>(() => new ServicioBiometrico());
        public static ServicioBiometrico Compartido => _compartido.Value;

        // ── Configuración ──────────────────────────────────
        private const int UMBRAL_CALIDAD = 0;           // SDK devuelve 0 siempre; validación via template
        private const float UMBRAL_COMPARACION = 0.7f; // FAR ~1%: acepta matches con variación de posición
        private const int MAX_MUESTRAS_ENROLAMIENTO = 4; // centro, derecha, izquierda, confirmación

        // ── Estado interno ─────────────────────────────────
        private Reader _lector;
        private ReaderCollection _lectoresCol; // mantener referencia para evitar GC
        private Thread _hiloCaptura;
        private bool _capturando = false;
        private bool _disposed = false;
        private System.Management.ManagementEventWatcher _watcherUsb;
        private System.Management.ManagementEventWatcher _watcherUsbConexion;
        private System.Threading.Timer _timerReconexion;
        private int _erroresConsecutivos = 0;
        private const int MAX_ERRORES_DESCONEXION = 2;
        private int _manejandoDesconexion = 0; // evita doble ejecución concurrente

        // ── Eventos públicos ───────────────────────────────
        public event EventHandler<ResultadoCaptura> OnHuellaCapturada;
        public event EventHandler<ResultadoIdentificacion> OnEstudianteIdentificado;
        public event EventHandler<EstadoLector> OnCambioEstado;
        public event EventHandler<string> OnMensaje;
        public event EventHandler<Bitmap> OnImagenCapturada;

        // ── Propiedades ─────────────────────────────────────
        public EstadoLector Estado { get; private set; } = EstadoLector.Desconectado;
        public bool EstaListo => Estado == EstadoLector.Listo || Estado == EstadoLector.Capturando;
        public string UltimoMensaje { get; private set; } = "";
        public string InfoLector { get; private set; } = "";

        /// <summary>
        /// Filtra las comparaciones biométricas a solo la institución indicada.
        /// null = sin filtro (compara contra todas las instituciones).
        /// </summary>
        public int? IdInstitucionFiltro { get; set; }

        /// <summary>
        /// Filtra las comparaciones biométricas a solo la sede indicada.
        /// Se aplica después del filtro de institución.
        /// null = sin filtro de sede.
        /// </summary>
        public int? IdSedeFiltro { get; set; }

        // ── Modo operación ──────────────────────────────────
        public enum ModoCaptura { Enrolamiento, Identificacion, Prueba }
        private ModoCaptura _modo = ModoCaptura.Identificacion;
        private List<Fmd> _muestrasEnrolamiento = new List<Fmd>();

        // ═══════════════════════════════════════════════════
        // INICIALIZACIÓN
        // ═══════════════════════════════════════════════════

        /// <summary>
        /// Inicializa el lector biométrico DigitalPersona.
        /// Debe llamarse en un hilo separado o con async/await.
        /// </summary>
        public async Task<bool> InicializarAsync()
        {
            return await Task.Run(() => Inicializar());
        }

        private bool Inicializar()
        {
            // Si el lector ya está abierto y funcionando, no hacer nada.
            // Volver a llamar Open() sobre un lector ya abierto puede corromper
            // el estado interno del SDK y dejar el lector sin responder.
            if (Estado == EstadoLector.Listo || Estado == EstadoLector.Capturando)
            {
                EmitirMensaje("Lector ya inicializado.");
                return true;
            }

            CambiarEstado(EstadoLector.Inicializando);

            // ── 1. Verificar servicio de Windows ──────────────
            EmitirMensaje("Verificando servicio DigitalPersona...");
            string servicioEstado = VerificarServicioDP();
            if (servicioEstado != null)
                EmitirMensaje(servicioEstado);

            // ── 2. Obtener lectores ───────────────────────────
            // GetReaders() NO debe llamarse más de una vez por sesión: el SDK no
            // libera el handle interno a tiempo y llamadas adicionales dejan handles
            // colgados que corrompen el estado del dispositivo.
            if (_lectoresCol == null || _lectoresCol.Count == 0)
            {
                EmitirMensaje("Buscando lector...");
                try
                {
                    _lectoresCol = ReaderCollection.GetReaders();
                }
                catch (Exception ex)
                {
                    EmitirMensaje($"SDK error al buscar lector: {ex.Message}");
                }
            }
            else
            {
                EmitirMensaje("Reutilizando lector detectado previamente...");
            }

            if (_lectoresCol == null || _lectoresCol.Count == 0)
            {
                CambiarEstado(EstadoLector.Error);
                string diag = DiagnosticoUSB();
                EmitirMensaje(
                    "LECTOR NO ENCONTRADO\n" +
                    "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                    diag);
                return false;
            }

            // ── 3. Abrir lector ───────────────────────────────
            _lector = _lectoresCol[0];
            InfoLector = $"U.are.U 5300  ·  S/N: {_lector.Description.SerialNumber}";
            EmitirMensaje($"Lector encontrado: {InfoLector}");

            // Reintentar Open() hasta 5 veces con pausa entre intentos.
            // El SDK puede tardar en liberar el handle USB de una sesión anterior.
            // IMPORTANTE: Open() puede lanzar SDKException (DP_FAILURE) cuando el
            // Reader en _lectoresCol[0] ya no corresponde al dispositivo físico
            // reconectado — capturar la excepción y tratar como fallo recuperable.
            Constants.ResultCode res = Constants.ResultCode.DP_DEVICE_BUSY;
            const int MAX_INTENTOS_OPEN = 5;
            for (int i = 1; i <= MAX_INTENTOS_OPEN; i++)
            {
                try
                {
                    res = _lector.Open(Constants.CapturePriority.DP_PRIORITY_EXCLUSIVE);
                    if (res == Constants.ResultCode.DP_SUCCESS) break;
                    res = _lector.Open(Constants.CapturePriority.DP_PRIORITY_COOPERATIVE);
                    if (res == Constants.ResultCode.DP_SUCCESS) break;
                }
                catch (Exception ex)
                {
                    // SDK lanzó excepción (ej. DP_FAILURE) en lugar de devolver ResultCode.
                    // El handle del Reader ya no es válido — marcar como fallo y reintentar.
                    EmitirMensaje($"Error abriendo lector: {ex.Message.Split('\n')[0]}");
                    res = Constants.ResultCode.DP_DEVICE_BUSY;
                    // En el último intento, limpiar _lectoresCol para que el siguiente
                    // ciclo de reconexión llame GetReaders() con el dispositivo ya estable.
                    if (i == MAX_INTENTOS_OPEN)
                    {
                        _lectoresCol = null;
                        _lector = null;
                    }
                }
                if (i < MAX_INTENTOS_OPEN)
                {
                    EmitirMensaje($"Esperando que el sensor se libere... ({i}/{MAX_INTENTOS_OPEN})");
                    Thread.Sleep(1500);
                }
            }

            if (res != Constants.ResultCode.DP_SUCCESS)
            {
                CambiarEstado(EstadoLector.Error);
                // Limpiar referencia para que el siguiente intento llame GetReaders() de nuevo
                _lectoresCol = null;
                _lector = null;
                EmitirMensaje($"No se pudo abrir el lector ({res}). " +
                              "Cierre otros programas que usen el sensor e intente de nuevo.");
                return false;
            }

            CambiarEstado(EstadoLector.Listo);
            EmitirMensaje("Lector listo. Coloque su dedo al CENTRO del sensor.");
            IniciarWatcherUsb();
            return true;
        }

        private void IniciarWatcherUsb()
        {
            try
            {
                _watcherUsb?.Stop();
                _watcherUsb?.Dispose();
                // WITHIN 0.5: polling cada 500 ms para detectar desconexión más rápido
                var query = new System.Management.WqlEventQuery(
                    "SELECT * FROM __InstanceDeletionEvent WITHIN 0.5 " +
                    "WHERE TargetInstance ISA 'Win32_PnPEntity' " +
                    "AND TargetInstance.DeviceID LIKE '%VID_05BA%'");
                _watcherUsb = new System.Management.ManagementEventWatcher(query);
                _watcherUsb.EventArrived += (s, e) => ManejarDesconexion();
                _watcherUsb.Start();
            }
            catch { /* WMI no disponible: fallback por errores consecutivos */ }
        }

        private void IniciarWatcherUsbConexion()
        {
            try
            {
                _watcherUsbConexion?.Stop();
                _watcherUsbConexion?.Dispose();
                // WITHIN 0.5: polling cada 500 ms para detectar reconexión más rápido
                var query = new System.Management.WqlEventQuery(
                    "SELECT * FROM __InstanceCreationEvent WITHIN 0.5 " +
                    "WHERE TargetInstance ISA 'Win32_PnPEntity' " +
                    "AND TargetInstance.DeviceID LIKE '%VID_05BA%'");
                _watcherUsbConexion = new System.Management.ManagementEventWatcher(query);
                _watcherUsbConexion.EventArrived += (s, e) => Task.Run(() => ManejarConexion());
                _watcherUsbConexion.Start();
            }
            catch { /* WMI no disponible: el timer de reconexión sirve de fallback */ }
        }

        private void ManejarConexion()
        {
            if (_disposed || Estado == EstadoLector.Inicializando ||
                Estado == EstadoLector.Listo || Estado == EstadoLector.Capturando) return;

            try { _watcherUsbConexion?.Stop(); _watcherUsbConexion?.Dispose(); _watcherUsbConexion = null; } catch { }
            _timerReconexion?.Dispose();
            _timerReconexion = null;

            // Esperar a que el sistema operativo y el servicio DPHostSvc terminen de
            // inicializar el dispositivo USB antes de intentar abrirlo
            Thread.Sleep(1500);

            bool ok = Inicializar();
            if (ok)
            {
                IniciarCaptura(ModoCaptura.Identificacion);
            }
            else
            {
                // Si el SDK aún no reconoce el dispositivo, volver al timer de reconexión
                _timerReconexion = new System.Threading.Timer(_ => IntentarReconexion(), null, 3000, 3000);
            }
        }

        public void ManejarDesconexion()
        {
            // Evitar doble ejecución si WMI y el hilo de captura la llaman simultáneamente
            if (Interlocked.Exchange(ref _manejandoDesconexion, 1) == 1) return;
            try
            {
                if (Estado == EstadoLector.Desconectado) return;
                _capturando = false;
                _erroresConsecutivos = 0;
                try { _lector?.CancelCapture(); } catch { }
                // NO llamar Dispose() sobre el Reader: el SDK queda en estado corrupto
                // si se dispone y re-abre más de una vez — Open() devuelve éxito pero
                // Capture() falla silenciosamente. El handle USB ya lo libera el SO al
                // desconectar el dispositivo. El Reader vive en _lectoresCol[0] y se
                // re-abre limpiamente con Open() en cada reconexión.
                _lector = null;
                // NO limpiar _lectoresCol: GetReaders() no puede llamarse más de una vez
                // por proceso sin dejar handles colgados que corrompen el estado del SDK.
                try { _watcherUsb?.Stop(); _watcherUsb?.Dispose(); _watcherUsb = null; } catch { }
                CambiarEstado(EstadoLector.Desconectado);
                EmitirMensaje("Lector desconectado. Reintentando reconexión...");
                _timerReconexion?.Dispose();
                _timerReconexion = new System.Threading.Timer(_ => IntentarReconexion(), null, 3000, 3000);
                IniciarWatcherUsbConexion();
            }
            finally
            {
                Interlocked.Exchange(ref _manejandoDesconexion, 0);
            }
        }

        private void IntentarReconexion()
        {
            if (_disposed || Estado == EstadoLector.Inicializando ||
                Estado == EstadoLector.Listo || Estado == EstadoLector.Capturando) return;
            bool ok = Inicializar();
            if (ok)
            {
                _timerReconexion?.Dispose();
                _timerReconexion = null;
                IniciarCaptura(ModoCaptura.Identificacion);
            }
        }

        /// <summary>Verifica si el servicio de DigitalPersona está corriendo.</summary>
        private string VerificarServicioDP()
        {
            // Nombres conocidos del servicio en distintas versiones del SDK
            string[] nombres = { "DpHost", "DPHostSvc", "DpSvc", "DigitalPersonaService",
                                  "DPAgent", "DpFusionSvc" };
            try
            {
                var servicios = ServiceController.GetServices();
                foreach (var nombre in nombres)
                {
                    var svc = servicios.FirstOrDefault(s =>
                        s.ServiceName.IndexOf(nombre, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        s.DisplayName.IndexOf("DigitalPersona", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (svc != null)
                    {
                        if (svc.Status == ServiceControllerStatus.Running)
                            return $"✔ Servicio '{svc.DisplayName}' corriendo.";

                        // Intentar iniciar el servicio automáticamente
                        try
                        {
                            EmitirMensaje($"Servicio '{svc.DisplayName}' detenido. Iniciando...");
                            svc.Start();
                            svc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(12));
                            Thread.Sleep(2000); // esperar que el driver registre el lector
                            return $"✔ Servicio '{svc.DisplayName}' iniciado correctamente.";
                        }
                        catch
                        {
                            return $"⚠ Servicio '{svc.DisplayName}' está {svc.Status}. " +
                                   "Inícielo manualmente en services.msc";
                        }
                    }
                }
                return "⚠ No se encontró el servicio de DigitalPersona en este equipo.";
            }
            catch { return null; }
        }

        /// <summary>Genera un diagnóstico de dispositivos USB para ayudar a identificar el problema.</summary>
        private string DiagnosticoUSB()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                // 1) Buscar por nombre (DigitalPersona / U.are.U / Fingerprint)
                var byName = new System.Management.ManagementObjectSearcher(
                    "SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%DigitalPersona%' " +
                    "OR Name LIKE '%U.are.U%' OR Name LIKE '%Fingerprint%'").Get();

                // 2) Buscar por VID del fabricante (VID_05BA = DigitalPersona/HID Global)
                var byVid = new System.Management.ManagementObjectSearcher(
                    "SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE '%VID_05BA%'").Get();

                var encontrados = new System.Collections.Generic.HashSet<string>();
                bool hayErrorDriver = false;

                foreach (System.Management.ManagementObject d in byName)
                    encontrados.Add(d["DeviceID"]?.ToString() ?? "");
                foreach (System.Management.ManagementObject d in byVid)
                    encontrados.Add(d["DeviceID"]?.ToString() ?? "");

                if (encontrados.Count > 0)
                {
                    // Obtener detalles de los dispositivos encontrados
                    var allDevs = new System.Collections.Generic.List<System.Management.ManagementObject>();
                    foreach (System.Management.ManagementObject d in byName) allDevs.Add(d);
                    foreach (System.Management.ManagementObject d in byVid)
                    {
                        string id = d["DeviceID"]?.ToString() ?? "";
                        if (!allDevs.Exists(x => x["DeviceID"]?.ToString() == id)) allDevs.Add(d);
                    }

                    sb.AppendLine("Dispositivos detectados por Windows:");
                    foreach (var d in allDevs)
                    {
                        string nombre = d["Name"]?.ToString() ?? "(sin nombre)";
                        string status = d["Status"]?.ToString() ?? "?";
                        uint errorCode = 0;
                        try { errorCode = (uint)d["ConfigManagerErrorCode"]; } catch { }
                        string errorInfo = errorCode != 0 ? $" ⚠ Error de driver (código {errorCode})" : "";
                        sb.AppendLine($"  • {nombre}  [{status}]{errorInfo}");
                        if (errorCode != 0) hayErrorDriver = true;
                    }

                    if (hayErrorDriver)
                    {
                        sb.AppendLine();
                        sb.AppendLine("→ El dispositivo tiene un error de driver.");
                        sb.AppendLine("  Soluciones:");
                        sb.AppendLine("  1) Desinstale el driver en el Administrador de dispositivos.");
                        sb.AppendLine("  2) Reinstale: DigitalPersona U.are.U RTE para Windows.");
                        sb.AppendLine("  3) Reinicie el equipo después de reinstalar.");
                    }
                    else
                    {
                        sb.AppendLine();
                        sb.AppendLine("→ El dispositivo está visible pero el SDK no puede abrirlo.");
                        sb.AppendLine("  Soluciones:");
                        sb.AppendLine("  1) Verifique que el servicio 'DPHostSvc' esté corriendo (services.msc).");
                        sb.AppendLine("  2) Cierre cualquier otro programa que use el sensor.");
                        sb.AppendLine("  3) Reinstale los drivers desde el instalador del U.are.U RTE.");
                    }
                }
                else
                {
                    sb.AppendLine("Windows NO detecta el lector en el USB.");
                    sb.AppendLine("→ Pruebe otro puerto USB (preferiblemente USB 2.0).");
                    sb.AppendLine("→ Verifique que el cable esté bien conectado.");
                    sb.AppendLine("→ Instale el driver: DigitalPersona U.are.U RTE para Windows.");
                    sb.AppendLine("→ Si usa USB 3.0, intente en un puerto USB 2.0 (azul/negro).");
                }
            }
            catch
            {
                sb.AppendLine("Verifique:");
                sb.AppendLine("  1) Cable USB conectado firmemente");
                sb.AppendLine("  2) Driver U.are.U 5300 instalado (DigitalPersona RTE)");
                sb.AppendLine("  3) Servicio DPHostSvc activo (services.msc)");
            }
            return sb.ToString();
        }

        // ═══════════════════════════════════════════════════
        // CAPTURA ASINCRÓNICA
        // ═══════════════════════════════════════════════════

        /// <summary>
        /// Inicia la captura asincrónica del lector.
        /// El evento OnHuellaCapturada se dispara cuando se detecta una huella.
        /// </summary>
        public void IniciarCaptura(ModoCaptura modo = ModoCaptura.Identificacion)
        {
            if (_lector == null || Estado == EstadoLector.Desconectado) return;

            // Detener el hilo anterior solo si está vivo. Llamar CancelCapture()
            // en un lector sin captura activa puede corromper el estado del SDK.
            _capturando = false;
            if (_hiloCaptura != null && _hiloCaptura.IsAlive)
            {
                try { _lector.CancelCapture(); } catch { }
                _hiloCaptura.Join(3000);
            }

            _modo = modo;
            if (modo == ModoCaptura.Enrolamiento)
                _muestrasEnrolamiento.Clear();

            _capturando = true;
            IniciarCapturaInterna();
        }

        private void IniciarCapturaInterna()
        {
            if (_lector == null || !_capturando) return;
            _hiloCaptura = new Thread(() =>
            {
                while (_capturando)
                {
                    try
                    {
                        CambiarEstado(EstadoLector.Capturando);
                        var captureResult = _lector.Capture(
                            Constants.Formats.Fid.ANSI,
                            Constants.CaptureProcessing.DP_IMG_PROC_DEFAULT,
                            1500,  // timeout: 1.5 s — detecta desconexión más rápido que 3 s
                            500);  // resolución: 500 dpi (U.are.U 5300)
                        if (!_capturando) break;
                        _erroresConsecutivos = 0;
                        OnCapturedHandler(captureResult);
                        if (_capturando) Thread.Sleep(300);
                    }
                    catch (Exception ex)
                    {
                        _erroresConsecutivos++;
                        if (_erroresConsecutivos >= MAX_ERRORES_DESCONEXION)
                        {
                            ManejarDesconexion();
                            break;
                        }
                        EmitirMensaje($"Reintentando... ({ex.Message})");
                        Thread.Sleep(100);
                    }
                }
            }) { IsBackground = true };
            _hiloCaptura.Start();
        }

        /// <summary>
        /// Manejador del evento On del lector (llamado automáticamente por el SDK).
        /// </summary>
        private void OnCapturedHandler(CaptureResult captureResult)
        {
            try
            {
                // Validar resultado
                if (captureResult == null)
                {
                    EmitirMensaje("Sin respuesta del lector. Reintentando...");
                    return;
                }
                if (captureResult.ResultCode == Constants.ResultCode.DP_DEVICE_FAILURE)
                {
                    // Ocurre normalmente cuando no hay dedo en el sensor o el SDK
                    // interrumpe la captura. No es un error fatal — continuar el loop.
                    EmitirMensaje("Coloque su dedo en el sensor...");
                    return;
                }
                if (captureResult.ResultCode == Constants.ResultCode.DP_DEVICE_BUSY)
                {
                    // El lector está ocupado con otra captura. Esperar y reintentar.
                    EmitirMensaje("Lector ocupado, reintentando...");
                    Thread.Sleep(800);
                    return;
                }
                if (captureResult.ResultCode != Constants.ResultCode.DP_SUCCESS)
                {
                    EmitirMensaje($"Captura fallida ({captureResult.ResultCode}). Intente de nuevo.");
                    return;
                }

                // Verificar calidad (UMBRAL_CALIDAD=0 deshabilita el filtro porque este SDK
                // siempre devuelve Quality=0; la calidad real se valida al generar el template)
                int calidad = (int)captureResult.Quality;
                if (UMBRAL_CALIDAD > 0 && calidad < UMBRAL_CALIDAD)
                {
                    EmitirMensaje($"Calidad baja ({calidad}%). Coloque el dedo completo y firme sobre el sensor.");
                    return;
                }

                // Obtener imagen de la huella para mostrar en UI
                var imagen = ObtenerImagenHuella(captureResult.Data);

                switch (_modo)
                {
                    case ModoCaptura.Identificacion:
                        ProcesarIdentificacion(captureResult, imagen);
                        break;
                    case ModoCaptura.Enrolamiento:
                        ProcesarEnrolamiento(captureResult, imagen);
                        break;
                    case ModoCaptura.Prueba:
                        ProcesarPrueba(captureResult, imagen);
                        break;
                }
            }
            catch (Exception ex)
            {
                EmitirMensaje("Error procesando huella: " + ex.Message);
            }
        }

        // ═══════════════════════════════════════════════════
        // IDENTIFICACIÓN BIOMÉTRICA
        // ═══════════════════════════════════════════════════

        private void ProcesarIdentificacion(CaptureResult captureResult, Bitmap imagen)
        {
            EmitirMensaje("Identificando...");
            OnImagenCapturada?.Invoke(this, imagen);

            // Generar template de la captura actual
            var featureSet = ExtractFeatures(captureResult.Data);
            if (featureSet == null)
            {
                EmitirMensaje("No se pudo extraer features. Intente de nuevo.");
                return;
            }

            // El caché ya viene filtrado por institución cuando IdInstitucionFiltro tiene valor,
            // por lo que no se cargan ni almacenan huellas de otras instituciones.
            var huellasAComparar = CacheHuellas.ObtenerCache(IdInstitucionFiltro);

            // Filtro adicional por sede (kiosk por sede)
            if (IdSedeFiltro.HasValue)
                huellasAComparar = huellasAComparar
                    .Where(h => h.IdSede == IdSedeFiltro.Value)
                    .ToList();

            EmitirMensaje($"Identificando... ({huellasAComparar.Count} huellas" +
                          (IdInstitucionFiltro.HasValue ? $" · institución {IdInstitucionFiltro}" : "") +
                          (IdSedeFiltro.HasValue ? $" · sede {IdSedeFiltro}" : "") + ")");

            Identificado mejor = null;
            float mejorPuntaje = float.MaxValue;
            object lockMejor = new object();

            // Parallel.ForEach distribuye las comparaciones en todos los núcleos disponibles.
            // El lock solo protege la actualización del mejor resultado (operación mínima).
            System.Threading.Tasks.Parallel.ForEach(huellasAComparar, huellaCache =>
            {
                try
                {
                    var templateBD = new Fmd(
                        huellaCache.TemplateBiometrico,
                        (int)Constants.Formats.Fmd.ANSI,
                        Constants.WRAPPER_VERSION);

                    var comparacion = Comparison.Compare(featureSet.Data, 0, templateBD, 0);
                    if (comparacion.ResultCode == Constants.ResultCode.DP_SUCCESS &&
                        comparacion.Score < UMBRAL_COMPARACION)
                    {
                        lock (lockMejor)
                        {
                            if (comparacion.Score < mejorPuntaje)
                            {
                                mejorPuntaje = comparacion.Score;
                                mejor = new Identificado
                                {
                                    IdEstudiante     = huellaCache.IdEstudiante,
                                    NombreEstudiante = huellaCache.NombreEstudiante,
                                    IdSede           = huellaCache.IdSede,
                                    Puntaje          = comparacion.Score
                                };
                            }
                        }
                    }
                }
                catch (Exception ex) { EmitirMensaje($"Error comparando huella: {ex.Message}"); }
            });

            if (mejor != null)
            {
                // Cargar datos completos del estudiante
                var repo = new Repositories.EstudianteRepository();
                var estudiante = repo.ObtenerPorId(mejor.IdEstudiante);

                OnEstudianteIdentificado?.Invoke(this, new ResultadoIdentificacion
                {
                    Identificado = true,
                    Estudiante = estudiante,
                    Puntaje = mejor.Puntaje,
                    Mensaje = $"Identificado: {mejor.NombreEstudiante}"
                });
            }
            else
            {
                string detalle = mejorPuntaje < float.MaxValue
                    ? $" (mejor score: {mejorPuntaje:F4}, umbral: {UMBRAL_COMPARACION})"
                    : $" (caché: {huellasAComparar.Count} huellas, sin coincidencias)";
                OnEstudianteIdentificado?.Invoke(this, new ResultadoIdentificacion
                {
                    Identificado = false,
                    Mensaje = "Estudiante no identificado." + detalle
                });
            }
        }

        // ═══════════════════════════════════════════════════
        // ENROLAMIENTO
        // ═══════════════════════════════════════════════════

        private static readonly string[] _instruccionesPosicion = {
            "Coloque el dedo al CENTRO del sensor",
            "Incline el dedo ligeramente hacia la DERECHA",
            "Incline el dedo ligeramente hacia la IZQUIERDA",
            "Coloque el dedo al CENTRO nuevamente (confirmación)"
        };

        private void ProcesarEnrolamiento(CaptureResult captureResult, Bitmap imagen)
        {
            OnImagenCapturada?.Invoke(this, imagen);

            // Extraer features mientras el Fid está fresco (el SDK puede invalidarlo después).
            var feature = ExtractFeatures(captureResult.Data);
            if (feature == null)
            {
                EmitirMensaje("No se pudo extraer features de la muestra. Intente de nuevo.");
                return;
            }
            _muestrasEnrolamiento.Add(feature.Data);
            int capturadas = _muestrasEnrolamiento.Count;
            EmitirMensaje($"✔ Muestra {capturadas}/{MAX_MUESTRAS_ENROLAMIENTO} capturada. Calidad: {(int)captureResult.Quality}%");

            OnHuellaCapturada?.Invoke(this, new ResultadoCaptura
            {
                Exitosa = true,
                Calidad = (int)captureResult.Quality,
                ImagenHuella = imagen,
                Mensaje = $"Muestra {capturadas} de {MAX_MUESTRAS_ENROLAMIENTO}"
            });

            // Indicar la posición para la siguiente muestra
            if (capturadas < MAX_MUESTRAS_ENROLAMIENTO)
                EmitirMensaje(_instruccionesPosicion[capturadas]); // índice = próxima posición

            if (capturadas >= MAX_MUESTRAS_ENROLAMIENTO)
            {
                // Crear template final consolidando todas las muestras
                var template = GenerarTemplateEnrolamiento();
                if (template != null)
                {
                    OnHuellaCapturada?.Invoke(this, new ResultadoCaptura
                    {
                        Exitosa = true,
                        Template = template,
                        Calidad = (int)captureResult.Quality,
                        ImagenHuella = imagen,
                        Mensaje = "Enrolamiento completado exitosamente."
                    });
                    _capturando = false; // Detener captura después del enrolamiento
                    CambiarEstado(EstadoLector.Listo);
                }
                else
                {
                    EmitirMensaje("Error generando template. Intente nuevamente.");
                    _muestrasEnrolamiento.Clear();
                }
            }
        }

        private byte[] GenerarTemplateEnrolamiento()
        {
            try
            {
                if (_muestrasEnrolamiento.Count < 2)
                {
                    EmitirMensaje("Muy pocas muestras válidas. Repita el proceso con el dedo bien apoyado.");
                    return null;
                }

                EmitirMensaje($"Generando template con {_muestrasEnrolamiento.Count} muestras...");

                var enrollResult = DPUruNet.Enrollment.CreateEnrollmentFmd(
                    Constants.Formats.Fmd.ANSI, _muestrasEnrolamiento);

                if (enrollResult.ResultCode == Constants.ResultCode.DP_SUCCESS)
                    return enrollResult.Data.Bytes;

                EmitirMensaje($"Error al generar template ({enrollResult.ResultCode}). Repita el proceso.");
                return null;
            }
            catch (Exception ex)
            {
                EmitirMensaje($"Error generando template: {ex.Message}");
                return null;
            }
        }

        // ═══════════════════════════════════════════════════
        // PRUEBA DE LECTOR
        // ═══════════════════════════════════════════════════

        private void ProcesarPrueba(CaptureResult captureResult, Bitmap imagen)
        {
            OnImagenCapturada?.Invoke(this, imagen);
            OnHuellaCapturada?.Invoke(this, new ResultadoCaptura
            {
                Exitosa = true,
                Calidad = (int)captureResult.Quality,
                ImagenHuella = imagen,
                Mensaje = $"Captura OK. Calidad: {(int)captureResult.Quality}%"
            });
        }

        // ═══════════════════════════════════════════════════
        // UTILIDADES INTERNAS
        // ═══════════════════════════════════════════════════

        private DataResult<Fmd> ExtractFeatures(Fid captureData)
        {
            try
            {
                var result = FeatureExtraction.CreateFmdFromFid(
                    captureData, Constants.Formats.Fmd.ANSI);
                if (result.ResultCode == Constants.ResultCode.DP_SUCCESS)
                    return result;
            }
            catch { }
            return null;
        }

        private Bitmap ObtenerImagenHuella(Fid captureData)
        {
            try
            {
                if (captureData?.Views == null || captureData.Views.Count == 0)
                    return null;
                var view = captureData.Views[0];
                var bmp = new Bitmap(view.Width, view.Height,
                    System.Drawing.Imaging.PixelFormat.Format8bppIndexed);
                // Configurar paleta de grises
                var palette = bmp.Palette;
                for (int i = 0; i < 256; i++)
                    palette.Entries[i] = Color.FromArgb(i, i, i);
                bmp.Palette = palette;
                var data = bmp.LockBits(
                    new Rectangle(0, 0, view.Width, view.Height),
                    System.Drawing.Imaging.ImageLockMode.WriteOnly,
                    System.Drawing.Imaging.PixelFormat.Format8bppIndexed);
                System.Runtime.InteropServices.Marshal.Copy(view.RawImage, 0, data.Scan0, view.RawImage.Length);
                bmp.UnlockBits(data);
                return bmp;
            }
            catch { return null; }
        }

        private void ReiniciarCaptura()
        {
            if (_capturando && _lector != null && Estado != EstadoLector.Error)
                IniciarCapturaInterna();
        }

        public void DetenerCaptura()
        {
            _capturando = false;
            try { _lector?.CancelCapture(); } catch { }

            // Esperar a que el hilo de captura termine antes de liberar el device
            if (_hiloCaptura != null && _hiloCaptura.IsAlive)
                _hiloCaptura.Join(3000); // máximo 3 segundos de espera

            CambiarEstado(EstadoLector.Listo);
        }

        private void CambiarEstado(EstadoLector nuevoEstado)
        {
            Estado = nuevoEstado;
            OnCambioEstado?.Invoke(this, nuevoEstado);
        }

        private void EmitirMensaje(string mensaje)
        {
            UltimoMensaje = mensaje;
            OnMensaje?.Invoke(this, mensaje);
        }

        // ═══════════════════════════════════════════════════
        // DISPOSE
        // ═══════════════════════════════════════════════════

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _capturando = false;

                _timerReconexion?.Dispose();
                _timerReconexion = null;
                try { _watcherUsb?.Stop(); _watcherUsb?.Dispose(); _watcherUsb = null; } catch { }
                try { _watcherUsbConexion?.Stop(); _watcherUsbConexion?.Dispose(); _watcherUsbConexion = null; } catch { }

                // Cancelar captura pendiente y esperar que el hilo termine
                try { _lector?.CancelCapture(); } catch { }
                if (_hiloCaptura != null && _hiloCaptura.IsAlive)
                    _hiloCaptura.Join(3000);

                // Cerrar el reader antes de disponer (libera el USB para otras instancias)
                if (_lector != null)
                {
                    try { _lector.Dispose(); } catch { }
                    _lector = null;
                }
                if (_lectoresCol != null)
                {
                    try { _lectoresCol.Dispose(); } catch { }
                    _lectoresCol = null;
                }
            }
        }

        // Clase auxiliar interna
        private class Identificado
        {
            public int IdEstudiante { get; set; }
            public string NombreEstudiante { get; set; }
            public int IdSede { get; set; }
            public float Puntaje { get; set; }
        }
    }
}
