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

// App.xaml.cs -- candado de instancia unica. Ahora que cerrar la ventana la minimiza a la
// bandeja en vez de terminar el proceso (MainWindow.OnClosing), era facil terminar con varias
// copias corriendo sin darse cuenta (cada una con su propio icono en la bandeja -- justo lo que
// reporto el usuario 2026-08-19: "no se porque son 2"). Un Mutex con nombre fijo detecta si ya
// hay una instancia viva ANTES de crear cualquier ventana/icono.
//
// "Global\" (no un nombre simple): sin el prefijo, el Mutex vive en el namespace de la SESION de
// Terminal Services de quien lo crea -- confirmado real 2026-09-07, seguian apareciendo 2
// instancias porque una se abrio elevada (desde el instalador/UAC) y otra sin elevar, y el
// control de acceso por defecto del Mutex de la primera no dejaba a la segunda ni siquiera
// ABRIRLO para preguntar si ya existia (UnauthorizedAccessException), asi que cada una creaba el
// suyo por separado. "Global\" + MutexSecurity explicita (Everyone: Synchronize+Modify) asegura
// que cualquier instancia, elevada o no, vea la misma.
using System;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows;

namespace BrosLMV.DescargasUI
{
    public partial class App : Application
    {
        private const string NombreMutex = @"Global\BrosLMV.DescargasUI.InstanciaUnica";
        private const string NombreEventoMostrar = @"Global\BrosLMV.DescargasUI.Mostrar";
        private Mutex _mutexInstanciaUnica;
        private EventWaitHandle _eventoMostrar;

        // Lo usa el boton de Comercial: pide traer al frente esta ventana y, opcionalmente, abrir la
        // empresa con ese RFC. Se pasa por un archivo pequeno porque el evento no lleva datos.
        public static string RfcInicial { get; private set; } = "";
        private static string ArchivoPedido => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BrosLMV", "descargas_abrir.txt");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            for (int i = 0; i < e.Args.Length - 1; i++)
                if (string.Equals(e.Args[i], "--rfc", StringComparison.OrdinalIgnoreCase)) RfcInicial = (e.Args[i + 1] ?? "").Trim().ToUpperInvariant();

            // Una excepcion en cualquier manejador de la interfaz (p. ej. doble clic en una celda de
            // una tabla de reportes, 2026-10-01) cerraba TODA la aplicacion. Ahora se anota en la
            // bitacora y se avisa, y la ventana sigue abierta.
            DispatcherUnhandledException += (s, ex) =>
            {
                try { BrosLMV.Descargas.Cola.Bitacora.EscribirError("UI: " + ex.Exception); } catch { }
                try { MessageBox.Show("Ocurrio un error y la accion no se completo:\n\n" + ex.Exception.Message + "\n\nQuedo anotado en la bitacora.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Warning); } catch { }
                ex.Handled = true;
            };

            bool esNueva;
            try
            {
                var seguridad = new MutexSecurity();
                seguridad.AddAccessRule(new MutexAccessRule(
                    new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                    MutexRights.Synchronize | MutexRights.Modify, AccessControlType.Allow));
                _mutexInstanciaUnica = MutexAcl.Create(true, NombreMutex, out esNueva, seguridad);
            }
            catch
            {
                // Create pide mas derechos de los que concede la ACL abierta (probado 2026-10-12:
                // UnauthorizedAccessException en la segunda copia, que se creia la primera). Si el
                // mutex ya existe basta abrirlo solo para sincronizar; eso prueba que hay otra viva.
                esNueva = true;
                try
                {
                    using (MutexAcl.OpenExisting(NombreMutex, MutexRights.Synchronize)) { esNueva = false; }
                }
                catch { }
            }

            if (!esNueva)
            {
                // Ya hay una abierta (a veces solo en la bandeja): se le pide mostrarse en vez de avisar.
                bool avisada = false;
                try
                {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ArchivoPedido));
                    System.IO.File.WriteAllText(ArchivoPedido, RfcInicial);
                    using (var ev = EventWaitHandleAcl.OpenExisting(NombreEventoMostrar, EventWaitHandleRights.Modify))
                    { ev.Set(); avisada = true; }
                }
                catch { }
                if (!avisada)
                    MessageBox.Show("BrosLMV Descargas ya está corriendo — revisa la bandeja del sistema (junto al reloj).",
                        "BrosLMV Descargas", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                Environment.Exit(0); // Shutdown() solo no basta: la ventana de StartupUri se crea igual
            }
            EscucharPedidosDeMostrar();
        }

        private void EscucharPedidosDeMostrar()
        {
            try
            {
                var seguridad = new EventWaitHandleSecurity();
                seguridad.AddAccessRule(new EventWaitHandleAccessRule(
                    new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                    EventWaitHandleRights.Synchronize | EventWaitHandleRights.Modify, AccessControlType.Allow));
                bool creado;
                _eventoMostrar = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, NombreEventoMostrar, out creado, seguridad);
                var hilo = new Thread(() =>
                {
                    while (true)
                    {
                        try { _eventoMostrar.WaitOne(); } catch { return; }
                        string rfc = "";
                        try { rfc = System.IO.File.ReadAllText(ArchivoPedido).Trim().ToUpperInvariant(); } catch { }
                        Dispatcher.BeginInvoke(new Action(() => (MainWindow as MainWindow)?.MostrarYAbrir(rfc)));
                    }
                }) { IsBackground = true, Name = "Descargas-Mostrar" };
                hilo.Start();
            }
            catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mutexInstanciaUnica?.ReleaseMutex();
            base.OnExit(e);
        }
    }
}
