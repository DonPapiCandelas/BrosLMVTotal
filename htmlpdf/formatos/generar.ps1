# Genera los formatos HTML gen&eacute;ricos de BrosLMV (gris / azul, con QR, multi-partida sin
# saltos feos de p&aacute;gina). Salida: htmlpdf\formatos\*.html
# Etiquetas: sintaxis de Comercial ([Tag], <DETAIL>, [Format(x,#,##0.00)]) + [QRBros]
# (lo resuelve GenerarDocumentoPDF.ctx llamando a XEngine GetQRCode).

$ErrorActionPreference = 'Stop'
$out = $PSScriptRoot
$QRLIB = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot "_qrcode.min.js"))

$CSS = @'
<meta charset="utf-8">
<style>
  @page { size: Letter; margin: 12mm 12mm 15mm; }
  *{ box-sizing:border-box; -webkit-print-color-adjust:exact; print-color-adjust:exact; }
  body{ font:10pt/1.4 "Segoe UI",Arial,Helvetica,sans-serif; color:#1f2a37; margin:0; }
  @media screen{ html{ background:#5b616b; } body{ width:216mm; min-height:279mm; margin:18px auto;
    padding:12mm 12mm 15mm; background:#fff; box-shadow:0 3px 22px rgba(0,0,0,.35); } }
  .doc{ width:100%; }
  h1,h2,h3,p{ margin:0; }
  .hdr{ display:flex; justify-content:space-between; align-items:flex-start; gap:16px; padding-bottom:10px; border-bottom:3px solid #2F5AA8; }
  .emp .name{ font-size:16pt; font-weight:700; color:#2F5AA8; letter-spacing:.3px; }
  .emp .rfc{ font-size:8pt; color:#64748b; font-weight:600; }
  .emp .addr{ font-size:8pt; color:#475569; margin-top:4px; max-width:280px; }
  .tit{ text-align:right; }
  .tit .t{ font-size:15pt; font-weight:700; color:#334155; text-transform:uppercase; letter-spacing:1px; }
  .tit .folio{ font-size:12pt; font-weight:700; color:#2F5AA8; margin-top:2px; }
  .tit .meta{ font-size:8pt; color:#64748b; margin-top:2px; }
  .qr{ margin-top:6px; }
  .qr img{ width:78px; height:78px; }
  .boxes{ display:flex; gap:10px; margin-top:12px; }
  .box{ flex:1; border:1px solid #d7dee8; border-radius:6px; overflow:hidden; }
  .box .bt{ background:#334155; color:#fff; font-size:7.5pt; font-weight:700; letter-spacing:.6px; padding:5px 9px; text-transform:uppercase; }
  .box .bb{ padding:8px 9px; font-size:8.5pt; }
  .box .bb .k{ color:#64748b; } .box .bb .v{ font-weight:600; color:#1f2a37; }
  .box .bb .line{ display:flex; justify-content:space-between; gap:8px; padding:1.5px 0; }
  table.items{ width:100%; border-collapse:collapse; margin-top:12px; }
  table.items thead{ display:table-header-group; }
  table.items th{ background:#2F5AA8; color:#fff; font-size:7.5pt; font-weight:700; letter-spacing:.4px; text-transform:uppercase; padding:6px 7px; text-align:left; }
  table.items th.r,table.items td.r{ text-align:right; }
  table.items th.c,table.items td.c{ text-align:center; }
  table.items td{ padding:6px 7px; font-size:8.5pt; border-bottom:1px solid #e6ebf2; vertical-align:top; }
  table.items tbody tr{ page-break-inside:avoid; }
  table.items tbody tr:nth-child(even){ background:#f6f8fb; }
  td .cmt{ display:block; color:#64748b; font-size:7.5pt; }
  .lower{ display:flex; gap:12px; margin-top:14px; page-break-inside:avoid; }
  .notes{ flex:1.3; }
  .notes .nt{ font-size:7.5pt; font-weight:700; color:#334155; letter-spacing:.5px; text-transform:uppercase; border-bottom:1px solid #d7dee8; padding-bottom:3px; margin-bottom:5px; }
  .notes .nb{ font-size:8.5pt; color:#475569; white-space:pre-wrap; }
  .tot{ flex:1; border:1px solid #d7dee8; border-radius:6px; overflow:hidden; align-self:flex-start; }
  .tot .row{ display:flex; justify-content:space-between; padding:6px 11px; font-size:9pt; border-bottom:1px solid #eef2f7; }
  .tot .row .k{ color:#475569; } .tot .row .v{ font-weight:600; }
  .tot .grand{ background:#2F5AA8; color:#fff; font-weight:700; font-size:11pt; border-bottom:0; }
  .tot .letra{ padding:6px 11px; font-size:7.5pt; color:#64748b; font-style:italic; }
  .sign{ margin-top:16px; page-break-inside:avoid; }
  .sign .st{ background:#334155; color:#fff; font-size:7.5pt; font-weight:700; letter-spacing:.6px; padding:5px 9px; text-transform:uppercase; }
  .sign .sg{ display:flex; gap:30px; padding:22px 9px 6px; }
  .sign .sg > div{ flex:1; border-top:1px solid #94a3b8; padding-top:4px; font-size:7.5pt; color:#64748b; text-align:center; }
  .foot{ margin-top:10px; text-align:center; font-size:7pt; color:#94a3b8; letter-spacing:.5px; }
</style>
'@

function EmpBox {
@'
  <div class="box"><div class="bt">Empresa emisora</div><div class="bb">
    <div class="v" style="font-size:9.5pt">[EmisorRazonSocial]</div>
    <div class="k">RFC: [EmisorRfc]</div>
    <div class="addr" style="margin-top:4px">[EmisorDomicilioFiscalCalle] [EmisorDomicilioFiscalNoExterior] [EmisorDomicilioFiscalNoInterior]<br>
    [EmisorDomicilioFiscalColonia]<br>[EmisorDomicilioFiscalMunicipio], [EmisorDomicilioFiscalEstado]<br>C.P. [EmisorDomicilioFiscalCodigoPostal]</div>
  </div></div>
'@
}
function DocBox($extra) {
@"
  <div class="box"><div class="bt">Datos del documento</div><div class="bb">
    <div class="line"><span class="k">Fecha</span><span class="v">[FechaDocumento]</span></div>
    <div class="line"><span class="k">Entrega</span><span class="v">[FechaEntrega]</span></div>
    <div class="line"><span class="k">Moneda</span><span class="v">[Moneda]</span></div>
    <div class="line"><span class="k">Condici&oacute;n</span><span class="v">[CondicionPago]</span></div>
    <div class="line"><span class="k">Elabor&oacute;</span><span class="v">[NombreVendedorDocumento]</span></div>
    $extra
  </div></div>
"@
}
function TerBox($label, $body) {
@"
  <div class="box"><div class="bt">$label</div><div class="bb">$body</div></div>
"@
}
$terCliente  = TerBox 'Cliente'   '<div class="v" style="font-size:9.5pt">[ReceptorRazonSocial]</div><div class="k">RFC: [ReceptorRfc]</div>'
$terProveedor= TerBox 'Proveedor' '<div class="v" style="font-size:9.5pt">[ReceptorRazonSocial]</div><div class="k">RFC: [ReceptorRfc]</div>'
$terAlmacen  = TerBox 'Almac&eacute;n'   '<div class="v" style="font-size:9.5pt">[DepotName]</div><div class="k">[DepotStreet] [DepotExtNumber], [DepotMunicipality]</div>'
$terTraspaso = TerBox 'Movimiento entre almacenes' '<div class="line"><span class="k">Origen</span><span class="v">[DepotFrom]</span></div><div class="line"><span class="k">Destino</span><span class="v">[DepotTo]</span></div>'

# columnas -> (encabezado th, celda td)
$COL = @{
  num   = @('<th class="c" style="width:26px">#</th>',            '<td class="c">[NumeroPartida]</td>')
  cant  = @('<th class="r" style="width:52px">Cant.</th>',        '<td class="r">[Cantidad]</td>')
  um    = @('<th class="c" style="width:46px">U.M.</th>',         '<td class="c">[UnidadDeMedida]</td>')
  cod   = @('<th style="width:80px">C&oacute;digo</th>',                 '<td>[NumeroIdentificacion]</td>')
  desc  = @('<th>Descripci&oacute;n</th>',                               '<td>[Descripcion]<span class="cmt">[ComentarioLinea]</span></td>')
  pu    = @('<th class="r" style="width:80px">P. unitario</th>',  '<td class="r">$[Format(ValorUnitario,#,##0.00)]</td>')
  dsc   = @('<th class="r" style="width:48px">Desc.</th>',        '<td class="r">[PorcentajeDescuentoLinea]%</td>')
  iva   = @('<th class="r" style="width:44px">IVA</th>',          '<td class="r">[PorcentajeIVALinea]%</td>')
  imp   = @('<th class="r" style="width:88px">Importe</th>',      '<td class="r">$[Format(Importe,#,##0.00)]</td>')
  tgas  = @('<th style="width:150px">Tipo de gasto</th>',         '<td>[TipoGastoLinea]</td>')
}

$totMoney = @'
  <div class="tot">
    <div class="row keep"><span class="k">Subtotal</span><span class="v">$[Format(SubTotal,#,##0.00)]</span></div>
    <div class="row"><span class="k">Descuento</span><span class="v">-$[Format(DescuentoTotal,#,##0.00)]</span></div>
    [DesgloseImpuestos]
    <div class="row grand keep"><span>TOTAL [Moneda]</span><span>$[Format(Total,#,##0.00)]</span></div>
    <div class="letra">[CantidadConLetra]</div>
  </div>
  <script>
    // oculta renglones de total en cero (Descuento, impuestos que no aplican)
    document.querySelectorAll('.tot .row:not(.keep)').forEach(function(r){
      var t=(r.querySelector('.v')||{}).textContent||''; var n=parseFloat(t.replace(/[^0-9.\-]/g,''));
      if(!n) r.remove();
    });
  </script>
'@

$signRecep = @'
  <div class="sign"><div class="st">Recepci&oacute;n en almac&eacute;n</div>
   <div class="sg"><div>Fecha</div><div>Recibi&oacute; (nombre)</div><div>Firma</div></div></div>
'@
$signAlmacen = @'
  <div class="sign"><div class="st">Firmas</div>
   <div class="sg"><div>Entreg&oacute;</div><div>Recibi&oacute;</div><div>Autoriz&oacute;</div></div></div>
'@

# definici&oacute;n de los 10 documentos
$docs = @(
  @{ file='PedidoCliente_BrosLMV.html';       titulo='Pedido de cliente';        ter=$terCliente;   cols='num cant cod desc pu dsc iva imp'; tot=$totMoney; sign='' }
  @{ file='Remision_BrosLMV.html';            titulo='Remisi&oacute;n';                 ter=$terCliente;   cols='num cant cod desc pu dsc iva imp'; tot=$totMoney; sign='' }
  @{ file='FacturaCliente_BrosLMV.html';      titulo='Factura';                  ter=$terCliente;   cols='num cant cod desc pu dsc iva imp'; tot=$totMoney; sign=''; fiscal=$true }
  @{ file='NotaCreditoCliente_BrosLMV.html';  titulo='Nota de cr&eacute;dito';          ter=$terCliente;   cols='num cant cod desc pu dsc iva imp'; tot=$totMoney; sign=''; fiscal=$true }
  @{ file='OrdenCompra_BrosLMV.html';         titulo='Orden de compra';          ter=$terProveedor; cols='num cant um cod desc pu dsc iva imp'; tot=$totMoney; sign=$signRecep }
  @{ file='RecepcionMercancia_BrosLMV.html';  titulo='Recepci&oacute;n de mercanc&iacute;a';   ter=$terProveedor; cols='num cant um cod desc'; tot=''; sign=$signRecep }
  @{ file='Gastos_BrosLMV.html';              titulo='Documento de gastos';      ter=$terProveedor; cols='num tgas desc iva imp'; tot=$totMoney; sign='' }
  @{ file='EntradaAlmacen_BrosLMV.html';      titulo='Entrada de almac&eacute;n';       ter=$terAlmacen;   cols='num cant um cod desc'; tot=''; sign=$signAlmacen }
  @{ file='Traspaso_BrosLMV.html';            titulo='Traspaso entre almacenes'; ter=$terTraspaso;  cols='num cant um cod desc'; tot=''; sign=$signAlmacen }
  @{ file='SalidaAlmacen_BrosLMV.html';       titulo='Salida de almac&eacute;n';        ter=$terAlmacen;   cols='num cant um cod desc'; tot=''; sign=$signAlmacen }
)

foreach ($d in $docs) {
  $keys = $d.cols.Split(' ')
  $ths = ($keys | ForEach-Object { $COL[$_][0] }) -join "`n      "
  $tds = ($keys | ForEach-Object { $COL[$_][1] }) -join "`n        "
  $docExtra = ''
  if ($d.fiscal) { $docExtra = '<div class="line"><span class="k">UUID</span><span class="v" style="font-size:7pt">[Uuid]</span></div><div class="line"><span class="k">Timbrado</span><span class="v">[FechaTimbrado]</span></div>' }
  $lowerRight = if ($d.tot) { $d.tot } else { '' }
  $html = @"
<!doctype html><html lang="es"><head>
$CSS
<title>[Documento] [Serie][Folio]</title></head><body><div class="doc">

  <div class="hdr">
    <div class="emp">
      <div class="name">[EmisorRazonSocial]</div>
      <div class="rfc">RFC: [EmisorRfc]</div>
    </div>
    <div class="tit">
      <div class="t">$($d.titulo)</div>
      <div class="folio">[Serie][Folio]</div>
      <div class="meta">[FechaDocumento] &nbsp;&middot;&nbsp; [Moneda]</div>
      <div class="qr" data-qr="[QRPayload]"></div>
    </div>
  </div>

  <div class="boxes">
$(EmpBox)
$(DocBox $docExtra)
$($d.ter)
  </div>

  <table class="items">
    <thead><tr>
      $ths
    </tr></thead>
    <tbody>
      <DETAIL><tr>
        $tds
      </tr></DETAIL>
    </tbody>
  </table>

  <div class="lower">
    <div class="notes">
      <div class="nt">Observaciones</div>
      <div class="nb">[Comments]</div>
    </div>
    $lowerRight
  </div>

  $($d.sign)

  <div class="foot">Generado con BrosLMV &nbsp;&middot;&nbsp; [EmisorRazonSocial]</div>
</div>
  <script>__QRLIB__
    (function(){
      var el=document.querySelector('.qr[data-qr]');
      var txt=el?(el.getAttribute('data-qr')||'').trim():'';
      if(el&&txt){try{
        var q=qrcode(0,'M'); q.addData(txt); q.make();
        el.innerHTML='<img alt="QR" width="78" height="78" src="'+q.createDataURL(4,0)+'">';
      }catch(e){ el.style.display='none'; }} else if(el){ el.style.display='none'; }
    })();
  </script>
</body></html>
"@
  $html = $html.Replace("__QRLIB__", $QRLIB)
  $path = Join-Path $out $d.file
  [System.IO.File]::WriteAllText($path, $html, (New-Object System.Text.UTF8Encoding($false)))
  Write-Host ("  {0}  ({1} bytes)" -f $d.file, $html.Length)
}
Write-Host "LISTO -> $out"
