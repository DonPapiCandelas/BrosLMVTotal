// PaginaTerceros.xaml.cs -- «Terceros y cuentas»: la cuenta de cada cliente/proveedor por ROL y por MONEDA (base del Contabilizador).
// Lee de Contabilidad (solo lectura) con «Sincronizar»; lo que el usuario escribe es «Manual» y no se pisa al sincronizar.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BrosLMV.Descargas.Datos;

namespace BrosLMV.DescargasUI.Paginas
{
    public partial class PaginaTerceros : UserControl
    {
        // PROPIEDADES: WPF no resuelve campos.
        public sealed class TerceroVista
        {
            internal ContaTerceroFila T;
            public string Nombre => string.IsNullOrWhiteSpace(T.Nombre) ? "(sin nombre)" : T.Nombre;
            public string Rfc => T.Rfc;
            public string Tipo => T.Tipo;
            public int NumCfdi => T.NumCfdi;
            public string Principal => T.Resumen(ContaRoles.Provision, ContaRoles.Cliente);
            public string Gasto => T.Resumen(ContaRoles.Gasto);
            public string Otras => T.Resumen(ContaRoles.PasivoComplementaria, ContaRoles.GastoComplementaria, ContaRoles.Anticipo, ContaRoles.ClienteComplementaria, ContaRoles.Ingreso);
            public string Estado => T.Origen == "CFDI" ? "No está en Contabilidad" : !T.TieneCuentaPrincipal ? "Sin cuenta" : T.Asignaciones.Select(a => a.Moneda).Distinct().Count() > 1 ? "Varias monedas" : "Con cuenta";
        }

        private readonly Sesion _s;
        private List<ContaTerceroFila> _terceros = new List<ContaTerceroFila>();
        private bool _ocupado;

        public PaginaTerceros(Sesion sesion)
        {
            InitializeComponent();
            _s = sesion;
            Loaded += (s, e) => Cargar();
        }

        private Window Ventana => Window.GetWindow(this);
        private string Rfc => _s.Empresa.RFC;

        private void Cargar()
        {
            try
            {
                _terceros = ContaDatos.ObtenerTerceros(_s.Conn, Rfc);
                var ult = ContaDatos.UltimaSincronizacion(_s.Conn, Rfc);
                var cfg = BrosSatDb.ObtenerContabilidad(_s.Conn, Rfc);
                LblEstado.Text = cfg == null ? "Esta empresa no tiene Contabilidad configurada. Configúrala en «Empresas y e.firma» → Editar."
                    : ult == null ? "Aún no has sincronizado con Contabilidad (" + cfg.BaseDatos + "). Pulsa «Sincronizar con Contabilidad»: se leen el catálogo de cuentas, los clientes y proveedores, y las cuentas que ya tienen. No se modifica nada en Contabilidad."
                    : "Última sincronización: " + ult.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + " · " + _terceros.Count.ToString("N0") + " terceros · " + _terceros.Sum(t => t.Asignaciones.Count).ToString("N0") + " cuentas asignadas. Doble clic en un tercero para ver o cambiar sus cuentas.";
                Pintar();
            }
            catch (Exception ex) { LblEstado.Text = "No se pudo leer: " + ex.Message; }
        }

        private void Pintar()
        {
            string txt = (TxtBuscar.Text ?? "").Trim().ToUpperInvariant();
            string filtro = (CmbFiltro.SelectedItem as ComboBoxItem)?.Tag as string ?? "todos";
            IEnumerable<ContaTerceroFila> q = _terceros;
            switch (filtro)
            {
                case "proveedores": q = q.Where(t => t.EsProveedor); break;
                case "clientes": q = q.Where(t => t.EsCliente); break;
                case "sincuenta": q = q.Where(t => !t.TieneCuentaPrincipal); break;
                case "monedas": q = q.Where(t => t.Asignaciones.Select(a => a.Moneda).Distinct().Count() > 1); break;
                case "cfdi": q = q.Where(t => t.Origen == "CFDI"); break;
            }
            if (txt.Length > 0) q = q.Where(t => (t.Nombre ?? "").ToUpperInvariant().Contains(txt) || (t.Rfc ?? "").ToUpperInvariant().Contains(txt) || t.Asignaciones.Any(a => a.Cuenta.Contains(txt)));
            var lista = q.Select(t => new TerceroVista { T = t }).ToList();
            Grid.ItemsSource = lista.Take(5000).ToList();
            LblConteo.Text = lista.Count.ToString("N0") + " terceros" + (lista.Count > 5000 ? " (se muestran 5,000; afina el filtro)" : "");
        }

        private void Filtro_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) Pintar(); }

        private void Grid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!(Grid.SelectedItem is TerceroVista v)) return;
            var w = new TerceroCuentasWindow(_s.Conn, Rfc, v.T) { Owner = Ventana };
            w.ShowDialog();
            if (w.Cambio) Cargar();
        }

        private async void BtnSincronizar_Click(object sender, RoutedEventArgs e)
        {
            if (_ocupado) return;
            var cfg = BrosSatDb.ObtenerContabilidad(_s.Conn, Rfc);
            if (cfg == null) { MessageBox.Show(Ventana, "Esta empresa no tiene Contabilidad configurada. Configúrala en «Empresas y e.firma» → Editar.", "Terceros y cuentas", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            _ocupado = true; BtnSincronizar.IsEnabled = false;
            try
            {
                var r = await Task.Run(() => ContaDatos.Sincronizar(_s.Conn, Rfc, cfg.BaseDatos, t => Dispatcher.Invoke(() => LblEstado.Text = t)));
                if (!r.Ok) { MessageBox.Show(Ventana, "No se pudo sincronizar:\n\n" + r.Error, "Terceros y cuentas", MessageBoxButton.OK, MessageBoxImage.Warning); }
                else MessageBox.Show(Ventana, r.Cuentas.ToString("N0") + " cuentas, " + r.Terceros.ToString("N0") + " terceros y " + r.Asignaciones.ToString("N0") + " cuentas asignadas leídas." +
                    (r.Descubiertos > 0 ? "\n" + r.Descubiertos + " terceros de tus CFDI no están en Contabilidad (filtro «Solo en tus CFDI»)." : "") + "\n\nLo que escribiste a mano se conservó.", "Terceros y cuentas", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally { _ocupado = false; BtnSincronizar.IsEnabled = true; Cargar(); }
        }

        private void BtnExportar_Click(object sender, RoutedEventArgs e)
        {
            if (_terceros.Count == 0) { MessageBox.Show(Ventana, "Primero sincroniza con Contabilidad.", "Terceros y cuentas", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Libro de Excel (*.xlsx)|*.xlsx", FileName = "Terceros y cuentas " + Rfc + " " + DateTime.Today.ToString("yyyy-MM-dd") + ".xlsx" };
            if (dlg.ShowDialog(Ventana) != true) return;
            try
            {
                ContaExcel.Exportar(dlg.FileName, _s.Empresa.Nombre, _terceros, ContaDatos.ObtenerCuentas(_s.Conn, Rfc, true));
                if (MessageBox.Show(Ventana, "Libro guardado. ¿Abrirlo?", "Terceros y cuentas", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show(Ventana, "No se pudo guardar el libro:\n\n" + ex.Message, "Terceros y cuentas", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void BtnImportar_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Libro de Excel (*.xlsx)|*.xlsx" };
            if (dlg.ShowDialog(Ventana) != true) return;
            try
            {
                var filas = ContaExcel.Leer(dlg.FileName);
                var prueba = ContaDatos.AplicarImportacion(_s.Conn, Rfc, filas, true);
                string resumen = prueba.Guardadas + " asignaciones por guardar, " + prueba.Borradas + " por borrar, " + prueba.Errores.Count + " con error, " + prueba.Avisos.Count + " avisos.";
                if (prueba.Errores.Count > 0) resumen += "\n\nErrores (esas filas se omiten):\n" + string.Join("\n", prueba.Errores.Take(12)) + (prueba.Errores.Count > 12 ? "\n… y " + (prueba.Errores.Count - 12) + " más" : "");
                if (prueba.Avisos.Count > 0) resumen += "\n\nAvisos:\n" + string.Join("\n", prueba.Avisos.Take(6)) + (prueba.Avisos.Count > 6 ? "\n… y " + (prueba.Avisos.Count - 6) + " más" : "");
                if (prueba.Guardadas + prueba.Borradas == 0) { MessageBox.Show(Ventana, resumen, "Importar de Excel", MessageBoxButton.OK, MessageBoxImage.Information); return; }
                if (MessageBox.Show(Ventana, resumen + "\n\n¿Aplicar los cambios?", "Importar de Excel", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                var r = ContaDatos.AplicarImportacion(_s.Conn, Rfc, filas, false);
                MessageBox.Show(Ventana, r.Guardadas + " asignaciones guardadas y " + r.Borradas + " borradas.", "Importar de Excel", MessageBoxButton.OK, MessageBoxImage.Information);
                Cargar();
            }
            catch (Exception ex) { MessageBox.Show(Ventana, "No se pudo leer el libro:\n\n" + ex.Message, "Importar de Excel", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
    }
}
