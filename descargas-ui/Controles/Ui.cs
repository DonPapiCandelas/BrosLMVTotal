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

using System.Windows;

namespace BrosLMV.DescargasUI.Controles
{
    // Propiedades adjuntas del sistema de diseno (ver Estilos/Controles.xaml):
    //   Ui.Glifo  -> ícono (glifo de Segoe MDL2 Assets) a la izquierda del contenido de un botón.
    //   Ui.Marca  -> texto de ayuda gris dentro de un campo de texto vacío.
    public static class Ui
    {
        public static readonly DependencyProperty GlifoProperty =
            DependencyProperty.RegisterAttached("Glifo", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(null));
        public static string GetGlifo(DependencyObject d) => (string)d.GetValue(GlifoProperty);
        public static void SetGlifo(DependencyObject d, string v) => d.SetValue(GlifoProperty, v);

        public static readonly DependencyProperty MarcaProperty =
            DependencyProperty.RegisterAttached("Marca", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(null));
        public static string GetMarca(DependencyObject d) => (string)d.GetValue(MarcaProperty);
        public static void SetMarca(DependencyObject d, string v) => d.SetValue(MarcaProperty, v);

        // Insignia numerica o de texto a la derecha de un elemento del menu lateral, y su nivel (ok / aviso / critico / info).
        public static readonly DependencyProperty InsigniaProperty =
            DependencyProperty.RegisterAttached("Insignia", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(null));
        public static string GetInsignia(DependencyObject d) => (string)d.GetValue(InsigniaProperty);
        public static void SetInsignia(DependencyObject d, string v) => d.SetValue(InsigniaProperty, v);

        public static readonly DependencyProperty NivelInsigniaProperty =
            DependencyProperty.RegisterAttached("NivelInsignia", typeof(string), typeof(Ui), new FrameworkPropertyMetadata("info"));
        public static string GetNivelInsignia(DependencyObject d) => (string)d.GetValue(NivelInsigniaProperty);
        public static void SetNivelInsignia(DependencyObject d, string v) => d.SetValue(NivelInsigniaProperty, v);
    }

    // Glifos de Segoe MDL2 Assets (existe en Windows 10 y 11) usados en la aplicación.
    public static class G
    {
        public const string Inicio = "";
        public const string Documento = "";
        public const string Descargar = "";
        public const string Subir = "";
        public const string ArchivoSubir = "";
        public const string Sincronizar = "";
        public const string Buscar = "";
        public const string Calendario = "";
        public const string Configuracion = "";
        public const string Historial = "";
        public const string Actividad = "";
        public const string Salud = "";
        public const string Alerta = "";
        public const string Lista = "";
        public const string Reporte = "";
        public const string Llave = "";
        public const string Guardar = "";
        public const string Actualizar = "";
        public const string Agregar = "";
        public const string Cancelar = "";
        public const string Ojo = "";
        public const string Escudo = "";
        public const string Chevron = "";
        public const string ChevronDerecha = "";
        public const string Copiar = "";
        public const string Informacion = "";
        public const string Carpeta = "";
        public const string Biblioteca = "";
        public const string Editar = "";
        public const string Eliminar = "";
        public const string Enlace = "";
        public const string Palomita = "";
        public const string Reloj = "";
        public const string Filtro = "";
        public const string Computadora = "";
    }
}
