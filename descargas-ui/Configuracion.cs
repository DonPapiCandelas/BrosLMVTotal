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

// Configuracion.cs -- config.json vive junto al exe, gitignored. Solo trae la cadena de
// conexion a la BD propia -- las empresas (FIEL, contrasena cifrada) viven EN esa BD
// (tabla Empresa), no aqui, para poder tener varias sin editar un archivo por cada una.

using System;
using System.IO;
using System.Text.Json;

namespace BrosLMV.DescargasUI
{
    internal sealed class Configuracion
    {
        public string CadenaConexion { get; set; }

        private static string RutaArchivo => Path.Combine(AppContext.BaseDirectory, "config.json");

        public static Configuracion Cargar()
        {
            if (!File.Exists(RutaArchivo)) return null;
            return JsonSerializer.Deserialize<Configuracion>(File.ReadAllText(RutaArchivo));
        }

        public void Guardar()
        {
            var opciones = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(RutaArchivo, JsonSerializer.Serialize(this, opciones));
        }
    }
}
