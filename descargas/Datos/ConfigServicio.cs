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

// ConfigServicio.cs -- configuracion del Servicio de Windows "BrosLMV Descargas", que reemplaza
// las 3 Tareas Programadas (pedido explicito del usuario 2026-08-19: "quiero todo en segundo
// plano... debe existir un servicio" -- las Tareas Programadas, al correr LogonType=Interactive
// con un exe de consola, abrian una ventana de CMD visible cada 10 minutos).
//
// %ProgramData% (no %LocalAppData%) porque el servicio corre como LocalSystem -- una carpeta de
// usuario no es confiable ahi. La UI (AutomatizacionWindow) lee/escribe el mismo archivo para
// cambiar el intervalo sin tocar el registro de tareas de Windows; el servicio lo relee en cada
// vuelta de su loop, asi que un cambio aplica solo, sin reiniciar el servicio.

using System;
using System.IO;
using System.Text.Json;

namespace BrosLMV.Descargas.Datos
{
    public sealed class ConfigServicio
    {
        public string CadenaConexion { get; set; }
        public int IntervaloSolicitarMinutos { get; set; } = 120;

        public static string RutaArchivo => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BrosLMV", "Descargas", "servicio.json");

        public static ConfigServicio Cargar()
        {
            if (!File.Exists(RutaArchivo)) return null;
            try { return JsonSerializer.Deserialize<ConfigServicio>(File.ReadAllText(RutaArchivo)); }
            catch { return null; }
        }

        public void Guardar()
        {
            string carpeta = Path.GetDirectoryName(RutaArchivo);
            Directory.CreateDirectory(carpeta);
            var opciones = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(RutaArchivo, JsonSerializer.Serialize(this, opciones));
        }
    }
}
