// job: safe-offline
// Companion headless de PLANTILLA_ADJUNTOS_DOCUMENTO_FORMS_CSHARP.ctx para el caso de humo
// #25. NO es la plantilla real (esa abre una ventana WinForms modal con ShowDialog() y
// depende de OpenFileDialog/selección en el grid nativo -- no automatizable headless con las
// herramientas de este proyecto, igual que el resto del catálogo de plantillas Forms C#).
// Reproduce EXACTAMENTE la lógica de datos del botón "Eliminar" (control de propiedad +
// override de administrador) sobre una fila de prueba ya insertada por el .ps1 vía sqlcmd,
// para poder validarla sin abrir Comercial ni la ventana.
//
// Busca la fila de prueba por Description='HUMO_ADJUNTOS_TEST' (en vez de por selección de
// grid, que no existe en este modo) y aplica la MISMA regla que el botón real:
//   permitido = (CreatedBy == usuario activo) OR (zzBrosPref Usuario=activo Tipo='AdjuntosAdmin' Valor='1')
// Si está permitido, borra la fila (mismo DELETE que usa la plantilla). Si no, no hace nada
// -- el .ps1 verifica el resultado consultando engModuleFile directo por sqlcmd, no por el
// exit code del Runner (que siempre sale 0 aquí: negar el borrado no es un error del script).

// Nota: este script NO debe mostrar UI (ctx.Msg abre un MessageBox real incluso en corridas
// headless de BrosLMV.Runner.exe) -- el .ps1 verifica el resultado consultando engModuleFile
// directo por sqlcmd, nunca leyendo esta salida, así que un MessageBox aquí solo interrumpe
// a quien tenga la sesión de Windows abierta mientras corre la batería de humo.
var fila = ctx.Query("SELECT TOP 1 ModuleFileID, CreatedBy FROM dbo.engModuleFile WHERE Description='HUMO_ADJUNTOS_TEST'");
if (fila.Count == 0) { return; }

long moduleFileId = Convert.ToInt64(fila[0]["ModuleFileID"]);
int creador = Convert.ToInt32(fila[0]["CreatedBy"]);
int userId = ctx.UserIdReal();

bool esAdmin = false;
var prefAdmin = ctx.Query("SELECT Valor FROM zzBrosPref WHERE Usuario=" + userId + " AND Tipo='AdjuntosAdmin'");
if (prefAdmin.Count > 0 && Convert.ToString(prefAdmin[0]["Valor"]) == "1") esAdmin = true;

if (creador == userId || esAdmin) {
    ctx.NonQuery("DELETE FROM dbo.engModuleFile WHERE ModuleFileID=" + moduleFileId);
}
