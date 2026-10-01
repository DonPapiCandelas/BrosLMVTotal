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

// Worker.cs -- loop de 1 minuto que dispara las 3 tareas que antes vivian en Tareas Programadas
// separadas. Los intervalos aqui son de CADENCIA (cada cuanto SE REVISA), no de correccion: la
// correccion real (no duplicar solicitudes, no gastar cupo de mas) ya vive adentro de
// AutoSolicitador/BrosSatDb via el estado guardado en la BD (ObtenerUltimaFechaCubierta,
// candados sp_getapplock) -- asi que reiniciar el servicio (Windows Update, etc.) y perder estos
// contadores en memoria es inofensivo, en el peor caso solo dispara una revision un poco antes
// de lo normal, nunca una solicitud real de mas.

using BrosLMV.Descargas.Cola;
using BrosLMV.Descargas.Datos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BrosLMV.Descargas.Servicio
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private DateTime _ultimoAutoTodas = DateTime.MinValue;
        private DateTime _ultimoSolicitar = DateTime.MinValue;
        private DateTime _ultimoVerificar = DateTime.MinValue;
        private DateTime _ultimoLatido = DateTime.MinValue;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("BrosLMV Descargas (servicio) iniciado.");
            Bitacora.Escribir("Servicio iniciado.");

            while (!stoppingToken.IsCancellationRequested)
            {
                // TODO el cuerpo del ciclo va protegido: una excepcion fuera de EjecutarSeguro (por
                // ejemplo leer la configuracion, o la propia bitacora si el archivo esta bloqueado)
                // tumbaba el servicio entero, que se quedaba detenido hasta que alguien lo
                // reiniciaba a mano ("deja de descargar hasta que lo vuelvo a abrir").
                try
                {
                await CicloAsync();
                }
                catch (Exception ex)
                {
                    try { _logger.LogError(ex, "Error en el ciclo del servicio"); } catch { }
                    try { Bitacora.EscribirError("Servicio (ciclo): " + ex.Message); } catch { }
                }

                try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
                catch (TaskCanceledException) { }
            }

            try { Bitacora.Escribir("Servicio detenido."); } catch { }
        }

        private async Task CicloAsync()
        {
            {
                // Latido: una linea por hora en la bitacora, para saber que el servicio sigue vivo
                // aunque no haya nada que descargar (y, si la linea deja de aparecer, desde cuando).
                if ((DateTime.UtcNow - _ultimoLatido).TotalMinutes >= 60)
                {
                    Bitacora.Escribir("Latido: servicio activo.");
                    _ultimoLatido = DateTime.UtcNow;
                }

                var config = ConfigServicio.Cargar();
                if (config == null || string.IsNullOrWhiteSpace(config.CadenaConexion))
                {
                    Bitacora.EscribirError("Servicio: sin configuracion (" + ConfigServicio.RutaArchivo + " no existe o esta incompleto) -- nada que hacer.");
                }
                else
                {
                    var ahora = DateTime.UtcNow;

                    if ((ahora - _ultimoAutoTodas).TotalMinutes >= 10)
                    {
                        await EjecutarSeguro("auto-todas", () => Program.AutoTodasAsync(config.CadenaConexion));
                        _ultimoAutoTodas = ahora;
                    }

                    int intervaloSolicitar = Math.Max(15, config.IntervaloSolicitarMinutos);
                    if ((ahora - _ultimoSolicitar).TotalMinutes >= intervaloSolicitar)
                    {
                        await EjecutarSeguro("auto-solicitar-todas", () => Program.AutoSolicitarTodasAsync(config.CadenaConexion));
                        _ultimoSolicitar = ahora;
                    }

                    // Sin costo de cupo (servicio publico del SAT). Cada pasada revisa los CFDI con la
                    // revision mas vieja (los de los ultimos 120 dias cada 6 h, los demas cada 24 h,
                    // ver BrosSatDb.ObtenerCfdiParaVerificar), asi una cancelacion se detecta en
                    // horas y no hasta la corrida del dia siguiente.
                    if ((ahora - _ultimoVerificar).TotalMinutes >= 30)
                    {
                        await EjecutarSeguro("verificar-estatus", () => Program.VerificarEstatusAsync(config.CadenaConexion));
                        _ultimoVerificar = ahora;
                    }
                }

            }
        }

        private async Task EjecutarSeguro(string nombre, Func<Task<int>> accion)
        {
            try
            {
                var tarea = accion();
                // Vigilante: una pasada que se queda colgada (red, SQL) detenia todo el ciclo sin dar
                // error. Si pasa de 40 min se termina el proceso con codigo de error y Windows lo
                // reinicia solo (failureflag + restart configurados al instalar el servicio).
                if (await Task.WhenAny(tarea, Task.Delay(TimeSpan.FromMinutes(40))) != tarea)
                {
                    try { Bitacora.EscribirError("Servicio (" + nombre + "): la pasada lleva mas de 40 min sin terminar; se reinicia el servicio."); } catch { }
                    Environment.Exit(1);
                }
                await tarea;
            }
            catch (Exception ex)
            {
                try { _logger.LogError(ex, "Error en {Nombre}", nombre); } catch { }
                try { Bitacora.EscribirError("Servicio (" + nombre + "): " + ex.Message); } catch { }
            }
        }
    }
}
