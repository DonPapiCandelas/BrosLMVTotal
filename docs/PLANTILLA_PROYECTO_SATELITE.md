# AGENTS.md de este proyecto (satélite de BrosLMV)

> Copia este archivo a la raíz de un proyecto de cliente nuevo (renómbralo `AGENTS.md`
> ahí) o pégalo al inicio del chat cuando abras una sesión de IA en ese proyecto —
> antes del primer mensaje de trabajo. Reemplaza `<NOMBRE_DEL_PROYECTO>` por el nombre
> real. Este archivo vive en BrosLMV (`docs/PLANTILLA_PROYECTO_SATELITE.md`) como
> plantilla — el que se usa de verdad es la copia dentro del proyecto satélite.

Este proyecto (`<NOMBRE_DEL_PROYECTO>`) construye sobre **BrosLMV**
(`C:\MLVTotal`, https://github.com/DonPapiCandelas/BrosLMVTotal) — el addon/familia
de herramientas para CONTPAQi Comercial PRO. Antes de trabajar aquí, lee
`C:\MLVTotal\AGENTS.md` completo — sus reglas (regla de oro, nunca reimplementar el
cifrado de CONTPAQi, contraseñas solo por stdin, nunca nombrar terceros/clientes)
aplican igual en este proyecto.

## La regla que más importa aquí

**Este proyecto NO es donde vive el conocimiento genérico de BrosLMV — `C:\MLVTotal`
sí.** Si mientras trabajas aquí construyes, corriges o descubres algo que no es
específico de este cliente (un patrón nuevo de `ctx.erp`, una tabla propia reusable,
un bug real del addon, una forma de resolver algo que Comercial no hace solo, un
hallazgo sobre cómo funciona XEngine/Comercial por dentro), **no basta con que
funcione y quede documentado aquí** — eso se pierde en cuanto este proyecto se
archiva o nadie lo vuelve a abrir.

Anótalo en `PENDIENTE_BACKPORT_BROSLMV.md` en la raíz de este proyecto (créalo si no
existe), una entrada por hallazgo:

```markdown
## <fecha> — <título corto>
Qué es (genérico, sin nombrar a este cliente): ...
Dónde vive aquí: <ruta relativa en este proyecto>
Por qué importa para BrosLMV en general: ...
```

Cuando el usuario diga "llévalo a BrosLMV" o "revisa este proyecto e intégralo" (en
una sesión de Claude Code sobre `C:\MLVTotal`), ese archivo es el punto de partida.

## Qué SÍ se queda solo aquí

Lo específico de este cliente: nombres, cuentas contables reales, rutas de red,
servidores, credenciales, bases de datos, cualquier dato que identifique a la empresa.
**Nunca** se sube a `C:\MLVTotal` ni a GitHub tal cual — si algo de esto resulta
genérico y vale la pena llevarlo, se generaliza primero (sanitizar nombres/rutas,
convertir a patrón reusable) — ver `docs/MOTOR_ASIENTOS_CONTABLES.md` en BrosLMV como
ejemplo ya hecho de esa operación completa.
