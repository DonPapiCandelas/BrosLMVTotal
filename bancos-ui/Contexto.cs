// BrosLMV - Botones personalizados para CONTPAQi Comercial PRO
// Copyright (C) 2026 Cristofer Candelas Garcia
// GPL-3.0-or-later. Ver LICENSE en la raiz del repositorio.

using System;
using BrosLMV.Bancos.Carga;

namespace BrosLMV.BancosUI
{
    // La empresa y el usuario con los que se abrio el programa: los dice el boton de Comercial.
    //   BrosLMV.BancosUI.exe --servidor localhost\COMPAC --bd Empresa1 --empresa 1 --usuario 2 --nombre-empresa "Alma Vegetales" --nombre-usuario "Cristofer"
    public class Contexto
    {
        public string Servidor = "", BaseDeDatos = "", NombreEmpresa = "", NombreUsuario = "";
        public int Empresa, Usuario;
        public ComercialBanco Banco;

        public static Contexto DeArgumentos(string[] args)
        {
            var c = new Contexto();
            for (int i = 0; i + 1 < args.Length; i += 2)
            {
                string v = args[i + 1];
                switch (args[i].ToLowerInvariant())
                {
                    case "--servidor": c.Servidor = v; break;
                    case "--bd": c.BaseDeDatos = v; break;
                    case "--empresa": int.TryParse(v, out c.Empresa); break;
                    case "--usuario": int.TryParse(v, out c.Usuario); break;
                    case "--nombre-empresa": c.NombreEmpresa = v; break;
                    case "--nombre-usuario": c.NombreUsuario = v; break;
                }
            }
            return c;
        }

        public bool Completo { get { return Servidor.Length > 0 && BaseDeDatos.Length > 0 && Empresa > 0 && Usuario > 0; } }

        public void Conectar()
        {
            Banco = new ComercialBanco(Conexion.Para(Servidor, BaseDeDatos), Empresa, Usuario);
            Banco.Asegurar();
        }

        // Solo para desarrollo: el laboratorio, con la credencial de esta terminal.
        public static Contexto Laboratorio()
        {
            string cred = Conexion.CredencialDeLaTerminal(), servidor = "localhost\\COMPAC";
            foreach (var p in cred.Split(';')) { int i = p.IndexOf('='); if (i > 0 && (p.Substring(0, i).Trim().Equals("Server", StringComparison.OrdinalIgnoreCase) || p.Substring(0, i).Trim().Equals("Data Source", StringComparison.OrdinalIgnoreCase))) servidor = p.Substring(i + 1).Trim(); }
            return new Contexto { Servidor = servidor, BaseDeDatos = "BROSLMV_DESARROLLO", Empresa = 1, Usuario = 1, NombreEmpresa = "Laboratorio", NombreUsuario = "Admin" };
        }
    }
}
