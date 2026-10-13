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

// Sesion.cs -- estado compartido de la ventana principal: la conexion a la base local, la lista de empresas y la empresa activa.
// Todas las paginas reciben la misma Sesion; cuando cambia la empresa activa se avisa con EmpresaCambiada.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using BrosLMV.Descargas.Datos;
using Microsoft.Data.SqlClient;

namespace BrosLMV.DescargasUI
{
    public sealed class Sesion
    {
        public SqlConnection Conn { get; }
        public Window Ventana { get; }
        public List<EmpresaRow> Empresas { get; private set; } = new List<EmpresaRow>();
        public EmpresaRow Empresa { get; private set; }

        public event Action EmpresaCambiada;
        public event Action EmpresasRecargadas;
        public event Action ResumenActualizado;

        // Resumen de la empresa activa (salud, indicadores, totales del mes, cobertura). Se calcula al elegir la empresa y al pedirlo.
        internal ResumenEmpresa Resumen { get; private set; }

        internal void CalcularResumen()
        {
            var datos = DatosEmpresa();
            Resumen = datos == null ? null : ResumenEmpresa.Calcular(Conn, datos);
            ResumenActualizado?.Invoke();
        }

        public Sesion(SqlConnection conn, Window ventana) { Conn = conn; Ventana = ventana; }

        // Vuelve a leer las empresas del catalogo (con su conteo de CFDI y su ultima solicitud) y conserva la activa si sigue existiendo.
        public void RecargarEmpresas(string rfcPreferido = null)
        {
            var previa = rfcPreferido ?? Empresa?.RFC;
            var lista = new List<EmpresaRow>();
            foreach (var e in BrosSatDb.ObtenerEmpresas(Conn))
            {
                int total;
                using (var cmd = new SqlCommand("SELECT COUNT(*) FROM CfdiRecibido WHERE RFCReceptor=@r OR RFCEmisor=@r", Conn))
                { cmd.Parameters.AddWithValue("@r", e.RFC); total = Convert.ToInt32(cmd.ExecuteScalar()); }
                var solicitudes = BrosSatDb.ObtenerSolicitudesRecientes(Conn, e.RFC, 1);
                lista.Add(ADesdeFila(e, total, solicitudes.Count > 0 ? solicitudes[0].Estatus : "—"));
            }
            Empresas = lista;
            var elegida = lista.FirstOrDefault(x => string.Equals(x.RFC, previa, StringComparison.OrdinalIgnoreCase)) ?? lista.FirstOrDefault();
            bool cambio = !string.Equals(Empresa?.RFC, elegida?.RFC, StringComparison.OrdinalIgnoreCase);
            Empresa = elegida;
            if (cambio) Resumen = null;
            EmpresasRecargadas?.Invoke();
            if (cambio) EmpresaCambiada?.Invoke();
        }

        public void Seleccionar(EmpresaRow e)
        {
            if (e == null || string.Equals(Empresa?.RFC, e.RFC, StringComparison.OrdinalIgnoreCase)) return;
            Empresa = e;
            Resumen = null;
            EmpresaCambiada?.Invoke();
        }

        public bool Seleccionar(string rfc)
        {
            var e = Empresas.FirstOrDefault(x => string.Equals(x.RFC, rfc, StringComparison.OrdinalIgnoreCase));
            if (e == null) return false;
            Seleccionar(e);
            return true;
        }

        // Datos completos de la empresa activa tal como estan en la base (para las paginas que trabajan con EmpresaFila).
        internal EmpresaFila DatosEmpresa() => Empresa == null ? null : BrosSatDb.ObtenerEmpresas(Conn).FirstOrDefault(x => x.EmpresaID == Empresa.EmpresaID);

        internal static EmpresaRow ADesdeFila(EmpresaFila e, int totalCfdi = 0, string ultimoEstatus = "—") => new EmpresaRow
        {
            EmpresaID = e.EmpresaID, Nombre = e.Nombre, RFC = e.RFC, RutaCer = e.RutaCer, RutaKey = e.RutaKey, PasswordCifrada = e.PasswordCifrada,
            TotalCfdi = totalCfdi, UltimoEstatus = ultimoEstatus, CarpetaXml = e.CarpetaXml, EstructuraCarpetas = e.EstructuraCarpetas,
            PlantillaNombreArchivo = e.PlantillaNombreArchivo, ComercialCarpetaXmlRecibidos = e.ComercialCarpetaXmlRecibidos,
            ComercialCarpetaXmlEmitidos = e.ComercialCarpetaXmlEmitidos, ComercialConexionSql = e.ComercialConexionSql, OcultarNominas = e.OcultarNominas
        };

        internal static EmpresaFila AFila(EmpresaRow r) => new EmpresaFila
        {
            EmpresaID = r.EmpresaID, Nombre = r.Nombre, RFC = r.RFC, RutaCer = r.RutaCer, RutaKey = r.RutaKey, PasswordCifrada = r.PasswordCifrada,
            CarpetaXml = r.CarpetaXml, EstructuraCarpetas = r.EstructuraCarpetas, PlantillaNombreArchivo = r.PlantillaNombreArchivo,
            ComercialCarpetaXmlRecibidos = r.ComercialCarpetaXmlRecibidos, ComercialCarpetaXmlEmitidos = r.ComercialCarpetaXmlEmitidos,
            ComercialConexionSql = r.ComercialConexionSql, OcultarNominas = r.OcultarNominas
        };
    }
}
