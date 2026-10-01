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

// BrosSatDb.cs -- acceso a datos de la BD propia de BrosLMV.Descargas. Todo parametrizado
// (SqlParameter), nunca concatenacion de strings hacia SQL.

using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas.Datos
{
    internal sealed class EmpresaFila
    {
        public int EmpresaID;
        public string Nombre;
        public string RFC;
        public string RutaCer;
        public string RutaKey;
        public byte[] PasswordCifrada;
        public bool Activa;
        public string CarpetaXml; // NULL = usa "xml" relativa (compatibilidad con empresas viejas)
        public string EstructuraCarpetas; // Plana / Anio / AnioMes / AnioTipoMes
        public string PlantillaNombreArchivo; // ej. "{UUID}", "{Fecha}_{RFCEmisor}_{Folio}_{UUID}"
        public string ComercialCarpetaXmlRecibidos; // NULL = no copiar a Comercial Pro
        public string ComercialCarpetaXmlEmitidos; // NULL = no copiar a Comercial Pro
        public string ComercialConexionSql; // NULL = no automatizar el Importar (solo copiar archivo)
        public int? AnioInicioDescargas; // Año elegido para la descarga historica inicial (NULL = default viejo de 90 dias)
    }

    internal sealed class SolicitudFila
    {
        public string IdSolicitud;
        public string Tipo;
        public string TipoSolicitud; // CFDI / Metadata
        public DateTime FechaInicial;
        public DateTime FechaFinal;
        public string Estatus;
        public int? NumeroCFDIs;
        public DateTime FechaSolicitud;
        // Motivo real de un paquete que agoto sus 2 descargas sin exito -- NULL si ninguno de los
        // paquetes de esta solicitud esta en ese estado. Estatus="Terminada" solo dice que el SAT
        // ya proceso la solicitud, NO que la descarga del contenido haya funcionado -- son cosas
        // distintas (ver bug #16 en DOCUMENTACION.md).
        public string UltimoErrorPaquete;
    }

    internal sealed class CfdiFila
    {
        public int CfdiID;
        public Guid UUID;
        public string RFCEmisor;
        public string NombreEmisor;
        public string RFCReceptor;
        public string TipoComprobante;
        public DateTime FechaEmision;
        public decimal? Subtotal;
        public decimal? Descuento;
        public decimal? IVA;
        public decimal? Retenciones;
        public decimal? Total;
        public string Moneda;
        public decimal? TipoCambio;
        public string FormaPago;
        public string MetodoPago;
        public string EstatusSat;
        public bool Archivado;
        public string RutaArchivoXml;
        public DateTime? FechaSincronizadoComercial;
    }

    // Una lectura diaria de la actividad real. No se infiere a partir de solicitudes: se cuenta
    // solamente un CFDI que ya quedo registrado en la base, para que el calendario sea una
    // evidencia clara de que el archivo si fue descargado.
    internal sealed class ActividadDescargaFila
    {
        public DateTime Fecha;
        public int Cantidad;
        public decimal Total;
    }

    internal sealed class CoberturaDescargaFila
    {
        public DateTime? UltimoXml;
        public DateTime? UltimoMetadata;
        public int TotalMetadata;
    }

    internal sealed class CfdiDetalleFila
    {
        public int CfdiID;
        public Guid UUID;
        public string RFCEmisor;
        public string NombreEmisor;
        public string RFCReceptor;
        public string Serie;
        public string Folio;
        public string TipoComprobante;
        public string FormaPago;
        public string MetodoPago;
        public string UsoCFDI;
        public decimal? Subtotal;
        public decimal? Descuento;
        public decimal? IVA;
        public decimal? Retenciones;
        public decimal? Total;
        public string Moneda;
        public decimal? TipoCambio;
        public DateTime FechaEmision;
        public string EstatusSat;
        public string RutaArchivoXml;
        public DateTime FechaDescarga;
        public bool Archivado;
    }

    internal sealed class ConceptoFila
    {
        public string ClaveProdServ;
        public string Descripcion;
        public decimal? Cantidad;
        public decimal? ValorUnitario;
        public decimal? Importe;
    }

    internal sealed class PagoDoctoFila
    {
        public Guid UUIDRelacionado;
        public string Serie;
        public string Folio;
        public DateTime? FechaPago;
        public string FormaDePago;
        public string Moneda;
        public int? NumParcialidad;
        public decimal? ImpSaldoAnt;
        public decimal? ImpPagado;
        public decimal? ImpSaldoInsoluto;
    }

    internal sealed class CfdiVinculoFila
    {
        public int DocumentID;
        public string TipoVinculo;
        public DateTime FechaVinculo;
    }

    internal sealed class SolicitudPendiente
    {
        public int SolicitudDescargaID;
        public string IdSolicitud;
        public string RfcSolicitante;
        public string Estatus;
        public string TipoSolicitud; // CFDI / Metadata -- el worker decide si parsea XML o solo guarda el TXT
        public string Tipo; // Recibidos / Emitidos -- para elegir la subcarpeta en OrganizadorArchivos
    }

    internal static class BrosSatDb
    {
        // Solicitudes que el worker todavia necesita atender: las que siguen en proceso en el
        // SAT (hay que preguntar de nuevo), o las que ya terminaron pero les falta descargar
        // algun paquete detectado (VecesDescargado = 0).
        //
        // rfcFiltro es OBLIGATORIO en la practica cuando hay mas de una empresa en la BD: la
        // firma XML-DSig de VerificaSolicitud/Descarga esta atada al certificado de UNA FIEL, y
        // el SAT rechaza la llamada si el RfcSolicitante de la solicitud no es el RFC de esa FIEL.
        // Sin filtro (rfcFiltro=null) se procesan TODAS las solicitudes pendientes de la BD, sin
        // importar de que empresa son -- solo tiene sentido cuando se sabe que hay una sola
        // empresa en el catalogo (uso historico del CLI de un solo comando).
        public static List<SolicitudPendiente> ObtenerSolicitudesPendientes(SqlConnection conn, string rfcFiltro = null)
        {
            string sql = @"
SELECT DISTINCT s.SolicitudDescargaID, s.IdSolicitud, s.RfcSolicitante, s.Estatus, s.TipoSolicitud, s.Tipo
FROM SolicitudDescarga s
LEFT JOIN SolicitudDescargaPaquete p ON p.SolicitudDescargaID = s.SolicitudDescargaID AND (p.VecesDescargado = 0 OR (p.VecesDescargado < 2 AND p.UltimoError IS NOT NULL))
WHERE (s.Estatus IN ('Aceptada', 'EnProceso') OR p.SolicitudDescargaPaqueteID IS NOT NULL)
  AND (@RfcFiltro IS NULL OR s.RfcSolicitante = @RfcFiltro);";

            var resultado = new List<SolicitudPendiente>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@RfcFiltro", (object)rfcFiltro ?? DBNull.Value);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        resultado.Add(new SolicitudPendiente
                        {
                            SolicitudDescargaID = reader.GetInt32(0),
                            IdSolicitud = reader.GetString(1),
                            RfcSolicitante = reader.GetString(2),
                            Estatus = reader.GetString(3),
                            TipoSolicitud = reader.GetString(4),
                            Tipo = reader.GetString(5)
                        });
                    }
                }
            }
            return resultado;
        }

        // Registra los paquetes que VerificaSolicitud acaba de reportar como listos, con
        // VecesDescargado=0 -- es decir "sabemos que existen, todavia no los bajamos". El
        // worker luego los toma de aqui para descargarlos. Idempotente: si ya se conocia el
        // paquete (por ejemplo porque ya se descargo antes), no lo pisa.
        public static void RegistrarPaquetesDetectados(SqlConnection conn, string idSolicitud, IEnumerable<string> idsPaquetes)
        {
            const string sql = @"
DECLARE @SolID INT = (SELECT SolicitudDescargaID FROM SolicitudDescarga WHERE IdSolicitud = @IdSolicitud);
IF @SolID IS NOT NULL AND NOT EXISTS (SELECT 1 FROM SolicitudDescargaPaquete WHERE SolicitudDescargaID = @SolID AND IdPaquete = @IdPaquete)
    INSERT INTO SolicitudDescargaPaquete (SolicitudDescargaID, IdPaquete, VecesDescargado)
    VALUES (@SolID, @IdPaquete, 0);";

            foreach (var idPaquete in idsPaquetes)
            {
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@IdSolicitud", idSolicitud);
                    cmd.Parameters.AddWithValue("@IdPaquete", idPaquete);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static List<string> ObtenerPaquetesSinDescargar(SqlConnection conn, string idSolicitud)
        {
            const string sql = @"
SELECT p.IdPaquete
FROM SolicitudDescargaPaquete p
JOIN SolicitudDescarga s ON s.SolicitudDescargaID = p.SolicitudDescargaID
WHERE s.IdSolicitud = @IdSolicitud AND (p.VecesDescargado = 0 OR (p.VecesDescargado < 2 AND p.UltimoError IS NOT NULL));";

            var resultado = new List<string>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@IdSolicitud", idSolicitud);
                using (var reader = cmd.ExecuteReader())
                    while (reader.Read()) resultado.Add(reader.GetString(0));
            }
            return resultado;
        }

        public static int RegistrarSolicitud(SqlConnection conn, string idSolicitud, string rfcSolicitante,
            string tipo, DateTime desde, DateTime hasta, string origen, string tipoSolicitud = "CFDI")
        {
            const string sql = @"
INSERT INTO SolicitudDescarga (IdSolicitud, RfcSolicitante, Tipo, FechaInicial, FechaFinal, Origen, TipoSolicitud)
OUTPUT INSERTED.SolicitudDescargaID
VALUES (@IdSolicitud, @RfcSolicitante, @Tipo, @FechaInicial, @FechaFinal, @Origen, @TipoSolicitud);";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@IdSolicitud", idSolicitud);
                cmd.Parameters.AddWithValue("@RfcSolicitante", rfcSolicitante);
                cmd.Parameters.AddWithValue("@Tipo", tipo);
                cmd.Parameters.AddWithValue("@FechaInicial", desde);
                cmd.Parameters.AddWithValue("@FechaFinal", hasta);
                cmd.Parameters.AddWithValue("@Origen", origen);
                cmd.Parameters.AddWithValue("@TipoSolicitud", tipoSolicitud);
                return (int)cmd.ExecuteScalar();
            }
        }

        // Registra un intento de SolicitaDescarga que el SAT rechazo DE INMEDIATO (CodEstatus !=
        // 5000, ej. 5002 "solicitudes de por vida agotadas", 301, etc) -- antes esto tronaba como
        // excepcion y nunca dejaba rastro en la BD, asi que AutoSolicitador lo volvia a intentar
        // en cada corrida sin freno. IdSolicitud es sintetico (el SAT nunca llego a asignar uno
        // real) para poder reusar la misma tabla sin duplicar columnas.
        public static void RegistrarIntentoFallido(SqlConnection conn, string rfcSolicitante, string tipo,
            DateTime desde, DateTime hasta, string origen, string tipoSolicitud, string codEstatus, string mensaje)
        {
            const string sql = @"
INSERT INTO SolicitudDescarga (IdSolicitud, RfcSolicitante, Tipo, FechaInicial, FechaFinal, Estatus, Origen, TipoSolicitud, UltimoCodEstatus, UltimoMensaje)
VALUES (@IdSolicitud, @RfcSolicitante, @Tipo, @FechaInicial, @FechaFinal, 'Rechazada', @Origen, @TipoSolicitud, @CodEstatus, @Mensaje);";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@IdSolicitud", "RECHAZO-" + Guid.NewGuid().ToString("N"));
                cmd.Parameters.AddWithValue("@RfcSolicitante", rfcSolicitante);
                cmd.Parameters.AddWithValue("@Tipo", tipo);
                cmd.Parameters.AddWithValue("@FechaInicial", desde);
                cmd.Parameters.AddWithValue("@FechaFinal", hasta);
                cmd.Parameters.AddWithValue("@Origen", origen);
                cmd.Parameters.AddWithValue("@TipoSolicitud", tipoSolicitud);
                cmd.Parameters.AddWithValue("@CodEstatus", (object)codEstatus ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Mensaje", (object)mensaje ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        // Cuenta cuantos de los intentos MAS RECIENTES (Manual o Automatica, cualquier rango) de
        // este RFC/direccion/tipo salieron Rechazada SEGUIDOS, empezando por el mas nuevo y
        // parando en el primero que no lo sea -- circuito de freno explicito pedido tras
        // encontrar 205 rechazos reales acumulados (2026-09-07): sin esto, un tramo que el SAT
        // rechaza de forma persistente (cupo agotado, etc) se sigue reintentando en CADA corrida
        // del servicio sin limite, gastando cupo real cada vez aunque el resultado nunca cambie.
        public static int ContarRechazosConsecutivos(SqlConnection conn, string rfc, string tipo, string tipoSolicitud)
        {
            const string sql = @"
SELECT TOP 20 Estatus FROM SolicitudDescarga
WHERE RfcSolicitante = @Rfc AND Tipo = @TipoRecEmi AND TipoSolicitud = @TipoSolicitud
ORDER BY FechaSolicitud DESC;";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                cmd.Parameters.AddWithValue("@TipoRecEmi", tipo);
                cmd.Parameters.AddWithValue("@TipoSolicitud", tipoSolicitud);
                using (var reader = cmd.ExecuteReader())
                {
                    int rechazosSeguidos = 0;
                    while (reader.Read())
                    {
                        if (reader.GetString(0) != "Rechazada") break;
                        rechazosSeguidos++;
                    }
                    return rechazosSeguidos;
                }
            }
        }

        public static void ActualizarEstatusSolicitud(SqlConnection conn, string idSolicitud, string estatus, int? numeroCfdis)
        {
            const string sql = @"
UPDATE SolicitudDescarga
SET Estatus = @Estatus, NumeroCFDIs = @NumeroCFDIs, FechaUltimaVerificacion = SYSUTCDATETIME()
WHERE IdSolicitud = @IdSolicitud;";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Estatus", estatus);
                cmd.Parameters.AddWithValue("@NumeroCFDIs", (object)numeroCfdis ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IdSolicitud", idSolicitud);
                cmd.ExecuteNonQuery();
            }
        }

        // Anota una verificacion que NO dio un estado utilizable (EstadoSolicitud distinto de 1..6,
        // por ejemplo 0): la solicitud conserva su estatus para que la siguiente pasada la
        // vuelva a verificar, y queda a la vista lo que el SAT respondio de verdad.
        public static void RegistrarVerificacionSinEstado(SqlConnection conn, string idSolicitud, string codEstatus, string mensaje)
        {
            const string sql = @"
UPDATE SolicitudDescarga
SET FechaUltimaVerificacion = SYSUTCDATETIME(), UltimoCodEstatus = @CodEstatus, UltimoMensaje = @Mensaje
WHERE IdSolicitud = @IdSolicitud;";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@CodEstatus", (object)Recortar(codEstatus, 10) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Mensaje", (object)Recortar(mensaje, 500) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IdSolicitud", idSolicitud);
                cmd.ExecuteNonQuery();
            }
        }

        private static string Recortar(string texto, int maximo) =>
            string.IsNullOrEmpty(texto) ? null : (texto.Length <= maximo ? texto : texto.Substring(0, maximo));

        // Upsert por UUID: el SAT puede repetir un paquete o una solicitud de Metadata. La última
        // línea conserva el estado más reciente sin duplicar el mismo comprobante en el catálogo.
        public static void GuardarMetadata(SqlConnection conn, MetadataParseada m, string tipo, string idSolicitud)
        {
            const string sql = @"
DECLARE @SolicitudDescargaID INT = (SELECT SolicitudDescargaID FROM SolicitudDescarga WHERE IdSolicitud=@IdSolicitud);
IF EXISTS (SELECT 1 FROM CfdiMetadata WHERE UUID=@UUID)
  UPDATE CfdiMetadata SET RFCEmisor=@RFCEmisor, NombreEmisor=@NombreEmisor, RFCReceptor=@RFCReceptor,
    NombreReceptor=@NombreReceptor, RFCPac=@RFCPac, FechaEmision=@FechaEmision,
    FechaCertificacion=@FechaCertificacion, Total=@Total, EfectoComprobante=@Efecto,
    EstatusSat=@Estatus, FechaCancelacion=@FechaCancelacion, Tipo=@Tipo,
    SolicitudDescargaID=@SolicitudDescargaID, LineaCruda=@LineaCruda, FechaDescarga=SYSUTCDATETIME()
  WHERE UUID=@UUID;
ELSE
  INSERT INTO CfdiMetadata (UUID,RFCEmisor,NombreEmisor,RFCReceptor,NombreReceptor,RFCPac,
    FechaEmision,FechaCertificacion,Total,EfectoComprobante,EstatusSat,FechaCancelacion,Tipo,
    SolicitudDescargaID,LineaCruda)
  VALUES (@UUID,@RFCEmisor,@NombreEmisor,@RFCReceptor,@NombreReceptor,@RFCPac,@FechaEmision,
    @FechaCertificacion,@Total,@Efecto,@Estatus,@FechaCancelacion,@Tipo,@SolicitudDescargaID,@LineaCruda);

-- Metadata es el canal del SAT que informa las cancelaciones: si el XML ya descargado figura
-- Vigente y el SAT lo reporta Cancelado, se actualiza (y queda en el historial). Solo en ese
-- sentido: Metadata es una foto y no debe regresar a Vigente algo ya detectado como Cancelado.
DECLARE @CfdiID INT, @Anterior NVARCHAR(30);
SELECT @CfdiID = CfdiID, @Anterior = EstatusSat FROM CfdiRecibido WHERE UUID = @UUID;
IF @CfdiID IS NOT NULL AND @Estatus = 'Cancelado' AND @Anterior = 'Vigente'
BEGIN
    UPDATE CfdiRecibido SET EstatusSat = 'Cancelado', FechaCambioEstatus = SYSUTCDATETIME() WHERE CfdiID = @CfdiID;
    INSERT INTO CfdiEstatusHistorial (CfdiID, UUID, EstatusAnterior, EstatusNuevo, Fuente)
    VALUES (@CfdiID, @UUID, 'Vigente', 'Cancelado', 'Metadata');
END";
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@UUID", m.UUID);
                cmd.Parameters.AddWithValue("@RFCEmisor", (object)m.RFCEmisor ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@NombreEmisor", (object)m.NombreEmisor ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@RFCReceptor", (object)m.RFCReceptor ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@NombreReceptor", (object)m.NombreReceptor ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@RFCPac", (object)m.RFCPac ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@FechaEmision", (object)m.FechaEmision ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@FechaCertificacion", (object)m.FechaCertificacion ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Total", (object)m.Total ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Efecto", (object)m.EfectoComprobante ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Estatus", (object)m.EstatusSat ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@FechaCancelacion", (object)m.FechaCancelacion ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Tipo", tipo);
                cmd.Parameters.AddWithValue("@IdSolicitud", idSolicitud);
                cmd.Parameters.AddWithValue("@LineaCruda", m.LineaCruda);
                cmd.ExecuteNonQuery();
            }
        }

        // Ocultar no borra nada: únicamente reduce el ruido de la cola diaria. Las pendientes
        // jamás se ocultan y siempre continúan siendo atendidas por el servicio.
        public static int OcultarSolicitudesHistoricas(SqlConnection conn, string rfc, int diasConservar = 30)
        {
            const string sql = @"
UPDATE SolicitudDescarga SET Oculta=1
WHERE RfcSolicitante=@Rfc AND Estatus NOT IN ('Aceptada','EnProceso')
  AND (Estatus='Reemplazada' OR FechaSolicitud < DATEADD(day, -@Dias, SYSUTCDATETIME()));
SELECT @@ROWCOUNT;";
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                cmd.Parameters.AddWithValue("@Dias", diasConservar);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        // error=null cuando el intento fue exitoso (limpia cualquier UltimoError previo).
        // error != null registra el motivo real (CodEstatus/Mensaje del SAT, o el error de
        // parseo/red) -- sin esto la UI solo sabe "VecesDescargado subio", no POR QUE fallo.
        public static void RegistrarPaquete(SqlConnection conn, string idSolicitud, string idPaquete, string error = null)
        {
            const string sql = @"
DECLARE @SolID INT = (SELECT SolicitudDescargaID FROM SolicitudDescarga WHERE IdSolicitud = @IdSolicitud);
IF @SolID IS NULL RETURN;

IF EXISTS (SELECT 1 FROM SolicitudDescargaPaquete WHERE SolicitudDescargaID = @SolID AND IdPaquete = @IdPaquete)
    UPDATE SolicitudDescargaPaquete
    SET VecesDescargado = VecesDescargado + 1, FechaUltimaDescarga = SYSUTCDATETIME(), UltimoError = @Error
    WHERE SolicitudDescargaID = @SolID AND IdPaquete = @IdPaquete;
ELSE
    INSERT INTO SolicitudDescargaPaquete (SolicitudDescargaID, IdPaquete, VecesDescargado, FechaUltimaDescarga, UltimoError)
    VALUES (@SolID, @IdPaquete, 1, SYSUTCDATETIME(), @Error);";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@IdSolicitud", idSolicitud);
                cmd.Parameters.AddWithValue("@IdPaquete", idPaquete);
                cmd.Parameters.AddWithValue("@Error", (object)error ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        // Idempotente por UUID (UNIQUE) -- si el CFDI ya existia (misma descarga corrida dos
        // veces, o el mismo XML llego por otra solicitud con rango traslapado), no duplica.
        // Regresa el CfdiID siempre (nuevo o ya existente) -- esNuevo dice si hubo que insertar,
        // para que el llamador sepa si tambien debe guardar relaciones/conceptos (evita
        // duplicarlos si el CFDI ya estaba).
        public static int GuardarCfdiRecibido(SqlConnection conn, CfdiParseado c, string rutaArchivoXml, out bool esNuevo)
        {
            const string sql = @"
IF EXISTS (SELECT 1 FROM CfdiRecibido WHERE UUID = @UUID)
    SELECT CfdiID, 0 AS EsNuevo FROM CfdiRecibido WHERE UUID = @UUID;
ELSE
BEGIN
    INSERT INTO CfdiRecibido
        (UUID, RFCEmisor, NombreEmisor, RFCReceptor, Serie, Folio, TipoComprobante,
         FormaPago, MetodoPago, UsoCFDI, Subtotal, Descuento, IVA, Retenciones, Total, Moneda, TipoCambio,
         FechaEmision, RutaArchivoXml)
    OUTPUT INSERTED.CfdiID, 1 AS EsNuevo
    VALUES
        (@UUID, @RFCEmisor, @NombreEmisor, @RFCReceptor, @Serie, @Folio, @TipoComprobante,
         @FormaPago, @MetodoPago, @UsoCFDI, @Subtotal, @Descuento, @IVA, @Retenciones, @Total, @Moneda, @TipoCambio,
         @FechaEmision, @RutaArchivoXml);
END";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@UUID", c.UUID);
                cmd.Parameters.AddWithValue("@RFCEmisor", (object)c.RFCEmisor ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@NombreEmisor", (object)c.NombreEmisor ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@RFCReceptor", (object)c.RFCReceptor ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Serie", (object)c.Serie ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Folio", (object)c.Folio ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@TipoComprobante", (object)c.TipoComprobante ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@FormaPago", (object)c.FormaPago ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@MetodoPago", (object)c.MetodoPago ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@UsoCFDI", (object)c.UsoCFDI ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Subtotal", (object)c.Subtotal ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Descuento", (object)c.Descuento ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IVA", (object)c.IVA ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Retenciones", (object)c.Retenciones ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Total", (object)c.Total ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Moneda", (object)c.Moneda ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@TipoCambio", (object)c.TipoCambio ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@FechaEmision", c.FechaEmision);
                cmd.Parameters.AddWithValue("@RutaArchivoXml", (object)rutaArchivoXml ?? DBNull.Value);
                using (var reader = cmd.ExecuteReader())
                {
                    reader.Read();
                    int cfdiId = reader.GetInt32(0);
                    esNuevo = reader.GetInt32(1) == 1;
                    return cfdiId;
                }
            }
        }

        // Reemplazo completo (borra e inserta) -- idempotente para que --reparsear pueda volver a
        // llamarlo sin duplicar partidas cada vez que se corre sobre un CFDI ya guardado.
        public static void GuardarConceptos(SqlConnection conn, int cfdiId, IEnumerable<ConceptoParseado> conceptos)
        {
            using (var cmdDel = new SqlCommand("DELETE FROM CfdiConcepto WHERE CfdiID=@Id", conn))
            {
                cmdDel.Parameters.AddWithValue("@Id", cfdiId);
                cmdDel.ExecuteNonQuery();
            }

            const string sql = @"
INSERT INTO CfdiConcepto (CfdiID, ClaveProdServ, Descripcion, Cantidad, ValorUnitario, Importe)
VALUES (@CfdiID, @Clave, @Descripcion, @Cantidad, @ValorUnitario, @Importe);";

            foreach (var co in conceptos)
            {
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@CfdiID", cfdiId);
                    cmd.Parameters.AddWithValue("@Clave", (object)co.ClaveProdServ ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Descripcion", (object)co.Descripcion ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Cantidad", (object)co.Cantidad ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ValorUnitario", (object)co.ValorUnitario ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Importe", (object)co.Importe ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static List<ConceptoFila> ObtenerConceptos(SqlConnection conn, int cfdiId)
        {
            const string sql = "SELECT ClaveProdServ, Descripcion, Cantidad, ValorUnitario, Importe FROM CfdiConcepto WHERE CfdiID=@Id ORDER BY CfdiConceptoID;";
            var resultado = new List<ConceptoFila>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Id", cfdiId);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        resultado.Add(new ConceptoFila
                        {
                            ClaveProdServ = reader.IsDBNull(0) ? null : reader.GetString(0),
                            Descripcion = reader.IsDBNull(1) ? null : reader.GetString(1),
                            Cantidad = reader.IsDBNull(2) ? (decimal?)null : reader.GetDecimal(2),
                            ValorUnitario = reader.IsDBNull(3) ? (decimal?)null : reader.GetDecimal(3),
                            Importe = reader.IsDBNull(4) ? (decimal?)null : reader.GetDecimal(4)
                        });
                    }
                }
            }
            return resultado;
        }

        // Reemplazo completo (borra e inserta) -- mismo patron que GuardarConceptos, idempotente
        // para que --reparsear pueda volver a llamarlo sin duplicar filas.
        public static void GuardarPagosDocumentos(SqlConnection conn, int cfdiId, IEnumerable<PagoDoctoParseado> pagos)
        {
            using (var cmdDel = new SqlCommand("DELETE FROM CfdiPagoDocto WHERE CfdiID=@Id", conn))
            {
                cmdDel.Parameters.AddWithValue("@Id", cfdiId);
                cmdDel.ExecuteNonQuery();
            }

            const string sql = @"
INSERT INTO CfdiPagoDocto (CfdiID, UUIDRelacionado, Serie, Folio, FechaPago, FormaDePago, Moneda, NumParcialidad, ImpSaldoAnt, ImpPagado, ImpSaldoInsoluto)
VALUES (@CfdiID, @UUIDRelacionado, @Serie, @Folio, @FechaPago, @FormaDePago, @Moneda, @NumParcialidad, @ImpSaldoAnt, @ImpPagado, @ImpSaldoInsoluto);";

            foreach (var p in pagos)
            {
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@CfdiID", cfdiId);
                    cmd.Parameters.AddWithValue("@UUIDRelacionado", p.UUIDRelacionado);
                    cmd.Parameters.AddWithValue("@Serie", (object)p.Serie ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Folio", (object)p.Folio ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@FechaPago", (object)p.FechaPago ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@FormaDePago", (object)p.FormaDePago ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Moneda", (object)p.Moneda ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@NumParcialidad", (object)p.NumParcialidad ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ImpSaldoAnt", (object)p.ImpSaldoAnt ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ImpPagado", (object)p.ImpPagado ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ImpSaldoInsoluto", (object)p.ImpSaldoInsoluto ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static List<PagoDoctoFila> ObtenerPagosDocumentos(SqlConnection conn, int cfdiId)
        {
            const string sql = @"
SELECT UUIDRelacionado, Serie, Folio, FechaPago, FormaDePago, Moneda, NumParcialidad, ImpSaldoAnt, ImpPagado, ImpSaldoInsoluto
FROM CfdiPagoDocto WHERE CfdiID=@Id ORDER BY CfdiPagoDoctoID;";
            var resultado = new List<PagoDoctoFila>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Id", cfdiId);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        resultado.Add(new PagoDoctoFila
                        {
                            UUIDRelacionado = reader.GetGuid(0),
                            Serie = reader.IsDBNull(1) ? null : reader.GetString(1),
                            Folio = reader.IsDBNull(2) ? null : reader.GetString(2),
                            FechaPago = reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3),
                            FormaDePago = reader.IsDBNull(4) ? null : reader.GetString(4),
                            Moneda = reader.IsDBNull(5) ? null : reader.GetString(5),
                            NumParcialidad = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6),
                            ImpSaldoAnt = reader.IsDBNull(7) ? (decimal?)null : reader.GetDecimal(7),
                            ImpPagado = reader.IsDBNull(8) ? (decimal?)null : reader.GetDecimal(8),
                            ImpSaldoInsoluto = reader.IsDBNull(9) ? (decimal?)null : reader.GetDecimal(9)
                        });
                    }
                }
            }
            return resultado;
        }

        public static void GuardarRelaciones(SqlConnection conn, CfdiParseado c)
        {
            if (c.Relaciones.Count == 0) return;

            const string sql = @"
IF NOT EXISTS (SELECT 1 FROM CfdiRelacion WHERE UUID = @UUID AND UUIDRelacionado = @UUIDRelacionado)
    INSERT INTO CfdiRelacion (UUID, UUIDRelacionado, TipoRelacion)
    VALUES (@UUID, @UUIDRelacionado, @TipoRelacion);";

            foreach (var (uuidRelacionado, tipoRelacion) in c.Relaciones)
            {
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@UUID", c.UUID);
                    cmd.Parameters.AddWithValue("@UUIDRelacionado", uuidRelacionado);
                    cmd.Parameters.AddWithValue("@TipoRelacion", (object)tipoRelacion ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // Rutas de XML ya descargados, para reparsear en disco sin volver a llamar al SAT (ver
        // Program.cs --reparsear) -- por ejemplo cuando CfdiXmlParser aprende a leer un campo
        // nuevo (TipoCambio, Retenciones, el complemento de Pagos) y hay que rellenarlo en filas
        // que ya se habian guardado antes de ese cambio.
        public static List<(int CfdiID, string RutaArchivoXml)> ObtenerRutasXmlParaReparseo(SqlConnection conn)
        {
            const string sql = "SELECT CfdiID, RutaArchivoXml FROM CfdiRecibido WHERE RutaArchivoXml IS NOT NULL;";
            var resultado = new List<(int, string)>();
            using (var cmd = new SqlCommand(sql, conn))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                    resultado.Add((reader.GetInt32(0), reader.GetString(1)));
            }
            return resultado;
        }

        public static void ActualizarCamposParseados(SqlConnection conn, int cfdiId, CfdiParseado c)
        {
            const string sql = @"
UPDATE CfdiRecibido
SET NombreEmisor = @NombreEmisor, FormaPago = @FormaPago, MetodoPago = @MetodoPago, UsoCFDI = @UsoCFDI,
    Subtotal = @Subtotal, Descuento = @Descuento, IVA = @IVA, Retenciones = @Retenciones,
    Total = @Total, Moneda = @Moneda, TipoCambio = @TipoCambio
WHERE CfdiID = @Id;";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@NombreEmisor", (object)c.NombreEmisor ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@FormaPago", (object)c.FormaPago ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@MetodoPago", (object)c.MetodoPago ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@UsoCFDI", (object)c.UsoCFDI ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Subtotal", (object)c.Subtotal ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Descuento", (object)c.Descuento ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IVA", (object)c.IVA ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Retenciones", (object)c.Retenciones ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Total", (object)c.Total ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Moneda", (object)c.Moneda ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@TipoCambio", (object)c.TipoCambio ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Id", cfdiId);
                cmd.ExecuteNonQuery();
            }
        }

        // Solo lectura -- usadas por la UI para pintar la cola/historial de UNA empresa
        // (rfc = RfcSolicitante / RFCReceptor). TOP acotado a proposito (panel de escritorio,
        // no reporte de auditoria completo).
        public static List<SolicitudFila> ObtenerSolicitudesRecientes(SqlConnection conn, string rfc, int top = 50)
        {
            string sql = $@"
SELECT TOP ({top}) s.IdSolicitud, s.Tipo, s.FechaInicial, s.FechaFinal, s.Estatus, s.NumeroCFDIs, s.FechaSolicitud,
    (SELECT TOP 1 p.UltimoError FROM SolicitudDescargaPaquete p
     WHERE p.SolicitudDescargaID = s.SolicitudDescargaID AND p.VecesDescargado >= 2 AND p.UltimoError IS NOT NULL
     ORDER BY p.FechaUltimaDescarga DESC) AS UltimoErrorPaquete,
    s.TipoSolicitud
FROM SolicitudDescarga s
WHERE s.RfcSolicitante = @Rfc AND s.Oculta = 0 AND s.Estatus <> 'Reemplazada'
ORDER BY s.FechaSolicitud DESC;";

            var resultado = new List<SolicitudFila>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        resultado.Add(new SolicitudFila
                        {
                            IdSolicitud = reader.GetString(0),
                            Tipo = reader.GetString(1),
                            FechaInicial = reader.GetDateTime(2),
                            FechaFinal = reader.GetDateTime(3),
                            Estatus = reader.GetString(4),
                            NumeroCFDIs = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5),
                            // Se guardo con SYSUTCDATETIME() -- hay que marcarlo UTC explicito
                            // para que el que lo muestre pueda convertirlo a hora local. Sin
                            // esto, DateTime.Kind queda "Unspecified" y .ToLocalTime() no hace
                            // nada -- bug real visto en vivo (mostraba un dia adelantado).
                            FechaSolicitud = DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc),
                            UltimoErrorPaquete = reader.IsDBNull(7) ? null : reader.GetString(7),
                            TipoSolicitud = reader.GetString(8)
                        });
                    }
                }
            }
            return resultado;
        }

        // Hasta que fecha esta "cubierto" un RFC para un TipoSolicitud (CFDI/Metadata) -- solo
        // cuenta una solicitud como cubierta si de verdad termino bien: Estatus=Terminada Y
        // ningun paquete suyo se quedo con error sin resolver (mismo criterio que usa la UI para
        // mostrar "Error de descarga" en vez de "Terminada", ver UltimoErrorPaquete arriba).
        // Rechazada/Error/Vencida/Terminada-con-error nunca cuentan como cubiertas -- el catch-up
        // automatico (--auto-solicitar-todas) las vuelve a pedir solo porque esta fecha no avanza,
        // sin necesitar un contador de reintentos aparte. NULL si nunca se ha pedido nada de ese
        // tipo para ese RFC.
        // tipo = Recibidos/Emitidos -- OBLIGATORIO distinguirlo aqui ademas de tipoSolicitud
        // (CFDI/Metadata): RfcSolicitante es el mismo RFC en ambos casos (siempre es el RFC de la
        // propia empresa), asi que sin este filtro una solicitud "Terminada" de Recibidos haria
        // creer que Emitidos tambien esta cubierto (o viceversa) -- bug real que se hubiera
        // introducido al agregar Emitidos si no se filtraba tambien por Tipo.
        public static DateTime? ObtenerUltimaFechaCubierta(SqlConnection conn, string rfc, string tipoSolicitud, string tipo)
        {
            const string sql = @"
SELECT MAX(s.FechaFinal)
FROM SolicitudDescarga s
WHERE s.RfcSolicitante = @Rfc AND s.TipoSolicitud = @Tipo AND s.Tipo = @TipoRecEmi AND s.Estatus = 'Terminada'
  AND NOT EXISTS (
    SELECT 1 FROM SolicitudDescargaPaquete p
    WHERE p.SolicitudDescargaID = s.SolicitudDescargaID AND p.VecesDescargado >= 2 AND p.UltimoError IS NOT NULL);";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                cmd.Parameters.AddWithValue("@Tipo", tipoSolicitud);
                cmd.Parameters.AddWithValue("@TipoRecEmi", tipo);
                var resultado = cmd.ExecuteScalar();
                // FechaFinal es un limite de rango de CALENDARIO (lo que se le pidio al SAT), no
                // un instante -- a diferencia de FechaSolicitud, no se guardo con SYSUTCDATETIME()
                // y no debe marcarse UTC (confundiria a quien despues le aplique .ToLocalTime()).
                return resultado == null || resultado == DBNull.Value ? (DateTime?)null : (DateTime)resultado;
            }
        }

        // CFDI cancelados (segun el SAT) que aun no se han marcado Cancelado en Comercial.
        public static List<(int CfdiID, Guid UUID, DateTime? Fecha)> ObtenerCancelacionesSinPropagar(SqlConnection conn, string rfc)
        {
            const string sql = @"
SELECT CfdiID, UUID, FechaCambioEstatus FROM CfdiRecibido
WHERE (RFCReceptor = @r OR RFCEmisor = @r) AND EstatusSat = 'Cancelado' AND FechaCancelacionComercial IS NULL
  AND FechaSincronizadoComercial IS NOT NULL;";
            var lista = new List<(int, Guid, DateTime?)>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@r", rfc);
                using (var l = cmd.ExecuteReader())
                    while (l.Read())
                        lista.Add((l.GetInt32(0), l.GetGuid(1), l.IsDBNull(2) ? (DateTime?)null : DateTime.SpecifyKind(l.GetDateTime(2), DateTimeKind.Utc)));
            }
            return lista;
        }

        public static void MarcarCancelacionPropagada(SqlConnection conn, int cfdiId, int documentId, string descripcion)
        {
            const string sql = @"
UPDATE CfdiRecibido SET FechaCancelacionComercial = SYSUTCDATETIME() WHERE CfdiID = @Id;
IF @Doc <> 0 AND NOT EXISTS (SELECT 1 FROM CfdiCancelacionComercial WHERE CfdiID = @Id)
    INSERT INTO CfdiCancelacionComercial (CfdiID, UUID, DocumentID, Descripcion)
    SELECT CfdiID, UUID, @Doc, @Desc FROM CfdiRecibido WHERE CfdiID = @Id;";
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Id", cfdiId);
                cmd.Parameters.AddWithValue("@Doc", documentId);
                cmd.Parameters.AddWithValue("@Desc", (object)Recortar(descripcion, 300) ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        public static List<(int Id, DateTime Fecha, string Empresa, Guid UUID, string Descripcion, bool Revisada)> ObtenerCancelacionesConDocumento(SqlConnection conn, bool incluirRevisadas)
        {
            const string sql = @"
SELECT k.CfdiCancelacionComercialID, k.FechaDeteccion, ISNULL(e.Nombre, c.RFCEmisor), k.UUID, k.Descripcion, k.Revisada
FROM CfdiCancelacionComercial k
JOIN CfdiRecibido c ON c.CfdiID = k.CfdiID
LEFT JOIN Empresa e ON e.RFC = c.RFCReceptor OR e.RFC = c.RFCEmisor
WHERE (@Todas = 1 OR k.Revisada = 0)
ORDER BY k.FechaDeteccion DESC;";
            var lista = new List<(int, DateTime, string, Guid, string, bool)>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Todas", incluirRevisadas);
                using (var l = cmd.ExecuteReader())
                    while (l.Read())
                        lista.Add((l.GetInt32(0), DateTime.SpecifyKind(l.GetDateTime(1), DateTimeKind.Utc), l.GetString(2), l.GetGuid(3), l.IsDBNull(4) ? "" : l.GetString(4), l.GetBoolean(5)));
            }
            return lista;
        }

        public static void MarcarCancelacionRevisada(SqlConnection conn, int id)
        {
            using (var cmd = new SqlCommand("UPDATE CfdiCancelacionComercial SET Revisada = 1, FechaRevision = SYSUTCDATETIME() WHERE CfdiCancelacionComercialID = @i", conn))
            {
                cmd.Parameters.AddWithValue("@i", id);
                cmd.ExecuteNonQuery();
            }
        }

        // Estado del servicio (latido, ultimas pasadas): una fila por clave.
        public static void GuardarEstado(SqlConnection conn, string clave, string valor = null)
        {
            const string sql = @"
UPDATE ServicioEstado SET Valor=@v, Fecha=SYSUTCDATETIME() WHERE Clave=@c;
IF @@ROWCOUNT = 0 INSERT INTO ServicioEstado (Clave, Valor) VALUES (@c, @v);";
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@c", clave);
                cmd.Parameters.AddWithValue("@v", (object)valor ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        public static DateTime? LeerEstado(SqlConnection conn, string clave)
        {
            using (var cmd = new SqlCommand("SELECT Fecha FROM ServicioEstado WHERE Clave=@c", conn))
            {
                cmd.Parameters.AddWithValue("@c", clave);
                var r = cmd.ExecuteScalar();
                return r == null || r == DBNull.Value ? (DateTime?)null : DateTime.SpecifyKind((DateTime)r, DateTimeKind.Utc);
            }
        }

        // Meses con CFDI vigentes que el SAT reporta en Metadata y que NO existen como XML descargado
        // (CfdiRecibido). Es la comprobacion de que no falta nada.
        public static List<(int Anio, int Mes, int Faltan)> ObtenerMesesConFaltantes(SqlConnection conn, string rfc, string tipo)
        {
            const string sql = @"
SELECT YEAR(m.FechaEmision), MONTH(m.FechaEmision), COUNT(*)
FROM CfdiMetadata m
WHERE m.Tipo = @Tipo AND m.EstatusSat = 'Vigente' AND m.FechaEmision IS NOT NULL
  AND ((@Tipo = 'Recibidos' AND m.RFCReceptor = @Rfc) OR (@Tipo = 'Emitidos' AND m.RFCEmisor = @Rfc))
  AND NOT EXISTS (SELECT 1 FROM CfdiRecibido c WHERE c.UUID = m.UUID)
GROUP BY YEAR(m.FechaEmision), MONTH(m.FechaEmision)
ORDER BY 1, 2;";
            var lista = new List<(int, int, int)>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                cmd.Parameters.AddWithValue("@Tipo", tipo);
                using (var reader = cmd.ExecuteReader())
                    while (reader.Read()) lista.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)));
            }
            return lista;
        }

        // Mapa de dias CUBIERTOS de CFDI para este RFC/direccion: cada solicitud cuenta con su propio
        // rango [FechaInicial, FechaFinal], no solo la ultima fecha. Reemplaza a
        // ObtenerUltimaFechaCubierta, que con un MAX(FechaFinal) dejaba un hueco permanente: si el
        // dia 17 fallaba y el 19 terminaba bien, el sistema seguia desde el 20 y NUNCA volvia al 17.
        // Cuenta como cubierta una solicitud que:
        //  - esta Terminada y no tiene un paquete perdido (2 descargas gastadas con error) ni uno
        //    que nunca se bajo y ya paso su vida de 72 h; o
        //  - sigue Aceptada/EnProceso y es reciente (se espera su resultado; se descarta si pasan
        //    'diasEspera' dias, ver VencerSolicitudesAtoradas).
        public static List<(DateTime Desde, DateTime Hasta)> ObtenerRangosCubiertos(SqlConnection conn, string rfc, string tipoSolicitud, string tipo, int diasEspera = 3)
        {
            const string sql = @"
SELECT s.FechaInicial, s.FechaFinal
FROM SolicitudDescarga s
WHERE s.RfcSolicitante = @Rfc AND s.TipoSolicitud = @Tipo AND s.Tipo = @TipoRecEmi
  AND (
        (s.Estatus IN ('Aceptada','EnProceso') AND s.FechaSolicitud > DATEADD(DAY, -@DiasEspera, SYSUTCDATETIME()))
     OR (s.Estatus = 'Terminada'
         AND NOT EXISTS (
             SELECT 1 FROM SolicitudDescargaPaquete p
             WHERE p.SolicitudDescargaID = s.SolicitudDescargaID
               AND ((p.VecesDescargado >= 2 AND p.UltimoError IS NOT NULL)
                 OR (p.VecesDescargado = 0 AND s.FechaUltimaVerificacion < DATEADD(HOUR, -72, SYSUTCDATETIME())))))
      );";

            var rangos = new List<(DateTime, DateTime)>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                cmd.Parameters.AddWithValue("@Tipo", tipoSolicitud);
                cmd.Parameters.AddWithValue("@TipoRecEmi", tipo);
                cmd.Parameters.AddWithValue("@DiasEspera", diasEspera);
                using (var reader = cmd.ExecuteReader())
                    while (reader.Read()) rangos.Add((reader.GetDateTime(0), reader.GetDateTime(1)));
            }
            return rangos;
        }

        // Rangos que el SAT rechazo de inmediato en las ultimas 'horas' horas (1 h si fue 5002, que se
        // reintenta ensanchando el rango): no se vuelven a pedir hasta que pase ese tiempo (y se siga con los demas huecos).
        public static List<(DateTime Desde, DateTime Hasta)> ObtenerRangosRechazadosRecientes(SqlConnection conn, string rfc, string tipoSolicitud, string tipo, int horas)
        {
            const string sql = @"
SELECT FechaInicial, FechaFinal FROM SolicitudDescarga
WHERE RfcSolicitante = @Rfc AND TipoSolicitud = @Tipo AND Tipo = @TipoRecEmi
  AND Estatus = 'Rechazada'
  AND FechaSolicitud > DATEADD(MINUTE, -CASE WHEN UltimoCodEstatus = '5002' THEN 60 ELSE @Horas * 60 END, SYSUTCDATETIME());";
            var rangos = new List<(DateTime, DateTime)>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                cmd.Parameters.AddWithValue("@Tipo", tipoSolicitud);
                cmd.Parameters.AddWithValue("@TipoRecEmi", tipo);
                cmd.Parameters.AddWithValue("@Horas", horas);
                using (var reader = cmd.ExecuteReader())
                    while (reader.Read()) rangos.Add((reader.GetDateTime(0), reader.GetDateTime(1)));
            }
            return rangos;
        }

        // Cuantas veces ya se pidio EXACTAMENTE este rango (cualquier resultado, rechazos incluidos).
        // El SAT limita las solicitudes con los mismos parametros (CodEstatus 5002 "de por vida"),
        // asi que un reintento del mismo hueco amplia el rango un dia por cada intento previo.
        public static int ContarSolicitudesDelRango(SqlConnection conn, string rfc, string tipoSolicitud, string tipo, DateTime desde, DateTime hasta)
        {
            const string sql = @"
SELECT COUNT(*) FROM SolicitudDescarga
WHERE RfcSolicitante = @Rfc AND TipoSolicitud = @Tipo AND Tipo = @TipoRecEmi
  AND FechaInicial = @Desde AND FechaFinal = @Hasta;";
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                cmd.Parameters.AddWithValue("@Tipo", tipoSolicitud);
                cmd.Parameters.AddWithValue("@TipoRecEmi", tipo);
                cmd.Parameters.AddWithValue("@Desde", desde);
                cmd.Parameters.AddWithValue("@Hasta", hasta);
                return (int)cmd.ExecuteScalar();
            }
        }

        // Una solicitud que el SAT acepto pero nunca termino en 'dias' dias deja de esperarse: pasa
        // a Vencida y su rango se vuelve a pedir (ObtenerRangosCubiertos ya no la cuenta).
        public static int VencerSolicitudesAtoradas(SqlConnection conn, string rfc, int dias)
        {
            const string sql = @"
UPDATE SolicitudDescarga
SET Estatus = 'Vencida', UltimoMensaje = 'Sin resultado del SAT tras ' + CAST(@Dias AS NVARCHAR(5)) + ' dias; se vuelve a pedir.'
WHERE RfcSolicitante = @Rfc AND Estatus IN ('Aceptada','EnProceso')
  AND FechaSolicitud < DATEADD(DAY, -@Dias, SYSUTCDATETIME());";
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                cmd.Parameters.AddWithValue("@Dias", dias);
                return cmd.ExecuteNonQuery();
            }
        }

        // Fecha (UTC) de la solicitud mas reciente de este tipo/origen, o null.
        public static DateTime? ObtenerFechaUltimaSolicitudDe(SqlConnection conn, string rfc, string tipo, string tipoSolicitud, string origen = null)
        {
            const string sql = @"
SELECT MAX(FechaSolicitud) FROM SolicitudDescarga
WHERE RfcSolicitante = @Rfc AND Tipo = @TipoRecEmi AND TipoSolicitud = @Tipo AND (@Origen IS NULL OR Origen = @Origen);";
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                cmd.Parameters.AddWithValue("@TipoRecEmi", tipo);
                cmd.Parameters.AddWithValue("@Tipo", tipoSolicitud);
                cmd.Parameters.AddWithValue("@Origen", (object)origen ?? DBNull.Value);
                var r = cmd.ExecuteScalar();
                return r == null || r == DBNull.Value ? (DateTime?)null : (DateTime)r;
            }
        }

        // Fecha de la ultima vez que se PIDIO Metadata para este RFC/direccion, sin importar si
        // esa solicitud llego a Terminada o se quedo atorada -- pedido explicito del usuario
        // 2026-08-19 tras confirmar que Metadata se quedo atorado en 13 de 13 intentos reales
        // (2h a 105h de espera, 0 completados): se apaga el pedido automatico diario, pero se
        // reintenta 1 vez por semana por si el SAT algun dia lo resuelve de su lado -- es el
        // UNICO canal del SAT que informa CFDI cancelados desde su origen (CFDI tipo "CFDI" solo
        // acepta EstadoComprobante=Vigente, jamas trae cancelados). Ver AutoSolicitador.
        public static DateTime? ObtenerFechaUltimaSolicitudMetadata(SqlConnection conn, string rfc, string tipo)
        {
            const string sql = @"
SELECT MAX(FechaSolicitud) FROM SolicitudDescarga
WHERE RfcSolicitante = @Rfc AND TipoSolicitud = 'Metadata' AND Tipo = @TipoRecEmi;";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                cmd.Parameters.AddWithValue("@TipoRecEmi", tipo);
                var resultado = cmd.ExecuteScalar();
                return resultado == null || resultado == DBNull.Value ? (DateTime?)null : (DateTime)resultado;
            }
        }

        // Fecha de la ultima vez que se pidio el "reintento retroactivo" (Origen='Retroactivo')
        // para este RFC/direccion -- pedido explicito del usuario 2026-08-19: un CFDI se puede
        // timbrar hasta 72 horas DESPUES de su Fecha de emision (regla real del SAT/PACs), asi
        // que el avance normal de "fecha cubierta" (ObtenerUltimaFechaCubierta, que nunca vuelve
        // a pedir un rango ya marcado Terminada) puede dejar fuera para siempre un CFDI que
        // llegue tarde dentro de esa ventana. Este reintento vuelve a pedir la ultima semana
        // completa SIN importar si ya estaba cubierta -- es seguro repetirlo: GuardarCfdiRecibido
        // es idempotente por UUID (UNIQUE), asi que un CFDI ya guardado simplemente se salta y
        // solo entran los que faltaban. Ver AutoSolicitador.
        public static DateTime? ObtenerFechaUltimaSolicitudRetroactiva(SqlConnection conn, string rfc, string tipo)
        {
            const string sql = @"
SELECT MAX(FechaSolicitud) FROM SolicitudDescarga
WHERE RfcSolicitante = @Rfc AND Origen = 'Retroactivo' AND Tipo = @TipoRecEmi;";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                cmd.Parameters.AddWithValue("@TipoRecEmi", tipo);
                var resultado = cmd.ExecuteScalar();
                return resultado == null || resultado == DBNull.Value ? (DateTime?)null : (DateTime)resultado;
            }
        }

        // Marca que este CFDI ya se copio a la carpeta de Comercial correspondiente -- llamado
        // desde SolicitudWorker (copia automatica en cada descarga nueva) y ComercialSync (backfill
        // manual). Por UUID porque es lo que ya se tiene a la mano en ambos puntos de llamada, sin
        // necesidad de conocer el CfdiID.
        public static void MarcarSincronizadoComercial(SqlConnection conn, Guid uuid)
        {
            using (var cmd = new SqlCommand("UPDATE CfdiRecibido SET FechaSincronizadoComercial = SYSUTCDATETIME() WHERE UUID = @Uuid", conn))
            {
                cmd.Parameters.AddWithValue("@Uuid", uuid);
                cmd.ExecuteNonQuery();
            }
        }

        // Todos los CFDI (Recibidos + Emitidos) de esta empresa que ya tienen un XML en disco --
        // usado por ComercialSync.SincronizarTodo para el "backfill" de una sola vez, pedido
        // explicito del usuario 2026-08-18: "ya baje mas de mil CFDI, necesitamos importarlos" --
        // esos se descargaron ANTES de que existiera la copia automatica hacia Comercial (agregada
        // en esta misma sesion), asi que nunca se copiaron. EsEmitido distingue el lado del CFDI
        // (RFCEmisor=rfc) del lado Recibido (RFCReceptor=rfc) para saber a que carpeta va cada uno.
        public static List<(Guid UUID, string RutaArchivoXml, bool EsEmitido)> ObtenerCfdiConArchivo(SqlConnection conn, string rfc)
        {
            const string sql = @"
SELECT UUID, RutaArchivoXml, CASE WHEN RFCEmisor = @Rfc THEN 1 ELSE 0 END AS EsEmitido
FROM CfdiRecibido
WHERE (RFCReceptor = @Rfc OR RFCEmisor = @Rfc) AND RutaArchivoXml IS NOT NULL AND Archivado = 0;";

            var resultado = new List<(Guid, string, bool)>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                        resultado.Add((reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2) == 1));
                }
            }
            return resultado;
        }

        // Filtros opcionales para el historial de CFDI de la UI (todos nullable = "sin filtrar").
        // busqueda es un LIKE de texto libre contra RFC/nombre/UUID/folio/serie/importe/fecha --
        // pedido explicito del usuario: "necesito busque por uuid, importe, rfc, nombre del
        // cliente, fecha, cualquier dato". incluirArchivados=false (default) es el comportamiento
        // de "limpiar" el historial sin borrar la fila ni el XML en disco.
        // tipo = "Recibidos" (default, RFCReceptor=rfc, el CFDI te lo generaron a ti) o
        // "Emitidos" (RFCEmisor=rfc, tu lo generaste) -- pedido explicito del usuario 2026-08-18:
        // "la app no tiene forma de ver los XML Recibidos o Emitidos, creo que los junta todos" --
        // antes esta consulta SIEMPRE filtraba por RFCReceptor, los Emitidos nunca se mostraban
        // en la grilla aunque ya estuvieran descargados en la BD.
        public static List<CfdiFila> ObtenerCfdiRecientes(SqlConnection conn, string rfc, int top = 100,
            string estatusSat = null, string tipoComprobante = null, string busqueda = null, bool incluirArchivados = false,
            string tipo = "Recibidos")
        {
            bool esEmitidos = tipo == "Emitidos";
            string sql = $@"
SELECT TOP ({top}) CfdiID, UUID, RFCEmisor, NombreEmisor, RFCReceptor, TipoComprobante, FechaEmision,
       Subtotal, Descuento, IVA, Retenciones, Total, Moneda, TipoCambio, FormaPago, MetodoPago,
       EstatusSat, Archivado, RutaArchivoXml, FechaSincronizadoComercial
FROM CfdiRecibido
WHERE {(esEmitidos ? "RFCEmisor" : "RFCReceptor")} = @Rfc
  AND (@IncluirArchivados = 1 OR Archivado = 0)
  AND (@EstatusSat IS NULL OR EstatusSat = @EstatusSat)
  AND (@TipoComprobante IS NULL OR TipoComprobante = @TipoComprobante)
  AND (@Busqueda IS NULL OR
       RFCEmisor LIKE @Busqueda OR
       NombreEmisor LIKE @Busqueda OR
       CAST(UUID AS NVARCHAR(50)) LIKE @Busqueda OR
       Folio LIKE @Busqueda OR
       Serie LIKE @Busqueda OR
       CAST(Total AS NVARCHAR(30)) LIKE @Busqueda OR
       CAST(Subtotal AS NVARCHAR(30)) LIKE @Busqueda OR
       CONVERT(NVARCHAR(10), FechaEmision, 23) LIKE @Busqueda)
ORDER BY FechaDescarga DESC;";

            var resultado = new List<CfdiFila>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                cmd.Parameters.AddWithValue("@IncluirArchivados", incluirArchivados);
                cmd.Parameters.AddWithValue("@EstatusSat", (object)estatusSat ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@TipoComprobante", (object)tipoComprobante ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Busqueda", busqueda == null ? (object)DBNull.Value : "%" + busqueda + "%");
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        resultado.Add(new CfdiFila
                        {
                            CfdiID = reader.GetInt32(0),
                            UUID = reader.GetGuid(1),
                            RFCEmisor = reader.GetString(2),
                            NombreEmisor = reader.IsDBNull(3) ? null : reader.GetString(3),
                            RFCReceptor = reader.GetString(4),
                            TipoComprobante = reader.IsDBNull(5) ? null : reader.GetString(5),
                            FechaEmision = reader.GetDateTime(6),
                            Subtotal = reader.IsDBNull(7) ? (decimal?)null : reader.GetDecimal(7),
                            Descuento = reader.IsDBNull(8) ? (decimal?)null : reader.GetDecimal(8),
                            IVA = reader.IsDBNull(9) ? (decimal?)null : reader.GetDecimal(9),
                            Retenciones = reader.IsDBNull(10) ? (decimal?)null : reader.GetDecimal(10),
                            Total = reader.IsDBNull(11) ? (decimal?)null : reader.GetDecimal(11),
                            Moneda = reader.IsDBNull(12) ? null : reader.GetString(12),
                            TipoCambio = reader.IsDBNull(13) ? (decimal?)null : reader.GetDecimal(13),
                            FormaPago = reader.IsDBNull(14) ? null : reader.GetString(14),
                            MetodoPago = reader.IsDBNull(15) ? null : reader.GetString(15),
                            EstatusSat = reader.GetString(16),
                            Archivado = reader.GetBoolean(17),
                            RutaArchivoXml = reader.IsDBNull(18) ? null : reader.GetString(18),
                            FechaSincronizadoComercial = reader.IsDBNull(19) ? (DateTime?)null : reader.GetDateTime(19)
                        });
                    }
                }
            }
            return resultado;
        }

        public static List<ActividadDescargaFila> ObtenerActividadDescargas(SqlConnection conn, string rfc,
            DateTime desde, DateTime hasta)
        {
            const string sql = @"
SELECT CAST(FechaDescarga AS date) AS Fecha, COUNT(*) AS Cantidad, ISNULL(SUM(Total), 0) AS Total
FROM CfdiRecibido
WHERE (RFCReceptor = @Rfc OR RFCEmisor = @Rfc)
  AND FechaDescarga >= @Desde AND FechaDescarga < @Hasta
GROUP BY CAST(FechaDescarga AS date)
ORDER BY Fecha;";

            var resultado = new List<ActividadDescargaFila>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                cmd.Parameters.AddWithValue("@Desde", desde.Date);
                cmd.Parameters.AddWithValue("@Hasta", hasta.Date);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        resultado.Add(new ActividadDescargaFila
                        {
                            Fecha = reader.GetDateTime(0),
                            Cantidad = reader.GetInt32(1),
                            Total = reader.GetDecimal(2)
                        });
                    }
                }
            }
            return resultado;
        }

        public static CoberturaDescargaFila ObtenerCoberturaDescargas(SqlConnection conn, string rfc)
        {
            const string sql = @"
SELECT
 (SELECT MAX(FechaDescarga) FROM CfdiRecibido WHERE RFCReceptor=@Rfc OR RFCEmisor=@Rfc),
 (SELECT MAX(FechaDescarga) FROM CfdiMetadata WHERE RFCReceptor=@Rfc OR RFCEmisor=@Rfc),
 (SELECT COUNT(*) FROM CfdiMetadata WHERE RFCReceptor=@Rfc OR RFCEmisor=@Rfc);";
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read()) return new CoberturaDescargaFila();
                    return new CoberturaDescargaFila
                    {
                        UltimoXml = reader.IsDBNull(0) ? (DateTime?)null : reader.GetDateTime(0),
                        UltimoMetadata = reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1),
                        TotalMetadata = reader.IsDBNull(2) ? 0 : reader.GetInt32(2)
                    };
                }
            }
        }

        // Fila completa de UN CFDI para la ventana de detalle (doble clic en el historial) --
        // ObtenerCfdiRecientes trae solo lo que cabe en la grilla, esto trae todos los campos.
        public static CfdiDetalleFila ObtenerCfdiDetalle(SqlConnection conn, int cfdiId)
        {
            const string sql = @"
SELECT CfdiID, UUID, RFCEmisor, NombreEmisor, RFCReceptor, Serie, Folio, TipoComprobante,
       FormaPago, MetodoPago, UsoCFDI, Subtotal, Descuento, IVA, Retenciones, Total, Moneda,
       TipoCambio, FechaEmision, EstatusSat, RutaArchivoXml, FechaDescarga, Archivado
FROM CfdiRecibido
WHERE CfdiID = @Id;";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Id", cfdiId);
                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read()) return null;
                    return new CfdiDetalleFila
                    {
                        CfdiID = reader.GetInt32(0),
                        UUID = reader.GetGuid(1),
                        RFCEmisor = reader.GetString(2),
                        NombreEmisor = reader.IsDBNull(3) ? null : reader.GetString(3),
                        RFCReceptor = reader.GetString(4),
                        Serie = reader.IsDBNull(5) ? null : reader.GetString(5),
                        Folio = reader.IsDBNull(6) ? null : reader.GetString(6),
                        TipoComprobante = reader.IsDBNull(7) ? null : reader.GetString(7),
                        FormaPago = reader.IsDBNull(8) ? null : reader.GetString(8),
                        MetodoPago = reader.IsDBNull(9) ? null : reader.GetString(9),
                        UsoCFDI = reader.IsDBNull(10) ? null : reader.GetString(10),
                        Subtotal = reader.IsDBNull(11) ? (decimal?)null : reader.GetDecimal(11),
                        Descuento = reader.IsDBNull(12) ? (decimal?)null : reader.GetDecimal(12),
                        IVA = reader.IsDBNull(13) ? (decimal?)null : reader.GetDecimal(13),
                        Retenciones = reader.IsDBNull(14) ? (decimal?)null : reader.GetDecimal(14),
                        Total = reader.IsDBNull(15) ? (decimal?)null : reader.GetDecimal(15),
                        Moneda = reader.IsDBNull(16) ? null : reader.GetString(16),
                        TipoCambio = reader.IsDBNull(17) ? (decimal?)null : reader.GetDecimal(17),
                        FechaEmision = reader.GetDateTime(18),
                        EstatusSat = reader.GetString(19),
                        RutaArchivoXml = reader.IsDBNull(20) ? null : reader.GetString(20),
                        FechaDescarga = DateTime.SpecifyKind(reader.GetDateTime(21), DateTimeKind.Utc),
                        Archivado = reader.GetBoolean(22)
                    };
                }
            }
        }

        // CfdiVinculo todavia no se escribe desde ningun lado de la UI (punto 4 del roadmap --
        // asociar a un documento de Comercial Pro es una pieza grande, pendiente de diseño), pero
        // la tabla ya existe en el esquema -- esto es solo lectura, para que la ventana de
        // detalle diga "vinculado" o "sin vincular" cuando esa pieza exista.
        public static List<CfdiVinculoFila> ObtenerVinculos(SqlConnection conn, Guid uuid)
        {
            const string sql = "SELECT DocumentID, TipoVinculo, FechaVinculo FROM CfdiVinculo WHERE UUID = @Uuid;";
            var resultado = new List<CfdiVinculoFila>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Uuid", uuid);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        resultado.Add(new CfdiVinculoFila
                        {
                            DocumentID = reader.GetInt32(0),
                            TipoVinculo = reader.GetString(1),
                            FechaVinculo = DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc)
                        });
                    }
                }
            }
            return resultado;
        }

        // Archivar NO borra la fila ni el XML en disco (RutaArchivoXml queda intacto) -- solo lo
        // saca del historial que se muestra por defecto. Reversible: llamar con archivar=false
        // para desarchivar.
        public static void ArchivarCfdi(SqlConnection conn, int cfdiId, bool archivar)
        {
            using (var cmd = new SqlCommand("UPDATE CfdiRecibido SET Archivado=@Archivado WHERE CfdiID=@Id", conn))
            {
                cmd.Parameters.AddWithValue("@Archivado", archivar);
                cmd.Parameters.AddWithValue("@Id", cfdiId);
                cmd.ExecuteNonQuery();
            }
        }

        // Refresca EstatusSat (Vigente/Cancelado) contra el servicio publico de consulta del SAT
        // (ver SatSoapClient.ConsultarEstatusCfdiAsync) -- no gasta cupo, se puede llamar cuantas
        // veces se quiera.
        // "No Encontrado" NO se guarda como estatus: no significa que el CFDI no exista, sino que la
        // consulta no lo ubico (total/RFC distinto, servicio del SAT con retraso...). Pisar un
        // Vigente real con eso borraria informacion buena; solo se marca la fecha de revision.
        // Si el estatus cambia, queda una fila en CfdiEstatusHistorial.
        public static void ActualizarEstatusCfdi(SqlConnection conn, int cfdiId, string estatusSat,
            string estatusCancelacion = null, string fuente = "Consulta", string validacionEfos = null)
        {
            bool valido = estatusSat == "Vigente" || estatusSat == "Cancelado";
            const string sql = @"
DECLARE @Anterior NVARCHAR(30), @Uuid UNIQUEIDENTIFIER;
SELECT @Anterior = EstatusSat, @Uuid = UUID FROM CfdiRecibido WHERE CfdiID = @Id;

UPDATE CfdiRecibido
SET FechaUltimaVerificacionEstatus = SYSUTCDATETIME(),
    EstatusSat = CASE WHEN @Valido = 1 THEN @Estatus ELSE EstatusSat END,
    EstatusCancelacion = CASE WHEN @Valido = 1 THEN @EstatusCancelacion ELSE EstatusCancelacion END,
    FechaCambioEstatus = CASE WHEN @Valido = 1 AND @Anterior <> @Estatus THEN SYSUTCDATETIME() ELSE FechaCambioEstatus END,
    ValidacionEFOS = ISNULL(@Efos, ValidacionEFOS)
WHERE CfdiID = @Id;

IF @Valido = 1 AND @Anterior IS NOT NULL AND @Anterior <> @Estatus
    INSERT INTO CfdiEstatusHistorial (CfdiID, UUID, EstatusAnterior, EstatusNuevo, EstatusCancelacion, Fuente)
    VALUES (@Id, @Uuid, @Anterior, @Estatus, @EstatusCancelacion, @Fuente);";
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Estatus", estatusSat ?? "");
                cmd.Parameters.AddWithValue("@Valido", valido);
                cmd.Parameters.AddWithValue("@EstatusCancelacion", (object)Recortar(estatusCancelacion, 60) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Efos", (object)Recortar(validacionEfos, 10) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Fuente", fuente);
                cmd.Parameters.AddWithValue("@Id", cfdiId);
                cmd.ExecuteNonQuery();
            }
        }

        // Los CFDI que toca revisar ahora, los de revision mas antigua primero (nunca revisados
        // antes que todos). Un CFDI de los ultimos 120 dias se vuelve a revisar cada 6 h (es cuando
        // mas se cancelan); los demas cada 24 h. 'limite' acota cada pasada; el resto se atiende en
        // las siguientes, asi el ciclo es continuo y no una sola corrida gigante al dia.
        public static List<CfdiDetalleFila> ObtenerCfdiParaVerificar(SqlConnection conn, int limite)
        {
            const string sql = @"
SELECT TOP (@Limite) CfdiID, UUID, RFCEmisor, RFCReceptor, Total, EstatusSat, TipoComprobante
FROM CfdiRecibido
WHERE FechaUltimaVerificacionEstatus IS NULL
   OR FechaUltimaVerificacionEstatus < DATEADD(HOUR, CASE WHEN FechaEmision >= DATEADD(DAY, -120, SYSUTCDATETIME()) THEN -6 ELSE -24 END, SYSUTCDATETIME())
ORDER BY ISNULL(FechaUltimaVerificacionEstatus, '0001-01-01'), CfdiID;";

            var resultado = new List<CfdiDetalleFila>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Limite", limite);
                using (var reader = cmd.ExecuteReader())
                    while (reader.Read())
                        resultado.Add(new CfdiDetalleFila
                        {
                            CfdiID = reader.GetInt32(0),
                            UUID = reader.GetGuid(1),
                            RFCEmisor = reader.GetString(2),
                            RFCReceptor = reader.GetString(3),
                            Total = reader.IsDBNull(4) ? (decimal?)null : reader.GetDecimal(4),
                            EstatusSat = reader.GetString(5),
                            TipoComprobante = reader.IsDBNull(6) ? null : reader.GetString(6)
                        });
            }
            return resultado;
        }

        // Solo lectura -- para --verificar-estatus (CLI) y para refrescos masivos desde la UI.
        // incluirArchivados=false por default (igual que ObtenerCfdiRecientes): archivar significa
        // "no me interesa seguir viendo esto", no tiene caso gastar la llamada en refrescarlo.
        public static List<CfdiDetalleFila> ObtenerTodosLosCfdiParaVerificar(SqlConnection conn, bool incluirArchivados = false)
        {
            string sql = @"
SELECT CfdiID, UUID, RFCEmisor, RFCReceptor, Total, EstatusSat, TipoComprobante
FROM CfdiRecibido
WHERE (@IncluirArchivados = 1 OR Archivado = 0);";

            var resultado = new List<CfdiDetalleFila>();
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@IncluirArchivados", incluirArchivados);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        resultado.Add(new CfdiDetalleFila
                        {
                            CfdiID = reader.GetInt32(0),
                            UUID = reader.GetGuid(1),
                            RFCEmisor = reader.GetString(2),
                            RFCReceptor = reader.GetString(3),
                            Total = reader.IsDBNull(4) ? (decimal?)null : reader.GetDecimal(4),
                            EstatusSat = reader.GetString(5),
                            TipoComprobante = reader.IsDBNull(6) ? null : reader.GetString(6)
                        });
                    }
                }
            }
            return resultado;
        }

        // El servicio publico de verificacion indexa por el Total ORIGINAL que el SAT timbro, no
        // por ningun valor "corregido" para mostrar en pantalla. Para CFDI tipo Pago (P) ese
        // original es SIEMPRE 0 (el monto real vive en el complemento -- ver bug #15 en
        // DOCUMENTACION.md, donde CfdiRecibido.Total se sobreescribe con MontoTotalPagos para que
        // la UI muestre algo util). Usar el Total mostrado en vez de 0 para un CFDI tipo P hace
        // que el SAT regrese "No Encontrado" -- visto en vivo, hubo que revertirlo.
        public static decimal TotalParaVerificarEstatus(CfdiDetalleFila c) =>
            string.Equals(c.TipoComprobante, "P", StringComparison.OrdinalIgnoreCase) ? 0m : (c.Total ?? 0m);

        // ===== Catalogo de empresas =====

        public static List<EmpresaFila> ObtenerEmpresas(SqlConnection conn)
        {
            const string sql = @"
SELECT EmpresaID, Nombre, RFC, RutaCer, RutaKey, PasswordCifrada, Activa,
       CarpetaXml, EstructuraCarpetas, PlantillaNombreArchivo,
       ComercialCarpetaXmlRecibidos, ComercialCarpetaXmlEmitidos, ComercialConexionSql,
       AnioInicioDescargas
FROM Empresa
ORDER BY Nombre;";

            var resultado = new List<EmpresaFila>();
            using (var cmd = new SqlCommand(sql, conn))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    resultado.Add(new EmpresaFila
                    {
                        EmpresaID = reader.GetInt32(0),
                        Nombre = reader.GetString(1),
                        RFC = reader.GetString(2),
                        RutaCer = reader.GetString(3),
                        RutaKey = reader.GetString(4),
                        PasswordCifrada = (byte[])reader[5],
                        Activa = reader.GetBoolean(6),
                        CarpetaXml = reader.IsDBNull(7) ? null : reader.GetString(7),
                        EstructuraCarpetas = reader.GetString(8),
                        PlantillaNombreArchivo = reader.GetString(9),
                        ComercialCarpetaXmlRecibidos = reader.IsDBNull(10) ? null : reader.GetString(10),
                        ComercialCarpetaXmlEmitidos = reader.IsDBNull(11) ? null : reader.GetString(11),
                        ComercialConexionSql = reader.IsDBNull(12) ? null : reader.GetString(12),
                        AnioInicioDescargas = reader.IsDBNull(13) ? (int?)null : reader.GetInt32(13)
                    });
                }
            }
            return resultado;
        }

        public static int GuardarEmpresaNueva(SqlConnection conn, string nombre, string rfc, string rutaCer, string rutaKey, byte[] passwordCifrada,
            string carpetaXml = null, string estructuraCarpetas = "AnioTipoMes", string plantillaNombreArchivo = "{UUID}",
            string comercialCarpetaXmlRecibidos = null, string comercialCarpetaXmlEmitidos = null, string comercialConexionSql = null,
            int? anioInicioDescargas = null)
        {
            const string sql = @"
INSERT INTO Empresa (Nombre, RFC, RutaCer, RutaKey, PasswordCifrada, CarpetaXml, EstructuraCarpetas, PlantillaNombreArchivo,
                      ComercialCarpetaXmlRecibidos, ComercialCarpetaXmlEmitidos, ComercialConexionSql, AnioInicioDescargas)
OUTPUT INSERTED.EmpresaID
VALUES (@Nombre, @Rfc, @RutaCer, @RutaKey, @Pwd, @CarpetaXml, @EstructuraCarpetas, @Plantilla,
        @ComercialRecibidos, @ComercialEmitidos, @ComercialConexionSql, @AnioInicio);";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Nombre", nombre);
                cmd.Parameters.AddWithValue("@Rfc", rfc);
                cmd.Parameters.AddWithValue("@RutaCer", rutaCer);
                cmd.Parameters.AddWithValue("@RutaKey", rutaKey);
                cmd.Parameters.AddWithValue("@Pwd", passwordCifrada);
                cmd.Parameters.AddWithValue("@CarpetaXml", (object)carpetaXml ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@EstructuraCarpetas", estructuraCarpetas);
                cmd.Parameters.AddWithValue("@Plantilla", plantillaNombreArchivo);
                cmd.Parameters.AddWithValue("@ComercialRecibidos", (object)comercialCarpetaXmlRecibidos ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ComercialEmitidos", (object)comercialCarpetaXmlEmitidos ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ComercialConexionSql", (object)comercialConexionSql ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@AnioInicio", (object)anioInicioDescargas ?? DBNull.Value);
                return (int)cmd.ExecuteScalar();
            }
        }

        // passwordCifrada=null deja la contraseña actual sin cambios (la UI no puede desencriptar
        // para "mostrarla" al editar, asi que un campo en blanco significa "no tocar").
        public static void ActualizarEmpresa(SqlConnection conn, int empresaId, string nombre, string rutaCer, string rutaKey,
            byte[] passwordCifrada, string carpetaXml, string estructuraCarpetas, string plantillaNombreArchivo,
            string comercialCarpetaXmlRecibidos, string comercialCarpetaXmlEmitidos, string comercialConexionSql)
        {
            string sql = @"
UPDATE Empresa
SET Nombre = @Nombre, RutaCer = @RutaCer, RutaKey = @RutaKey,
    CarpetaXml = @CarpetaXml, EstructuraCarpetas = @EstructuraCarpetas, PlantillaNombreArchivo = @Plantilla,
    ComercialCarpetaXmlRecibidos = @ComercialRecibidos, ComercialCarpetaXmlEmitidos = @ComercialEmitidos,
    ComercialConexionSql = @ComercialConexionSql"
                + (passwordCifrada != null ? ", PasswordCifrada = @Pwd" : "") + @"
WHERE EmpresaID = @Id;";

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Nombre", nombre);
                cmd.Parameters.AddWithValue("@RutaCer", rutaCer);
                cmd.Parameters.AddWithValue("@RutaKey", rutaKey);
                cmd.Parameters.AddWithValue("@CarpetaXml", (object)carpetaXml ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@EstructuraCarpetas", estructuraCarpetas);
                cmd.Parameters.AddWithValue("@Plantilla", plantillaNombreArchivo);
                cmd.Parameters.AddWithValue("@ComercialRecibidos", (object)comercialCarpetaXmlRecibidos ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ComercialEmitidos", (object)comercialCarpetaXmlEmitidos ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ComercialConexionSql", (object)comercialConexionSql ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Id", empresaId);
                if (passwordCifrada != null) cmd.Parameters.AddWithValue("@Pwd", passwordCifrada);
                cmd.ExecuteNonQuery();
            }
        }

        public static void EliminarEmpresa(SqlConnection conn, int empresaId)
        {
            using (var cmd = new SqlCommand("DELETE FROM Empresa WHERE EmpresaID=@Id", conn))
            {
                cmd.Parameters.AddWithValue("@Id", empresaId);
                cmd.ExecuteNonQuery();
            }
        }
    }
}
