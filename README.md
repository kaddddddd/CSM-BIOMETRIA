# CSM Biométrico WPF

Sistema de control de asistencia biométrica para instituciones educativas. Identifica estudiantes mediante huella dactilar y registra automáticamente su estado de ingreso según el horario configurado.

---

## Índice

1. [Stack tecnológico](#stack-tecnológico)
2. [Arquitectura general](#arquitectura-general)
3. [Modos de operación](#modos-de-operación)
4. [Flujo de inicio (App.xaml.cs)](#flujo-de-inicio)
5. [Base de datos](#base-de-datos)
6. [Capa de modelos](#capa-de-modelos)
7. [Capa de repositorios](#capa-de-repositorios)
8. [Capa de servicios](#capa-de-servicios)
9. [Servicio biométrico](#servicio-biométrico)
10. [Vistas y ventanas](#vistas-y-ventanas)
11. [Páginas de administración](#páginas-de-administración)
12. [Diálogos](#diálogos)
13. [Sistema de horarios](#sistema-de-horarios)
14. [Sistema de asistencia](#sistema-de-asistencia)
15. [Roles y permisos](#roles-y-permisos)
16. [Seguridad](#seguridad)
17. [Modo offline](#modo-offline)
18. [Requisitos e instalación](#requisitos-e-instalación)

---

## Stack tecnológico

| Elemento | Detalle |
|---|---|
| Framework | .NET 8.0 WPF (`net8.0-windows`) |
| Lenguaje | C# 12 |
| Plataforma objetivo | **x86** (obligatorio por el SDK biométrico nativo) |
| Base de datos | SQLite — archivo `csm_biometrico.db` local junto al ejecutable |
| Lector biométrico | DigitalPersona U.are.U 5300 (USB) |
| SDK biométrico | `DPUruNet.dll` / `DPCtlUruNet.dll` (incluidas en `/libs`) |
| Exportación Excel | ClosedXML 0.104.2 |
| Síntesis de voz | System.Speech 8.0.0 |
| ORM / acceso BD | Microsoft.Data.Sqlite 8.0.0 (sin ORM, queries directas parametrizadas) |

---

## Arquitectura general

El proyecto sigue una **arquitectura en capas** sin dependencias inversas entre ellas:

```
┌─────────────────────────────────────────┐
│              Views (XAML + CS)          │  ← Presentación
│  Windows / Pages / Dialogs             │
└────────────────┬────────────────────────┘
                 │ usa
┌────────────────▼────────────────────────┐
│             Services                    │  ← Lógica de negocio
│  AuthService · AsistenciaService        │
│  OfflineService · CacheHuellas          │
└────────────────┬────────────────────────┘
                 │ usa
┌────────────────▼────────────────────────┐
│            Repositories                 │  ← Acceso a datos
│  15+ repositorios con queries SQLite    │
└────────────────┬────────────────────────┘
                 │ usa
┌────────────────▼────────────────────────┐
│          Data / Models                  │  ← Infraestructura
│  ConexionDB · DatabaseInitializer       │
│  Entidades (POCOs)                      │
└─────────────────────────────────────────┘

┌─────────────────────────────────────────┐
│           Biometria (transversal)       │
│  ServicioBiometrico (Singleton)         │
│  Interactúa con Views y Services        │
└─────────────────────────────────────────┘
```

### Estructura de directorios

```
CSMBiometricoWPF/
├── App.xaml / App.xaml.cs          — Punto de entrada, selección de modo
├── Models/
│   └── Entidades.cs                — Todos los POCOs del dominio
├── Data/
│   ├── ConexionDB.cs               — Gestor de conexión SQLite
│   └── DatabaseInitializer.cs      — Creación de esquema y migraciones
├── Repositories/
│   └── Repositorios.cs             — Todos los repositorios (único archivo)
├── Services/
│   └── Servicios.cs                — AuthService, AsistenciaService, OfflineService, CacheHuellas
├── Biometria/
│   └── ServicioBiometrico.cs       — Wrapper del SDK DigitalPersona (Singleton)
├── Views/
│   ├── LoginWindow.xaml(.cs)
│   ├── MainWindow.xaml(.cs)
│   ├── KioskWindow.xaml(.cs)
│   ├── PanelEntradaWindow.xaml(.cs)
│   ├── Pages/                      — 14 páginas navegables
│   └── Dialogs/                    — 10 diálogos modales
├── libs/                           — DLLs nativas DigitalPersona
└── Images/                         — Recursos gráficos (logo, fondo)
```

---

## Modos de operación

El mismo ejecutable soporta tres modos distintos según los argumentos de inicio:

```
CSMBiometricoWPF.exe              → Modo Admin   (login + panel completo)
CSMBiometricoWPF.exe --kiosk      → Modo Kiosk   (identificación sin login)
CSMBiometricoWPF.exe --panel      → Modo Panel   (visualización de ingresos)
```

Cada modo abre una ventana diferente y activa un subconjunto distinto de funcionalidades.

---

## Flujo de inicio

`App.xaml.cs` controla el arranque completo:

1. **Cultura**: fija `es-CO` para formato de fechas y números.
2. **Inicialización de BD**: llama a `DatabaseInitializer.InicializarSiNecesario()`, que crea las tablas, vistas e índices si la base no existe, y ejecuta las migraciones pendientes.
3. **Verificación de conexión**: si SQLite no responde, el usuario puede continuar en modo offline.
4. **Selección de ventana** según el argumento de línea de comandos:
   - Sin argumento → `LoginWindow`
   - `--kiosk` → `KioskWindow`
   - `--panel` → `PanelEntradaWindow`

---

## Base de datos

La base de datos es un único archivo SQLite (`csm_biometrico.db`) ubicado junto al ejecutable. No requiere instalación de servidor.

### Tablas principales

```
roles                    — Roles del sistema (SUPERADMIN, ADMINISTRADOR, OPERADOR)
usuarios                 — Cuentas de acceso con hash SHA-256
instituciones            — Entidades educativas
sedes                    — Sedes físicas de cada institución
grados                   — Grados académicos (1°, 2°, ... 11°)
grupos                   — Grupos dentro de un grado (A, B, C...)
periodos_academicos      — Períodos definidos por institución (inicio/fin por mes y día)

estudiantes              — Datos personales + id_sede + id_grado + id_grupo
huellas_digitales        — Templates biométricos binarios (1..10 por estudiante, por TipoDedo)

horarios                 — Horario base por sede/grado/grupo/día de semana
franjas_horario          — Jornadas adicionales dentro de un horario (ej: entrada AM + entrada PM)
horario_excepciones      — Excepciones por fecha (reemplaza el horario base ese día)
franjas_excepcion        — Franjas dentro de una excepción

registros_ingreso        — Cada ingreso registrado con estado, hora, franja y observaciones
logs_sistema             — Auditoría de todas las acciones del sistema
```

### Vistas SQL

| Vista | Propósito |
|---|---|
| `v_registros_ingreso_detalle` | Une registros con datos de estudiante, sede, grado y grupo |
| `v_estadisticas_hoy` | Totales del día (presentes, tardanzas, ausentes) por sede |

### Inicialización y migraciones

`DatabaseInitializer` crea el esquema si no existe y aplica **migraciones incrementales** (ADD COLUMN con IF NOT EXISTS). Esto permite actualizar instalaciones existentes sin perder datos. Al finalizar, inserta el usuario `SUPERADMIN` por defecto si la tabla de usuarios está vacía.

> **Credenciales por defecto:** usuario `admin` / contraseña `Admin123!`

---

## Capa de modelos

`Models/Entidades.cs` contiene todos los POCOs del dominio. Los más importantes:

### Estudiante
```csharp
IdEstudiante, Identificacion, NombreCompleto, Foto (byte[])
IdSede, IdGrado, IdGrupo
NombreSede, NombreGrado, NombreGrupo  // desnormalizados para UI
```

### HuellaDigital
```csharp
IdHuella, IdEstudiante
TipoDedo  // enum: PULGAR_DERECHO, INDICE_DERECHO, ... (10 valores)
Template  // byte[] — plantilla biométrica del SDK
Activo
```

### Horario
```csharp
IdHorario, IdSede, IdGrado?, IdGrupo?
DiaSemana   // enum: LUNES, MARTES, MIERCOLES, JUEVES, VIERNES, SABADO, DOMINGO
HoraInicio, HoraLimiteTarde, HoraCierreIngreso  // TimeSpan
Activo
```

### RegistroIngreso
```csharp
IdRegistro, IdEstudiante, IdSede
FechaIngreso (DateTime), HoraIngreso (TimeSpan)
EstadoIngreso  // enum: A_TIEMPO, TARDE, FUERA_DE_HORARIO, YA_REGISTRADO
NombreFranja?  // nombre de la franja/excepción que aplicó
PuntajeBiometrico (float)
Observaciones?
Sincronizado (bool)  // para modo offline
```

### SesionActiva
Singleton estático que mantiene el estado de la sesión en curso:
```csharp
SesionActiva.UsuarioActual       // Usuario logueado
SesionActiva.InstitucionActual   // Institución seleccionada
SesionActiva.EsSuperAdmin        // true si rol == "SUPERADMIN"
```

---

## Capa de repositorios

Todos los repositorios están en `Repositories/Repositorios.cs`. Cada uno encapsula las operaciones CRUD de una entidad usando **queries SQLite parametrizadas** directas (sin ORM).

| Repositorio | Responsabilidad |
|---|---|
| `UsuarioRepository` | Autenticación, CRUD de usuarios, roles |
| `InstitucionRepository` | CRUD de instituciones |
| `SedeRepository` | CRUD de sedes, filtro por institución |
| `GradoRepository` | CRUD de grados |
| `GrupoRepository` | CRUD de grupos |
| `EstudianteRepository` | CRUD + búsqueda por nombre/documento |
| `HuellaRepository` | Guardar/obtener/desactivar templates biométricos |
| `HorarioRepository` | CRUD de horarios, consulta por día de semana, franjas por sede |
| `FranjaHorarioRepository` | CRUD de franjas adicionales dentro de un horario |
| `HorarioExcepcionRepository` | CRUD de excepciones + sus franjas, resolución por prioridad |
| `RegistroIngresoRepository` | Guardar, consultar, actualizar registros de asistencia |
| `LogRepository` | Insertar y consultar logs de auditoría |
| `PeriodoAcademicoRepository` | CRUD de períodos académicos |

### Patrón de consulta

Todos los métodos abren una conexión nueva por operación (no hay conexión persistente) y ejecutan PRAGMA `foreign_keys = ON` al abrirla:

```csharp
public Entidad Obtener(int id)
{
    using var conn = ConexionDB.ObtenerConexion();
    using var cmd  = new SqliteCommand("SELECT ... WHERE id=@id", conn);
    cmd.Parameters.AddWithValue("@id", id);
    using var dr = cmd.ExecuteReader();
    if (dr.Read()) return Mapear(dr);
    return null;
}
```

---

## Capa de servicios

### AuthService
Gestiona el login y los permisos por módulo.

**Login:**
1. Hashea la contraseña en SHA-256.
2. Consulta la tabla `usuarios` (estado=1, bloqueado=0).
3. Verifica que el usuario pertenezca a la institución seleccionada (excepto SUPERADMIN).
4. Carga `SesionActiva` con el usuario e institución.
5. Registra en log.

**TienePermiso(modulo):** tabla de permisos por rol definida en código:
- `SUPERADMIN` → acceso total.
- `ADMINISTRADOR` → gestión académica (horarios, estudiantes, reportes, etc.).
- `OPERADOR` → solo consulta, verificación y dashboard.

---

### AsistenciaService

Núcleo del registro de asistencia. El método principal es `RegistrarIngreso(estudiante, puntaje)`:

```
1. Obtener franjas vigentes para (sede, grado, grupo) del estudiante
   └─ ObtenerFranjasVigentes()
       ├─ ¿Hay excepción para hoy? → usa franjas de la excepción
       └─ No hay excepción → usa horario semanal normal
           ├─ ¿Tiene franjas configuradas? → usa cada franja
           └─ No tiene franjas → usa HoraInicio/LimiteTarde/Cierre del horario base

2. Determinar franja activa en este momento
   ├─ ¿Estamos dentro de alguna franja? → franjaActiva
   └─ ¿Llegó hasta 60 min antes de la próxima? → esLlegadaAnticipada = true

3. Verificar duplicado
   ├─ Con franja activa: YaRegistroEnFranja(inicio, cierre)
   └─ Sin franja o anticipado: YaRegistroHoy()

4. Calcular estado
   ├─ franjaActiva == null             → FUERA_DE_HORARIO
   ├─ esLlegadaAnticipada || antes de inicio → A_TIEMPO
   ├─ hora <= franja.LimiteTarde       → TARDE
   └─ hora > franja.LimiteTarde        → FUERA_DE_HORARIO

5. Guardar en BD (online) o en offline_records.json (offline)

6. Disparar evento IngresoRegistrado (el PanelEntradaWindow escucha este evento)
```

---

### OfflineService

Cuando `ConexionDB.EstaConectado` es `false`, los registros se serializan en JSON:

```
offline_records.json   (junto al ejecutable)
[
  { "IdEstudiante": 5, "EstadoIngreso": "A_TIEMPO", "Sincronizado": false, ... },
  ...
]
```

Al recuperar la conexión, `SincronizarConMySQL()` (nombre heredado) envía los registros pendientes a SQLite y marca `Sincronizado = true`.

---

### CacheHuellas

Mantiene en memoria las plantillas biométricas para que la identificación 1:N no haga consultas a disco en cada lectura.

- **Expiración**: 30 minutos.
- **Filtrado por institución**: cada institución tiene su propia entrada en el caché. El SUPERADMIN tiene un caché global separado.
- **Invalidación manual**: se llama a `Invalidar()` o `InvalidarInstitucion(id)` tras enrolar o modificar huellas.

```
ObtenerCache(idInstitucion?)
  ├─ Con idInstitucion → caché por institución (carga solo sus huellas)
  └─ Sin idInstitucion → caché global (todas las huellas activas)
```

---

## Servicio biométrico

`Biometria/ServicioBiometrico.cs` es un **Singleton** (`ServicioBiometrico.Compartido`) que gestiona todo el ciclo de vida del lector.

### Inicialización
1. Abre el primer dispositivo USB disponible vía `DPUruNet`.
2. Configura la calidad mínima de captura.
3. Registra **WMI watchers** para detectar conexión/desconexión del dispositivo en caliente.
4. Si el dispositivo se desconecta, intenta reinicializar automáticamente.

### Ciclo de captura (identificación)

```
IniciarCaptura(idInstitucion?)
  └─ Hilo dedicado (no bloquea UI)
      └─ Bucle:
          1. Captura muestra del lector
          2. Si calidad OK → busca en CacheHuellas con Parallel.ForEach
          3. Si puntaje ≥ umbral → dispara OnEstudianteIdentificado(estudiante, puntaje)
          4. Si no coincide → dispara OnIdentificacionFallida
          5. Siempre dispara OnImagenCapturada(bitmap) para mostrar la imagen en UI
```

### Ciclo de enrolamiento

```
IniciarEnrolamiento(estudiante, tipoDedo)
  └─ 4 capturas secuenciales (centro, derecha, izquierda, centro)
      └─ Cada captura exitosa dispara OnHuellaCapturada(muestra, paso)
      └─ Al completar 4 → GenerarTemplate(muestras) → OnTemplateGenerado(template)
```

### Eventos públicos

| Evento | Descripción |
|---|---|
| `OnEstudianteIdentificado` | Identificación exitosa: devuelve `Estudiante` y puntaje |
| `OnIdentificacionFallida` | No se encontró coincidencia |
| `OnHuellaCapturada` | Muestra capturada durante enrolamiento (con imagen) |
| `OnTemplateGenerado` | Template final listo para guardar |
| `OnImagenCapturada` | Imagen del dedo capturado (para mostrar en UI) |
| `OnCambioEstado` | Estado del lector cambió (listo, error, desconectado) |
| `OnMensaje` | Mensaje textual para mostrar al usuario |

> **Importante**: las vistas solo se **suscriben y desuscriben** a estos eventos. Nunca llaman a `Dispose()` sobre el servicio compartido.

---

## Vistas y ventanas

### LoginWindow

Pantalla inicial en modo Admin:

1. Carga el listado de instituciones activas en un `ComboBox`.
2. El usuario ingresa username y contraseña.
3. Llama a `AuthService.Login()`.
4. Si es SUPERADMIN, no requiere seleccionar institución.
5. En éxito: abre `MainWindow` y se cierra.
6. Incluye opción de restablecer contraseña (abre `RestablecerPasswordDialog`).

---

### MainWindow

Ventana principal en modo Admin. Contiene:

- **Menú de navegación lateral** con botones por módulo.
- **Frame central** donde se cargan las páginas (`Page`).
- Los botones del menú se muestran u ocultan según `AuthService.TienePermiso(modulo)`.
- Botón de logout que llama a `AuthService.Logout()` y vuelve a `LoginWindow`.

---

### KioskWindow

Ventana de operación desatendida (pantalla de entrada):

1. **Selector de sede**: si la institución tiene una sola sede, la selecciona automáticamente; si tiene varias, pide selección manual al inicio.
2. **Reloj en tiempo real** actualizado cada segundo.
3. **Indicador de franja activa**: muestra el nombre de la jornada en curso (o la próxima).
4. **Captura continua**: inicia `ServicioBiometrico.IniciarCaptura(idInstitucion)`.
5. Al identificar un estudiante:
   - Llama a `AsistenciaService.RegistrarIngreso()`.
   - Muestra panel de resultado con color según estado:
     - Verde → `A_TIEMPO`
     - Naranja → `TARDE`
     - Rojo → `FUERA_DE_HORARIO`
     - Gris → `YA_REGISTRADO`
   - Síntesis de voz (TTS) anuncia el nombre y estado.
6. **Salida**: `Ctrl+Alt+Q`.

---

### PanelEntradaWindow

Vista de supervisión en tiempo real (para operador o pantalla secundaria):

1. Selector de institución y sede.
2. Grid con los ingresos del día, ordenados por hora.
3. **Actualización automática** cada 30 segundos + actualización inmediata al suscribirse a `AsistenciaService.IngresoRegistrado`.
4. **Estadísticas del día**: presentes, tardanzas, ausentes.
5. Indicador online/offline.
6. `F5` para forzar recarga, `Escape` para cerrar.

---

## Páginas de administración

### Dashboard
- KPIs del período seleccionado: Total estudiantes, Presentes, Ausentes, Tardanzas, Parciales (falta una franja), Huellas enroladas.
- Filtros: institución, sede, grado, grupo, período (hoy / 1 semana / 15 días / 30 días / período académico / personalizado).
- Grid expandible con el detalle de faltas por estudiante.
- SUPERADMIN ve todas las instituciones; otros roles solo ven la suya.

### Estudiantes
- Listado con búsqueda por nombre o documento.
- Agregar / editar / desactivar estudiantes.
- Vista de huellas enroladas por estudiante.
- Filtrado por sede/grado/grupo.

### Enrolamiento
Proceso guiado de 4 pasos para registrar la huella de un estudiante:

```
Paso 1: Centro del sensor
Paso 2: Inclinado a la derecha
Paso 3: Inclinado a la izquierda
Paso 4: Centro (confirmación)
```

Cada paso muestra la imagen capturada. Al terminar el paso 4, el SDK genera el template consolidado y se guarda en `huellas_digitales`. El caché se invalida automáticamente.

### Verificación
Permite verificar manualmente la identidad de una persona: busca el estudiante, luego captura una huella y compara contra sus templates registrados.

### Consulta de Asistencia
- Búsqueda de estudiante por documento o nombre.
- Selector de período (mismo sistema que el Dashboard).
- Grid agrupado por fecha con todos los registros del estudiante.
- Contadores de asistencias, tardanzas y faltas.
- **Doble clic** sobre un registro abre `JustificarAsistenciaDialog` para corregir el estado o agregar observaciones.

### Horarios
Gestión de horarios semanales (ver [Sistema de horarios](#sistema-de-horarios)).

### Instituciones / Sedes / Grados / Grupos
CRUD estándar de las entidades maestras. Cada uno tiene su diálogo de edición.

### Usuarios
CRUD de usuarios con asignación de rol e institución. Incluye opción de restablecer contraseña.

### Periodos Académicos
Define períodos con nombre, mes/día de inicio y mes/día de fin, asociados a una institución. Se usan como filtro en Dashboard y Consulta de Asistencia.

### Reportes
Genera reportes de asistencia filtrados y los exporta a Excel (`.xlsx`) con ClosedXML.

### Logs
Tabla de auditoría con todos los eventos del sistema, filtrable por nivel (`INFO`, `ADVERTENCIA`, `ERROR`, `CRITICO`) e institución.

### Prueba de Lector
Página de diagnóstico: permite comprobar que el lector biométrico funciona correctamente capturando una huella de prueba y mostrando su imagen y calidad.

---

## Diálogos

| Diálogo | Uso |
|---|---|
| `EditarEstudianteDialog` | Crear / editar datos de un estudiante |
| `EditarInstitucionDialog` | Crear / editar institución |
| `EditarSedeDialog` | Crear / editar sede |
| `EditarGradoDialog` | Crear / editar grado |
| `EditarGrupoDialog` | Crear / editar grupo |
| `EditarUsuarioDialog` | Crear / editar usuario y asignar rol |
| `RestablecerPasswordDialog` | Cambiar contraseña de un usuario |
| `HorariosSedeDialog` | Ver/editar horarios desde un contexto de sede |
| `FranjasHorarioDialog` | Gestionar franjas adicionales de un horario |
| `ExcepcionesSedeDialog` | Gestionar excepciones de horario por fecha |
| `JustificarAsistenciaDialog` | Corregir estado de un registro de asistencia |
| `CustomMessageBox` | Reemplazo del `MessageBox` estándar con estilo personalizado |

---

## Sistema de horarios

El sistema permite definir horarios con **tres niveles de granularidad** y resuelve cuál aplica con el siguiente orden de prioridad (de mayor a menor):

```
1. Sede + Grado + Grupo específico  (ej: sede Norte, 5°, Grupo A)
2. Sede + Grado                     (ej: sede Norte, 5°, todos los grupos)
3. Sede general                     (ej: sede Norte, todos los grados)
```

Cada horario tiene **tres horas clave** por día:

| Campo | Significado |
|---|---|
| `HoraInicio` | Inicio de la ventana de ingreso |
| `HoraLimiteTarde` | Hasta esta hora el ingreso cuenta como TARDE |
| `HoraCierreIngreso` | Después de esta hora el ingreso es FUERA_DE_HORARIO |

### Franjas adicionales

Un horario puede tener múltiples **franjas** (ej: entrada de la mañana + entrada de la tarde). Cada franja tiene las mismas tres horas clave. Se configuran desde `FranjasHorarioDialog`.

Si un horario **no tiene franjas**, se usa el horario base directamente como una única ventana.

### Excepciones

Una **excepción** reemplaza completamente el horario de un día específico. Se define para una fecha concreta y puede tener sus propias franjas. La prioridad de resolución es:

```
1. Sede + Grado específico
2. Sede genérica
3. Grado en todas las sedes de la institución
4. Institución completa (todas las sedes y grados)
```

Si una excepción existe pero **no tiene franjas configuradas**, se ignora y se usa el horario normal.

---

## Sistema de asistencia

### Estados de ingreso

| Estado | Condición |
|---|---|
| `A_TIEMPO` | Dentro del horario, antes del límite de tardanza (o llegada anticipada ≤60 min antes) |
| `TARDE` | Después de `HoraInicio` pero antes de `HoraLimiteTarde` |
| `FUERA_DE_HORARIO` | Después de `HoraCierreIngreso` o sin franja activa para ese momento |
| `YA_REGISTRADO` | El estudiante ya tiene un registro en esa franja ese día |

### Detección de duplicados

- **Con franja activa**: verifica duplicados solo dentro de la ventana horaria de esa franja (`YaRegistroEnFranja`).
- **Sin franja / llegada anticipada**: verifica duplicados en todo el día (`YaRegistroHoy`). Esto evita que un estudiante que llega antes de la franja pueda registrarse de nuevo al entrar a la franja.

### Justificación de asistencia

Desde `ConsultaAsistenciaPage`, haciendo doble clic en un registro se puede:
- Cambiar el estado de `TARDE` o `FUERA_DE_HORARIO` a `A_TIEMPO`.
- Editar el nombre de la franja asociada.
- Agregar observaciones (máx. 200 caracteres).

---

## Roles y permisos

| Módulo | SUPERADMIN | ADMINISTRADOR | OPERADOR |
|---|---|---|---|
| Usuarios | ✓ | — | — |
| Instituciones | ✓ | ✓ | — |
| Sedes | ✓ | — | — |
| Grados / Grupos | ✓ | ✓ | — |
| Horarios | ✓ | ✓ | — |
| Estudiantes | ✓ | ✓ | — |
| Enrolamiento | ✓ | ✓ | — |
| Reportes | ✓ | ✓ | — |
| Períodos | ✓ | ✓ | — |
| Logs | ✓ | ✓ | — |
| Dashboard | ✓ | ✓ | ✓ |
| Consulta Asistencia | ✓ | ✓ | ✓ |
| Verificación | ✓ | ✓ | ✓ |
| Prueba Lector | ✓ | ✓ | ✓ |

---

## Seguridad

- **Contraseñas**: hash SHA-256 (sin salt — considerar mejora a bcrypt en futuras versiones).
- **SQL Injection**: todas las queries usan `Parameters.AddWithValue()`, ninguna concatenación de strings.
- **Bloqueo de cuenta**: tras 5 intentos fallidos de login, la cuenta se bloquea 30 segundos.
- **Sesión**: `SesionActiva` (singleton estático) se limpia completamente en logout.
- **Auditoría**: cada acción relevante (login, logout, CRUD, enrolamiento, registro de asistencia, errores) se registra en `logs_sistema` con nivel, usuario y timestamp.
- **Foreign keys**: `PRAGMA foreign_keys = ON` en cada conexión.

---

## Modo offline

Cuando SQLite no está disponible (raro en un escenario de archivo local, pero posible en rutas de red):

1. `ConexionDB.EstaConectado` devuelve `false`.
2. `AsistenciaService.RegistrarIngreso` llama a `OfflineService.GuardarOffline()`.
3. Los registros se acumulan en `offline_records.json` junto al ejecutable.
4. Al restaurar la conexión, `OfflineService.SincronizarConMySQL()` envía los registros pendientes y marca `Sincronizado = true`.

---

## Requisitos e instalación

### Requisitos del sistema

- **SO**: Windows 10 / 11 (x86 o x64 con WOW64 habilitado)
- **Runtime**: .NET 8.0 Desktop Runtime
- **Hardware**: Lector DigitalPersona U.are.U 5300 conectado por USB
- **Driver**: DigitalPersona OneTouch for Windows (debe instalarse antes de ejecutar)
- **Espacio en disco**: ~50 MB para la aplicación + crecimiento del archivo `.db`

### Primeros pasos

1. Instalar el driver de DigitalPersona.
2. Conectar el lector biométrico.
3. Ejecutar `CSMBiometricoWPF.exe` — la base de datos se crea automáticamente.
4. Iniciar sesión con `admin` / `Admin123!`.
5. Cambiar la contraseña del SUPERADMIN desde **Usuarios**.
6. Crear la institución → sede → horarios → grados y grupos → estudiantes → enrolar huellas.

### Compilación

El proyecto **debe compilarse en x86** por limitación del SDK nativo de DigitalPersona. En Visual Studio:

```
Plataforma: x86
Configuración: Debug o Release
```

Las DLLs nativas (`DPXUru.dll`, `DPCtlXUru.dll`) se copian automáticamente al directorio de salida en cada build.
