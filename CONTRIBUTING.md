# Cómo contribuir a BrosLMV

¡Gracias por tu interés! BrosLMV es software libre bajo **GPL-3.0**: un motor de botones y
scripting multi-lenguaje (C#/Python/SQL) para CONTPAQi Comercial PRO, con generación de PDF,
ejecución headless y un subproducto de descarga de CFDI del SAT. Tiene además un motor de
recetas no-code (MVP funcional, `src/Recetas.cs`) — útil, pero no es lo único ni lo
principal del proyecto hoy.

> Antes de escribir código, lee [`AGENTS.md`](AGENTS.md) (reglas no-negociables),
> [`docs/INDICE.md`](docs/INDICE.md) (índice de toda la documentación) y
> [`docs/ESTADO.md`](docs/ESTADO.md) (estado actual).

---

## 1. Reglas de oro del repositorio

Estas reglas son **obligatorias** y mantienen la calidad del proyecto (detalle completo en
[`AGENTS.md`](AGENTS.md)):

1. **Toda la documentación es total.** Cada cambio de código se acompaña, en el **mismo
   PR**, de:
   - Nueva entrada en [`docs/CHANGELOG.md`](docs/CHANGELOG.md).
   - Subir `AssemblyVersion` en [`src/ClsMain.cs`](src/ClsMain.cs) (SemVer).
   - Bloque nuevo en `src/assets/notas_version.html`.
   - Actualizar los `.md` afectados (API → `SCRIPTING_CONTRATOS.md`, etc.).
   - Pasar `build/verificar_regla_de_oro.ps1` y `build/probar_humo.ps1` en verde.
2. **Versionado atómico.** Un PR = un cambio coherente. Mensaje de commit descriptivo
   (qué y por qué).
3. **Las pruebas de humo viven en `build/humo/casos/`** (33 casos, `build/probar_humo.ps1`
   los corre todos). Si tu cambio lo amerita, agrega un caso nuevo ahí; para experimentos
   sueltos que no se van a quedar, usa una carpeta local fuera del repo (nunca se commitean).
4. **Nunca** subas credenciales, cadenas de conexión reales, datos de empresas, ni nombres de
   terceros/clientes — ver la lista de reglas en [`AGENTS.md`](AGENTS.md) §3.

## 2. Flujo de trabajo

1. Haz *fork* del repo y crea una rama descriptiva (`feature/receta-cambiar-estatus`).
2. Compila y prueba (ver §3).
3. Actualiza la documentación (regla #1).
4. Abre un *Pull Request* contra `main` con la plantilla.

## 3. Compilar y probar

Requiere **.NET SDK** (8+). Desde la raíz:

```powershell
# Compila el núcleo (DLL) y actualiza instalador\bin
.\build\generar_instalador.ps1

# (Opcional) empaqueta los .exe
.\build\generar_exes.ps1
```

Guía detallada: [`docs/DESARROLLO.md`](docs/DESARROLLO.md).

## 4. Estilo de código

- Sigue el estilo del código existente: comentarios en español, nombres descriptivos,
  densidad de comentarios similar a la del archivo que tocas.
- C# moderno (`LangVersion=latest`), target `net48`.
- No introduzcas dependencias nuevas sin justificarlo en el PR.

## 5. Aportar una receta no-code

Las recetas son el contrato extensible (ver [`docs/RECETAS_NOCODE.md`](docs/RECETAS_NOCODE.md)).
Una receta nueva debe declarar su **esquema de configuración** (qué pregunta al crear el
botón) y su **implementación**. Documenta el caso de uso y un ejemplo.

## 6. Reportar errores o pedir features

Usa las plantillas de *Issues*. Para **vulnerabilidades de seguridad**, NO abras un issue
público: sigue [`SECURITY.md`](SECURITY.md).

## 7. Licencia de tus contribuciones

Al contribuir, aceptas que tu aporte se publique bajo la **GPL-3.0** del proyecto.
