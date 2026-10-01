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
                    // Se pide el PRIMER HUECO del mapa de dias cubiertos, no "lo que sigue despues de la
                    // ultima fecha": si el dia 17 fallo y el 19 salio bien, el 17 sigue sin cubrir y se
                    // vuelve a pedir (antes se quedaba perdido para siempre). Una solicitud que el SAT
                    // acepto pero nunca termino en 3 dias deja de esperarse y su rango se reabre.
                    int vencidas = BrosSatDb.VencerSolicitudesAtoradas(conn, empresa.RFC, 3);
                    if (vencidas > 0) Bitacora.Escribir("  [" + tipoRecEmi + "] " + vencidas + " solicitud(es) sin resultado tras 3 dias -> Vencida, su rango se vuelve a pedir.");

                    DateTime inicioHistorico = empresa.AnioInicioDescargas.HasValue
                        ? new DateTime(empresa.AnioInicioDescargas.Value, 1, 1)
                        : DateTime.Today.AddDays(-90);
                    // Un hueco que el SAT rechazo hace menos de 6 h se deja para despues y se sigue con el
                    // SIGUIENTE (antes un rechazo -p. ej. 5002 en enero- detenia toda la direccion y los demas
                    // meses nunca se pedian).
                    var rangosCubiertos = BrosSatDb.ObtenerRangosCubiertos(conn, empresa.RFC, "CFDI", tipoRecEmi);
                    rangosCubiertos.AddRange(BrosSatDb.ObtenerRangosRechazadosRecientes(conn, empresa.RFC, "CFDI", tipoRecEmi, 6));
                    var hueco = Huecos.PrimerHueco(rangosCubiertos, inicioHistorico, hasta);
                    DateTime desde = hueco?.Desde ?? hasta.AddDays(1);
                    var cubierta = hueco.HasValue ? (DateTime?)null : hasta;

                    bool hayTramoPendiente = hueco.HasValue;

                    // Reintento retroactivo -- pedido explicito del usuario 2026-08-19: "es
                    // posible timbrar una factura poniendo una fecha de 72 horas atras", asi que
                    // un CFDI que se timbra tarde dentro de esa ventana NUNCA se vuelve a pedir
                    // solo una vez que "cubierta" ya avanzo mas alla de su Fecha de emision. Cada
                    // ~24h se vuelve a pedir la ultima semana COMPLETA, sin importar que ya este
                    // marcada como cubierta -- es seguro repetir el rango porque el guardado es
                    // idempotente por UUID (ver BrosSatDb.ObtenerFechaUltimaSolicitudRetroactiva).
                    var ultimaRetroactiva = BrosSatDb.ObtenerFechaUltimaSolicitudRetroactiva(conn, empresa.RFC, tipoRecEmi);
                    bool pedirRetroactivo = !ultimaRetroactiva.HasValue || (DateTime.UtcNow - ultimaRetroactiva.Value).TotalHours >= 20;

                    if (!hayTramoPendiente && !pedirRetroactivo && !BarridoToca(conn, empresa, tipoRecEmi))
                    {
                        Bitacora.Escribir("  [" + tipoRecEmi + "] Ya al dia (cubierto hasta " + (cubierta?.ToString("yyyy-MM-dd") ?? "-") + "), nada que solicitar.");
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

                        // Sin tope de "un tramo por pasada": se piden TODOS los huecos (hasta MaxTramosPorPasada = 36 meses)
                        // en la misma pasada, el mas antiguo primero. El cupo del SAT no es una preocupacion
                        // aqui: lo que importa es que no quede ningun CFDI sin descargar.
                        int tramosPedidos = 0;
                        for (int vuelta = 0; hayTramoPendiente && vuelta < MaxTramosPorPasada; vuelta++)
                        {
                            var tramo = SolicitudChunker.PartirEnMeses(desde, hueco.Value.Hasta).First();
                            int intentosPrevios = BrosSatDb.ContarSolicitudesDelRango(conn, empresa.RFC, "CFDI", tipoRecEmi, tramo.Desde, tramo.Hasta);
                            if (intentosPrevios > 0)
                            {
                                // Mismos parametros que una solicitud anterior: el SAT los limita (5002). Se amplia el
                                // inicio un dia por intento previo (se traslapa con dias ya bajados; el guardado es
                                // idempotente por UUID) para que los parametros sean distintos.
                                var ampliado = tramo.Desde.AddDays(-Math.Min(intentosPrevios, 10));
                                if (ampliado < inicioHistorico.AddYears(-1)) ampliado = tramo.Desde;
                                tramo = (ampliado, tramo.Hasta);
                            }

                            // Metadata es el UNICO canal del SAT que informa CFDI cancelados desde su origen
                            // (TipoSolicitud=CFDI solo acepta EstadoComprobante=Vigente). Se creia atorado
                            // (13 de 13 intentos, 2026-08-19) pero era el bug 18 (estado "0" que sacaba la
                            // solicitud de la cola), asi que se pide con CADA tramo cuyo rango aun no tenga
                            // Metadata en curso o terminada.
                            var rangosMetadata = BrosSatDb.ObtenerRangosCubiertos(conn, empresa.RFC, "Metadata", tipoRecEmi);
                            bool pedirMetadata = Huecos.PrimerHueco(rangosMetadata, tramo.Desde, tramo.Hasta).HasValue;

                            Bitacora.Escribir("  [" + tipoRecEmi + "] Pendiente desde " + desde.ToString("yyyy-MM-dd") + " -- pidiendo tramo " + tramo.Desde.ToString("yyyy-MM-dd") + " a " + tramo.Hasta.ToString("yyyy-MM-dd") +
                                (pedirMetadata ? " (CFDI + Metadata)..." : " (solo CFDI -- su Metadata ya esta pedida)..."));

                            var solicCfdi = await SatSoapClient.SolicitarDescargaAsync(
                                cert, llave, auth.Token, rfcSolicitante: empresa.RFC, rfcEmisor: rfcEmisor, rfcReceptor: rfcReceptor,
                                desde: tramo.Desde, hasta: tramo.Hasta, tipoSolicitud: "CFDI");
                            // 5002 = el SAT ya recibio demasiadas veces ESTOS mismos parametros (aunque las haya
                            // hecho otro sistema con el mismo RFC). No se espera: se repite de inmediato con el
                            // inicio un dia antes (rango distinto, mismo contenido + un dia de traslape), hasta 5 veces.
                            for (int ensanche = 1; !solicCfdi.Exito && solicCfdi.CodEstatus == "5002" && ensanche <= 5; ensanche++)
                            {
                                BrosSatDb.RegistrarIntentoFallido(conn, empresa.RFC, tipoRecEmi, tramo.Desde, tramo.Hasta, "Automatica", "CFDI", solicCfdi.CodEstatus, solicCfdi.Mensaje);
                                tramo = (tramo.Desde.AddDays(-1), tramo.Hasta);
                                Bitacora.Escribir("  [" + tipoRecEmi + "] 5002 (parametros repetidos); se reintenta con el inicio en " + tramo.Desde.ToString("yyyy-MM-dd") + "...");
                                solicCfdi = await SatSoapClient.SolicitarDescargaAsync(
                                    cert, llave, auth.Token, rfcSolicitante: empresa.RFC, rfcEmisor: rfcEmisor, rfcReceptor: rfcReceptor,
                                    desde: tramo.Desde, hasta: tramo.Hasta, tipoSolicitud: "CFDI");
                            }
                            if (!solicCfdi.Exito)
                            {
                                BrosSatDb.RegistrarIntentoFallido(conn, empresa.RFC, tipoRecEmi, tramo.Desde, tramo.Hasta, "Automatica", "CFDI", solicCfdi.CodEstatus, solicCfdi.Mensaje);
                                errores++;
                                Bitacora.EscribirError("  [" + tipoRecEmi + "] SAT rechazo " + tramo.Desde.ToString("yyyy-MM-dd") + " a " + tramo.Hasta.ToString("yyyy-MM-dd") + ": " + solicCfdi.Error + " -- se reintenta en 6 h (con el rango ensanchado) y se sigue con el siguiente tramo.");
                                rangosCubiertos = BrosSatDb.ObtenerRangosCubiertos(conn, empresa.RFC, "CFDI", tipoRecEmi);
                                rangosCubiertos.AddRange(BrosSatDb.ObtenerRangosRechazadosRecientes(conn, empresa.RFC, "CFDI", tipoRecEmi, 6));
                                hueco = Huecos.PrimerHueco(rangosCubiertos, inicioHistorico, hasta);
                                desde = hueco?.Desde ?? hasta.AddDays(1);
                                hayTramoPendiente = hueco.HasValue;
                                continue;
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
                            tramosPedidos++;

                            // La solicitud recien registrada ya cuenta como cubierta (Aceptada): se busca el
                            // siguiente hueco para pedirlo en esta misma pasada.
                            rangosCubiertos = BrosSatDb.ObtenerRangosCubiertos(conn, empresa.RFC, "CFDI", tipoRecEmi);
                            rangosCubiertos.AddRange(BrosSatDb.ObtenerRangosRechazadosRecientes(conn, empresa.RFC, "CFDI", tipoRecEmi, 6));
                            hueco = Huecos.PrimerHueco(rangosCubiertos, inicioHistorico, hasta);
                            desde = hueco?.Desde ?? hasta.AddDays(1);
                            hayTramoPendiente = hueco.HasValue;
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

                        // Barrido de verificacion: una vez por semana (en fin de semana, o si nunca se ha
                        // hecho) se vuelven a pedir los ultimos MesesBarrido meses COMPLETOS, para asegurar
                        // que no falte ningun XML aunque una solicitud anterior haya salido "Terminada"
                        // incompleta. Es seguro repetirlo (guardado idempotente por UUID).
                        // (no en la misma pasada que acaba de pedir huecos: seria repetir los mismos meses al instante)
                        if (tramosPedidos == 0) await BarridoAsync(conn, empresa, tipoRecEmi, cert, llave, auth.Token, rfcEmisor, rfcReceptor, hasta);

                        // Verificacion contra Metadata: si el SAT informo (Metadata) CFDI vigentes que NO tenemos
                        // como XML, el mes completo se vuelve a pedir ya mismo (no espera al barrido semanal).
                        await FaltantesAsync(conn, empresa, tipoRecEmi, cert, llave, auth.Token, rfcEmisor, rfcReceptor, hasta);
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

        // Cuantos meses hacia atras revisa el barrido semanal (mes actual incluido).
        private const int MesesBarrido = 12;
        private const int MaxTramosPorPasada = 36;

        // Meses con CFDI vigentes que el SAT reporta (Metadata) y que no tenemos descargados. Se pide de
        // nuevo el mes completo (a lo mas cada 6 h por mes) ampliando el inicio un dia por intento previo.
        private static async Task FaltantesAsync(SqlConnection conn, EmpresaFila empresa, string tipoRecEmi,
            System.Security.Cryptography.X509Certificates.X509Certificate2 cert, System.Security.Cryptography.RSA llave,
            string token, string rfcEmisor, string rfcReceptor, DateTime hasta)
        {
            var meses = BrosSatDb.ObtenerMesesConFaltantes(conn, empresa.RFC, tipoRecEmi);
            foreach (var (anio, mes, faltan) in meses)
            {
                var primero = new DateTime(anio, mes, 1);
                var fin = primero.AddMonths(1).AddDays(-1).AddHours(23).AddMinutes(59).AddSeconds(59);
                if (fin > hasta.AddHours(23).AddMinutes(59).AddSeconds(59)) fin = hasta.AddHours(23).AddMinutes(59).AddSeconds(59);
                if (primero > fin) continue;

                var ultimo = BrosSatDb.ObtenerFechaUltimaSolicitudDe(conn, empresa.RFC, tipoRecEmi, "CFDI", "Faltantes");
                if (ultimo.HasValue && (DateTime.UtcNow - ultimo.Value).TotalHours < 6) { Bitacora.Escribir("  [" + tipoRecEmi + "] " + faltan + " CFDI faltan en " + anio + "-" + mes.ToString("00") + " (reintento reciente, se espera)."); return; }

                int previos = BrosSatDb.ContarSolicitudesDelRango(conn, empresa.RFC, "CFDI", tipoRecEmi, primero, fin);
                var desde = primero.AddDays(-Math.Min(previos, 10));
                Bitacora.Escribir("  [" + tipoRecEmi + "] Faltan " + faltan + " CFDI vigentes de " + anio + "-" + mes.ToString("00") + " segun Metadata; se pide el mes completo " + desde.ToString("yyyy-MM-dd") + " a " + fin.ToString("yyyy-MM-dd") + "...");
                var solic = await SatSoapClient.SolicitarDescargaAsync(cert, llave, token, rfcSolicitante: empresa.RFC, rfcEmisor: rfcEmisor, rfcReceptor: rfcReceptor,
                    desde: desde, hasta: fin, tipoSolicitud: "CFDI");
                if (!solic.Exito)
                {
                    BrosSatDb.RegistrarIntentoFallido(conn, empresa.RFC, tipoRecEmi, desde, fin, "Faltantes", "CFDI", solic.CodEstatus, solic.Mensaje);
                    Bitacora.EscribirError("  [" + tipoRecEmi + "] Faltantes rechazado por el SAT: " + solic.Error);
                    return;
                }
                BrosSatDb.RegistrarSolicitud(conn, solic.IdSolicitud, empresa.RFC, tipoRecEmi, desde, fin, "Faltantes", "CFDI");
                return; // un mes por pasada: el siguiente se revisa cuando termine este
            }
        }

        // Toca barrido si nunca se ha hecho, si ya pasaron 6 dias y es fin de semana, o si ya pasaron
        // 9 dias (para que un servidor que estuvo apagado el fin de semana no se salte la semana).
        private static bool BarridoToca(SqlConnection conn, EmpresaFila empresa, string tipoRecEmi)
        {
            var ultimo = BrosSatDb.ObtenerFechaUltimaSolicitudDe(conn, empresa.RFC, tipoRecEmi, "CFDI", "Barrido");
            if (!ultimo.HasValue) return true;
            double dias = (DateTime.UtcNow - ultimo.Value).TotalDays;
            bool finDeSemana = DateTime.Today.DayOfWeek == DayOfWeek.Saturday || DateTime.Today.DayOfWeek == DayOfWeek.Sunday;
            return (dias >= 6 && finDeSemana) || dias >= 9;
        }

        private static async Task BarridoAsync(SqlConnection conn, EmpresaFila empresa, string tipoRecEmi,
            System.Security.Cryptography.X509Certificates.X509Certificate2 cert, System.Security.Cryptography.RSA llave,
            string token, string rfcEmisor, string rfcReceptor, DateTime hasta)
        {
            if (!BarridoToca(conn, empresa, tipoRecEmi)) return;

            for (int m = 0; m < MesesBarrido; m++)
            {
                var primero = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-m);
                var fin = primero.AddMonths(1).AddDays(-1).AddHours(23).AddMinutes(59).AddSeconds(59);
                if (fin > hasta.AddHours(23).AddMinutes(59).AddSeconds(59)) fin = hasta.AddHours(23).AddMinutes(59).AddSeconds(59);
                if (primero > fin) continue;

                // Cada barrido del mismo mes amplia el inicio un dia mas (maximo 10) para que los
                // parametros sean distintos y el SAT no los cuente como la misma solicitud (5002).
                int previos = BrosSatDb.ContarSolicitudesDelRango(conn, empresa.RFC, "CFDI", tipoRecEmi, primero, fin);
                var desde = primero.AddDays(-Math.Min(previos, 10));
                Bitacora.Escribir("  [" + tipoRecEmi + "] Barrido: " + desde.ToString("yyyy-MM-dd") + " a " + fin.ToString("yyyy-MM-dd") + " (verificar que no falte ningun XML)...");
                var solic = await SatSoapClient.SolicitarDescargaAsync(cert, llave, token, rfcSolicitante: empresa.RFC, rfcEmisor: rfcEmisor, rfcReceptor: rfcReceptor,
                    desde: desde, hasta: fin, tipoSolicitud: "CFDI");
                if (!solic.Exito)
                {
                    BrosSatDb.RegistrarIntentoFallido(conn, empresa.RFC, tipoRecEmi, desde, fin, "Barrido", "CFDI", solic.CodEstatus, solic.Mensaje);
                    Bitacora.EscribirError("  [" + tipoRecEmi + "] Barrido rechazado por el SAT: " + solic.Error);
                    return;
                }
                BrosSatDb.RegistrarSolicitud(conn, solic.IdSolicitud, empresa.RFC, tipoRecEmi, desde, fin, "Barrido", "CFDI");
            }
        }
    }

    // Calculo de huecos sobre un conjunto de rangos ya cubiertos.
    internal static class Huecos
    {
        // Primer tramo de dias SIN cubrir dentro de [desde, hasta] (fechas de calendario), o null si
        // todo el periodo esta cubierto. Un rango cubierto tapa desde el dia de su FechaInicial hasta
        // el dia de su FechaFinal, ambos incluidos.
        // Todos los tramos sin cubrir dentro de [desde, hasta], en orden.
        public static System.Collections.Generic.List<(DateTime Desde, DateTime Hasta)> Todos(System.Collections.Generic.List<(DateTime Desde, DateTime Hasta)> cubiertos, DateTime desde, DateTime hasta)
        {
            var lista = new System.Collections.Generic.List<(DateTime, DateTime)>();
            var extra = new System.Collections.Generic.List<(DateTime Desde, DateTime Hasta)>(cubiertos);
            DateTime d1 = hasta.Date;
            for (int i = 0; i < 2000; i++)
            {
                var h = PrimerHueco(extra, desde, hasta);
                if (!h.HasValue) break;
                lista.Add((h.Value.Desde, h.Value.Hasta));
                extra.Add((h.Value.Desde, h.Value.Hasta));
            }
            return lista;
        }

        public static (DateTime Desde, DateTime Hasta)? PrimerHueco(System.Collections.Generic.List<(DateTime Desde, DateTime Hasta)> cubiertos, DateTime desde, DateTime hasta)
        {
            DateTime d0 = desde.Date, d1 = hasta.Date;
            if (d0 > d1) return null;
            var ordenados = cubiertos.Select(r => (Desde: r.Desde.Date, Hasta: r.Hasta.Date)).OrderBy(r => r.Desde).ToList();
            DateTime cursor = d0;
            foreach (var r in ordenados)
            {
                if (r.Hasta < cursor) continue;
                if (r.Desde > cursor) return (cursor, (r.Desde.AddDays(-1) < d1 ? r.Desde.AddDays(-1) : d1));
                cursor = r.Hasta.AddDays(1);
                if (cursor > d1) return null;
            }
            return cursor <= d1 ? ((DateTime, DateTime)?)(cursor, d1) : null;
        }
    }
}
