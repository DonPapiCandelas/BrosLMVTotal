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

// AutoSolicitador.cs -- pide el siguiente tramo pendiente (CFDI+Metadata, Recibidos+Emitidos)
// de UNA empresa. Extraido de Program.cs (AutoSolicitarTodasAsync) para poder reusarlo desde
// dos lugares sin duplicar la logica real de llamada al SAT: la Tarea Programada diaria (CLI,
// todas las empresas) y el alta de una empresa nueva desde la UI (pedido explicito del usuario
// 2026-08-19: "al instalar lo primero que haga sea realizar una descarga" -- dispara el primer
// tramo de inmediato en vez de esperar hasta la proxima corrida programada).
//
// SI gasta cupo diario real del SAT -- quien llame a esto debe saberlo.

using System;
using System.Linq;
using System.Threading.Tasks;
using BrosLMV.Descargas.Datos;
using BrosLMV.Descargas.Sat;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas.Cola
{
    internal static class AutoSolicitador
    {
        // Circuito de freno -- bug real encontrado 2026-09-07: un rechazo del SAT (ej.
        // CodEstatus=5002 "solicitudes de por vida agotadas") tronaba como excepcion SIN dejar
        // registro en la BD, asi que el ciclo automatico lo volvia a intentar en CADA corrida sin
        // limite -- se acumularon 205 rechazos reales antes de notarlo. A partir de este umbral de
        // rechazos SEGUIDOS para el mismo (RFC, direccion, TipoSolicitud), se deja de reintentar
        // solo -- hace falta forzarlo a mano ("Nueva solicitud" en la UI) una vez resuelto lo que
        // sea que este rechazando del lado del SAT.
        private const int UmbralRechazosConsecutivos = 3;

        // Si nunca se ha pedido nada de este RFC/direccion, arranca desde
        // empresa.AnioInicioDescargas (elegido por el usuario al dar de alta la empresa) o, si
        // no se eligio (empresas creadas antes de este campo), 90 dias atras como antes.
        public static async Task<int> SolicitarSiguienteTramoAsync(SqlConnection conn, EmpresaFila empresa)
        {
            // Incluye el dia de HOY, pero no antes de las 15:00 hora local (pedido explicito del
            // usuario 2026-08-19, ajustado el mismo dia tras confirmar en produccion real que el
            // SAT rechaza sistematicamente las solicitudes del dia en curso si se piden temprano
            // -- 4 ciclos seguidos de 06:00 a 12:00 salieron "Rechazada", gastando 8 solicitudes
            // de cupo diario en puros rechazos antes de que una se aceptara por la tarde). No es
            // una hora confirmada como "la correcta" del lado del SAT, es un umbral razonable
            // para reducir el desperdicio sin perder la ventaja de tener el dia actualizado antes
            // de que termine.
            var hasta = DateTime.Now.Hour >= 15 ? DateTime.Today : DateTime.Today.AddDays(-1);
            int errores = 0;

            // Recibidos (compras, RfcReceptor=empresa) y Emitidos (ventas, RfcEmisor=empresa) se
            // piden por separado -- cada uno con su propio avance de fecha cubierta (ver el
            // comentario en BrosSatDb.ObtenerUltimaFechaCubierta sobre por que hace falta filtrar
            // por Tipo ademas de TipoSolicitud).
            foreach (var tipoRecEmi in new[] { "Recibidos", "Emitidos" })
            {
                try
                {
                    var cubierta = BrosSatDb.ObtenerUltimaFechaCubierta(conn, empresa.RFC, "CFDI", tipoRecEmi);
                    DateTime desde;
                    if (cubierta.HasValue) desde = cubierta.Value.Date.AddDays(1);
                    else if (empresa.AnioInicioDescargas.HasValue) desde = new DateTime(empresa.AnioInicioDescargas.Value, 1, 1);
                    else desde = DateTime.Today.AddDays(-90);

                    bool hayTramoPendiente = desde <= hasta;

                    // Reintento retroactivo -- pedido explicito del usuario 2026-08-19: "es
                    // posible timbrar una factura poniendo una fecha de 72 horas atras", asi que
                    // un CFDI que se timbra tarde dentro de esa ventana NUNCA se vuelve a pedir
                    // solo una vez que "cubierta" ya avanzo mas alla de su Fecha de emision. Cada
                    // ~24h se vuelve a pedir la ultima semana COMPLETA, sin importar que ya este
                    // marcada como cubierta -- es seguro repetir el rango porque el guardado es
                    // idempotente por UUID (ver BrosSatDb.ObtenerFechaUltimaSolicitudRetroactiva).
                    var ultimaRetroactiva = BrosSatDb.ObtenerFechaUltimaSolicitudRetroactiva(conn, empresa.RFC, tipoRecEmi);
                    bool pedirRetroactivo = !ultimaRetroactiva.HasValue || (DateTime.UtcNow - ultimaRetroactiva.Value).TotalHours >= 20;

                    if (!hayTramoPendiente && !pedirRetroactivo)
                    {
                        Bitacora.Escribir("  [" + tipoRecEmi + "] Ya al dia (cubierto hasta " + (cubierta?.ToString("yyyy-MM-dd") ?? "-") + "), nada que solicitar.");
                        continue;
                    }

                    int rechazosSeguidosCfdi = BrosSatDb.ContarRechazosConsecutivos(conn, empresa.RFC, tipoRecEmi, "CFDI");
                    if (rechazosSeguidosCfdi >= UmbralRechazosConsecutivos)
                    {
                        Bitacora.EscribirError("  [" + tipoRecEmi + "] " + rechazosSeguidosCfdi + " rechazos seguidos del SAT (CFDI) -- pausando reintentos automaticos. Revisa el motivo (cupo agotado, etc) y usa \"Nueva solicitud\" para forzar a mano cuando este resuelto.");
                        continue;
                    }

                    string passwordEmpresa = DpapiHelper.Descifrar(empresa.PasswordCifrada);
                    var cert = SatFirmaXml.CargarFiel(empresa.RutaCer, empresa.RutaKey, passwordEmpresa, out var llave);
                    using (llave)
                    {
                        var auth = await SatSoapClient.AutenticarAsync(cert, llave);
                        if (!auth.Exito) throw new Exception("Autenticacion: " + auth.Error);

                        string rfcEmisor = tipoRecEmi == "Emitidos" ? empresa.RFC : null;
                        string rfcReceptor = tipoRecEmi == "Recibidos" ? empresa.RFC : null;

                        if (hayTramoPendiente)
                        {
                            var tramo = SolicitudChunker.PartirEnMeses(desde, hasta).First();

                            // Metadata se quedo atorado en 13 de 13 intentos reales (2h a 105h de
                            // espera, 0 completados -- confirmado 2026-08-19) -- ya no se pide en
                            // CADA corrida diaria como antes, solo si ya paso una semana desde el
                            // ultimo intento (sin importar si ese intento se completo o se quedo
                            // atorado). Es el UNICO canal del SAT que informa CFDI cancelados
                            // desde su origen (CFDI tipo "CFDI" solo acepta
                            // EstadoComprobante=Vigente), asi que no se apaga del todo.
                            var ultimaMetadata = BrosSatDb.ObtenerFechaUltimaSolicitudMetadata(conn, empresa.RFC, tipoRecEmi);
                            bool pedirMetadata = !ultimaMetadata.HasValue || (DateTime.UtcNow - ultimaMetadata.Value).TotalDays >= 7;

                            Bitacora.Escribir("  [" + tipoRecEmi + "] Pendiente desde " + desde.ToString("yyyy-MM-dd") + " -- pidiendo tramo " + tramo.Desde.ToString("yyyy-MM-dd") + " a " + tramo.Hasta.ToString("yyyy-MM-dd") +
                                (pedirMetadata ? " (CFDI + Metadata)..." : " (solo CFDI -- Metadata se reintenta semanal, ultimo intento " + (ultimaMetadata?.ToString("yyyy-MM-dd") ?? "-") + ")..."));

                            var solicCfdi = await SatSoapClient.SolicitarDescargaAsync(
                                cert, llave, auth.Token, rfcSolicitante: empresa.RFC, rfcEmisor: rfcEmisor, rfcReceptor: rfcReceptor,
                                desde: tramo.Desde, hasta: tramo.Hasta, tipoSolicitud: "CFDI");
                            if (!solicCfdi.Exito)
                            {
                                BrosSatDb.RegistrarIntentoFallido(conn, empresa.RFC, tipoRecEmi, tramo.Desde, tramo.Hasta, "Automatica", "CFDI", solicCfdi.CodEstatus, solicCfdi.Mensaje);
                                throw new Exception("SolicitaDescarga (CFDI): " + solicCfdi.Error);
                            }
                            BrosSatDb.RegistrarSolicitud(conn, solicCfdi.IdSolicitud, empresa.RFC, tipoRecEmi, tramo.Desde, tramo.Hasta, "Automatica", "CFDI");

                            if (pedirMetadata)
                            {
                                var solicMetadata = await SatSoapClient.SolicitarDescargaAsync(
                                    cert, llave, auth.Token, rfcSolicitante: empresa.RFC, rfcEmisor: rfcEmisor, rfcReceptor: rfcReceptor,
                                    desde: tramo.Desde, hasta: tramo.Hasta, tipoSolicitud: "Metadata");
                                if (!solicMetadata.Exito) throw new Exception("SolicitaDescarga (Metadata): " + solicMetadata.Error);
                                BrosSatDb.RegistrarSolicitud(conn, solicMetadata.IdSolicitud, empresa.RFC, tipoRecEmi, tramo.Desde, tramo.Hasta, "Automatica", "Metadata");
                            }
                            Bitacora.Escribir("  [" + tipoRecEmi + "] Solicitado.");
                        }

                        if (pedirRetroactivo)
                        {
                            var desdeRetro = hasta.AddDays(-6);
                            Bitacora.Escribir("  [" + tipoRecEmi + "] Retroactivo: repidiendo " + desdeRetro.ToString("yyyy-MM-dd") + " a " + hasta.ToString("yyyy-MM-dd") + " (por si hay XML timbrado tarde, hasta 72h despues de su Fecha)...");
                            var solicRetro = await SatSoapClient.SolicitarDescargaAsync(
                                cert, llave, auth.Token, rfcSolicitante: empresa.RFC, rfcEmisor: rfcEmisor, rfcReceptor: rfcReceptor,
                                desde: desdeRetro, hasta: hasta, tipoSolicitud: "CFDI");
                            if (!solicRetro.Exito)
                            {
                                BrosSatDb.RegistrarIntentoFallido(conn, empresa.RFC, tipoRecEmi, desdeRetro, hasta, "Retroactivo", "CFDI", solicRetro.CodEstatus, solicRetro.Mensaje);
                                throw new Exception("SolicitaDescarga (Retroactivo): " + solicRetro.Error);
                            }
                            BrosSatDb.RegistrarSolicitud(conn, solicRetro.IdSolicitud, empresa.RFC, tipoRecEmi, desdeRetro, hasta, "Retroactivo", "CFDI");
                            Bitacora.Escribir("  [" + tipoRecEmi + "] Retroactivo solicitado.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    errores++;
                    Bitacora.EscribirError("  [" + tipoRecEmi + "] ERROR: " + ex.Message);
                }
            }

            return errores;
        }
    }
}
