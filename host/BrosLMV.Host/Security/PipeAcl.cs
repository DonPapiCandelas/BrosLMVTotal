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

// PipeAcl.cs — ACL del Named Pipe (decisión D6). El pipe solo es accesible para el
// USUARIO de Windows actual; ningún otro usuario del equipo puede conectarse. Es la
// primera barrera; la segunda es el token UUID que valida cada mensaje (ver PipeServer).

using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace BrosLMV.Host.Security;

public static class PipeAcl
{
    // Construye una ACL que concede control total SOLO al usuario actual de Windows.
    public static PipeSecurity CurrentUserOnly()
    {
        var security = new PipeSecurity();
        using var identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User
            ?? throw new InvalidOperationException("No se pudo obtener el SID del usuario actual.");

        // Solo el usuario actual: leer, escribir y crear nuevas instancias del pipe.
        security.AddAccessRule(new PipeAccessRule(
            user,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));

        return security;
    }
}
