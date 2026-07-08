# Política de seguridad

BrosLMV se ejecuta **dentro de CONTPAQi Comercial** y maneja **conexiones a SQL Server**
y **credenciales**. Por eso la seguridad es prioritaria.

## Reportar una vulnerabilidad

**No abras un issue público** para vulnerabilidades. Repórtalas en privado:

- Correo: **cristofer.candelas.garcia@gmail.com**
- Asunto sugerido: `[SECURITY] BrosLMV — <resumen>`

Incluye, si puedes: versión afectada (`AssemblyVersion`), pasos para reproducir, impacto
y una posible mitigación. Te responderemos lo antes posible y coordinaremos una
divulgación responsable una vez exista el arreglo.

## Áreas sensibles del proyecto

- **Credenciales:** desde la v2.5.0 se cifran con DPAPI (`broslmv_cred.dat`, ámbito
  LocalMachine + entropía). Nunca debe volver a guardarse una contraseña en texto plano.
  Ver `docs/CHANGELOG.md` (2.5.0) y `src/Rutas.cs`.
- **SQL:** todo acceso debe ser **parametrizado**. El motor de tokens
  (`ResolverTokensCore`) sustituye decimales con cultura invariante para no romper el SQL,
  pero **no** es un saneador contra inyección: el SQL de las recetas debe parametrizarse.
- **v3.0 (Python):** el diseño separa el runtime en un host fuera de proceso con SQL
  **solo-proxy** (el script nunca ve la contraseña) y un token UUID en el Named Pipe.
  Ver `docs/ARQUITECTURA_V3.md` §6.

## Versiones soportadas

Se da soporte de seguridad a la **última versión** publicada. Actualiza antes de reportar.
