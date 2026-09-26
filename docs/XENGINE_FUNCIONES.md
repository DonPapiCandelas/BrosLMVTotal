# Catálogo de funciones XEngine (insumo para `ctx.erp.*`)

> Lista de miembros de **`XEngineLib`** (el motor interno de CONTPAQi Comercial PRO),
> extraída del Excel `Xengine.xlsx` (569 miembros). Se descartaron **128** funciones
> `AplicarMigracion*` (actualizaciones internas de esquema, sin valor para scripting),
> quedando **411 útiles**.
>
> Esta lista **define la futura capa `ctx.erp.*`**: operaciones que el addon C# (que vive
> **en proceso** dentro de ComercialSP) ejecuta **a petición** de un worker Python (que
> vive **fuera de proceso**). El script no toca XEngine directamente; pide
> `ctx.erp.<operación>(...)` y el addon la corre nativa y devuelve el resultado.
>
> **Cómo regenerar:** descomprimir `Xengine.xlsx` (es un zip), parsear
> `xl/sharedStrings.xml` + `xl/worksheets/sheet1.xml`, filtrar los nombres que no
> empiecen con `AplicarMigracion`/`Migracion`.

---

> **Estado (2026-06-26):** Las categorías principales ya están implementadas en
> **`ctx.erp.*`** (ver [`SCRIPTING_CONTRATOS.md`](SCRIPTING_CONTRATOS.md) §7).
> Los scripts C# pueden llamarlas directamente; Python las usará via proxy Named Pipes (v3.0).
> La lista completa de 411 miembros sigue en el apéndice como referencia para
> añadir funciones no cubiertas aún.

## Categorías curadas (las que importan para botones)

### Operaciones de documento (núcleo)
Afectar inventario, recalcular, cancelar/reactivar, guardar, estatus:

`AffectStock` · `AffectStockNEW` · `AffectProductKardexTable1` · `RecalcStock` ·
`RecalcProductStock` · `RecalcCostComercial` · `RecalcCostFiscal` · `CalcularCostos` ·
`CancelDocument` · `ReactivateDocument` · `Delete` · `Save` · `SaveAndClose` ·
`SaveAndNew` · `UpdateStatus` · `UpdateStatusDelivery` · `UpdateDocumentPaidInfo` ·
`ActualizarParcialidad` · `AjustarSaldosInsolutos` · `AjustarSaldosInsolutosMasivo` ·
`ReleaseRecord` · `EditValidated` · `EditComments`

### Navegación / módulos / refresco de UI
`GotoModuleID` · `OpenModule` · `ActiveModuleID` · `GetModuleID` ·
`GetModuleIDDocumentType` · `NewInstanceOfThisModule` · `NewCopyOfThisModule` ·
`RefreshGrid` · `RefreshRibbon` · `ReloadRibbonDataFromDB` · `MustRefreshGrid` ·
`SetActiveTab` · `ShowPanel` · `ShowPanelTab` · `GetPanel` · `GetPanelID` ·
`HideRibbonGroups` · `Modules` · `Documents`

### Consultas-helper (sin escribir SQL)
`DLookup` · `DLookupValue` · `DLookupWithCase` · `GetValue` · `GetValueString` ·
`GetMaxValueField` · `GetDefaultValue` · `TableExists` · `FieldExists` ·
`FieldExistsInTable` · `GetFieldType` · `GetFieldTypeName` · `queryExists` ·
`GetSQLView` · `GetSQLForThisDBEngine` · `ExecuteFunction` · `ExecuteFunction1` ·
`RunScripts` · `UnboundReadData`

### Negocio / inventario / precios
`GetProductStock` · `GetSalePrice` · `GetBusinessEntitySalePrice` · `GetBuyPrice` ·
`GetCostPrice` · `GetCostPriceComercial` · `GetCostLast` · `GetCurrencyRate` ·
`GetRateBanxico` · `GetRateCurrencies` · `GetRateCurrencyDate` · `GetPriceWithTaxes` ·
`GetProductDiscountPriceList` · `GetProductDeliverToCustomer` · `GetCoefConversion` ·
`VerifyCreditLimit` · `VerifyCreditLimitOverdue` · `ProductIsKit` ·
`GetProportionalAmountPerc` · `GetStatusPaidID`

### CFDI / timbrado / SAT
`ChangeStatusQR` · `ChangeStatusQRFactura` · `GetCadenaOriginalTFD` ·
`GetInfoCertificado` · `DocumentSignature` · `GetQRCode` · `Addendas` ·
`AddendaValores` · `ShowAddendaForm` · `AlreadyDocsSigned` · `ValidRFC` ·
`ValidateFinancialEntityNumber`

### Importar / exportar (nativo de CONTPAQi)
`ImportCatalogFromExcel` · `ImportExcelToTable` · `ExportQueryToExcel` ·
`ExportJanusToExcel` · `ExportJanusToWord` · `ExportJanusToWordText` ·
`ExportToNotePad` · `IsExcelInstalled` · `IsWordInstalled` · `CreatePDF` ·
`GetHTMLReportStructure` · `GetHtmlContent`

> Nota: CONTPAQi **ya trae** export/import a Excel. Para muchos reportes **no hace falta**
> empacar `openpyxl` en Python.

### Impresión
`PrintDoc` · `PrintModule` · `PrintModuleItems` · `PrintDocumentPrepare` ·
`PrintDocumentRelease` · `PrintPreviewProformat` · `ModuleHasPrintFormat` ·
`UpdatePrintedOn` · `RegeneratePrintQueryFields`

### Correo
`SendMail` · `SendMailXE` · `SendMailURL` · `CreateEmail` · `CreateEmails` ·
`SendMultipleEmail` · `AddNewEmailToEntity` · `GetEmailTemplateID` ·
`GetEmailTemplateParams` · `Mail`

### Combos / formularios (UI nativa, por si modelo A)
`FillCombo` · `FillComboFromTable` · `LoadCboValues` · `LoadCboTaxTypes` ·
`SetCboItemValue` · `SetCboListindex` · `SetCboValueDisplay` ·
`SetComboItemValueBasedOnText` · `RetrieveCboTextItem` · `FillDataForm` ·
`GetValuesForm` · `FillArrayData` · `LockAllFields` · `UnlockAllFields` ·
`ShowMessage2` · `ShowFrmStatusActivity` · `UpdateStatusActivity` · `OpenBrowser`

### Utilidades joya
`GetTotalLetter` (importe con letra) · `GetTotalLetterEN` · `FormatCurrency` ·
`GetBarCode` · `GetFormatedDateValue` · `GetLastDayMonth` · `ConvertDateTimeToUTC` ·
`DateFromString` · `Truncate` · `TruncateDouble` · `Pad` · `EncryptString` ·
`DecryptString` · `NumDecimalesMoneda` · `NumDecimalesPUnitario` · `NumDecimalesConceptos`

### Sesión / contexto / sistema
`userID` · `UserName` · `UserLanguageID` · `OwnedBusinessEntityID` · `CurrencyID` ·
`CountryID` · `GroupID` · `ActiveModuleID` · `COMERCIAL_RFC` · `COMERCIAL_LICENCE` ·
`DataLayer` · `Datalayers` · `DBVersion` · `DBType` · `SoftwareVersion` · `XEVersion` ·
`VersionMajor` · `GetModuleConnectionString` · `GetModuleParameter` ·
`SaveModuleParameter` · `GetParameter` · `SetParameter`

### Logging / auditoría
`WriteToLog` · `WriteToLogFile` · `WriteToTableLog` · `LogWindowsEvent` ·
`EnqueueLogError` · `EnqueueLogInfo` · `EnqueueLogWarning` · `EnqueueLogDebug` ·
`EnqueueLogCritical` · `TraceApplicacion`

### Web / red
`GetWebContent` · `GetHTMLFromURL` · `GetWSResponse` · `DownloadURLImage` ·
`checkInternetConnection` · `IsConnectedToInternet` · `RunShellExecute`

---

## Notas de diseño

- **No todo se expone.** Muchas funciones son internas (migraciones, licencias, sync
  nube). La capa `ctx.erp.*` expondrá un **subconjunto curado y seguro**, con permisos
  (`erp.refresh`, `erp.open_document`, `erp.affect_stock`, etc.).
- Helpers como `GetTotalLetter`, `GetProductStock`, `GetSalePrice`, `ValidRFC`,
  `FormatCurrency` se exponen **directos** para que un script ni escriba SQL.
- Las operaciones que escriben (`AffectStockNEW`, `CancelDocument`, `Save`) requieren
  **permiso explícito** y pasan por la auditoría del host.
- Llamarlas siempre en el **hilo de UI correcto** del addon (son COM del proceso 32-bit).

---

## Apéndice — lista completa de los 411 miembros útiles

<details><summary>Desplegar lista alfabética completa</summary>

```
AccountingGetFormatLevel, ActivatePanelMenu, ActivatedSecurity, ActiveModuleID,
ActualizarParcialidad, AddBCLToCollection, AddButton, AddButtonToRibbonGroup,
AddNewEmailToEntity, AddOnActivation, AddendaValores, Addendas,
AffectProductKardexTable1, AffectStock, AffectStockNEW, AjustarSaldosInsolutos,
AjustarSaldosInsolutosMasivo, AllBCLClosed, AlreadyDocsSigned,
AplicarNuevasColumnasRecepcionCompra, AplicarNuevoMetodoDeCosteo,
AplicarNuevosPolizaConceptos, AreThereAnyRejectedDocuments, AreThereAnyRequiredDocuments,
ArrangeDBModuleID, ArrangeDBNewDLLVersion, ArrangeDBNewDRLVersion, BCLs,
BusinessEntityTimer, CalcularCostos, CalculateAccountingNumber, CancelDocument,
ChangeStatusQR, ChangeStatusQRFactura, CheckCboNotInListElement, checkInternetConnection,
CheckValidationObject, ClearData, ClearRecentFiles, CloseForInactivity,
CloseFrmStatusActivity, CloseFrmStatusActivity2, CmdlgShowOpen,
CmdlgShowOpenInitDirectory, COMERCIAL_LICENCE, COMERCIAL_RFC, ComercialDLL_Codigos,
ComercialDLL_FileVersion, ComercialDLL_Licencia, ComercialDLL_RFCs,
ComercialDLL_SerialNumber, ComercialDLL_TipoRFC, ComercialDLL_Token,
ComercialDLL_TokenRFC, ComercialIsAtLeast, ComercialIsMultiRFC,
CommandBarsCustomization, ConciliateIDs, ConvertDatabase, ConvertDateTimeToUTC,
ConvertSQLDatabase, ConvertToVersionString, CountryID, CrearBotonVerificarStatusCancelacion,
CrearCuentaBanco, CrearCuentaCliente, CrearCuentaProveedor, CrearCuentaTipoAcreedor,
CrearCuentaTipoGasto, CreateDocsSignatureRfcEntry, CreateEmail, CreateEmails, CreatePDF,
CreateWSPDFParameter, CRMActivated, Currencies, CurrencyID, DataLayer, Datalayers,
DateFromString, DBAvailable, DBisVirgin, DBType, DBVersion, DecryptString, Delete,
DeletedCancelAutogeneratedDocuments, DeleteFinancialOperations, DeleteInconsistantRecords,
DeleteResponse, DevEnvironment, DLLInUse, DLookup, DLookupValue, DLookupWithCase,
docDocumentCostLast, docDocumentItemCostLast, Documents, DocumentSignature,
DownloadURLImage, EditComments, EditValidated, EliminarPolizasConContabiliza,
EncryptString, engRefMultilinguals, engRefMultilingualsLargeKey, EnqueueLogCritical,
EnqueueLogDebug, EnqueueLogError, EnqueueLogInfo, EnqueueLogWarning,
ExecuteFunction, ExecuteFunction1, ExecuteProcessAfterSystemLoaded, ExitForm,
ExportJanus, ExportJanusToExcel, ExportJanusToWord, ExportJanusToWordText,
ExportQueryToExcel, ExportToNotePad, ExtraFieldsMandatoryOK, FieldExists,
FieldExistsInTable, FileCopy2, FillArrayData, FillCombo, FillComboFromTable, FillDataForm,
FinancialOperationConciliation, FormatCurrency, FormHWnd, GetAppKeyToken,
GetArrayNbrCols, GetArrayNbrRows, GetBarCode, GetBusinessEntitySalePrice, GetBuyPrice,
GetCadenaOriginalTFD, GetCoefConversion, GetContactPartName, GetContactValue,
GetCostLast, GetCostPrice, GetCostPriceComercial, GetCurrencyRate, GetDefaultValue,
GetDevelopmentToken, GetEmailTemplateID, GetEmailTemplateParams, GetExcelColumnLetter,
GetFieldType, GetFieldTypeName, GetFileName, GetFirstApplicationKey, GetFormatedDateValue,
GetFormatedXML, GetHtmlContent, GetHTMLFromURL, GetHTMLReportStructure, GetIcon,
GetInfoCertificado, GetLastDayMonth, GetLicenceActivatedForControl, GetLicenceKeys,
GetMaxValueField, GetMaxVersionSynchro, GetModuleConnectionString, GetModuleDLLName,
GetModuleID, GetModuleIDDocumentType, GetModuleParameter, GetMultilingualQuery,
GetMultilingualValue, GetMultilingualValueFromQuery, GetMultipleFilesCount,
GetMultipleFilesItem, GetMultipleFilesPath, GetNbrApplications, GetNumberOfFilesInZipFile,
GetOfficeVersion, GetPanel, GetPanelID, GetParameter, GetPath, GetPriceWithTaxes,
GetProductDeliverToCustomer, GetProductDiscountPriceList, GetProductStock,
GetProportionalAmountPerc, GetQRCode, GetRateBanxico, GetRateCurrencies,
GetRateCurrencyDate, GetSalePrice, GetSecurityFunctionality, GetSerialNumberNumValue,
GetSerialNumberPrefix, GetSQLForThisDBEngine, GetSQLView, GetStatusPaidID,
GetTotalLetter, GetTotalLetterEN, GetUserCanElevatePrivileges, GetValue, GetValueString,
GetValuesForm, GetWebContent, GetWindowsVersion, GetWSResponse, GotoModuleID,
GridExColumnExists, GroupID, HasValidFileCompressed, HideRibbonGroups, IconPath,
ImportCatalogFromExcel, ImportExcelToTable, InitPanelPositions, InternetConnection,
IsConnectedToInternet, IsExcelInstalled, IsValidXml, IsWordInstalled, JsColumnExists,
LICENCE_CONTPAQ, LicenceActivated, LicenceIsAtLeast, LoadCboTaxTypes, LoadCboValues,
LoadClasses, LoadFilePicture, LoadIcons, LoadLayoutCommandBars, LoadLicences,
LoadMultilingual, LoadRibbon, LoadShortcuts, LockAllFields, LogWindowsEvent, Mail,
MenuCTLPressed, ModuleHasPrintFormat, Modules, MultiLingualGetText,
MultilingualHideLabelControls, MultilingualLoadLabelControls,
MultilingualSaveLabelControls, MustRefreshGrid, NewCopyOfThisModule,
NewInstanceOfThisModule, NF, NodeExists, NumDecimalesConceptos, NumDecimalesMoneda,
NumDecimalesPUnitario, OpenBrowser, OpenModule, OwnedBusinessEntityID, Pad,
PaintLockedControls, PrintDoc, PrintDocumentPrepare, PrintDocumentRelease, PrintModule,
PrintModuleItems, PrintPreviewProformat, ProductID, ProductIsKit, ProductKey,
queryExists, ReactivateDocument, RecalcCostComercial, RecalcCostFiscal,
RecalcProductStock, RecalcStock, RecentFiles, RefreshGrid, RefreshInternetConnection,
RefreshRibbon, RegeneratePrintQueryFields, ReleaseRecord, ReloadRibbonDataFromDB,
ReloadSections, ReloadSecurityClasses, RemoveBCLFromCollection, RemoveDirectory,
ReplaceReferenceDictionaryKeysWithValues, ReplaceStringWithRecordFields, ResetLicencesComercial,
ResizePanels, ResizePicture, RetrieveCboTextItem, RibbonEditGroup, RibbonMenusEditGroup,
RunScripts, RunShellExecute, Save, SaveAllTaxesPayment, SaveAndClose, SaveAndNew,
SaveDataUC, SaveDefaultValue, SaveLayoutCommandBars, SaveModuleParameter,
SaveModuleParameter2, SelectFilePicture, SendMail, SendMailURL, SendMailXE,
SendModuleMessage, SendMultipleEmail, SendWizz, SetActiveTab, SetApplicationIcon,
SetCboItemValue, SetCboListindex, SetCboListIndexBasedOnItemData, SetCboValueDisplay,
SetComboItemValueBasedOnText, SetDatalayers, SetGUIDsToUsersAndBusinessEntities,
SetModulePermissions, SetParameter, SetSkinFrameWork, SetSoftwareKey,
SetStatusControlByLicence, ShowAddendaForm, ShowDatabaseSelection, ShowMessage2,
ShowMultilingualForm, ShowOnTop, ShowPanel, ShowPanelTab, ShowTaxTypeDetail,
ShowWorkFlowDates, SincronizarPolizasConContabiliza, SoftwareKey, SoftwareVersion,
StatusExtra, SyncCloudAccountingAccount, SynchronizeOutlookContacts, SyncQueryFacturaRetenciones,
SyncWithWopen, TableExists, TestConnectionWithQRInvoice, TestConnectionWithWopen,
TimeLeftClose, TraceApplicacion, TranslateCmd, TriggerAnalytics, TriggerNucleusForm,
Truncate, TruncateDouble, UnboundReadData, UnlockAllFields, UpdateDocumentPaidInfo,
UpdateMultilingualField, UpdatePrintedOn, UpdateStatus, UpdateStatusActivity,
UpdateStatusDelivery, UpdateTableSynchro, UpdateUserAction, UseMultilingual, userID,
UserLanguageID, UserName, ValidateFinancialEntityNumber, ValidRFC, VerifyCreditLimit,
VerifyCreditLimitOverdue, VerifyIfControlEnabledByLicence, VerifyRecordUsage,
VersionMajor, WOpenActive, WriteToLog, WriteToLogFile, WriteToLogRecalculate,
WriteToTableLog, XEApps, XEVersion, ZipFile
```

</details>
