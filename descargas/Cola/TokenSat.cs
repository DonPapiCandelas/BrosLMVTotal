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

// TokenSat.cs -- el token de Autenticacion del SAT dura ~5 minutos. Los bucles largos (muchos tramos,
// barrido de 12 meses, verificar y descargar decenas de solicitudes) lo reutilizaban sin mirar la hora y
// el SAT contestaba CodEstatus=300 "Token invalido" a mitad de la pasada. Este envoltorio lo renueva solo
// cuando lleva mas de 4 minutos.

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using BrosLMV.Descargas.Sat;

namespace BrosLMV.Descargas.Cola
{
    internal sealed class TokenSat
    {
        public static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(4);

        private readonly Func<Task<string>> _autenticar;
        private readonly Func<DateTime> _ahora;
        private string _token;
        private DateTime _obtenidoEn;

        // Para pruebas: autenticador y reloj inyectables.
        public TokenSat(Func<Task<string>> autenticar, string tokenInicial = null, Func<DateTime> ahora = null)
        {
            _autenticar = autenticar;
            _ahora = ahora ?? (() => DateTime.UtcNow);
            _token = tokenInicial;
            _obtenidoEn = _ahora();
        }

        // Produccion: se renueva con la FIEL de la empresa.
        public static TokenSat Para(X509Certificate2 cert, RSA llave, string tokenInicial)
        {
            return new TokenSat(async () =>
            {
                var auth = await SatSoapClient.AutenticarAsync(cert, llave).ConfigureAwait(false);
                if (!auth.Exito) throw new Exception("Autenticacion: " + auth.Error);
                return auth.Token;
            }, tokenInicial);
        }

        public int Renovaciones { get; private set; }

        public async Task<string> ObtenerAsync()
        {
            if (_token == null || _ahora() - _obtenidoEn >= Vigencia)
            {
                bool primera = _token == null;
                _token = await _autenticar().ConfigureAwait(false);
                _obtenidoEn = _ahora();
                if (!primera) Renovaciones++;
            }
            return _token;
        }
    }
}
