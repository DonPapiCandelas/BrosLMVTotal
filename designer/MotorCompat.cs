// Apoyo del motor (motor.cs + acciones.cs, generados en Backend.g.cs): las mismas funciones auxiliares que tiene el script de Configuración de formato.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BrosLMV.Disenador
{
    static partial class Motor
    {
        static CtxShim ctx;
        static int owned, userId;
        public static void Iniciar(CtxShim c, int empresa, int usuario) { ctx = c; owned = empresa; userId = usuario; }

        static object o(params object[] kv) { var d = new Dictionary<string, object>(); for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1]; return d; }
        static string S(object v) => v == null || v is DBNull ? "" : Convert.ToString(v);
        static bool B(object v) => v != null && !(v is DBNull) && Convert.ToBoolean(v);
        static long L(object v) => v == null || v is DBNull ? 0 : Convert.ToInt64(v);

        // Carpeta de los formatos HTML de Comercial (la de cualquier formato ya registrado; si no, las rutas habituales)
        static string carpetaFormatos()
        {
            try
            {
                var a = ctx.Query("SELECT TOP 1 FileName FROM engModulePrintFormat WHERE ISNULL(FileName,'')<>'' AND CHARINDEX('\\',FileName)>0");
                if (a.Count > 0) { var d = Path.GetDirectoryName(Convert.ToString(a[0]["FileName"])); if (Directory.Exists(d)) return d; }
            }
            catch { }
            foreach (var d in new[] { @"C:\Compac\ComercialSP\Formatos", @"C:\Program Files (x86)\Compac\ComercialSP\Formatos" }) if (Directory.Exists(d)) return d;
            return @"C:\Compac\ComercialSP\Formatos";
        }
    }
}
