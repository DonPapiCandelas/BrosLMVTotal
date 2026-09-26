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

// Program.cs -- UseWindowsService() detecta solo si lo lanzo el Service Control Manager (lo hace
// leyendo si el proceso padre es services.exe); corriendolo a mano desde una consola (para
// probar) simplemente se comporta como una app de consola normal, sin necesitar un flag aparte.

using BrosLMV.Descargas.Servicio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "BrosLMV Descargas");
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
