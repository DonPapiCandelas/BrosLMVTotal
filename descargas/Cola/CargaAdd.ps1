# BrosLMV Descargas - carga de XML al ADD de CONTPAQi Contabilidad.
# Lo lanza ContabilidadAdd.cs con Windows PowerShell 5.1 (las DLL de CONTPAQi son .NET Framework).
# Entrada: JSON por stdin. Salida: lineas "OK|...", "PROG|n|total", "ERR|mensaje".
# Habla con el servidor de aplicaciones de CONTPAQi (http://127.0.0.1:9080/saci/...), nunca escribe por SQL.
$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = [Text.Encoding]::UTF8
[Console]::OutputEncoding = [Text.Encoding]::UTF8
function Salir($txt) { [Console]::Out.WriteLine($txt); [Console]::Out.Flush() }
try {
    $in = [Console]::In.ReadToEnd() | ConvertFrom-Json
    $d = $in.carpetaDlls
    [AppDomain]::CurrentDomain.add_AssemblyResolve({ param($s, $e) $n = ($e.Name -split ',')[0]; $p = Join-Path $d ($n + '.dll'); if (Test-Path $p) { [Reflection.Assembly]::LoadFrom($p) } })
    Add-Type -AssemblyName System.ServiceModel
    $null = [Reflection.Assembly]::LoadFrom((Join-Path $d 'SaciReferences.dll'))
    $null = [Reflection.Assembly]::LoadFrom((Join-Path $d 'SaciEntities.dll'))
    $dsl = [Reflection.Assembly]::LoadFrom((Join-Path $d 'DSLCommon.dll'))
    $b = New-Object System.ServiceModel.BasicHttpBinding
    $b.MaxReceivedMessageSize = 10000000; $b.SendTimeout = '00:10:00'; $b.ReceiveTimeout = '00:10:00'
    function EP($s) { New-Object System.ServiceModel.EndpointAddress("http://127.0.0.1:9080/saci/$s") }
    $sec = New-Object SaciReferences.SecurityService.SecurityServicesClient($b, (EP 'sacisecurity'))
    $ses = New-Object SaciReferences.SessionsServices.SessionServicesClient($b, (EP 'sessions'))
    $fis = New-Object SaciReferences.FiscalServices.StampServiceClient($b, (EP 'fiscal'))

    # Sesion de tipo SACI (la que usa el importador de documentos): con la clave del usuario de Contabilidad.
    $tp = [SaciReferences.SecurityService.ISecurityServices].GetMethod('Login').GetParameters()[0].ParameterType
    $ss = [Activator]::CreateInstance($tp)
    $ss.aplication = New-Object ($tp.GetProperty('aplication').PropertyType.FullName)
    $ss.aplication.name = 'BrosLMV Descargas'; $ss.aplication.version = '19.3.1'; $ss.aplication.type = 'SACI'
    $ss.user = New-Object ($tp.GetProperty('user').PropertyType.FullName)
    $ss.user.name = [string]$in.usuario; $ss.user.password = [string]$in.clave
    try { $s = $sec.Login($ss) } catch { Salir ('ERR|No se pudo iniciar sesion en Contabilidad: ' + $_.Exception.Message); exit 3 }

    try {
        $ob = New-Object SaciEntities.Sessions.Etc.ObSession
        $ob.User = [string]$in.usuario; $ob.Company = [string]$in.empresaNombre; $ob.CompanyPath = [string]$in.empresaRuta
        $ob.Guid = $s.guid; $ob.IdCompany = ''; $ob.CompanyGuid = [string]$in.guidEmpresa
        $tag = New-Object SaciEntities.Sessions.Etc.ControlTagSaci
        $tag.Rfc = [string]$in.rfc; $tag.GuidDSL = [string]$in.guidDsl
        $ob.ControlTagSaci = $tag
        $ses.SetValueObSession($s, 'ObSession', $ob)

        $tUp = $dsl.GetType('DSLCommon.UploadDocument'); $ser = $dsl.GetType('DSLCommon.Serializer')
        $tipoCfdi = [Enum]::Parse($tUp.GetProperty('type').PropertyType, 'CFDI')
        $estRec = [Enum]::Parse($tUp.GetProperty('status').PropertyType, 'Recibido')
        $estEmi = [Enum]::Parse($tUp.GetProperty('status').PropertyType, 'Emitido')
        $lstType = [Collections.Generic.List[object]].GetGenericTypeDefinition().MakeGenericType($tUp)
        $fmt = New-Object Runtime.Serialization.Formatters.Binary.BinaryFormatter
        $tmp = [string]$in.carpetaTemporal
        New-Item -ItemType Directory $tmp -Force | Out-Null

        # Recibidos y emitidos van en lotes separados: CONTPAQi los valida distinto (SetDocuments / SetDocumentsIssued).
        $archivos = @($in.archivos); $total = $archivos.Count; $hechos = 0; $lote = 50
        foreach ($emitidos in @($false, $true)) {
            $grupo = @($archivos | Where-Object { [bool]$_.emitido -eq $emitidos })
            for ($i = 0; $i -lt $grupo.Count; $i += $lote) {
                $lst = [Activator]::CreateInstance($lstType)
                foreach ($a in $grupo[$i..([Math]::Min($i + $lote, $grupo.Count) - 1)]) {
                    $xml = [IO.File]::ReadAllText([string]$a.ruta)
                    $u = [Activator]::CreateInstance($tUp)
                    $u.fileName = ([string]$a.uuid) + '.xml'
                    $u.fileCompressed = $ser.GetMethod('Compress').Invoke($null, @(, [Text.Encoding]::UTF8.GetBytes($xml)))
                    $u.type = $tipoCfdi
                    $u.status = if ($emitidos) { $estEmi } else { $estRec }
                    $lst.Add($u)
                }
                $bin = Join-Path $tmp ([Guid]::NewGuid().ToString('N') + '.bin')
                $fs = [IO.File]::Create($bin); try { $fmt.Serialize($fs, $lst) } finally { $fs.Close() }
                try {
                    $q = if ($emitidos) { $fis.SetDocumentsIssued($s.guid, $bin) } else { $fis.SetDocuments($s.guid, $bin) }
                    $p = $null; $limite = (Get-Date).AddMinutes(10)
                    do { Start-Sleep -Milliseconds 700; $p = $fis.GetProgressSetDocuments($s.guid, $q) } while ($p.Percentage -lt 100 -and $p.Status -ne 'Finished' -and $p.Status -ne 'Failed' -and (Get-Date) -lt $limite)
                    if ($p.Status -eq 'Failed') { Salir ('ERR|El servidor de CONTPAQi rechazo un lote: ' + $p.Message) }
                } finally { Remove-Item $bin -Force -ErrorAction SilentlyContinue }
                $hechos += $lst.Count
                Salir ('PROG|' + $hechos + '|' + $total)
            }
        }
        Salir 'OK|fin'
    } finally {
        try { $ses.Release($s) } catch { }
    }
} catch {
    Salir ('ERR|' + $_.Exception.Message)
    exit 2
}
