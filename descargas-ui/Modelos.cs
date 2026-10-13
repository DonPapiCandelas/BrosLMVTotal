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

// Modelos.cs -- filas de la interfaz (ya con formato listo para mostrar; las consultas a la base regresan tipos crudos).

using System;

namespace BrosLMV.DescargasUI
{
    // Filas de UI (formato ya listo para mostrar -- las consultas a BD regresan tipos crudos).
    // OJO: deben ser PROPIEDADES, no campos -- el binding de WPF ({Binding Nombre}) solo
    // resuelve propiedades por reflexion; con campos publicos simplemente no encuentra nada
    // y la celda queda en blanco SIN error visible (bug real encontrado en vivo).
    public sealed class EmpresaRow
    {
        public int EmpresaID { get; set; }
        public string Nombre { get; set; }
        public string RFC { get; set; }
        public string RutaCer { get; set; }
        public string RutaKey { get; set; }
        public byte[] PasswordCifrada { get; set; }
        public int TotalCfdi { get; set; }
        public string UltimoEstatus { get; set; }
        public string CarpetaXml { get; set; }
        public string EstructuraCarpetas { get; set; }
        public string PlantillaNombreArchivo { get; set; }
        public string ComercialCarpetaXmlRecibidos { get; set; }
        public string ComercialCarpetaXmlEmitidos { get; set; }
        public string ComercialConexionSql { get; set; }
        public bool OcultarNominas { get; set; }
    }
    public sealed class SolicitudRow
    {
        public string IdSolicitud { get; set; }
        public string IdSolicitudCorto => string.IsNullOrEmpty(IdSolicitud) ? "-" :
            (IdSolicitud.StartsWith("RECHAZO-", StringComparison.OrdinalIgnoreCase) ? "Rechazo inmediato" :
            (IdSolicitud.Length > 16 ? IdSolicitud.Substring(0, 8) + "…" + IdSolicitud.Substring(IdSolicitud.Length - 4) : IdSolicitud));
        public string Tipo { get; set; }
        public string TipoSolicitud { get; set; } // CFDI / Metadata
        public string Rango { get; set; }
        public string EstatusSat { get; set; } // el Estatus crudo de la SOLICITUD, tal cual lo reporta el SAT
        public string ErrorDescarga { get; set; } // motivo real si algun paquete agoto sus 2 descargas sin exito
        public string Fecha { get; set; }
        public DateTime FechaSolicitudUtc { get; set; }
        public string NumeroCFDIs { get; set; }

        // El SAT no da ninguna barra de progreso -- lo unico que la app puede mostrar mientras
        // esta Aceptada/EnProceso es cuanto tiempo lleva esperando, para que quede claro que SI
        // se esta rastreando (no es que la app se haya quedado pasmada). Pedido directo del
        // usuario (2026-08-14): "no sabe si funcionara o no porque esta con estatus aceptada".
        public string TiempoTranscurrido
        {
            get
            {
                if (EstatusSat != "Aceptada" && EstatusSat != "EnProceso") return "-";
                var transcurrido = DateTime.UtcNow - FechaSolicitudUtc;
                if (transcurrido.TotalHours >= 1) return (int)transcurrido.TotalHours + "h " + transcurrido.Minutes + "min";
                return (int)transcurrido.TotalMinutes + " min";
            }
        }

        // "Terminada" en EstatusSat solo dice que el SAT ya proceso la SOLICITUD -- no que la
        // DESCARGA del paquete haya funcionado. Un paquete puede agotar sus 2 intentos sin traer
        // nunca los datos (visto en vivo: CodEstatus=5008), y sin este distingo la UI mostraba
        // "Terminada" con un numero de CFDIs que en realidad nunca llegaron a la base -- confuso
        // y enganoso, reportado directo por el usuario.
        public string Estatus => ErrorDescarga != null ? "Error de descarga" : EstatusSat;

        // Agrupa el Estatus mostrado en las 3 categorias que pide la pestana de filtro.
        public string EstatusCategoria =>
            ErrorDescarga != null ? "Rechazada" :
            EstatusSat == "Aceptada" || EstatusSat == "EnProceso" ? "Pendiente" :
            EstatusSat == "Terminada" ? "Terminada" :
            "Rechazada";
    }
    public sealed class CfdiRow
    {
        public bool Seleccionado { get; set; }
        public string RutaArchivoXml { get; set; }
        public int CfdiID { get; set; }
        public string UUID { get; set; }
        public string RFCEmisor { get; set; }
        public string NombreEmisor { get; set; }
        public string RFCReceptor { get; set; }
        // Contraparte = quien no es la propia empresa -- el proveedor si se ve la pestana
        // Recibidos, o el cliente si se ve Emitidos. Ver MainWindow.CargarDetalle.
        public string Contraparte { get; set; }
        public string NombreContraparte { get; set; }
        public string TipoComprobante { get; set; }
        public string Serie { get; set; }
        public string Folio { get; set; }
        public string SerieFolio => string.IsNullOrWhiteSpace(Serie) ? (Folio ?? "-") : (string.IsNullOrWhiteSpace(Folio) ? Serie : $"{Serie}-{Folio}");
        public string FechaEmision { get; set; }
        public string Subtotal { get; set; }
        public string Descuento { get; set; }
        public string IVA { get; set; }
        public string Retenciones { get; set; }
        public string Total { get; set; }
        public string Moneda { get; set; }
        public string TipoCambio { get; set; }
        public string FormaPago { get; set; }
        public string MetodoPago { get; set; }
        public string EstatusSat { get; set; }
        public bool Archivado { get; set; }
        public string ArchivadoTexto => Archivado ? "Sí" : "";
        public DateTime? FechaSincronizadoComercial { get; set; }
        public string SincronizadoComercialTexto => FechaSincronizadoComercial.HasValue
            ? "Sincronizado (" + FechaSincronizadoComercial.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + ")"
            : "Sin sincronizar";
    }
}
