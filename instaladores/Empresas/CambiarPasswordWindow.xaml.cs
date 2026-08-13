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

// CambiarPasswordWindow.xaml.cs -- exige la contrasena ACTUAL (verificada contra el hash ya
// guardado) antes de aceptar una nueva -- solo asi se puede cambiar; si se olvido, la unica
// salida documentada es resetear la fila en SQL (zzBrosConsolaPass), no hay "recuperarla".

using System;
using System.Data.SqlClient;
using System.Windows;

namespace BrosLMV.Empresas
{
    public partial class CambiarPasswordWindow : Window
    {
        private readonly string _connStr;

        public CambiarPasswordWindow(string server, string db, string user, string pass)
        {
            InitializeComponent();
            _connStr = $"Server={server};Database={db};User Id={user};Password={pass};TrustServerCertificate=True;Connect Timeout=15;";
            lblEmpresa.Text = "Empresa: " + db;
        }

        private void Cancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Cambiar_Click(object sender, RoutedEventArgs e)
        {
            string actual = pwdActual.Password;
            string nueva = pwdNueva.Password;
            string confirmar = pwdConfirmar.Password;

            if (string.IsNullOrEmpty(nueva))
            {
                MessageBox.Show(this, "La contraseña nueva no puede estar vacía.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (nueva != confirmar)
            {
                MessageBox.Show(this, "La confirmación no coincide con la contraseña nueva.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using (var cn = new SqlConnection(_connStr))
                {
                    cn.Open();

                    byte[] salActual, hashActual;
                    int iteracionesActual;
                    bool habilitado;
                    using (var cmd = cn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT Habilitado, Sal, Hash, Iteraciones FROM zzBrosConsolaPass WHERE Id=1";
                        using (var rd = cmd.ExecuteReader())
                        {
                            if (!rd.Read())
                            {
                                MessageBox.Show(this,
                                    "Esta empresa todavía no tiene contraseña de Consola configurada.\n\n" +
                                    "Actívala primero desde la casilla \"Proteger la Consola con contraseña\" al instalar/actualizar.",
                                    "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information);
                                return;
                            }
                            habilitado = Convert.ToBoolean(rd["Habilitado"]);
                            salActual = rd["Sal"] as byte[];
                            hashActual = rd["Hash"] as byte[];
                            iteracionesActual = rd["Iteraciones"] == DBNull.Value ? 0 : Convert.ToInt32(rd["Iteraciones"]);
                        }
                    }

                    if (!habilitado || salActual == null || hashActual == null)
                    {
                        MessageBox.Show(this,
                            "Esta empresa todavía no tiene contraseña de Consola configurada.\n\n" +
                            "Actívala primero desde la casilla \"Proteger la Consola con contraseña\" al instalar/actualizar.",
                            "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    if (!ConsolaPasswordHash.Verificar(actual, salActual, hashActual, iteracionesActual))
                    {
                        MessageBox.Show(this, "La contraseña actual no es correcta.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    byte[] salNueva, hashNueva;
                    int iteracionesNueva;
                    ConsolaPasswordHash.Generar(nueva, out salNueva, out hashNueva, out iteracionesNueva);

                    using (var cmd = cn.CreateCommand())
                    {
                        cmd.CommandText = @"
UPDATE zzBrosConsolaPass
SET Habilitado=1, Sal=@Sal, Hash=@Hash, Iteraciones=@Iter, FechaActualizacion=GETDATE()
WHERE Id=1;";
                        cmd.Parameters.AddWithValue("@Sal", salNueva);
                        cmd.Parameters.AddWithValue("@Hash", hashNueva);
                        cmd.Parameters.AddWithValue("@Iter", iteracionesNueva);
                        cmd.ExecuteNonQuery();
                    }
                }

                MessageBox.Show(this, "Contraseña de la Consola actualizada.", "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo cambiar la contraseña:\n\n" + ex.Message, "BrosLMV", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
