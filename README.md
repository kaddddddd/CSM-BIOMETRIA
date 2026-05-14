# CSM Biométrico WPF

Sistema de control de asistencia biométrica para instituciones educativas. Identifica estudiantes mediante huella dactilar y registra automáticamente su estado de ingreso según el horario configurado.

---

## Índice

1. [Stack tecnológico](#stack-tecnológico)
2. [Estructura de la solución](#estructura-de-la-solución)
3. [Arquitectura general](#arquitectura-general)
4. [Modos de operación](#modos-de-operación)
5. [Flujo de inicio](#flujo-de-inicio)
6. [Base de datos](#base-de-datos)
7. [Modo offline](#modo-offline)
8. [Capa de modelos](#capa-de-modelos)
9. [Capa de repositorios](#capa-de-repositorios)
10. [Capa de servicios](#capa-de-servicios)
11. [Servicio biométrico](#servicio-biométrico)
12. [Vistas y ventanas](#vistas-y-ventanas)
13. [Páginas de administración](#páginas-de-administración)
14. [Diálogos](#diálogos)
15. [Sistema de horarios](#sistema-de-horarios)
16. [Sistema de asistencia](#sistema-de-asistencia)
17. [Roles y permisos](#roles-y-permisos)
18. [Seguridad](#seguridad)
19. [Requisitos e instalación](#requisitos-e-instalación)

---

## Stack tecnológico

| Elemento | Detalle |
|---|---|
| Framework | .NET 8.0 WPF (`net8.0-windows`) |
| Lenguaje | C# 12 |
| Plataforma objetivo | **x86** (obligatorio por el SDK biométrico nativo) |
| BD principal | MySQL — servidor en red local |
| BD offline local | SQLite — archivo `csm_biometrico.db` junto al ejecutable |
| Lector biométrico | DigitalPersona U.are.U 5300 (USB) |
| SDK biométrico | `DPUruNet.dll` / `DPCtlUruNet.dll` (incluidas en `/libs`) |
| Exportación Excel | ClosedXML 0.104.2 |
| Síntesis de voz | System.Speech 8.0.0 |
| Acceso MySQL | MySqlConnector 2.3.7 (sin ORM, queries directas parametrizadas) |
| Acceso SQLite | Microsoft.Data.Sqlite 8.0.11 (sin ORM, queries directas parametrizadas) |

---

## Estructura de la solución

La solución contiene dos proyectos independientes:

```
CSM/
├── CSMBiometricoWPF/       — Aplicación principal (admin, enrolamiento, reportes)
│   └── CSMBiometricoWPF.sln
└── CSMPanelEntrada/        — Panel de entrada / kiosko (pantalla secundaria)
    └── CSMPanelEntrada.sln
```

Cada proyecto tiene su propio `.sln` y se abre por separado en Visual Studio.

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
│  SyncService                            │
└────────────────┬────────────────────────┘
                 │ usa
┌────────────────▼────────────────────────┐
│            Repositories                 │  ← Acceso a datos
│  Repositorios MySQL + métodos offline   │
└────────────────┬────────────────────────┘
                 │ usa
┌────────────────▼────────────────────────┐
│          Data / Models                  │  ← Infraestructura
│  ConexionDB (MySQL)                     │
│  ConexionSQLite (SQLite local)          │
│  DatabaseInitializer (esquema MySQL)    │
│  SQLiteInitializer (esquema SQLite)     │
│  Entidades (POCOs)                      │
└─────────────────────────────────────────┘

┌─────────────────────────────────────────┐
│           Biometria (transversal)       │
│  ServicioBiometrico (Singleton)         │
└─────────────────────────────────────────┘
```

### Estructura de directorios

```
CSMBiometricoWPF/
├── App.xaml / App.xaml.cs              — Punto de entrada, arranque online/offline
├── Models/
│   └── Entidades.cs                    — Todos los POCOs del dominio
├── Data/
│   ├── ConexionDB.cs                   — Conexión MySQL
│   ├── ConexionSQLite.cs               — Conexión SQLite local (offline)
│   ├── DatabaseInitializer.cs          — Crea/migra esquema MySQL
│   └── SQLiteInitializer.cs            — Crea tablas SQLite para cache offline
├── Repositories/
│   └── Repositorios.cs                 — Todos los repositorios (métodos MySQL + offline)
├── Services/
│   ├── Servicios.cs                    — AuthService, AsistenciaService, OfflineService, CacheHuellas
│   └── SyncService.cs                  — Sincronización MySQL ↔ SQLite
├── Biometria/
│   └── ServicioBiometrico.cs           — Wrapper del SDK DigitalPersona (Singleton)
├── Views/
│   ├── LoginWindow.xaml(.cs)
│   ├── MainWindow.xaml(.cs)
│   ├── Pages/                          — Páginas navegables (Dashboard, Estudiantes, etc.)
│   └── Dialogs/                        — Diálogos modales
├── libs/                               — DLLs nativas DigitalPersona
└── Images/                             — Recursos gráficos (logo, fondo)
```

---

## Modos de operación

### CSMBiometricoWPF (aplicación principal)

Siempre arranca con pantalla de login. Permite administrar instituciones, sedes, horarios, estudiantes, enrolamiento de huellas, reportes y consulta de asistencia.

### CSMPanelEntrada (panel kiosko)

Pantalla de identificación desatendida. Captura huellas en tiempo real, registra ingresos y muestra el resultado en pantalla con síntesis de voz. Se ejecuta como aplicación separada y puede funcionar sin conexión a MySQL (modo offline).

---

## Flujo de inicio

`App.xaml.cs` controla el arranque completo:

1. **Cultura**: fija `es-CO` para formato de fechas y números.
2. **Inicialización SQLite**: `SQLiteInitializer.InicializarSiNecesario()` crea las tablas locales de cache si no existen.
3. **Conexión MySQL**: intenta conectar. Si falla, el sistema arranca en **modo offline** con aviso al usuario.
4. **Sync inicial** (si hay MySQL): `SyncService.SincronizarTodo()` descarga datos MySQL → SQLite y envía registros pendientes → MySQL.
5. **Inicialización de esquema MySQL**: `DatabaseInitializer.InicializarSiNecesario()` aplica migraciones pendientes.
6. **Apertura de ventana**: `LoginWindow` (admin) o ventana de panel según el proyecto.

---

## Base de datos

### MySQL (principal)

Servidor centralizado accesible desde todas las PCs de la red. Contiene todos los datos del sistema.

**Tablas principales:**

```
roles                    — Roles del sistema (SUPERADMIN, ADMINISTRADOR, DOCENTE)
usuarios                 — Cuentas de acceso con hash SHA-256
instituciones            — Entidades educativas
sedes                    — Sedes físicas de cada institución
grados                   — Grados académicos (1°, 2°, ... 11°)
grupos                   — Grupos dentro de un grado (A, B, C...)
periodos_academicos      — Períodos definidos por institución

estudiantes              — Datos personales + id_sede + id_grado + id_grupo
huellas_digitales        — Templates biométricos binarios (hasta 10 por estudiante)

horarios                 — Horario base por sede/grado/grupo/día de semana
franjas_horario          — Jornadas dentro de un horario (AM, PM, Media Técnica, etc.)
horario_excepciones      — Excepciones por fecha (reemplaza el horario base ese día)
franjas_excepcion        — Franjas dentro de una excepción

registros_ingreso        — Cada ingreso con estado, hora, franja y observaciones
logs_sistema             — Auditoría de todas las acciones del sistema
```

**Vistas SQL:**

| Vista | Propósito |
|---|---|
| `v_registros_ingreso_detalle` | Une registros con datos de estudiante, sede, grado y grupo |
| `v_estadisticas_hoy` | Totales del día (presentes, tardanzas, ausentes) por sede |

### SQLite (cache offline)

Archivo local `csm_biometrico.db` junto al ejecutable. Se sincroniza desde MySQL al arrancar y se usa cuando MySQL no está disponible.

**Tablas SQLite:**

```
cache_huellas            — Templates biométricos para identificación offline
cache_estudiantes        — Datos básicos de estudiantes
cache_sedes              — Datos de sedes
cache_horarios           — Horarios y sus parámetros
cache_franjas            — Franjas de los horarios
cache_excepciones        — Excepciones de horario
cache_franjas_excepcion  — Franjas de las excepciones
registros_pendientes     — Ingresos registrados offline, pendientes de subir a MySQL
```

---

## Modo offline

Cuando MySQL no está disponible:

1. `ConexionDB.EstaConectado` devuelve `false`.
2. `CacheHuellas` carga los templates desde SQLite.
3. La identificación biométrica funciona con los datos de `cache_huellas` y `cache_estudiantes`.
4. `AsistenciaService.RegistrarIngreso` guarda en `registros_pendientes` (SQLite).
5. Los horarios y franjas se resuelven desde `cache_horarios`, `cache_franjas`, `cache_excepciones`.

Al recuperar la conexión MySQL, `SyncService.SincronizarTodo()` sube los `registros_pendientes` a MySQL y refresca el cache local.

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
DiaSemana   // LUNES, MARTES, MIERCOLES, JUEVES, VIERNES, SABADO
HoraInicio, HoraLimiteTarde, HoraCierreIngreso  // TimeSpan
Activo
```

### RegistroIngreso
```csharp
IdRegistro, IdEstudiante, IdSede
FechaIngreso (DateTime), HoraIngreso (TimeSpan)
EstadoIngreso  // enum: A_TIEMPO, TARDE, FUERA_DE_HORARIO, YA_REGISTRADO
NombreFranja?  // nombre de la franja que aplicó
PuntajeBiometrico (float)
Observaciones?
```

### SesionActiva
Singleton estático con el estado de la sesión en curso:
```csharp
SesionActiva.UsuarioActual       // Usuario logueado
SesionActiva.InstitucionActual   // Institución seleccionada
SesionActiva.EsSuperAdmin        // true si rol == "SUPERADMIN"
```

---

## Capa de repositorios

Todos los repositorios están en `Repositories/Repositorios.cs`. Usan **queries MySQL parametrizadas** directas. Cada método con potencial offline tiene una variante `*Offline` que consulta SQLite y se invoca automáticamente como fallback.

| Repositorio | Responsabilidad |
|---|---|
| `UsuarioRepository` | Autenticación, CRUD de usuarios |
| `InstitucionRepository` | CRUD de instituciones |
| `SedeRepository` | CRUD de sedes, filtro por institución |
| `GradoRepository` | CRUD de grados |
| `GrupoRepository` | CRUD de grupos |
| `EstudianteRepository` | CRUD + búsqueda por nombre/documento |
| `HuellaRepository` | Guardar/obtener/desactivar templates biométricos |
| `HorarioRepository` | CRUD de horarios, resolución de franjas por sede/grado/día |
| `FranjaHorarioRepository` | CRUD de franjas adicionales dentro de un horario |
| `HorarioExcepcionRepository` | CRUD de excepciones + sus franjas, resolución por prioridad |
| `RegistroIngresoRepository` | Guardar, consultar y actualizar registros de asistencia |
| `LogRepository` | Insertar y consultar logs de auditoría |
| `PeriodoAcademicoRepository` | CRUD de períodos académicos |

### Patrón de consulta

```csharp
public Entidad Obtener(int id)
{
    using var conn = ConexionDB.ObtenerConexion(); // MySQL
    using var cmd  = new MySqlCommand("SELECT ... WHERE id=@id", conn);
    cmd.Parameters.AddWithValue("@id", id);
    using var dr = cmd.ExecuteReader();
    if (dr.Read()) return Mapear(dr);
    return null;
}
```

### Fallback offline automático

Los métodos que lo requieren capturan excepciones de conexión y delegan a su versión SQLite:

```csharp
catch { return ObtenerPorFechaOffline(fecha, idSede, idInstitucion); }
```

---

## Capa de servicios

### AuthService
Gestiona el login y permisos por módulo.

1. Hashea la contraseña en SHA-256.
2. Consulta `usuarios` en MySQL (estado=1, bloqueado=0).
3. Verifica que el usuario pertenezca a la institución seleccionada (excepto SUPERADMIN).
4. Carga `SesionActiva`.
5. Registra en log.

**TienePermiso(modulo):** tabla de permisos por rol definida en código (ver [Roles y permisos](#roles-y-permisos)).

---

### AsistenciaService

Núcleo del registro de asistencia. Método principal: `RegistrarIngreso(estudiante, puntaje)`:

```
1. Obtener franjas vigentes para (sede, grado, grupo) del estudiante
   └─ ObtenerFranjasVigentes()
       ├─ ¿Hay excepción para hoy? → usa franjas de la excepción
       └─ No hay excepción → usa horario semanal normal

2. Determinar franja activa en este momento
   ├─ ¿Estamos dentro de alguna franja? → franjaActiva
   └─ ¿Llegó hasta 60 min antes de la próxima? → esLlegadaAnticipada

3. Verificar duplicado
   ├─ Con franja activa: YaRegistroEnFranja(inicio, cierre)
   └─ Sin franja o anticipado: YaRegistroHoy()

4. Calcular estado
   ├─ franjaActiva == null             → FUERA_DE_HORARIO
   ├─ esLlegadaAnticipada || antes de inicio → A_TIEMPO
   ├─ hora <= franja.LimiteTarde       → TARDE
   └─ hora > franja.LimiteTarde        → FUERA_DE_HORARIO

5. Guardar en MySQL (online) o en registros_pendientes SQLite (offline)

6. Disparar evento IngresoRegistrado
```

---

### OfflineService

Gestiona los registros pendientes en SQLite cuando MySQL no está disponible.

- `GuardarOffline(registro)` → inserta en `registros_pendientes`.
- `SincronizarPendientes(conn)` → sube los pendientes a MySQL y los marca como sincronizados.

---

### SyncService

Orquesta la sincronización bidireccional MySQL ↔ SQLite al arrancar:

1. Sube `registros_pendientes` (offline) → MySQL.
2. Descarga huellas, estudiantes, sedes, horarios, franjas y excepciones → SQLite.

---

### CacheHuellas

Mantiene en memoria los templates biométricos para identificación 1:N sin consultas repetidas a disco.

- **Expiración**: 30 minutos.
- **Filtrado por institución**: cada institución tiene su propia entrada en el cache.
- **Fuente**: MySQL online; SQLite si MySQL no está disponible.
- **Invalidación**: `Invalidar()` o `InvalidarInstitucion(id)` tras enrolar o modificar huellas.

---

## Servicio biométrico

`Biometria/ServicioBiometrico.cs` es un **Singleton** (`ServicioBiometrico.Compartido`) que gestiona el ciclo de vida del lector.

### Inicialización
1. Abre el primer dispositivo USB disponible vía `DPUruNet`.
2. Configura la calidad mínima de captura.
3. Registra **WMI watchers** para detectar conexión/desconexión en caliente.
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
          5. Siempre dispara OnImagenCapturada(bitmap)
```

### Ciclo de enrolamiento

```
IniciarEnrolamiento(estudiante, tipoDedo)
  └─ 4 capturas secuenciales (centro, derecha, izquierda, centro)
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
Pantalla inicial del modo admin:
1. Carga instituciones activas en un `ComboBox`.
2. El usuario ingresa username y contraseña.
3. Llama a `AuthService.Login()`.
4. En éxito: abre `MainWindow` y se cierra.

### MainWindow
Ventana principal en modo admin:
- Menú de navegación lateral con botones por módulo.
- Frame central donde se cargan las páginas.
- Los botones se muestran u ocultan según `AuthService.TienePermiso(modulo)`.
- Botón de logout que vuelve a `LoginWindow`.

### CSMPanelEntrada (proyecto separado)
Ventana de panel de entrada:
1. Selector de institución y sede.
2. Captura continua de huella.
3. Al identificar → llama a `AsistenciaService.RegistrarIngreso()`.
4. Muestra resultado con color según estado (verde / naranja / rojo / gris).
5. Síntesis de voz anuncia nombre y estado.
6. Grid con ingresos del día actualizado en tiempo real.
7. Funciona en modo offline si MySQL no está disponible.

---

## Páginas de administración

### Dashboard
- KPIs del período: Total estudiantes, Presentes, Ausentes, Tardanzas, Huellas enroladas.
- Filtros: institución, sede, grado, grupo, período (hoy / 1 semana / 15 días / 30 días / período académico / personalizado).
- Grid con detalle de faltas por estudiante.

### Estudiantes
- Listado con búsqueda por nombre completo o documento.
- Agregar / editar / desactivar estudiantes.
- Filtrado por sede/grado/grupo.

### Enrolamiento
Proceso guiado de 4 capturas para registrar la huella de un estudiante:
```
Paso 1: Centro del sensor
Paso 2: Inclinado a la derecha
Paso 3: Inclinado a la izquierda
Paso 4: Centro (confirmación)
```
Al terminar, el SDK genera el template consolidado y se guarda en `huellas_digitales`. El cache se invalida automáticamente.

### Verificación
Busca un estudiante, captura una huella y compara contra sus templates registrados.

### Consulta de Asistencia
- Búsqueda por documento o nombre (incluye búsqueda por nombre completo).
- Selector de período.
- Grid agrupado por fecha con todos los registros.
- Muestra ausencias calculadas automáticamente según el horario configurado.
- Contadores de asistencias, tardanzas y faltas.
- **Doble clic** sobre un registro abre `JustificarAsistenciaDialog`.

### Horarios
Gestión de horarios semanales por sede/grado/grupo con soporte de franjas múltiples y excepciones por fecha.

### Instituciones / Sedes / Grados / Grupos
CRUD estándar de las entidades maestras.

### Usuarios
CRUD de usuarios con asignación de rol e institución.

### Periodos Académicos
Define períodos con nombre y rango (mes/día de inicio y fin) por institución.

### Reportes
Exporta reportes de asistencia a Excel (`.xlsx`) con ClosedXML.

### Logs
Auditoría de todos los eventos del sistema, filtrable por nivel e institución.

### Prueba de Lector
Diagnóstico del lector biométrico: captura una huella de prueba y muestra imagen y calidad.

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
| `HorariosSedeDialog` | Ver/editar horarios de una sede |
| `FranjasHorarioDialog` | Gestionar franjas adicionales de un horario |
| `ExcepcionesSedeDialog` | Gestionar excepciones de horario por fecha |
| `JustificarAsistenciaDialog` | Corregir estado de un registro de asistencia |
| `CustomMessageBox` | Reemplazo del `MessageBox` estándar con estilo personalizado |

---

## Sistema de horarios

Define horarios con **tres niveles de granularidad**, resueltos por prioridad:

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

Un horario puede tener múltiples **franjas** (ej: entrada mañana + entrada tarde de Media Técnica). Cada franja tiene sus propias tres horas clave. La franja que aparece en el registro se resuelve buscando primero el horario del grado específico del estudiante, luego el genérico de la sede.

### Excepciones

Reemplazan completamente el horario de un día específico. Prioridad de resolución:
```
1. Sede + Grado específico
2. Sede genérica
3. Grado en todas las sedes de la institución
4. Institución completa
```

---

## Sistema de asistencia

### Estados de ingreso

| Estado | Condición |
|---|---|
| `A_TIEMPO` | Dentro del horario, antes del límite de tardanza (o llegada anticipada ≤60 min antes) |
| `TARDE` | Después de `HoraInicio` pero antes de `HoraLimiteTarde` |
| `FUERA_DE_HORARIO` | Después de `HoraCierreIngreso` o sin franja activa |
| `YA_REGISTRADO` | El estudiante ya tiene registro en esa franja ese día |

### Detección de duplicados

- **Con franja activa**: verifica solo dentro de la ventana de esa franja (`YaRegistroEnFranja`).
- **Sin franja / llegada anticipada**: verifica en todo el día (`YaRegistroHoy`).

### Justificación de asistencia

Desde `ConsultaAsistenciaPage`, doble clic en un registro permite:
- Cambiar el estado a `A_TIEMPO`, `TARDE`, etc.
- Editar el nombre de la franja asociada.
- Agregar observaciones (máx. 200 caracteres).

---

## Roles y permisos

| Módulo | SUPERADMIN | ADMINISTRADOR | DOCENTE |
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

- **Contraseñas**: hash SHA-256.
- **SQL Injection**: todas las queries usan `Parameters.AddWithValue()`, sin concatenación de strings.
- **Bloqueo de cuenta**: tras 5 intentos fallidos de login, la cuenta se bloquea 30 segundos.
- **Sesión**: `SesionActiva` se limpia completamente en logout.
- **Auditoría**: cada acción relevante se registra en `logs_sistema`.

---

## Requisitos e instalación

### Requisitos del sistema

- **SO**: Windows 10 / 11 (x86 o x64 con WOW64 habilitado)
- **Runtime**: .NET 8.0 Desktop Runtime
- **MySQL**: servidor MySQL 8.x accesible en la red (configurar en `App.config`)
- **Hardware**: Lector DigitalPersona U.are.U 5300 conectado por USB
- **Driver**: DigitalPersona OneTouch for Windows (debe instalarse antes de ejecutar)

### Configuración de conexión

Editar `App.config`:

```xml
<add name="MySqlConnection"
     connectionString="Server=IP_DEL_SERVIDOR;Port=3306;Database=csm_biometrico;
                       User ID=usuario;Password=contraseña;CharSet=utf8mb4;SslMode=None;"
     providerName="MySqlConnector" />
```

### Primeros pasos

1. Instalar el driver de DigitalPersona.
2. Configurar la cadena de conexión MySQL en `App.config`.
3. Ejecutar `CSMBiometricoWPF.exe` — el esquema MySQL y el cache SQLite se crean automáticamente.
4. Iniciar sesión con `admin` / `Admin123!`.
5. Cambiar la contraseña del SUPERADMIN desde **Usuarios**.
6. Crear institución → sede → horarios → grados y grupos → estudiantes → enrolar huellas.

### Compilación

El proyecto **debe compilarse en x86** por limitación del SDK nativo de DigitalPersona:

```
Plataforma: x86
Configuración: Debug o Release
```

Las DLLs nativas (`DPXUru.dll`, `DPCtlXUru.dll`) se copian automáticamente al directorio de salida en cada build.
