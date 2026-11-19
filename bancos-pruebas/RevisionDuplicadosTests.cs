using System;
using System.Collections.Generic;
using System.Linq;
using BrosLMV.Bancos.Carga;
using Xunit;

namespace BrosLMV.Bancos.Pruebas
{
    // Duplicados que YA estan en el estado de cuenta (los dejo otro cargador). Logica pura, sin base de datos.
    public class RevisionDuplicadosTests
    {
        private static long _id = 1000;
        private static RenglonEdoCta R(decimal cargo, decimal abono, decimal? saldo, string desc = "SPEI RECIBIDO 5948.02", bool ligado = false, string dia = "2026-11-02", long cuenta = 1, long? id = null)
        {
            return new RenglonEdoCta { EdoCtaId = id ?? ++_id, CuentaId = cuenta, Cuenta = "BBVA", Fecha = DateTime.Parse(dia), Cargo = cargo, Abono = abono, Saldo = saldo, Descripcion = desc, Ligado = ligado };
        }

        [Fact]
        public void ElCasoRealUnaCopiaLigadaYNueveSinLigarSeConservaLaLigada()
        {
            var lista = new List<RenglonEdoCta> { R(0, 5948.02m, 248983.77m, ligado: true, id: 52048) };
            for (int i = 0; i < 9; i++) lista.Add(R(0, 5948.02m, 248983.77m, id: 52354 + i));
            var g = RevisionDuplicados.Buscar(lista);
            Assert.Single(g);
            Assert.Equal(52048, g[0].Conservar.EdoCtaId);
            Assert.Equal(9, g[0].Sobrantes.Count);
            Assert.Empty(g[0].LigadosExtra);
            Assert.Equal(10, g[0].Copias);
        }

        [Fact]
        public void SinNingunoLigadoSeConservaElMasAntiguo()
        {
            var lista = new List<RenglonEdoCta> { R(0, 100m, 500m, id: 30), R(0, 100m, 500m, id: 10), R(0, 100m, 500m, id: 20) };
            var g = RevisionDuplicados.Buscar(lista).Single();
            Assert.Equal(10, g.Conservar.EdoCtaId);
            Assert.Equal(new long[] { 20, 30 }, g.Sobrantes.Select(x => x.EdoCtaId).OrderBy(x => x).ToArray());
        }

        [Fact]
        public void DosDepositosIgualesEnElMismoDiaConSaldoDistintoSonLegitimos()
        {
            // Mismo importe, fecha y descripcion, pero cada uno cambio el saldo: son dos movimientos reales.
            var lista = new List<RenglonEdoCta> { R(0, 1000m, 5000m), R(0, 1000m, 6000m) };
            Assert.Empty(RevisionDuplicados.Buscar(lista));
        }

        [Fact]
        public void SinSaldoGuardadoNoSeAgrupaNada()
        {
            // Sin saldo (null o 0) no hay con que distinguirlos: podrian ser dos movimientos legitimos.
            var lista = new List<RenglonEdoCta> { R(0, 1000m, null), R(0, 1000m, null), R(0, 1000m, 0m), R(0, 1000m, 0m) };
            Assert.Empty(RevisionDuplicados.Buscar(lista));
        }

        [Fact]
        public void NoMezclaCuentasNiFechasNiImportes()
        {
            var lista = new List<RenglonEdoCta> { R(0, 100m, 500m, cuenta: 1), R(0, 100m, 500m, cuenta: 2), R(0, 100m, 500m, dia: "2026-11-03"), R(0, 101m, 500m), R(100m, 0m, 500m) };
            Assert.Empty(RevisionDuplicados.Buscar(lista));
        }

        [Fact]
        public void LosEspaciosYMayusculasDeLaDescripcionNoLosHaceDistintos()
        {
            var lista = new List<RenglonEdoCta> { R(0, 100m, 500m, desc: "SPEI  RECIBIDO naranjos 499"), R(0, 100m, 500m, desc: " spei recibido NARANJOS 499 ") };
            Assert.Single(RevisionDuplicados.Buscar(lista));
        }

        [Fact]
        public void UnaDescripcionDistintaSiLosSepara()
        {
            var lista = new List<RenglonEdoCta> { R(0, 100m, 500m, desc: "PAGO A"), R(0, 100m, 500m, desc: "PAGO B") };
            Assert.Empty(RevisionDuplicados.Buscar(lista));
        }

        [Fact]
        public void UnaCopiaLigadaNuncaEsSobrante()
        {
            // Dos copias ligadas a pagos distintos y una sin ligar: solo la sin ligar es sobrante; la otra ligada se reporta para revision a mano.
            var lista = new List<RenglonEdoCta> { R(0, 100m, 500m, ligado: true, id: 1), R(0, 100m, 500m, ligado: true, id: 2), R(0, 100m, 500m, id: 3) };
            var g = RevisionDuplicados.Buscar(lista).Single();
            Assert.Equal(1, g.Conservar.EdoCtaId);
            Assert.Equal(new long[] { 3 }, g.Sobrantes.Select(x => x.EdoCtaId).ToArray());
            Assert.Equal(new long[] { 2 }, g.LigadosExtra.Select(x => x.EdoCtaId).ToArray());
            Assert.DoesNotContain(g.Sobrantes, x => x.Ligado);
        }

        [Fact]
        public void NueveMovimientosPorCatorceCopiasDan117Sobrantes()
        {
            // Lo que paso el 2-nov-2026: 9 movimientos, 14 renglones cada uno => 117 sobrantes.
            var lista = new List<RenglonEdoCta>();
            for (int m = 0; m < 9; m++)
                for (int c = 0; c < 14; c++) lista.Add(R(0, 100m + m, 1000m + m, desc: "MOV " + m));
            var g = RevisionDuplicados.Buscar(lista);
            Assert.Equal(9, g.Count);
            Assert.Equal(117, RevisionDuplicados.TotalSobrantes(g));
        }

        [Fact]
        public void UnaCuentaConSaldoDeCierreDelDiaSeOmiteEnVezDeMarcarMovimientosLegitimos()
        {
            // El saldo es el mismo en todo el dia (cierre): dos depositos iguales legitimos se verian igual que un renglon repetido.
            var lista = new List<RenglonEdoCta> { R(0, 100m, 900m, desc: "DEPOSITO A"), R(0, 100m, 900m, desc: "DEPOSITO A"), R(50m, 0m, 900m, desc: "COMISION") };
            List<string> omitidas;
            Assert.Empty(RevisionDuplicados.Buscar(lista, out omitidas));
            Assert.Equal(new[] { "BBVA" }, omitidas);
        }

        [Fact]
        public void UnaCuentaConSaldoPorMovimientoSiSeRevisaAunqueOtraDelMismoBancoSeOmita()
        {
            var lista = new List<RenglonEdoCta>
            {
                // cuenta 1: saldo por movimiento (dos saldos distintos el mismo dia) y una copia repetida
                R(0, 100m, 500m, desc: "A"), R(0, 100m, 500m, desc: "A"), R(0, 30m, 530m, desc: "B"),
                // cuenta 2: saldo de cierre
                R(0, 10m, 99m, desc: "X", cuenta: 2), R(0, 10m, 99m, desc: "X", cuenta: 2), R(0, 20m, 99m, desc: "Y", cuenta: 2)
            };
            List<string> omitidas;
            var g = RevisionDuplicados.Buscar(lista, out omitidas);
            Assert.Single(g);
            Assert.Equal(1, g[0].Conservar.CuentaId);
            Assert.Single(omitidas);
        }

        [Fact]
        public void SiNingunDiaTieneVariosMovimientosDistintosNoHayEvidenciaEnContraYSeRevisaNormal()
        {
            var lista = new List<RenglonEdoCta> { R(0, 100m, 500m), R(0, 100m, 500m) };
            List<string> omitidas;
            Assert.Single(RevisionDuplicados.Buscar(lista, out omitidas));
            Assert.Empty(omitidas);
        }

        [Fact]
        public void LoUnicoNoSeToca()
        {
            var lista = new List<RenglonEdoCta> { R(0, 1m, 10m), R(0, 2m, 12m), R(0, 3m, 15m) };
            Assert.Empty(RevisionDuplicados.Buscar(lista));
        }
    }
}
