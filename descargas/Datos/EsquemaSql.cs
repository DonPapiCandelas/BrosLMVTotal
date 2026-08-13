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
        // Cada CREATE va envuelto en "IF NOT EXISTS" -- correr esto en cada arranque del
        // programa es seguro e idempotente, no hace falta un sistema de migraciones aparte
        // todavia (el esquema es chico).
        private const string Ddl = @"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SolicitudDescarga')
CREATE TABLE SolicitudDescarga (
    SolicitudDescargaID INT IDENTITY PRIMARY KEY,
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

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SolicitudDescargaPaquete')
CREATE TABLE SolicitudDescargaPaquete (
    SolicitudDescargaPaqueteID INT IDENTITY PRIMARY KEY,
    SolicitudDescargaID INT NOT NULL REFERENCES SolicitudDescarga(SolicitudDescargaID),
    IdPaquete NVARCHAR(80) NOT NULL,
    VecesDescargado INT NOT NULL DEFAULT 0,
    FechaUltimaDescarga DATETIME2 NULL
);

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
";

        public static void Asegurar(SqlConnection conn)
        {
            using (var cmd = new SqlCommand(Ddl, conn))
                cmd.ExecuteNonQuery();
        }
    }
}
