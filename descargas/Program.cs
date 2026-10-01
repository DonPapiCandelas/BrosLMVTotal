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

// Program.cs
//
// Carga una FIEL real (.cer/.key + contrasena, pasados por argumentos -- nunca hardcoded ni
// pegados en un chat). Sin --autenticar, solo arma+firma el sobre de Autentica y lo verifica
// LOCALMENTE (Fase 0, sin red). Con --autenticar, ademas lo manda de verdad a Autenticacion.svc
// del SAT (Fase 1, primer paso) -- requiere el flag explicito porque ya es una llamada real
// contra el servicio de produccion del SAT, aunque Autenticacion en si no consuma del limite
// diario de SolicitaDescarga.
//
// Uso:
//   dotnet run -- --cer "C:\ruta\fiel.cer" --key "C:\ruta\fiel.key" --password "..." --rfc "XAXX010101000" [--autenticar]

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using BrosLMV.Descargas.Cola;
using BrosLMV.Descargas.Datos;
using BrosLMV.Descargas.Sat;
using Microsoft.Data.SqlClient;

namespace BrosLMV.Descargas
{
    // "public" (no "internal"): descargas-servicio referencia este proyecto directo para
    // reusar AutoTodasAsync/AutoSolicitarTodasAsync/VerificarEstatusAsync desde su propio loop en
    // segundo plano, en vez de invocar este exe como subproceso (evita abrir/cerrar una ventana de
    // consola cada 10 minutos -- eso era justo la "consola de CMD rara" que reporto el usuario
    // 2026-08-19 con las Tareas Programadas).
    public static class Program
    {
        private static int Main(string[] args) => MainAsync(args).GetAwaiter().GetResult();

        private static async Task<int> MainAsync(string[] args)
        {
            string rutaCer = null, rutaKey = null, password = null, rfc = null, salida = "sobre_firmado.xml", idSolicitud = null, idPaquete = null, salidaZip = null, conexionSql = null, tipoSolicitud = "CFDI";
            bool autenticar = false, solicitar = false, auto = false, reparsear = false, autoTodas = false, verificarEstatus = false;
            bool solicitarMetadataCatalogo = false, verificarMetadataCatalogo = false, autoSolicitarTodas = false, sincronizarComercial = false, inicializar = false, mostrarSalud = false, mostrarConciliacion = false, mostrarVinculos = false;
            string exportarRespaldo = null, importarRespaldo = null, carpetaFiel = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--cer": rutaCer = Siguiente(args, ref i); break;
                    case "--key": rutaKey = Siguiente(args, ref i); break;
                    case "--password": password = Siguiente(args, ref i); break;
                    case "--rfc": rfc = Siguiente(args, ref i); break;
                    case "--salida": salida = Siguiente(args, ref i); break;
                    // Opcional -- si se pasa, ademas de imprimir en consola, se registra todo en
                    // la BD propia (SolicitudDescarga/CfdiRecibido/CfdiRelacion). Sin este flag,
                    // el programa sigue funcionando exactamente igual que antes (solo consola).
                    case "--conn": conexionSql = Siguiente(args, ref i); break;
                    // Opt-in explicito: sin este flag, el programa NUNCA toca la red -- solo
                    // arma y verifica la firma localmente, como en la Fase 0.
                    case "--autenticar": autenticar = true; break;
                    // Este SI gasta cupo diario real del SAT -- flag separado, aparte, y
                    // requiere --autenticar tambien (necesita el token de ese paso).
                    case "--solicitar": solicitar = true; break;
                    // Diagnostico puntual (2026-08-14): el WSDL de SolicitaDescargaService
                    // expone TipoSolicitud como enum con 5 valores (CFDI/Metadata/PDF/
                    // PDFCOCEMA/TXTUUIDMASIVA) -- confirmado contra el XSD en vivo. "Metadata"
                    // deberia traer un TXT con una fila por CFDI que cumple los filtros, INCLUYENDO
                    // cancelados (a diferencia de "CFDI", que el SAT rechaza si no se pide
                    // EstadoComprobante=Vigente -- ver SatFirmaXml.FirmarSolicitud). Default "CFDI"
                    // para no cambiar el comportamiento existente.
                    case "--tipo-solicitud": tipoSolicitud = Siguiente(args, ref i); break;
                    // Diagnostico puntual usando el catalogo Empresa/DPAPI (para no pasar la
                    // contraseña de la FIEL por argumento) -- manda SolicitaDescarga con
                    // TipoSolicitud=Metadata (Recibidos, ayer) para la empresa con RFC=--rfc.
                    // Requiere --conn --rfc.
                    case "--solicitar-metadata-catalogo": solicitarMetadataCatalogo = true; break;
                    // Segunda mitad del diagnostico: verifica y descarga (a disco, sin BD) el
                    // resultado de la solicitud de --solicitar-metadata-catalogo. Requiere
                    // --conn --rfc --idsolicitud.
                    case "--verificar-metadata-catalogo": verificarMetadataCatalogo = true; break;
                    // Consulta el estatus de una solicitud YA hecha (IdSolicitud de una corrida
                    // anterior con --solicitar) -- esto NO gasta cupo diario, se puede llamar
                    // tantas veces como se quiera. Requiere --autenticar tambien.
                    case "--idsolicitud": idSolicitud = Siguiente(args, ref i); break;
                    // Descarga el ZIP de un paquete YA reportado listo por --idsolicitud --
                    // OJO: solo se puede descargar 2 veces por paquete y vive 72 horas.
                    case "--idpaquete": idPaquete = Siguiente(args, ref i); break;
                    case "--salida-zip": salidaZip = Siguiente(args, ref i); break;
                    // Una pasada del motor de cola: revisa TODAS las solicitudes pendientes en
                    // la BD (de cualquier --solicitar hecho antes), verifica estatus y descarga
                    // lo que ya este listo. Pensado para Tarea Programada de Windows. Requiere
                    // --conn (no tiene caso correrlo sin persistir resultados).
                    case "--auto": auto = true; break;
                    // Relee del disco los XML ya descargados (RutaArchivoXml en la BD) y los
                    // vuelve a parsear con CfdiXmlParser -- para rellenar campos que se agregaron
                    // DESPUES de que esos CFDI ya se habian guardado (TipoCambio, Retenciones,
                    // el Total real de los CFDI de tipo Pago). No toca la red ni gasta cupo del
                    // SAT -- no requiere FIEL, solo --conn.
                    case "--reparsear": reparsear = true; break;
                    // Como --auto, pero para TODAS las empresas activas del catalogo de la BD en
                    // una sola corrida -- no toma --cer/--key/--password por argumento, lee cada
                    // FIEL del catalogo Empresa y descifra su password con DPAPI (mismo mecanismo
                    // que ya usa la UI). Pensado para UNA sola Tarea Programada que cubra todas
                    // las empresas, sin tener que guardar ninguna contraseña en texto plano en la
                    // definicion de la tarea. No gasta cupo diario (solo verifica/descarga).
                    case "--auto-todas": autoTodas = true; break;
                    // Refresca EstatusSat (Vigente/Cancelado) de TODOS los CFDI ya descargados
                    // (de cualquier empresa) contra el servicio PUBLICO de consulta del SAT
                    // (ConsultaCFDIService.svc -- el mismo que valida el QR impreso). No requiere
                    // FIEL ni token, no gasta cupo -- solo --conn.
                    case "--verificar-estatus": verificarEstatus = true; break;
                    // La UNICA pieza que crea solicitudes nuevas de forma automatica -- SI gasta
                    // cupo diario (2 por empresa por corrida: CFDI + Metadata del primer tramo
                    // pendiente, acotado por SolicitudChunker). Calcula que tan atrasada esta cada
                    // empresa via BrosSatDb.ObtenerUltimaFechaCubierta y pide solo el siguiente
                    // tramo -- si hay meses de hueco, se pone al dia gradualmente en corridas
                    // sucesivas en vez de gastar todo el cupo de un jalon. Pensado para UNA Tarea
                    // Programada aparte, 1 vez al dia (distinta de --auto-todas).
                    case "--auto-solicitar-todas": autoSolicitarTodas = true; break;
                    // Backfill de una sola vez: copia hacia las carpetas de Comercial (Ruta XML
                    // Recibidos/Emitidos) TODOS los CFDI que ya estan descargados en la BD -- no
                    // vuelve a pedir nada al SAT, no gasta cupo. Pedido explicito del usuario
                    // 2026-08-18 tras agregar la copia automatica: "ya baje mas de mil CFDI,
                    // necesitamos importarlos" (esos se bajaron antes de que existiera la copia
                    // automatica en SolicitudWorker, asi que nunca se copiaron). Sin --rfc procesa
                    // TODAS las empresas activas del catalogo; con --rfc solo esa. Requiere --conn.
                    case "--sincronizar-comercial": sincronizarComercial = true; break;
                    case "--salud": mostrarSalud = true; break;
                    case "--conciliacion": mostrarConciliacion = true; break;
                    case "--vinculos": mostrarVinculos = true; break;
                    case "--exportar-respaldo": exportarRespaldo = Siguiente(args, ref i); break;
                    case "--importar-respaldo": importarRespaldo = Siguiente(args, ref i); break;
                    case "--carpeta-fiel": carpetaFiel = Siguiente(args, ref i); break;
                    // Crea la base de datos (si no existe) y el esquema y sale -- no requiere
                    // FIEL. Pensado para el instalador (BrosLMV.Descargas.Instalador): antes de
                    // crear las Tareas Programadas, deja la BD lista para que la primera corrida
                    // automatica no falle por "base de datos inexistente" (el CLI, a diferencia de
                    // la UI, nunca abrio una conexion "master" para crearla si faltaba).
                    case "--inicializar": inicializar = true; break;
                    default:
                        Console.Error.WriteLine("Argumento desconocido: " + args[i]);
                        return Uso();
                }
            }

            if (inicializar)
            {
                if (string.IsNullOrEmpty(conexionSql))
                {
                    Console.Error.WriteLine("--inicializar requiere --conn.");
                    return Uso();
                }
                EsquemaSql.AsegurarBaseDeDatos(conexionSql);
                using (var connInit = new SqlConnection(conexionSql))
                {
                    connInit.Open();
                    EsquemaSql.Asegurar(connInit);
                }
                Console.WriteLine("Base de datos y esquema listos.");
                return 0;
            }

            if (reparsear)
            {
                if (string.IsNullOrEmpty(conexionSql))
                {
                    Console.Error.WriteLine("--reparsear requiere --conn.");
                    return Uso();
                }
                return Reparsear(conexionSql);
            }

            if (verificarEstatus)
            {
                if (string.IsNullOrEmpty(conexionSql))
                {
                    Console.Error.WriteLine("--verificar-estatus requiere --conn.");
                    return Uso();
                }
                return await VerificarEstatusAsync(conexionSql);
            }

            if (solicitarMetadataCatalogo)
            {
                if (string.IsNullOrEmpty(conexionSql) || string.IsNullOrEmpty(rfc))
                {
                    Console.Error.WriteLine("--solicitar-metadata-catalogo requiere --conn --rfc.");
                    return Uso();
                }
                return await SolicitarMetadataCatalogoAsync(conexionSql, rfc);
            }

            if (verificarMetadataCatalogo)
            {
                if (string.IsNullOrEmpty(conexionSql) || string.IsNullOrEmpty(rfc) || string.IsNullOrEmpty(idSolicitud))
                {
                    Console.Error.WriteLine("--verificar-metadata-catalogo requiere --conn --rfc --idsolicitud.");
                    return Uso();
                }
                return await VerificarMetadataCatalogoAsync(conexionSql, rfc, idSolicitud);
            }

            if (exportarRespaldo != null || importarRespaldo != null)
            {
                if (string.IsNullOrWhiteSpace(conexionSql)) { Console.Error.WriteLine("El respaldo requiere --conn."); return 1; }
                // La contrasena NO va por argumento (se veria en la lista de procesos): variable de entorno o se pide en la consola.
                string clave = Environment.GetEnvironmentVariable("BROSLMV_RESPALDO_CLAVE");
                if (string.IsNullOrEmpty(clave))
                {
                    Console.Error.Write("Contrasena del respaldo: ");
                    var sb = new System.Text.StringBuilder();
                    ConsoleKeyInfo k;
                    while ((k = Console.ReadKey(true)).Key != ConsoleKey.Enter) { if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; } else sb.Append(k.KeyChar); }
                    Console.Error.WriteLine();
                    clave = sb.ToString();
                }
                try
                {
                    using (var cn = new SqlConnection(conexionSql))
                    {
                        cn.Open();
                        EsquemaSql.Asegurar(cn);
                        if (exportarRespaldo != null)
                            Console.WriteLine(Respaldo.Exportar(cn, exportarRespaldo, clave) + " empresa(s) respaldadas en " + exportarRespaldo);
                        else
                        {
                            string destino = carpetaFiel ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BrosLMV", "Descargas", "fiel");
                            Console.WriteLine(Respaldo.Importar(cn, importarRespaldo, clave, destino) + " empresa(s) restauradas; FIEL en " + destino);
                        }
                    }
                }
                catch (Exception ex) { Console.Error.WriteLine("ERROR: " + ex.Message); return 1; }
                return 0;
            }

            if (mostrarVinculos)
            {
                if (string.IsNullOrWhiteSpace(conexionSql)) { Console.Error.WriteLine("--vinculos requiere --conn."); return 1; }
                using (var cn = new SqlConnection(conexionSql))
                {
                    cn.Open();
                    EsquemaSql.Asegurar(cn);
                    foreach (var e in BrosSatDb.ObtenerEmpresas(cn).Where(x => x.Activa && !string.IsNullOrWhiteSpace(x.ComercialConexionSql) && (rfc == null || x.RFC == rfc)))
                    {
                        var res = await Vinculacion.AnalizarAsync(DpapiHelper.DescifrarConexionSql(e.ComercialConexionSql));
                        Console.WriteLine("== " + e.Nombre + " (" + e.RFC + "): " + res.SinDocumento.Count + " CFDI sin documento (" + res.SinDocumento.Count(x => x.Candidatos.Count > 0) +
                            " con sugerencia), " + res.Diferencias.Count + " totales distintos ==");
                        foreach (var x in res.SinDocumento.Take(8)) Console.WriteLine("  " + x.Fecha.ToString("yyyy-MM-dd") + " " + (x.Recibido ? "R" : "E") + " " + x.RfcContraparte + " " + x.Total.ToString("N2") + " -> " + x.MejorCandidato);
                        foreach (var x in res.Diferencias.Take(5)) Console.WriteLine("  DIF " + x.Folio + " cfdi " + x.TotalCfdi.ToString("N2") + " vs doc " + x.TotalDocumento.ToString("N2"));
                    }
                }
                return 0;
            }

            if (mostrarConciliacion)
            {
                if (string.IsNullOrWhiteSpace(conexionSql)) { Console.Error.WriteLine("--conciliacion requiere --conn."); return 1; }
                using (var cn = new SqlConnection(conexionSql))
                {
                    cn.Open();
                    EsquemaSql.Asegurar(cn);
                    foreach (var e in BrosSatDb.ObtenerEmpresas(cn).Where(x => x.Activa && (rfc == null || x.RFC == rfc)))
                    {
                        Console.WriteLine("== " + e.Nombre + " (" + e.RFC + ") ==");
                        Console.Write(Conciliacion.ACsv(Conciliacion.Calcular(cn, e.RFC)));
                    }
                }
                return 0;
            }

            if (mostrarSalud)
            {
                if (string.IsNullOrWhiteSpace(conexionSql)) { Console.Error.WriteLine("--salud requiere --conn."); return 1; }
                var todos = HallazgosDeSalud(conexionSql);
                if (todos.Count == 0) Console.WriteLine("Salud: todo en orden.");
                foreach (var h in todos) Console.WriteLine(h.Nivel + " [" + h.Empresa + "] " + h.Tema + ": " + h.Detalle);
                return todos.Any(h => h.Nivel == "CRITICO") ? 2 : 0;
            }

            if (sincronizarComercial)
            {
                if (string.IsNullOrEmpty(conexionSql))
                {
                    Console.Error.WriteLine("--sincronizar-comercial requiere --conn.");
                    return Uso();
                }
                return await SincronizarComercial(conexionSql, rfc);
            }

            if (autoSolicitarTodas)
            {
                if (string.IsNullOrEmpty(conexionSql))
                {
                    Console.Error.WriteLine("--auto-solicitar-todas requiere --conn.");
                    return Uso();
                }
                return await AutoSolicitarTodasAsync(conexionSql);
            }

            if (autoTodas)
            {
                if (string.IsNullOrEmpty(conexionSql))
                {
                    Console.Error.WriteLine("--auto-todas requiere --conn.");
                    return Uso();
                }
                return await AutoTodasAsync(conexionSql);
            }

            if (string.IsNullOrEmpty(rutaCer) || string.IsNullOrEmpty(rutaKey) || string.IsNullOrEmpty(password))
                return Uso();
            if (!auto && string.IsNullOrEmpty(rfc))
                return Uso();
            if (auto && string.IsNullOrEmpty(conexionSql))
            {
                Console.Error.WriteLine("--auto requiere --conn (no tiene caso correr el motor de cola sin persistir resultados).");
                return Uso();
            }

            SqlConnection conn = null;
            try
            {
                var cert = SatFirmaXml.CargarFiel(rutaCer, rutaKey, password, out var llave);
                Console.WriteLine("FIEL cargada: " + cert.Subject);
                Console.WriteLine("Vigente: " + cert.NotBefore.ToString("yyyy-MM-dd") + " a " + cert.NotAfter.ToString("yyyy-MM-dd"));

                if (DateTime.Now < cert.NotBefore || DateTime.Now > cert.NotAfter)
                    Console.Error.WriteLine("ADVERTENCIA: el certificado esta fuera de su periodo de vigencia.");

                if (auto)
                {
                    conn = new SqlConnection(conexionSql);
                    conn.Open();
                    EsquemaSql.Asegurar(conn);
                    Console.WriteLine("Pasada del motor de cola iniciada (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ")...");
                    await SolicitudWorker.EjecutarPasadaAsync(conn, cert, llave, carpetaXml: null, estructuraCarpetas: "AnioTipoMes", plantillaNombreArchivo: "{UUID}");
                    Console.WriteLine("Pasada terminada.");
                    return 0;
                }

                var doc = SatFirmaXml.FirmarAutentica(cert, llave);
                doc.Save(salida);
                Console.WriteLine("Sobre firmado guardado en: " + salida);

                bool valida = SatFirmaXml.VerificarFirma(doc);
                Console.WriteLine(valida
                    ? "Firma XML-DSig verificada localmente: OK."
                    : "ADVERTENCIA: la firma NO paso la verificacion local -- no mandar esto al SAT.");
                if (!valida) return 1;

                if (!autenticar)
                {
                    Console.WriteLine("(Solo verificacion local -- pasa --autenticar para mandarlo de verdad a Autenticacion.svc del SAT.)");
                    return 0;
                }

                if (!string.IsNullOrEmpty(conexionSql))
                {
                    conn = new SqlConnection(conexionSql);
                    conn.Open();
                    EsquemaSql.Asegurar(conn);
                    Console.WriteLine("Conectado a la BD propia -- esquema verificado/creado.");
                }

                Console.WriteLine("Mandando Autentica al SAT real (" + rfc + ")...");
                var resultado = await SatSoapClient.AutenticarAsync(cert, llave);
                if (!resultado.Exito)
                {
                    Console.Error.WriteLine("ERROR de Autenticacion: " + resultado.Error);
                    if (!string.IsNullOrEmpty(resultado.RespuestaCruda))
                        Console.Error.WriteLine("Respuesta cruda del SAT:\n" + resultado.RespuestaCruda);
                    return 1;
                }

                Console.WriteLine("Autenticacion OK. Token (primeros 40 caracteres): " + resultado.Token.Substring(0, Math.Min(40, resultado.Token.Length)) + "...");

                if (!string.IsNullOrEmpty(idPaquete))
                {
                    string destinoZip = salidaZip ?? (idPaquete.Replace(":", "_") + ".zip");
                    Console.WriteLine("Descargando paquete " + idPaquete + " (max. 2 veces por paquete, vive 72h)...");
                    var desc = await SatSoapClient.DescargarAsync(cert, llave, resultado.Token, idPaquete, rfc);
                    if (!desc.Exito)
                    {
                        Console.Error.WriteLine("ERROR de Descarga: " + desc.Error);
                        if (!string.IsNullOrEmpty(desc.RespuestaCruda))
                            Console.Error.WriteLine("Respuesta cruda del SAT:\n" + desc.RespuestaCruda);
                        return 1;
                    }

                    File.WriteAllBytes(destinoZip, desc.PaqueteZip);
                    Console.WriteLine("CodEstatus=" + desc.CodEstatus + " Mensaje=" + desc.Mensaje);
                    Console.WriteLine("Paquete guardado en: " + destinoZip + " (" + desc.PaqueteZip.Length + " bytes)");

                    if (conn != null)
                    {
                        BrosSatDb.RegistrarPaquete(conn, idSolicitud ?? "", idPaquete);

                        string carpetaXml = Path.Combine("xml", idPaquete.Replace(":", "_"));
                        Directory.CreateDirectory(carpetaXml);
                        using (var zip = new ZipArchive(new MemoryStream(desc.PaqueteZip), ZipArchiveMode.Read))
                        {
                            int nuevos = 0, yaExistian = 0, errores = 0;
                            foreach (var entrada in zip.Entries)
                            {
                                if (!entrada.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
                                string rutaXml = Path.Combine(carpetaXml, entrada.Name);
                                entrada.ExtractToFile(rutaXml, overwrite: true);
                                try
                                {
                                    var parseado = CfdiXmlParser.Parsear(File.ReadAllText(rutaXml));
                                    int cfdiId = BrosSatDb.GuardarCfdiRecibido(conn, parseado, Path.GetFullPath(rutaXml), out bool esNuevo);
                                    if (esNuevo)
                                    {
                                        BrosSatDb.GuardarRelaciones(conn, parseado);
                                        BrosSatDb.GuardarConceptos(conn, cfdiId, parseado.Conceptos);
                                        BrosSatDb.GuardarPagosDocumentos(conn, cfdiId, parseado.PagosDocumentos);
                                        nuevos++;
                                    }
                                    else yaExistian++;
                                }
                                catch (Exception exParse)
                                {
                                    errores++;
                                    Console.Error.WriteLine("  No se pudo parsear " + entrada.Name + ": " + exParse.Message);
                                }
                            }
                            Console.WriteLine("BD: " + nuevos + " CFDI nuevos, " + yaExistian + " ya existian, " + errores + " con error de parseo.");
                        }
                    }
                    return 0;
                }

                if (!string.IsNullOrEmpty(idSolicitud))
                {
                    Console.WriteLine("Consultando estatus de IdSolicitud=" + idSolicitud + " (no gasta cupo diario)...");
                    var verif = await SatSoapClient.VerificarSolicitudAsync(cert, llave, resultado.Token, idSolicitud, rfc);
                    if (!verif.Exito)
                    {
                        Console.Error.WriteLine("ERROR de VerificaSolicitud: " + verif.Error);
                        if (!string.IsNullOrEmpty(verif.RespuestaCruda))
                            Console.Error.WriteLine("Respuesta cruda del SAT:\n" + verif.RespuestaCruda);
                        return 1;
                    }

                    Console.WriteLine("EstadoSolicitud=" + verif.EstadoSolicitud + " (1=Aceptada 2=EnProceso 3=Terminada 4=Error 5=Rechazada 6=Vencida)");
                    Console.WriteLine("CodEstatus=" + verif.CodEstatus + " Mensaje=" + verif.Mensaje + " NumeroCFDIs=" + verif.NumeroCFDIs);

                    if (conn != null)
                    {
                        string[] nombresEstado = { "", "Aceptada", "EnProceso", "Terminada", "Error", "Rechazada", "Vencida" };
                        int numEstado;
                        string estadoTexto = int.TryParse(verif.EstadoSolicitud, out numEstado) && numEstado >= 1 && numEstado <= 6
                            ? nombresEstado[numEstado] : verif.EstadoSolicitud;
                        int? numeroCfdis = int.TryParse(verif.NumeroCFDIs, out var n) ? n : (int?)null;
                        BrosSatDb.ActualizarEstatusSolicitud(conn, idSolicitud, estadoTexto, numeroCfdis);
                    }

                    if (verif.IdsPaquetes.Count > 0)
                    {
                        Console.WriteLine("Paquetes listos para descargar:");
                        foreach (var id in verif.IdsPaquetes) Console.WriteLine("  " + id);
                    }
                    else
                    {
                        Console.WriteLine("Todavia sin paquetes listos -- normal si EstadoSolicitud sigue en 1 o 2, vuelve a intentar mas tarde.");
                    }
                    return 0;
                }

                if (!solicitar)
                {
                    Console.WriteLine("(Autenticacion confirmada. Pasa --solicitar para hacer la SolicitaDescarga real, o --idsolicitud <id> para consultar una ya hecha.)");
                    return 0;
                }

                // Rango de prueba deliberadamente chico: ayer completo, solo RECIBIDOS.
                var hasta = DateTime.Today.AddDays(-1).AddHours(23).AddMinutes(59).AddSeconds(59);
                var desde = DateTime.Today.AddDays(-1);
                Console.WriteLine("Mandando SolicitaDescarga (RECIBIDOS, TipoSolicitud=" + tipoSolicitud + ", " + desde.ToString("yyyy-MM-dd") + ")... esto gasta una solicitud real de tu cupo diario.");

                var solic = await SatSoapClient.SolicitarDescargaAsync(
                    cert, llave, resultado.Token, rfcSolicitante: rfc, rfcEmisor: null, rfcReceptor: rfc, desde: desde, hasta: hasta, tipoSolicitud: tipoSolicitud);

                if (!solic.Exito)
                {
                    Console.Error.WriteLine("ERROR de SolicitaDescarga: " + solic.Error);
                    if (!string.IsNullOrEmpty(solic.RespuestaCruda))
                        Console.Error.WriteLine("Respuesta cruda del SAT:\n" + solic.RespuestaCruda);
                    return 1;
                }

                Console.WriteLine("SolicitaDescarga aceptada. IdSolicitud: " + solic.IdSolicitud);
                Console.WriteLine("El SAT tarda de minutos a horas en dejarla lista. Consulta el estatus despues con:");
                Console.WriteLine("  --autenticar --idsolicitud " + solic.IdSolicitud);

                if (conn != null)
                    BrosSatDb.RegistrarSolicitud(conn, solic.IdSolicitud, rfc, "Recibidos", desde, hasta, "Manual");

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                return 1;
            }
            finally
            {
                conn?.Dispose();
            }
        }

        // Una pasada de actualizacion de estatus (Vigente/Cancelado) contra el servicio publico de
        // consulta del SAT (sin FIEL, sin cupo). Revisa primero los CFDI con la revision mas vieja
        // (ver BrosSatDb.ObtenerCfdiParaVerificar), con 4 consultas en paralelo, 2 reintentos por
        // CFDI y un tope de tiempo: lo que no alcance se atiende en la siguiente pasada, asi los
        // estatus se mantienen al dia de forma continua en vez de en una sola corrida diaria.
        // ---- Salud (pantalla Salud, bandeja y servicio) ----

        // Anota que el servicio hizo algo (clave = "Latido", "PasadaDescargas"...). Nunca lanza.
        public static void AnotarEstado(string conexionSql, string clave)
        {
            try
            {
                using (var conn = new SqlConnection(conexionSql))
                {
                    conn.Open();
                    EsquemaSql.Asegurar(conn);
                    BrosSatDb.GuardarEstado(conn, clave);
                }
            }
            catch { }
        }

        public static string ClaveLatido => Salud.ClaveLatido;
        public static string ClavePasadaDescargas => Salud.ClavePasadaDescargas;
        public static string ClavePasadaSolicitudes => Salud.ClavePasadaSolicitudes;
        public static string ClavePasadaEstatus => Salud.ClavePasadaEstatus;

        // Hallazgos NO "Ok" de todas las empresas activas, para que el servicio los anote en la
        // bitacora y en el Registro de eventos de Windows. Todo local.
        public static System.Collections.Generic.List<(string Empresa, string Nivel, string Tema, string Detalle)> HallazgosDeSalud(string conexionSql)
        {
            var salida = new System.Collections.Generic.List<(string, string, string, string)>();
            using (var conn = new SqlConnection(conexionSql))
            {
                conn.Open();
                EsquemaSql.Asegurar(conn);
                foreach (var e in BrosSatDb.ObtenerEmpresas(conn).Where(x => x.Activa))
                    foreach (var h in Salud.Evaluar(conn, e).Where(h => h.Nivel != NivelSalud.Ok))
                        salida.Add((e.Nombre + " (" + e.RFC + ")", h.Nivel == NivelSalud.Critico ? "CRITICO" : "AVISO", h.Tema, h.Detalle));
            }
            return salida;
        }

        public static async Task<int> VerificarEstatusAsync(string conexionSql, int limite = 800, int minutosMaximo = 12)
        {
            List<CfdiDetalleFila> cfdis;
            using (var conn = new SqlConnection(conexionSql))
            {
                conn.Open();
                EsquemaSql.Asegurar(conn);
                cfdis = BrosSatDb.ObtenerCfdiParaVerificar(conn, limite);
            }
            Bitacora.Escribir(cfdis.Count + " CFDI a verificar (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ").");
            if (cfdis.Count == 0) return 0;

            int cambios = 0, sinCambio = 0, sinTotal = 0, errores = 0, noEncontrados = 0, pendientes = 0;
            var limiteTiempo = DateTime.UtcNow.AddMinutes(minutosMaximo);
            using (var cupo = new System.Threading.SemaphoreSlim(4))
            {
                var tareas = cfdis.Select(async c =>
                {
                    await cupo.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        if (DateTime.UtcNow > limiteTiempo) { System.Threading.Interlocked.Increment(ref pendientes); return; }

                        bool esPago = string.Equals(c.TipoComprobante, "P", StringComparison.OrdinalIgnoreCase);
                        if (!esPago && c.Total == null) { System.Threading.Interlocked.Increment(ref sinTotal); return; }

                        SatConsultaEstatusResultado r = null;
                        for (int intento = 1; intento <= 3; intento++)
                        {
                            r = await SatSoapClient.ConsultarEstatusCfdiAsync(c.UUID.ToString(), c.RFCEmisor, c.RFCReceptor, BrosSatDb.TotalParaVerificarEstatus(c)).ConfigureAwait(false);
                            if (r.Exito) break;
                            await Task.Delay(1500 * intento).ConfigureAwait(false);
                        }
                        if (!r.Exito)
                        {
                            System.Threading.Interlocked.Increment(ref errores);
                            Bitacora.EscribirError("  " + c.UUID + ": ERROR " + r.Error);
                            return;
                        }

                        using (var conn = new SqlConnection(conexionSql))
                        {
                            conn.Open();
                            BrosSatDb.ActualizarEstatusCfdi(conn, c.CfdiID, r.Estado, r.EstatusCancelacion, "Consulta", r.ValidacionEFOS);
                        }

                        if (r.Estado != "Vigente" && r.Estado != "Cancelado")
                        {
                            System.Threading.Interlocked.Increment(ref noEncontrados);
                            Bitacora.Escribir("  " + c.UUID + ": el SAT contesto '" + r.Estado + "' (" + r.CodigoEstatus + ") -- se conserva '" + c.EstatusSat + "'.");
                        }
                        else if (r.Estado != c.EstatusSat)
                        {
                            System.Threading.Interlocked.Increment(ref cambios);
                            Bitacora.Escribir("  " + c.UUID + ": " + c.EstatusSat + " -> " + r.Estado + (string.IsNullOrEmpty(r.EstatusCancelacion) ? "" : " (" + r.EstatusCancelacion + ")"));
                        }
                        else System.Threading.Interlocked.Increment(ref sinCambio);
                    }
                    catch (Exception ex)
                    {
                        System.Threading.Interlocked.Increment(ref errores);
                        Bitacora.EscribirError("  " + c.UUID + ": ERROR " + ex.Message);
                    }
                    finally { cupo.Release(); }
                }).ToList();
                await Task.WhenAll(tareas).ConfigureAwait(false);
            }

            Bitacora.Escribir(cambios + " con cambio de estatus, " + sinCambio + " sin cambio, " + noEncontrados + " que el SAT no ubico, " + sinTotal +
                " sin Total, " + errores + " con error, " + pendientes + " para la siguiente pasada.");
            return errores > 0 ? 1 : 0;
        }

        // Diagnostico puntual (2026-08-14): confirmar en vivo el formato del TXT que regresa
        // TipoSolicitud=Metadata, y si incluye CFDI cancelados (que TipoSolicitud=CFDI nunca
        // trae -- el SAT exige EstadoComprobante=Vigente para ese tipo, ver SatFirmaXml.
        // FirmarSolicitud). Usa el catalogo Empresa/DPAPI en vez de --cer/--key/--password para
        // no manejar la contraseña de la FIEL fuera del mecanismo ya establecido. SI gasta cupo
        // diario real (aprobado explicitamente por el usuario antes de correrlo).
        private static async Task<int> SolicitarMetadataCatalogoAsync(string conexionSql, string rfc)
        {
            using (var conn = new SqlConnection(conexionSql))
            {
                conn.Open();
                EsquemaSql.Asegurar(conn);

                var empresa = BrosSatDb.ObtenerEmpresas(conn).FirstOrDefault(e => e.RFC == rfc);
                if (empresa == null)
                {
                    Console.Error.WriteLine("No hay ninguna empresa con RFC=" + rfc + " en el catalogo.");
                    return 1;
                }

                string passwordEmpresa = DpapiHelper.Descifrar(empresa.PasswordCifrada);
                var cert = SatFirmaXml.CargarFiel(empresa.RutaCer, empresa.RutaKey, passwordEmpresa, out var llave);
                using (llave)
                {
                    var auth = await SatSoapClient.AutenticarAsync(cert, llave);
                    if (!auth.Exito)
                    {
                        Console.Error.WriteLine("ERROR de Autenticacion: " + auth.Error);
                        return 1;
                    }

                    var hasta = DateTime.Today.AddDays(-1).AddHours(23).AddMinutes(59).AddSeconds(59);
                    var desde = DateTime.Today.AddDays(-1);
                    Console.WriteLine("Mandando SolicitaDescarga (Recibidos, TipoSolicitud=Metadata, " + desde.ToString("yyyy-MM-dd") + ", RFC=" + rfc + ")... esto gasta una solicitud real de tu cupo diario.");

                    var solic = await SatSoapClient.SolicitarDescargaAsync(
                        cert, llave, auth.Token, rfcSolicitante: rfc, rfcEmisor: null, rfcReceptor: rfc, desde: desde, hasta: hasta, tipoSolicitud: "Metadata");

                    if (!solic.Exito)
                    {
                        Console.Error.WriteLine("ERROR de SolicitaDescarga: " + solic.Error);
                        if (!string.IsNullOrEmpty(solic.RespuestaCruda))
                            Console.Error.WriteLine("Respuesta cruda del SAT:\n" + solic.RespuestaCruda);
                        return 1;
                    }

                    // Se registra en la BD como cualquier otra solicitud -- si no, no aparece en
                    // la cola de la UI y el usuario no tiene forma de verla ni de saber que existe
                    // (encontrado en vivo: "cuando me abres la ventana no salen las nuevas
                    // peticiones"). Ya no es un diagnostico desechable, es una solicitud real.
                    BrosSatDb.RegistrarSolicitud(conn, solic.IdSolicitud, rfc, "Recibidos", desde, hasta, "Manual", "Metadata");

                    Console.WriteLine("SolicitaDescarga (Metadata) aceptada. IdSolicitud: " + solic.IdSolicitud);
                    Console.WriteLine("Ya se registro en la BD -- se puede ver en la cola de solicitudes de la UI. Tambien se puede consultar con:");
                    Console.WriteLine("  --verificar-metadata-catalogo --conn <cadena> --rfc " + rfc + " --idsolicitud " + solic.IdSolicitud);
                    return 0;
                }
            }
        }

        private static async Task<int> VerificarMetadataCatalogoAsync(string conexionSql, string rfc, string idSolicitud)
        {
            using (var conn = new SqlConnection(conexionSql))
            {
                conn.Open();

                var empresa = BrosSatDb.ObtenerEmpresas(conn).FirstOrDefault(e => e.RFC == rfc);
                if (empresa == null)
                {
                    Console.Error.WriteLine("No hay ninguna empresa con RFC=" + rfc + " en el catalogo.");
                    return 1;
                }

                string passwordEmpresa = DpapiHelper.Descifrar(empresa.PasswordCifrada);
                var cert = SatFirmaXml.CargarFiel(empresa.RutaCer, empresa.RutaKey, passwordEmpresa, out var llave);
                using (llave)
                {
                    var auth = await SatSoapClient.AutenticarAsync(cert, llave);
                    if (!auth.Exito)
                    {
                        Console.Error.WriteLine("ERROR de Autenticacion: " + auth.Error);
                        return 1;
                    }

                    var verif = await SatSoapClient.VerificarSolicitudAsync(cert, llave, auth.Token, idSolicitud, rfc);
                    if (!verif.Exito)
                    {
                        Console.Error.WriteLine("ERROR de VerificaSolicitud: " + verif.Error);
                        return 1;
                    }

                    string[] nombresEstado = { "", "Aceptada", "EnProceso", "Terminada", "Error", "Rechazada", "Vencida" };
                    string estadoTexto = int.TryParse(verif.EstadoSolicitud, out var ne) && ne >= 1 && ne <= 6 ? nombresEstado[ne] : verif.EstadoSolicitud;
                    Console.WriteLine("EstadoSolicitud=" + verif.EstadoSolicitud + " (" + estadoTexto + ") CodEstatus=" + verif.CodEstatus + " Mensaje=" + verif.Mensaje + " NumeroCFDIs=" + verif.NumeroCFDIs);
                    if (verif.IdsPaquetes.Count == 0)
                    {
                        Console.WriteLine("Todavia sin paquetes listos -- vuelve a intentar mas tarde.");
                        return 0;
                    }

                    foreach (var idPaquete in verif.IdsPaquetes)
                    {
                        Console.WriteLine("Descargando paquete " + idPaquete + "...");
                        var desc = await SatSoapClient.DescargarAsync(cert, llave, auth.Token, idPaquete, rfc);
                        if (!desc.Exito)
                        {
                            Console.Error.WriteLine("  ERROR de Descarga: " + desc.Error);
                            continue;
                        }

                        string carpeta = Path.Combine("metadata_prueba", idPaquete.Replace(":", "_"));
                        Directory.CreateDirectory(carpeta);
                        using (var zip = new ZipArchive(new MemoryStream(desc.PaqueteZip), ZipArchiveMode.Read))
                        {
                            foreach (var entrada in zip.Entries)
                            {
                                string rutaDestino = Path.Combine(carpeta, entrada.Name);
                                entrada.ExtractToFile(rutaDestino, overwrite: true);
                                Console.WriteLine("  Guardado: " + rutaDestino);
                            }
                        }
                    }
                    return 0;
                }
            }
        }

        // Evita que 2 corridas del MISMO modo se traslapen (p.ej. la Tarea Programada dispara
        // --auto-todas cada 10 min pero una corrida tarda mas por un paquete grande) -- sin esto,
        // dos pasadas simultaneas podrian pisarse al registrar el mismo paquete/solicitud. Cada
        // modo usa su propio nombre de recurso, asi que --auto-todas y --auto-solicitar-todas SI
        // pueden correr al mismo tiempo (no compiten entre si), solo bloquean contra su propia
        // clase. LockTimeout=0 = si ya esta tomado, regresa de inmediato (no espera) -- la corrida
        // nueva simplemente se sale sin hacer nada, la Tarea Programada ya la reintenta despues.
        private static bool IntentarTomarCandado(SqlConnection conn, string recurso)
        {
            using (var cmd = new SqlCommand("sp_getapplock", conn) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                cmd.Parameters.AddWithValue("@Resource", recurso);
                cmd.Parameters.AddWithValue("@LockMode", "Exclusive");
                cmd.Parameters.AddWithValue("@LockOwner", "Session");
                cmd.Parameters.AddWithValue("@LockTimeout", 0);
                var retorno = cmd.Parameters.Add("@Retorno", System.Data.SqlDbType.Int);
                retorno.Direction = System.Data.ParameterDirection.ReturnValue;
                cmd.ExecuteNonQuery();
                return (int)retorno.Value >= 0; // 0 o 1 = candado adquirido; negativo = ya estaba tomado o error
            }
        }

        private static void LiberarCandado(SqlConnection conn, string recurso)
        {
            using (var cmd = new SqlCommand("sp_releaseapplock", conn) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                cmd.Parameters.AddWithValue("@Resource", recurso);
                cmd.Parameters.AddWithValue("@LockOwner", "Session");
                cmd.ExecuteNonQuery();
            }
        }

        private static async Task<int> SincronizarComercial(string conexionSql, string rfcFiltro)
        {
            using (var conn = new SqlConnection(conexionSql))
            {
                conn.Open();
                EsquemaSql.Asegurar(conn);

                var empresas = BrosSatDb.ObtenerEmpresas(conn).Where(e => e.Activa);
                if (!string.IsNullOrEmpty(rfcFiltro)) empresas = empresas.Where(e => e.RFC == rfcFiltro);
                var lista = empresas.ToList();

                if (lista.Count == 0)
                {
                    Bitacora.Escribir("Ninguna empresa activa para sincronizar" + (string.IsNullOrEmpty(rfcFiltro) ? "." : " con RFC=" + rfcFiltro + "."));
                    return 0;
                }

                int totalImportados = 0, totalCopiados = 0, totalErrores = 0;
                foreach (var empresa in lista)
                {
                    var r = await ComercialSync.SincronizarTodoAsync(conn, empresa);
                    totalImportados += r.ImportadosRecibidos + r.ImportadosEmitidos;
                    totalCopiados += r.CopiadosRecibidos + r.CopiadosEmitidos;
                    totalErrores += r.Errores;
                }

                Bitacora.Escribir("Sincronizacion con Comercial terminada: " + totalImportados + " importados directo, " + totalCopiados + " copiados a carpeta, " + totalErrores + " con error.");
                return totalErrores > 0 ? 1 : 0;
            }
        }

        public static async Task<int> AutoTodasAsync(string conexionSql)
        {
            using (var conn = new SqlConnection(conexionSql))
            {
                conn.Open();
                EsquemaSql.Asegurar(conn);

                const string candado = "BrosLMV_AutoTodas";
                if (!IntentarTomarCandado(conn, candado))
                {
                    Bitacora.Escribir("Ya hay una corrida de --auto-todas en curso -- se omite esta.");
                    return 0;
                }

                try
                {
                    var empresas = BrosSatDb.ObtenerEmpresas(conn).Where(e => e.Activa).ToList();
                    Bitacora.Escribir(empresas.Count + " empresa(s) activa(s) en el catalogo (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ").");

                    int errores = 0;
                    foreach (var empresa in empresas)
                    {
                        Bitacora.Escribir("--- " + empresa.Nombre + " (" + empresa.RFC + ") ---");
                        // Autoreparacion barata: archivos que quedaron con el nombre literal "[UUID]" se renombran
                        // (el servicio corre con permisos para tocar lo que el mismo creo).
                        try { OrganizadorArchivos.RepararNombresLiterales(conn, empresa.RFC); } catch { }
                        try
                        {
                            string passwordEmpresa = DpapiHelper.Descifrar(empresa.PasswordCifrada);
                            var cert = SatFirmaXml.CargarFiel(empresa.RutaCer, empresa.RutaKey, passwordEmpresa, out var llave);
                            using (llave)
                                await SolicitudWorker.EjecutarPasadaAsync(conn, cert, llave,
                                    empresa.CarpetaXml, empresa.EstructuraCarpetas, empresa.PlantillaNombreArchivo, empresa.RFC,
                                    empresa.ComercialCarpetaXmlRecibidos, empresa.ComercialCarpetaXmlEmitidos, empresa.ComercialConexionSql);
                            await ComercialSync.PropagarCancelacionesAsync(conn, empresa).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            errores++;
                            // Una empresa con problemas (FIEL vencida, password mal descifrada, etc.)
                            // no debe tumbar la corrida completa -- las demas se procesan igual.
                            Bitacora.EscribirError("  ERROR: " + ex.Message);
                        }
                    }

                    Bitacora.Escribir((empresas.Count - errores) + " empresa(s) procesada(s) sin error, " + errores + " con error.");
                    return errores > 0 ? 1 : 0;
                }
                finally
                {
                    LiberarCandado(conn, candado);
                }
            }
        }

        // La UNICA pieza que crea solicitudes nuevas de forma automatica -- ver el comentario del
        // flag --auto-solicitar-todas arriba. Calcula el rango pendiente por empresa via
        // BrosSatDb.ObtenerUltimaFechaCubierta y pide SOLO el primer tramo de
        // SolicitudChunker.PartirEnMeses -- acota el gasto a 2 solicitudes (CFDI+Metadata) por
        // empresa por corrida, sin importar que tan atrasada este. Si un tramo sale Rechazada/
        // Error/Vencida, o Terminada con un paquete que se quedo con error, la fecha cubierta NO
        // avanza -- la siguiente corrida lo vuelve a pedir solo, sin logica de reintento aparte.
        public static async Task<int> AutoSolicitarTodasAsync(string conexionSql)
        {
            using (var conn = new SqlConnection(conexionSql))
            {
                conn.Open();
                EsquemaSql.Asegurar(conn);

                const string candado = "BrosLMV_AutoSolicitarTodas";
                if (!IntentarTomarCandado(conn, candado))
                {
                    Bitacora.Escribir("Ya hay una corrida de --auto-solicitar-todas en curso -- se omite esta.");
                    return 0;
                }

                try
                {
                    var empresas = BrosSatDb.ObtenerEmpresas(conn).Where(e => e.Activa).ToList();
                    Bitacora.Escribir(empresas.Count + " empresa(s) activa(s) en el catalogo (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ").");

                    int errores = 0;
                    foreach (var empresa in empresas)
                    {
                        Bitacora.Escribir("--- " + empresa.Nombre + " (" + empresa.RFC + ") ---");
                        errores += await AutoSolicitador.SolicitarSiguienteTramoAsync(conn, empresa);
                    }

                    Bitacora.Escribir((empresas.Count - errores) + " empresa(s) procesada(s) sin error, " + errores + " con error.");
                    return errores > 0 ? 1 : 0;
                }
                finally
                {
                    LiberarCandado(conn, candado);
                }
            }
        }

        private static int Reparsear(string conexionSql)
        {
            using (var conn = new SqlConnection(conexionSql))
            {
                conn.Open();
                EsquemaSql.Asegurar(conn);

                var rutas = BrosSatDb.ObtenerRutasXmlParaReparseo(conn);
                Console.WriteLine(rutas.Count + " CFDI con XML en disco.");

                int actualizados = 0, sinArchivo = 0, errores = 0;
                foreach (var (cfdiId, ruta) in rutas)
                {
                    if (!File.Exists(ruta))
                    {
                        sinArchivo++;
                        continue;
                    }
                    try
                    {
                        var parseado = CfdiXmlParser.Parsear(File.ReadAllText(ruta));
                        BrosSatDb.ActualizarCamposParseados(conn, cfdiId, parseado);
                        BrosSatDb.GuardarConceptos(conn, cfdiId, parseado.Conceptos);
                        BrosSatDb.GuardarPagosDocumentos(conn, cfdiId, parseado.PagosDocumentos);
                        actualizados++;
                    }
                    catch (Exception ex)
                    {
                        errores++;
                        Console.Error.WriteLine("  CfdiID=" + cfdiId + " (" + ruta + "): " + ex.Message);
                    }
                }

                Console.WriteLine(actualizados + " actualizados, " + sinArchivo + " sin XML en disco, " + errores + " con error de parseo.");
                return errores > 0 ? 1 : 0;
            }
        }

        private static string Siguiente(string[] args, ref int i)
        {
            if (i + 1 >= args.Length) throw new ArgumentException("Falta el valor para " + args[i]);
            return args[++i];
        }

        private static int Uso()
        {
            Console.Error.WriteLine(
                "Uso: BrosLMV.Descargas --cer <ruta.cer> --key <ruta.key> --password <contrasena> [--rfc <RFC>] [--conn <cadena SQL>] [--salida <archivo.xml>] [--autenticar] [--solicitar | --idsolicitud <id> | --idpaquete <id> [--salida-zip <archivo.zip>] | --auto]\n" +
                "   o: BrosLMV.Descargas --reparsear --conn <cadena SQL>\n" +
                "   o: BrosLMV.Descargas --auto-todas --conn <cadena SQL>\n" +
                "   o: BrosLMV.Descargas --verificar-estatus --conn <cadena SQL>\n" +
                "   o: BrosLMV.Descargas --auto-solicitar-todas --conn <cadena SQL>\n" +
                "   o: BrosLMV.Descargas --inicializar --conn <cadena SQL>\n\n" +
                "Sin flags: arma y firma el sobre Autentica, lo guarda en disco y lo verifica localmente. No toca la red.\n" +
                "Con --autenticar: ademas lo manda de verdad a Autenticacion.svc del SAT real (no gasta cupo diario).\n" +
                "Con --autenticar --solicitar: ademas hace SolicitaDescarga real (RECIBIDOS de ayer) -- SI gasta cupo diario.\n" +
                "Con --autenticar --idsolicitud <id>: consulta el estatus de una solicitud ya hecha -- no gasta cupo diario.\n" +
                "Con --autenticar --idpaquete <id>: descarga el ZIP de un paquete ya listo -- max. 2 descargas por paquete, vive 72h.\n" +
                "Con --conn <cadena>: ademas de imprimir en consola, registra todo en la BD propia (crea el esquema si falta).\n" +
                "Con --auto --conn <cadena>: una pasada del motor de cola -- revisa TODO lo pendiente en la BD, verifica y descarga. Pensado para Tarea Programada. No requiere --rfc.\n" +
                "--reparsear --conn <cadena>: relee del disco los XML ya descargados y los vuelve a parsear -- rellena campos agregados despues de la descarga original (TipoCambio, Retenciones, etc). No toca la red, no requiere FIEL.\n" +
                "--auto-todas --conn <cadena>: como --auto pero para TODAS las empresas activas del catalogo -- lee cada FIEL del catalogo Empresa y descifra su password con DPAPI, no toma --cer/--key/--password. Pensado para UNA Tarea Programada que cubra todas las empresas sin passwords en texto plano. No gasta cupo diario.\n" +
                "--verificar-estatus --conn <cadena>: refresca EstatusSat (Vigente/Cancelado) de TODOS los CFDI ya descargados contra el servicio PUBLICO del SAT (el mismo que valida el QR impreso) -- no requiere FIEL ni token, no gasta cupo diario.\n" +
                "--solicitar-metadata-catalogo --conn <cadena> --rfc <RFC>: diagnostico puntual -- SolicitaDescarga con TipoSolicitud=Metadata usando la FIEL del catalogo (DPAPI). SI gasta cupo diario.\n" +
                "--verificar-metadata-catalogo --conn <cadena> --rfc <RFC> --idsolicitud <id>: verifica y descarga el resultado de la solicitud de arriba, guarda los TXT crudos en metadata_prueba\\ para inspeccionar el formato.\n" +
                "--auto-solicitar-todas --conn <cadena>: UNICO modo que CREA solicitudes nuevas de forma automatica -- calcula que tan atrasada esta cada empresa y pide el siguiente tramo pendiente (CFDI+Metadata, max 2 solicitudes por empresa por corrida). SI gasta cupo diario. Pensado para UNA Tarea Programada aparte, 1 vez al dia.\n" +
                "--inicializar --conn <cadena>: crea la base de datos (si falta) y el esquema, y sale -- no requiere FIEL. Pensado para el instalador, antes de crear las Tareas Programadas.\n" +
                "--exportar-respaldo <archivo> --conn <cadena>: respaldo CIFRADO (AES-256-GCM) de las empresas: FIEL, contrasenas, carpetas y conexion a Comercial. La contrasena se pide en la consola o se toma de BROSLMV_RESPALDO_CLAVE.\n" +
                "--importar-respaldo <archivo> --conn <cadena> [--carpeta-fiel <dir>]: restaura ese respaldo (por ejemplo en un servidor nuevo); las contrasenas se vuelven a cifrar con DPAPI de esta maquina.\n" +
                "--vinculos --conn <cadena> [--rfc <RFC>]: cruza la lista de XML de Comercial con sus documentos (CFDI sin documento + sugerencias, totales distintos). Solo lee Comercial.\n" +
                "--conciliacion --conn <cadena> [--rfc <RFC>]: tabla mensual SAT (Metadata) contra XML descargados, en CSV. Solo lee la base local.\n" +
                "--salud --conn <cadena>: muestra la salud de cada empresa activa (servicio, huecos, faltantes, estatus, archivos, Comercial); termina con codigo 2 si hay algo critico. Solo lee la base local.\n" +
                "--sincronizar-comercial --conn <cadena> [--rfc <RFC>]: copia hacia las carpetas de Comercial (Ruta XML Recibidos/Emitidos) TODOS los CFDI ya descargados en la BD -- no gasta cupo, no vuelve a pedir nada al SAT. Backfill de una sola vez para CFDI descargados antes de tener la copia automatica configurada. Sin --rfc procesa todas las empresas activas.");
            return 2;
        }
    }
}
