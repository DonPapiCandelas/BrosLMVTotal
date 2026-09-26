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

// LogPaths.cs — carpeta de logs del host. Por defecto C:\BrosLMV\logs; se puede
// redirigir con la variable de entorno BROSLMV_LOG_DIR (útil para tests).

namespace BrosLMV.Host.Logging;

public static class LogPaths
{
    public static string Dir =>
        Environment.GetEnvironmentVariable("BROSLMV_LOG_DIR") is { Length: > 0 } env
            ? env
            : @"C:\BrosLMV\logs";

    public static string File(string name) => Path.Combine(Dir, name);

    public static void EnsureDir(string filePath)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }
}
