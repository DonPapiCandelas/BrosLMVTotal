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

                    // Sin costo de cupo (servicio publico del SAT) -- no hace falta amarrarlo a
                    // una hora fija del dia, basta con "mas o menos una vez al dia".
                    if ((ahora - _ultimoVerificar).TotalHours >= 20)
                    {
                        await EjecutarSeguro("verificar-estatus", () => Program.VerificarEstatusAsync(config.CadenaConexion));
                        _ultimoVerificar = ahora;
                    }
                }

                try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
                catch (TaskCanceledException) { }
            }

            Bitacora.Escribir("Servicio detenido.");
        }

        private async Task EjecutarSeguro(string nombre, Func<Task<int>> accion)
        {
            try { await accion(); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en {Nombre}", nombre);
                Bitacora.EscribirError("Servicio (" + nombre + "): " + ex.Message);
            }
        }
    }
}
