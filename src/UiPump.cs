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

// UiPump.cs -- bombeo a UN hilo fijo con mensajes de Windows.
//
// Extraido de ClsMain.cs (2026-07-30) para poder reusarlo tambien en BrosLMV.Runner
// (T3.3, Python headless): el mecanismo no tiene nada de especifico a Comercial -- es
// un Form invisible que sirve de blanco de marshaling para Control.Invoke, sin importar
// quien bombee sus mensajes (Comercial en el addon; Application.Run() en el Runner).

using System;
using System.Windows.Forms;

namespace BrosLMV
{
    // Los botones Python corren su intercambio con el host EN SEGUNDO PLANO (para no
    // congelar Comercial mientras la ventana esta abierta), pero XEngineLib (el COM de
    // CONTPAQi) solo debe tocarse desde UN hilo a la vez -- si dos hilos lo llamaran a
    // la vez (el nuestro en segundo plano + el propio Comercial reaccionando a un clic
    // del usuario) el riesgo es corrupcion/crash del COM, peor que la congelada actual.
    // Por eso CADA llamada real a ctx.query/ctx.erp se reenvia (Invoke) a ESTE control,
    // creado una sola vez en el hilo dueno del engine: sigue siendo el UNICO hilo que
    // toca el COM, pero el llamador ya no se queda esperando bloqueado mientras tanto.
    internal static class UiPump
    {
        private static Control _bomba;

        // Debe llamarse desde el hilo dueno del engine (en el addon: el hilo de Comercial,
        // al entrar a ExecuteFunction; en el Runner: el hilo STA de Main, antes de correr
        // Python). Idempotente: la segunda vez en adelante no hace nada.
        internal static void Asegurar()
        {
            if (_bomba != null && !_bomba.IsDisposed) return;
            var f = new Form
            {
                ShowInTaskbar = false,
                Opacity = 0,
                Width = 0,
                Height = 0,
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Location = new System.Drawing.Point(-32000, -32000)
            };
            var h = f.Handle; // fuerza la creacion del HWND sin necesidad de Show()
            _bomba = f;
        }

        // Ejecuta f() en el hilo de la bomba y devuelve su resultado, sin importar desde
        // que hilo se llame Invoke. Si la bomba no esta lista (no deberia pasar tras
        // Asegurar()), corre f() directo como respaldo de seguridad.
        internal static T Invoke<T>(Func<T> f)
        {
            var b = _bomba;
            if (b == null || b.IsDisposed || !b.IsHandleCreated) return f();
            if (!b.InvokeRequired) return f(); // ya estamos en el hilo correcto
            return (T)b.Invoke(f);
        }

        internal static void Invoke(Action a) { Invoke<object>(() => { a(); return null; }); }
    }
}
