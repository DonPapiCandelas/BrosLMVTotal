// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// EsquemaSql.cs -- DDL de la base propia de BrosLMV.Descargas.
//
// Deliberadamente SIN CfdiConcepto de nomina/complementos especiales (decision explicita:
// "los de nomina no necesitamos darle tanto problema con eso") -- el XML original siempre
// queda en RutaArchivoXml como fuente de verdad completa; solo se parsea a columnas lo que
// realmente se usa para reportes de conciliacion.
//
// Vive en una BD propia (no dentro de la BD de Comercial) para poder evolucionar el esquema
// libremente sin tocar tablas nativas -- BrosLMV/Comercial solo la CONSULTA, nunca la escribe.

using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas.Datos
{
    internal static class EsquemaSql
    {
        // SQL Server no deja conectar directo a una base que no existe (ni con Windows Auth) --
        // hay que conectarse primero a "master" y crearla, ANTES de abrir la conexion real que
        // usa el resto de la app. Solo pasa la primera vez que se usa esta cadena de conexion.
        // Antes vivia solo en descargas-ui/MainWindow.xaml.cs (unico lugar que la llamaba) -- se
        // movio aqui 2026-08-19 para que el CLI (usado por las Tareas Programadas y el
        // instalador via --inicializar) tambien pueda crear la BD si todavia no existe, en vez
        // de asumir que el usuario ya abrio la UI una vez primero.
        public static void AsegurarBaseDeDatos(string cadenaConexion)
        {
            var builder = new SqlConnectionStringBuilder(cadenaConexion);
            string nombreBase = builder.InitialCatalog;
            builder.InitialCatalog = "master";

            using (var cn = new SqlConnection(builder.ConnectionString))
            {
                cn.Open();
                using (var cmd = cn.CreateCommand())
                {
                    // OJO: EXEC('...' + QUOTENAME(@x)) no parsea en T-SQL (limitacion real del
                    // parser -- confirmado con sqlcmd, "Incorrect syntax near 'QUOTENAME'").
                    // Hay que precalcular QUOTENAME en una variable aparte antes de concatenar.
                    cmd.CommandText = @"
DECLARE @quotado sysname = QUOTENAME(@nombre);
IF DB_ID(@nombre) IS NULL EXEC('CREATE DATABASE ' + @quotado);";
                    cmd.Parameters.AddWithValue("@nombre", nombreBase);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // Cada CREATE va envuelto en "IF NOT EXISTS" -- correr esto en cada arranque del
        // programa es seguro e idempotente, no hace falta un sistema de migraciones aparte
        // todavia (el esquema es chico).
        private const string Ddl = @"
-- Catalogo PROPIO del descargador: no depende de como esten organizadas las bases de
-- Comercial Pro (una base de Comercial puede agrupar varias razones sociales/RFC). Cada fila
-- es una razon social con su propia FIEL. PasswordCifrada usa DPAPI (DpapiHelper.cs), atado a
-- ESTA maquina -- igual mecanismo que ya usa BrosLMV para la cadena de conexion SQL (Rutas.cs).
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Empresa')
CREATE TABLE Empresa (
    EmpresaID INT IDENTITY PRIMARY KEY,
    Nombre NVARCHAR(200) NOT NULL,
    RFC NVARCHAR(13) NOT NULL UNIQUE,
    RutaCer NVARCHAR(400) NOT NULL,
    RutaKey NVARCHAR(400) NOT NULL,
    PasswordCifrada VARBINARY(MAX) NOT NULL,
    Activa BIT NOT NULL DEFAULT 1,
    FechaAlta DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
-- Donde y como se guardan los XML de ESTA empresa -- configurable por el usuario (pedido
-- explicito 2026-08-14: unos quieren organizado por mes, otros todo en una sola carpeta plana
-- para pasarla directo a Comercial, y algunos quieren el nombre de archivo con Folio/RFC/monto,
-- no solo el UUID). CarpetaXml NULL = usa la carpeta relativa xml de siempre (compatibilidad
-- con empresas ya creadas antes de esta columna).
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Empresa') AND name='CarpetaXml')
    ALTER TABLE Empresa ADD CarpetaXml NVARCHAR(400) NULL;
-- Plana / Anio / AnioMes / AnioTipoMes -- ver OrganizadorArchivos.cs.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Empresa') AND name='EstructuraCarpetas')
    ALTER TABLE Empresa ADD EstructuraCarpetas NVARCHAR(20) NOT NULL DEFAULT 'AnioTipoMes';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Empresa') AND name='PlantillaNombreArchivo')
    ALTER TABLE Empresa ADD PlantillaNombreArchivo NVARCHAR(200) NOT NULL DEFAULT '{UUID}';
-- Carpetas nativas de CONTPAQi Comercial Pro (Opciones -> CFDI -> Comprobante Fiscal Digital,
-- Ruta XML Recibidos / Ruta XML Emitidos) -- pedido explicito del usuario 2026-08-18: ademas de
-- su propia copia organizada (CarpetaXml de arriba), quiere que BrosLMV.Descargas deje una copia
-- plana aqui para que Comercial la recoja con su boton nativo Importar. NULL = no copiar (paso
-- opcional, la empresa puede no tener Comercial Pro instalado). Investigado y confirmado en
-- material interno antes de agregar estas columnas.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Empresa') AND name='ComercialCarpetaXmlRecibidos')
    ALTER TABLE Empresa ADD ComercialCarpetaXmlRecibidos NVARCHAR(400) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Empresa') AND name='ComercialCarpetaXmlEmitidos')
    ALTER TABLE Empresa ADD ComercialCarpetaXmlEmitidos NVARCHAR(400) NULL;
-- Cadena de conexion a la BASE DE DATOS de Comercial Pro (ej. EmpresaA) -- distinta de la
-- conexion a la BD propia de BrosLMV.Descargas. NULL = no automatizar el Importar, solo copiar
-- el archivo (comportamiento anterior). Si tiene valor, ComercialImportador.cs escribe directo
-- en docDocumentCFDiSAT/docDocumentItemCFDiSAT -- pedido explicito del usuario 2026-08-18:
-- configurar que se carguen los emitidos, porque esos los tuve que cargar manualmente.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Empresa') AND name='ComercialConexionSql')
    ALTER TABLE Empresa ADD ComercialConexionSql NVARCHAR(500) NULL;
-- Año elegido por el usuario al dar de alta la empresa para la descarga historica inicial
-- (pedido explicito 2026-08-19: preguntar desde que año quiere la descarga inicial). NULL =
-- empresa creada antes de este campo, se mantiene el default viejo de 90 dias atras (ver
-- AutoSolicitador.SolicitarSiguienteTramoAsync).
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Empresa') AND name='AnioInicioDescargas')
    ALTER TABLE Empresa ADD AnioInicioDescargas INT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SolicitudDescarga')
CREATE TABLE SolicitudDescarga (
    SolicitudDescargaID INT IDENTITY PRIMARY KEY,
    EmpresaID INT NULL REFERENCES Empresa(EmpresaID),
    IdSolicitud NVARCHAR(50) NOT NULL,
    RfcSolicitante NVARCHAR(13) NOT NULL,
    Tipo NVARCHAR(10) NOT NULL,                 -- Recibidos / Emitidos
    FechaInicial DATETIME2 NOT NULL,
    FechaFinal DATETIME2 NOT NULL,
    Estatus NVARCHAR(20) NOT NULL DEFAULT 'Aceptada', -- Aceptada/EnProceso/Terminada/Error/Rechazada/Vencida
    NumeroCFDIs INT NULL,
    Origen NVARCHAR(20) NOT NULL DEFAULT 'Manual',    -- Manual / Automatica
    FechaSolicitud DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    FechaUltimaVerificacion DATETIME2 NULL
);
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('SolicitudDescarga') AND name='EmpresaID')
    ALTER TABLE SolicitudDescarga ADD EmpresaID INT NULL;
-- El SAT solo acepta UN TipoSolicitud por SolicitaDescarga (CFDI/Metadata/PDF/PDFCOCEMA/
-- TXTUUIDMASIVA, confirmado contra el XSD en vivo): pedir los dos juntos en la practica
-- significa 2 solicitudes reales por rango (decision explicita del usuario 2026-08-14: Metadata
-- da el numero exacto de lo que hay en el SAT y sirve de respaldo con datos basicos cuando la
-- descarga del XML falla). Default CFDI para que las filas ya existentes (todas de antes de
-- esta columna) queden correctas sin tener que backfillear nada.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('SolicitudDescarga') AND name='TipoSolicitud')
    ALTER TABLE SolicitudDescarga ADD TipoSolicitud NVARCHAR(10) NOT NULL DEFAULT 'CFDI';

-- CodEstatus/Mensaje que el SAT devolvio en el momento de SolicitaDescarga -- bug real encontrado
-- 2026-09-07: un rechazo INMEDIATO (CodEstatus=5002, solicitudes de por vida agotadas, 301,
-- etc) tronaba como excepcion ANTES de llegar a RegistrarSolicitud, asi que nunca quedaba
-- registro de ese intento -- el circuito de reintentos (AutoSolicitador) lo volvia a intentar
-- CADA corrida sin freno, gastando mas cupo real en cada reintento del mismo tramo ya rechazado.
-- Ahora todo intento (exitoso o no) deja fila, con estas columnas NULL si se acepto limpio.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('SolicitudDescarga') AND name='UltimoCodEstatus')
    ALTER TABLE SolicitudDescarga ADD UltimoCodEstatus NVARCHAR(10) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('SolicitudDescarga') AND name='UltimoMensaje')
    ALTER TABLE SolicitudDescarga ADD UltimoMensaje NVARCHAR(500) NULL;
-- Rescate (bug 2026-09-30): cuando el SAT contestaba la verificacion con EstadoSolicitud=0 se
-- guardaba Estatus='0', la solicitud salia de la lista de pendientes y nunca mas se verificaba.
-- Se regresan a EnProceso para que la siguiente pasada pregunte de nuevo al SAT; el valor 0 ya
-- no se vuelve a escribir, asi que esta sentencia solo tiene efecto sobre filas dañadas.
UPDATE SolicitudDescarga SET Estatus='EnProceso' WHERE Estatus IN ('0','');
-- La cola no es un expediente: Limpiar en la app solo oculta solicitudes ya resueltas
-- de la vista diaria. Nunca borra solicitudes, XML ni resultados del SAT y se puede revertir
-- directamente desde SQL si alguna vez hiciera falta una auditoría completa.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('SolicitudDescarga') AND name='Oculta')
    ALTER TABLE SolicitudDescarga ADD Oculta BIT NOT NULL DEFAULT 0;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SolicitudDescargaPaquete')
CREATE TABLE SolicitudDescargaPaquete (
    SolicitudDescargaPaqueteID INT IDENTITY PRIMARY KEY,
    SolicitudDescargaID INT NOT NULL REFERENCES SolicitudDescarga(SolicitudDescargaID),
    IdPaquete NVARCHAR(80) NOT NULL,
    VecesDescargado INT NOT NULL DEFAULT 0,
    FechaUltimaDescarga DATETIME2 NULL
);
-- Motivo real del ultimo intento fallido (CodEstatus/Mensaje del SAT, o el error de red/parseo).
-- NULL si el ultimo intento fue exitoso. Sin esto, la UI solo tenia el Estatus de la SOLICITUD
-- (Terminada = el SAT ya proceso la solicitud), que es una cosa DISTINTA a si la DESCARGA del
-- paquete funciono: un paquete puede agotar sus 2 descargas permitidas sin traer nunca los
-- datos, y la solicitud se sigue viendo Terminada aunque no haya nada que mostrar. Encontrado
-- en vivo: CodEstatus=5008, Maximo de descargas permitidas, en un paquete de 1332 CFDIs.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('SolicitudDescargaPaquete') AND name='UltimoError')
    ALTER TABLE SolicitudDescargaPaquete ADD UltimoError NVARCHAR(500) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CfdiRecibido')
CREATE TABLE CfdiRecibido (
    CfdiID INT IDENTITY PRIMARY KEY,
    UUID UNIQUEIDENTIFIER NOT NULL UNIQUE,
    RFCEmisor NVARCHAR(13) NOT NULL,
    NombreEmisor NVARCHAR(300) NULL,
    RFCReceptor NVARCHAR(13) NOT NULL,
    Serie NVARCHAR(25) NULL,
    Folio NVARCHAR(40) NULL,
    TipoComprobante CHAR(1) NULL,
    FormaPago NVARCHAR(10) NULL,
    MetodoPago NVARCHAR(10) NULL,
    UsoCFDI NVARCHAR(10) NULL,
    Subtotal DECIMAL(18,6) NULL,
    Descuento DECIMAL(18,6) NULL,
    IVA DECIMAL(18,6) NULL,
    Total DECIMAL(18,6) NULL,
    Moneda NVARCHAR(5) NULL,
    FechaEmision DATETIME2 NOT NULL,
    EstatusSat NVARCHAR(15) NOT NULL DEFAULT 'Vigente',   -- Vigente / Cancelado
    FechaUltimaVerificacionEstatus DATETIME2 NULL,
    RutaArchivoXml NVARCHAR(400) NULL,
    FechaDescarga DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
-- Detalle de la ultima consulta de estatus al SAT y bitacora de cada cambio (Vigente <-> Cancelado,
-- cancelacion en proceso...), para saber CUANDO se detecto una cancelacion y no solo el estado actual.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('CfdiRecibido') AND name='EstatusCancelacion')
    ALTER TABLE CfdiRecibido ADD EstatusCancelacion NVARCHAR(60) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('CfdiRecibido') AND name='FechaCambioEstatus')
    ALTER TABLE CfdiRecibido ADD FechaCambioEstatus DATETIME2 NULL;
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CfdiEstatusHistorial')
CREATE TABLE CfdiEstatusHistorial (
    CfdiEstatusHistorialID INT IDENTITY PRIMARY KEY,
    CfdiID INT NOT NULL,
    UUID UNIQUEIDENTIFIER NOT NULL,
    EstatusAnterior NVARCHAR(30) NULL,
    EstatusNuevo NVARCHAR(30) NOT NULL,
    EstatusCancelacion NVARCHAR(60) NULL,
    Fuente NVARCHAR(20) NOT NULL,
    Fecha DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
-- Cancelaciones propagadas a Comercial y revision de documentos vinculados; validacion EFOS del emisor.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('CfdiRecibido') AND name='FechaCancelacionComercial')
    ALTER TABLE CfdiRecibido ADD FechaCancelacionComercial DATETIME2 NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('CfdiRecibido') AND name='ValidacionEFOS')
    ALTER TABLE CfdiRecibido ADD ValidacionEFOS NVARCHAR(10) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CfdiCancelacionComercial')
CREATE TABLE CfdiCancelacionComercial (
    CfdiCancelacionComercialID INT IDENTITY PRIMARY KEY,
    CfdiID INT NOT NULL,
    UUID UNIQUEIDENTIFIER NOT NULL,
    DocumentID INT NOT NULL,
    Descripcion NVARCHAR(300) NULL,
    FechaDeteccion DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    Revisada BIT NOT NULL DEFAULT 0,
    FechaRevision DATETIME2 NULL
);
-- Estado del servicio (latido y ultimas pasadas), para la pantalla Salud. Solo local.
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ServicioEstado')
CREATE TABLE ServicioEstado (
    Clave NVARCHAR(50) NOT NULL PRIMARY KEY,
    Valor NVARCHAR(200) NULL,
    Fecha DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
-- Archivar saca el CFDI del historial que se muestra por defecto sin borrar la fila ni el XML
-- en disco (RutaArchivoXml sigue intacto): una forma de limpiar el historial sin perder el XML.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('CfdiRecibido') AND name='Archivado')
    ALTER TABLE CfdiRecibido ADD Archivado BIT NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('CfdiRecibido') AND name='TipoCambio')
    ALTER TABLE CfdiRecibido ADD TipoCambio DECIMAL(18,6) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('CfdiRecibido') AND name='Retenciones')
    ALTER TABLE CfdiRecibido ADD Retenciones DECIMAL(18,6) NULL;
-- NULL = todavia no se ha copiado a la carpeta de Comercial (XMLRecibidos/XMLEmitidos), con
-- fecha = ya se copio -- pedido explicito del usuario 2026-08-18: necesitamos tener ese
-- estatus, que diga sincronizado a Comercial, o sin sincronizar. Se marca desde SolicitudWorker
-- (copia automatica en cada descarga nueva) y desde ComercialSync (backfill manual).
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('CfdiRecibido') AND name='FechaSincronizadoComercial')
    ALTER TABLE CfdiRecibido ADD FechaSincronizadoComercial DATETIME2 NULL;

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CfdiConcepto')
CREATE TABLE CfdiConcepto (
    CfdiConceptoID INT IDENTITY PRIMARY KEY,
    CfdiID INT NOT NULL REFERENCES CfdiRecibido(CfdiID),
    ClaveProdServ NVARCHAR(10) NULL,
    Descripcion NVARCHAR(1000) NULL,
    Cantidad DECIMAL(18,6) NULL,
    ValorUnitario DECIMAL(18,6) NULL,
    Importe DECIMAL(18,6) NULL
);

-- Documentos que paga un CFDI de tipo Pago (REP) -- una fila por cada <DoctoRelacionado> del
-- complemento de Pagos. Para tipo Pago, esto es lo relevante de mostrar (que factura(s) paga,
-- cuanto, saldo anterior/insoluto), NO partidas de producto/servicio -- el Concepto de un REP
-- es siempre un renglon generico sin informacion util (ver CfdiConcepto).
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CfdiPagoDocto')
CREATE TABLE CfdiPagoDocto (
    CfdiPagoDoctoID INT IDENTITY PRIMARY KEY,
    CfdiID INT NOT NULL REFERENCES CfdiRecibido(CfdiID),
    UUIDRelacionado UNIQUEIDENTIFIER NOT NULL,
    Serie NVARCHAR(25) NULL,
    Folio NVARCHAR(40) NULL,
    FechaPago DATETIME2 NULL,
    FormaDePago NVARCHAR(10) NULL,
    Moneda NVARCHAR(5) NULL,
    NumParcialidad INT NULL,
    ImpSaldoAnt DECIMAL(18,6) NULL,
    ImpPagado DECIMAL(18,6) NULL,
    ImpSaldoInsoluto DECIMAL(18,6) NULL
);

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CfdiRelacion')
CREATE TABLE CfdiRelacion (
    CfdiRelacionID INT IDENTITY PRIMARY KEY,
    UUID UNIQUEIDENTIFIER NOT NULL,
    UUIDRelacionado UNIQUEIDENTIFIER NOT NULL,
    TipoRelacion NVARCHAR(5) NULL
);

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CfdiVinculo')
CREATE TABLE CfdiVinculo (
    CfdiVinculoID INT IDENTITY PRIMARY KEY,
    UUID UNIQUEIDENTIFIER NOT NULL,
    DocumentID INT NOT NULL,               -- referencia logica a docDocument de Comercial (otra BD, sin FK real entre motores)
    TipoVinculo NVARCHAR(20) NOT NULL,     -- Compra / Gasto / PagoProveedor
    UsuarioID INT NULL,
    FechaVinculo DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);

-- Metadata es el TXT delimitado por ~ que entrega el SAT. Se guarda separado del XML para
-- conservar cancelados y comprobantes de los que el SAT no entrega XML, sin contaminar el
-- historial de CFDI descargados.
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CfdiMetadata')
CREATE TABLE CfdiMetadata (
    CfdiMetadataID BIGINT IDENTITY PRIMARY KEY,
    UUID UNIQUEIDENTIFIER NOT NULL UNIQUE,
    RFCEmisor NVARCHAR(13) NULL,
    NombreEmisor NVARCHAR(300) NULL,
    RFCReceptor NVARCHAR(13) NULL,
    NombreReceptor NVARCHAR(300) NULL,
    RFCPac NVARCHAR(13) NULL,
    FechaEmision DATETIME2 NULL,
    FechaCertificacion DATETIME2 NULL,
    Total DECIMAL(18,6) NULL,
    EfectoComprobante NVARCHAR(5) NULL,
    EstatusSat NVARCHAR(30) NULL,
    FechaCancelacion DATETIME2 NULL,
    Tipo NVARCHAR(10) NOT NULL,
    SolicitudDescargaID INT NULL,
    LineaCruda NVARCHAR(MAX) NOT NULL,
    FechaDescarga DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
";

        public static void Asegurar(SqlConnection conn)
        {
            using (var cmd = new SqlCommand(Ddl, conn))
                cmd.ExecuteNonQuery();
        }
    }
}
