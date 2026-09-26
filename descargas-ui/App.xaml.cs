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
        private Mutex _mutexInstanciaUnica;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

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
                // Si hasta crear el Mutex con ACL abierto falla, no hay forma confiable de saber
                // si ya hay otra instancia -- se deja continuar en vez de bloquear el arranque.
                esNueva = true;
            }

            if (!esNueva)
            {
                MessageBox.Show("BrosLMV Descargas ya está corriendo — revisa la bandeja del sistema (junto al reloj).",
                    "BrosLMV Descargas", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mutexInstanciaUnica?.ReleaseMutex();
            base.OnExit(e);
        }
    }
}
