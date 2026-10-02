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

// ErpSdkNativo.cs
// Funciones nativas de Comercial (XEngineLib y Payment.dll) que ctx.erp aun no envolvia, con firma tipada,
// coercion correcta de argumentos y comportamiento VERIFICADO en laboratorio (ver docs/SDK_FUNCIONES_NATIVAS.md).
// Misma convencion que el resto de ErpContext: ante un error COM la funcion devuelve el valor por omision y deja
// el motivo en ctx.erp.LastError (Com.Call traga las excepciones: revisalo cuando importe).
// Los nombres son los NATIVOS de XEngine a proposito: asi coinciden con docs/XENGINE_FUNCIONES.md y con
// ctx.erp.Call("Nombre", ...) de Python.

using System;
using System.Reflection;

namespace BrosLMV
{
    public partial class ErpContext
    {
        // ---- conversiones locales (el motor devuelve Variant: bool/short/int/double/string segun la funcion) ----
        private static bool SdkBool(object v)
        {
            if (v == null || v is DBNull) return false;
            if (v is bool) return (bool)v;
            try { return Convert.ToBoolean(v); } catch { return false; }
        }
        private static string SdkStr(object v) { return v == null || v is DBNull ? "" : Convert.ToString(v); }
        private static double SdkDbl(object v)
        {
            if (v == null || v is DBNull) return 0;
            try { return Convert.ToDouble(v); } catch { return 0; }
        }

        // =====================================================================
        //  Esquema y entorno
        // =====================================================================

        /// <summary>true si la tabla existe en la base de la empresa activa.</summary>
        public bool TableExists(string tableName)
        { return SdkBool(Com.Call(_xe, "TableExists", new object[] { tableName })); }

        /// <summary>true si la columna existe en la tabla. OJO: el orden de los argumentos es el nativo (campo, tabla).</summary>
        public bool FieldExistsInTable(string fieldName, string tableName)
        { return SdkBool(Com.Call(_xe, "FieldExistsInTable", new object[] { fieldName, tableName })); }

        /// <summary>ModuleID del modulo por naturaleza (tipo de documento + recipiente: 1 cliente, 2 proveedor...). 0 si no hay.</summary>
        public int GetModuleIDDocumentType(int documentTypeId, int docRecipientId)
        { return Com.ToInt(Com.Call(_xe, "GetModuleIDDocumentType", new object[] { (short)documentTypeId, (short)docRecipientId })); }

        /// <summary>Nombre de la DLL que atiende un modulo ("Document", "FinancialOperation"...). "" si no existe.</summary>
        public string GetModuleDLLName(int moduleId)
        { return SdkStr(Com.Call(_xe, "GetModuleDLLName", new object[] { (long)moduleId })); }

        /// <summary>true si el usuario activo tiene permiso sobre la funcionalidad del modulo (p. ej. "Document.Delete").</summary>
        public bool GetSecurityFunctionality(string functionalityKey, int moduleId)
        { return SdkBool(Com.Call(_xe, "GetSecurityFunctionality", new object[] { functionalityKey, (long)moduleId })); }

        /// <summary>true si el usuario activo puede elevar privilegios (administrador de Comercial).</summary>
        public bool GetUserCanElevatePrivileges()
        { return SdkBool(Com.Call(_xe, "GetUserCanElevatePrivileges", new object[0])); }

        // =====================================================================
        //  Parametros por empresa (engParameter: Key, Value, Description, CountryID)
        // =====================================================================

        /// <summary>Lee un valor de engParameter por clave y pais. "" si no existe.</summary>
        public string GetDefaultValue(string key, int countryId = 1)
        { return SdkStr(Com.Call(_xe, "GetDefaultValue", new object[] { key, (long)countryId })); }

        /// <summary>Escribe (inserta o sobrescribe) un valor en engParameter de la empresa activa. ESCRIBE en la configuracion nativa:
        /// usa claves propias con prefijo (p. ej. "BROS_...") y nunca una clave de Comercial.</summary>
        public void SaveDefaultValue(string key, string value, string description = "", int countryId = 1)
        { Com.Call(_xe, "SaveDefaultValue", new object[] { key, value, description ?? "", (long)countryId }); }

        // =====================================================================
        //  Fecha, texto y utilerias
        // =====================================================================

        /// <summary>Ultimo dia (28-31) del mes de la fecha.</summary>
        public int GetLastDayMonth(DateTime date)
        { return Com.ToInt(Com.Call(_xe, "GetLastDayMonth", new object[] { date })); }

        /// <summary>Arma una fecha a partir de "yyyy-MM-dd" y "HH:mm:ss".</summary>
        public DateTime DateFromString(string datePart, string timePart)
        {
            object r = Com.Call(_xe, "DateFromString", new object[] { datePart, timePart });
            return r is DateTime ? (DateTime)r : DateTime.MinValue;
        }

        /// <summary>Fecha en el formato de fecha-hora de Comercial para CFDI, con la zona horaria del equipo
        /// (p. ej. "2026-10-01T12:30:00 -06:00"; ojo: lleva un espacio antes del desfase).</summary>
        public string ConvertDateTimeToUTC(DateTime dateTime)
        { return SdkStr(Com.Call(_xe, "ConvertDateTimeToUTC", new object[] { dateTime })); }

        /// <summary>Fecha como texto "yyyy-MM-dd HH:mm:ss" (formato que Comercial usa en sus consultas).</summary>
        public string GetFormatedDateValue(DateTime date)
        { return SdkStr(Com.Call(_xe, "GetFormatedDateValue", new object[] { date })); }

        /// <summary>Rellena un texto a una longitud. alignment "L": el texto queda a la izquierda y se rellena a la derecha ("7" -> "70000");
        /// "R": el texto queda a la derecha y se rellena a la izquierda ("7" -> "00007").</summary>
        public string Pad(string value, int length, string fillWith, string alignment)
        { return SdkStr(Com.Call(_xe, "Pad", new object[] { value, (short)length, fillWith, alignment })); }

        /// <summary>Trunca (no redondea) un numero a N decimales: (12.98765, 2) = 12.98; (-3.999, 1) = -3.9.</summary>
        public double TruncateDouble(double value, int decimals)
        { return SdkDbl(Com.Call(_xe, "TruncateDouble", new object[] { value, (short)decimals })); }

        /// <summary>Parte alfabetica de un numero de serie ("ABC00123" -> "ABC").</summary>
        public string GetSerialNumberPrefix(string serialNumber)
        { return SdkStr(Com.Call(_xe, "GetSerialNumberPrefix", new object[] { serialNumber })); }

        /// <summary>Parte numerica de un numero de serie, conservando ceros ("ABC00123" -> "00123").</summary>
        public string GetSerialNumberNumValue(string serialNumber)
        { return SdkStr(Com.Call(_xe, "GetSerialNumberNumValue", new object[] { serialNumber })); }

        /// <summary>Devuelve el XML con sangrias (para mostrarlo o guardarlo legible). Si no es XML valido deja LastError.</summary>
        public string GetFormatedXML(string xml)
        { return SdkStr(Com.Call(_xe, "GetFormatedXML", new object[] { xml })); }

        /// <summary>Maximo entero de una columna: GetMaxValueField("DocumentID", "docDocument", "ModuleID=21"). where puede ir vacio.</summary>
        public long GetMaxValueField(string fieldName, string tableName, string where = "")
        { return Com.ToLong(Com.Call(_xe, "GetMaxValueField", new object[] { fieldName, tableName, where ?? "" })); }

        /// <summary>Codigo QR del texto, como imagen PNG en base64 (sirve en un &lt;img src="data:image/png;base64,..."&gt;).</summary>
        public string GetQRCode(string text)
        { return SdkStr(Com.Call(_xe, "GetQRCode", new object[] { text })); }

        // =====================================================================
        //  Costos
        // =====================================================================

        /// <summary>Costo de la ultima compra del producto (0 si no hay).</summary>
        public double GetCostLast(int productId)
        { return SdkDbl(Com.Call(_xe, "GetCostLast", new object[] { (long)productId })); }

        /// <summary>Recalcula el costo comercial vigente del producto (orgProduct.CostPriceComercial) desde su libro de costos.</summary>
        public void RecalcCostComercial(int productId)
        { Com.Call(_xe, "RecalcCostComercial", new object[] { (long)productId }); }

        /// <summary>Igual, para el costo fiscal.</summary>
        public void RecalcCostFiscal(int productId)
        { Com.Call(_xe, "RecalcCostFiscal", new object[] { (long)productId }); }

        // =====================================================================
        //  Cobros y pagos (Payment.clsMain / XEngine) -- los pagos se recalculan con la rutina de Comercial, no a mano
        // =====================================================================

        /// <summary>Reconstruye el reparto de impuestos de UN cobro (docFinancialOperationTaxDetail: base, importe, proporcion, retenciones,
        /// columnas MXN) con la rutina de Comercial. Es la opcion CONSERVADORA: rehace el reparto de los cobros que ya lo tenian y NO
        /// inventa reparto donde Comercial nunca lo genero (facturas PUE, pagos viejos). No toca saldos insolutos ni el documento
        /// (llama UpdateDocumentPaidInfo despues).</summary>
        public void SaveAllTaxesPayment(int financialOperationId)
        { Com.Call(_xe, "SaveAllTaxesPayment", new object[] { (long)financialOperationId }); }

        /// <summary>Reconstruye el reparto de impuestos Y los saldos anterior/insoluto de TODOS los cobros y notas de credito aplicados al
        /// documento (Payment.clsMain.RecalcDocumentPayments, la rutina que Comercial usa al guardar un cobro).
        /// Ojo: (1) a diferencia de SaveAllTaxesPayment, AGREGA reparto de impuestos a documentos que no lo tenian (facturas PUE, pagos
        /// viejos): usala con facturas PPD; (2) NO recalcula los saldos de renglones ya incluidos en un REP timbrado
        /// (docDocumentPayment.AltID &gt; 0), lo cual es correcto; (3) trabaja a nivel documento: si se borro a mano solo el reparto de
        /// UNA operacion de un documento con varias, puede fallar con "Division by zero" (revisa LastError); (4) no actualiza
        /// TotalPaid/Balance/StatusPaidID: llama UpdateDocumentPaidInfo despues.</summary>
        public void RecalcPagosDocumento(int documentId)
        {
            var pay = CrearHelper("Payment.clsMain");
            Com.Call(pay, "RecalcDocumentPayments", new object[] { (long)documentId });
        }

        /// <summary>Recalcula SaldoAnterior/SaldoInsoluto (complemento de pago) de los renglones de UN cobro, sin tocar impuestos.
        /// Ignora los renglones ya timbrados en un REP (AltID &gt; 0). paymentWithDocumentId = 0 para un cobro normal, o el DocumentID de la
        /// nota de credito si el "pago" es una NC aplicada. (Payment.clsMain.AjustarSaldosInsolutos.)</summary>
        public void AjustarSaldosInsolutos(int financialOperationId, int paymentWithDocumentId)
        {
            var pay = CrearHelper("Payment.clsMain");
            Com.Call(pay, "AjustarSaldosInsolutos", new object[] { (long)financialOperationId, (long)paymentWithDocumentId });
        }
    }
}
