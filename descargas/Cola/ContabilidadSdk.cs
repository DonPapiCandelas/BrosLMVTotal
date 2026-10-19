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

// ContabilidadSdk.cs -- escribir polizas en CONTPAQi Contabilidad con su SDK COM (SDKCONTPAQNG.TSdkSesion y compania).
// El SDK es un servidor COM de 32 bits FUERA de proceso: este programa (64 bits) lo usa directo, con enlace tardio (dynamic).
// Todo se probo en vivo el 2026-10-18; ver docs\SDK_CONTABILIDAD.md. SOLO escribe lo que se le pide: la lectura de catalogos y la
// confirmacion de lo escrito se hacen por SQL (solo lectura), no aqui.

using System;
using System.Collections.Generic;
using System.Threading;

namespace BrosLMV.Descargas.Cola
{
    internal enum TipoPolizaSdk { Ingresos = 1, Egresos = 2, Diario = 3, Orden = 4, Estadisticas = 5 }

    internal sealed class MovimientoSdk
    {
        public string Cuenta;          // codigo de la cuenta de Contabilidad (sin guiones)
        public bool Cargo;             // true = cargo, false = abono
        public decimal Importe;        // en la moneda de la cuenta (pesos o la extranjera segun la cuenta)
        public decimal ImporteME;      // importe en moneda extranjera (0 si no aplica)
        public string Referencia;
        public string Concepto;
        public string Segmento;        // codigo del segmento de negocio (obligatorio en algunas empresas)
        // Resultado
        public int NumMovto;
        public string Guid;
    }

    internal sealed class PolizaSdk
    {
        public TipoPolizaSdk Tipo = TipoPolizaSdk.Diario;
        public bool Afectar = true;    // false = «sin afectar»
        public DateTime Fecha;
        public string Concepto;
        public int Numero;             // 0 = automatico (siguiente folio del tipo y periodo)
        public int Diario;             // 0 = sin diario especial
        public List<MovimientoSdk> Movimientos = new List<MovimientoSdk>();
        // Resultado
        public long Id;
        public int Ejercicio, Periodo;
        public string Guid;
    }

    internal sealed class ResultadoSdk
    {
        public bool Ok;
        public string Error;
        public static ResultadoSdk Bien() => new ResultadoSdk { Ok = true };
        public static ResultadoSdk Mal(string e) => new ResultadoSdk { Ok = false, Error = e };
    }

    // Una sesion del SDK abierta sobre UNA empresa de Contabilidad. Se crea con Abrir(...) y se cierra con Dispose().
    // Los objetos COM viven en un hilo STA propio: todas las llamadas se ejecutan alli (EnHilo).
    internal sealed class ContabilidadSdkSesion : IDisposable
    {
        private const int SistOrigenContpaqNg = 11;
        private const int AsocPoliza = 0, AsocMovimiento = 1;

        private readonly Thread _hilo;
        private readonly Queue<Action> _cola = new Queue<Action>();
        private readonly object _candado = new object();
        private bool _fin;
        private dynamic _ses;

        private ContabilidadSdkSesion()
        {
            _hilo = new Thread(Bucle) { IsBackground = true, Name = "ContabilidadSdk" };
            _hilo.SetApartmentState(ApartmentState.STA);
            _hilo.Start();
        }

        private void Bucle()
        {
            while (true)
            {
                Action a;
                lock (_candado) { while (_cola.Count == 0 && !_fin) Monitor.Wait(_candado); if (_cola.Count == 0 && _fin) return; a = _cola.Dequeue(); }
                a();
            }
        }

        private T EnHilo<T>(Func<T> f)
        {
            T resultado = default(T); Exception error = null; var listo = new ManualResetEventSlim();
            lock (_candado) { _cola.Enqueue(() => { try { resultado = f(); } catch (Exception ex) { error = ex; } finally { listo.Set(); } }); Monitor.Pulse(_candado); }
            listo.Wait();
            if (error != null) throw new InvalidOperationException("SDK de Contabilidad: " + error.Message, error);
            return resultado;
        }

        private static dynamic Nuevo(string progId)
        {
            var t = Type.GetTypeFromProgID(progId, true);
            return Activator.CreateInstance(t);
        }

        // Empresas de Contabilidad que ve el SDK: (alias = nombre de la base ctXXX, nombre visible).
        public static List<(string alias, string nombre)> ListarEmpresas()
        {
            using (var s = new ContabilidadSdkSesion())
                return s.EnHilo(() =>
                {
                    s.Conectar(null, null);
                    var res = new List<(string, string)>();
                    dynamic lista = Nuevo("SDKCONTPAQNG.TSdkListaEmpresas");
                    if ((int)lista.buscaPrimero() != 0)
                        do { res.Add(((string)lista.NombreBDD, (string)lista.Nombre)); } while ((int)lista.buscaSiguiente() != 0);
                    return res;
                });
        }

        // alias = nombre de la base de la empresa (ctXXX). usuario/clave vacios = SUPERVISOR sin contrasena.
        public static ContabilidadSdkSesion Abrir(string alias, string usuario, string clave)
        {
            var s = new ContabilidadSdkSesion();
            try
            {
                s.EnHilo(() =>
                {
                    s.Conectar(usuario, clave);
                    int ok = (int)s._ses.abreEmpresa(alias);
                    if (ok == 0) throw new InvalidOperationException("No se pudo abrir la empresa «" + alias + "»: " + (string)s._ses.UltimoMsjError);
                    return 0;
                });
                return s;
            }
            catch { s.Dispose(); throw; }
        }

        private void Conectar(string usuario, string clave)
        {
            _ses = Nuevo("SDKCONTPAQNG.TSdkSesion");
            if ((int)_ses.conexionActiva == 0) _ses.iniciaConexion();
            if ((int)_ses.ingresoUsuario == 0) _ses.firmaUsuarioParams(string.IsNullOrWhiteSpace(usuario) ? "SUPERVISOR" : usuario, clave ?? "");
            if ((int)_ses.ingresoUsuario == 0) throw new InvalidOperationException("No se pudo iniciar sesión en Contabilidad: " + (string)_ses.UltimoMsjError);
        }

        // Crea la poliza con sus movimientos (todo o nada) y la relee para llenar Id, Ejercicio, Periodo, Numero, Guid y los Guid de los movimientos.
        public ResultadoSdk CrearPoliza(PolizaSdk p)
        {
            return EnHilo(() =>
            {
                dynamic pol = Nuevo("SDKCONTPAQNG.TSdkPoliza"); dynamic mov = Nuevo("SDKCONTPAQNG.TSdkMovimientoPoliza");
                pol.setSesion(_ses); mov.setSesion(_ses);
                pol.iniciarInfo();
                pol.Tipo = (int)p.Tipo; pol.Clase = p.Afectar ? 1 : 2; pol.Impresa = 0; pol.Fecha = p.Fecha.Date; pol.Diario = p.Diario;
                pol.SistOrigen = SistOrigenContpaqNg; pol.Ajuste = 0; pol.Concepto = p.Concepto ?? ""; pol.Numero = p.Numero;
                int n = 0;
                foreach (var m in p.Movimientos)
                {
                    mov.iniciarInfo();
                    mov.NumMovto = ++n; mov.CodigoCuenta = m.Cuenta; mov.TipoMovto = m.Cargo ? 1 : 2; mov.Importe = (double)m.Importe; mov.ImporteME = (double)m.ImporteME;
                    mov.Referencia = m.Referencia ?? ""; mov.Concepto = m.Concepto ?? "";
                    if (!string.IsNullOrEmpty(m.Segmento)) mov.SegmentoNegocio = m.Segmento;
                    if ((int)pol.agregaMovimiento(mov) == 0) return ResultadoSdk.Mal("Movimiento " + n + " (" + m.Cuenta + "): " + Texto(mov));
                }
                long id = Convert.ToInt64(pol.crea());
                if (id <= 0) return ResultadoSdk.Mal(Texto(pol));

                // Releer: crea() no deja Numero ni Guid en el objeto.
                pol.buscaPorId((int)id);
                p.Id = id; p.Ejercicio = (int)pol.Ejercicio; p.Periodo = (int)pol.Periodo; p.Numero = (int)pol.Numero; p.Guid = Convert.ToString(pol.Guid);
                dynamic mv = Nuevo("SDKCONTPAQNG.TSdkMovimientoPoliza"); mv.setSesion(_ses);
                if ((int)pol.getMovimientoPrimero(mv) != 0)
                    do
                    {
                        int num = (int)mv.NumMovto;
                        if (num >= 1 && num <= p.Movimientos.Count) { p.Movimientos[num - 1].NumMovto = num; p.Movimientos[num - 1].Guid = Convert.ToString(mv.Guid); }
                    } while ((int)pol.getMovimientoSiguiente(mv) != 0);
                return ResultadoSdk.Bien();
            });
        }

        // Asocia un CFDI (UUID, que debe estar ya en el ADD de la empresa) a una poliza (guidPoliza) o a un movimiento (guidMovimiento).
        public ResultadoSdk AsociarCfdi(string uuid, string guidRef, bool aMovimiento)
        {
            return EnHilo(() =>
            {
                dynamic a = Nuevo("SDKCONTPAQNG.TSdkAsocCFDI"); a.setSesion(_ses);
                a.iniciarInfo(); a.TipoAsoc = aMovimiento ? AsocMovimiento : AsocPoliza; a.GuidDocumento = guidRef;
                a.agregaUUID(uuid);
                return (int)a.crea() != 0 ? ResultadoSdk.Bien() : ResultadoSdk.Mal(Texto(a));
            });
        }

        // Borra una poliza (y con ella sus movimientos y asociaciones de CFDI). Sirve para «deshacer» un lote.
        public ResultadoSdk BorrarPoliza(int ejercicio, int periodo, TipoPolizaSdk tipo, int numero)
        {
            return EnHilo(() =>
            {
                dynamic pol = Nuevo("SDKCONTPAQNG.TSdkPoliza"); pol.setSesion(_ses);
                if ((int)pol.buscaPorLlavePrimaria(ejercicio, periodo, (int)tipo, numero) == 0) return ResultadoSdk.Mal("No se encontró la póliza " + tipo + " " + numero + " del " + periodo + "/" + ejercicio + ".");
                return (int)pol.borra() != 0 ? ResultadoSdk.Bien() : ResultadoSdk.Mal(Texto(pol));
            });
        }

        private static string Texto(dynamic o)
        {
            string m = null;
            try { m = Convert.ToString(o.getMensajeError()); } catch { }
            if (string.IsNullOrWhiteSpace(m)) { try { m = Convert.ToString(o.UltimoMsjError); } catch { } }
            return string.IsNullOrWhiteSpace(m) ? "el SDK no dio detalle del error" : m.Trim();
        }

        // OJO: no se llama finalizaConexion(): deja el servidor del SDK sin poder reconectar hasta que se reinicia SDKCONTPAQNG.exe.
        public void Dispose()
        {
            try { if (_ses != null) EnHilo(() => { try { _ses.cierraEmpresa(); } catch { } return 0; }); } catch { }
            lock (_candado) { _fin = true; Monitor.PulseAll(_candado); }
        }
    }
}
