# Compilar con GitHub (sin instalar herramientas)

GitHub Actions compila CausaFix en un Windows en la nube cada vez que subes cambios: ejecuta las pruebas, compila 2024, 2025 y 2026, y genera el zip instalable. Es gratis para repositorios públicos y, con límites mensuales, para privados.

## Primera vez

1. Crea una cuenta en https://github.com (gratis).
2. Instala **GitHub Desktop** (https://desktop.github.com) e inicia sesión con esa cuenta. Es más fiable que subir archivos desde el navegador (la web admite 100 archivos por subida y a veces omite la carpeta oculta `.github`).
3. En GitHub Desktop: **File → New repository** → Name `CausaFix`, Local path por ejemplo `C:\Repos` → **Create repository**.
4. Descomprime `CausaFix-0.1.0-source.zip` y copia **todo el contenido** de su carpeta `CausaFix` (incluida la carpeta `.github`) dentro de `C:\Repos\CausaFix`. Si el Explorador no muestra `.github`, activa *Vista → Elementos ocultos*.
5. En GitHub Desktop aparecen los archivos: escribe un resumen ("CausaFix 0.1.0") → **Commit to main** → **Publish repository** (deja marcado *Keep this code private*).
6. En el navegador abre tu repositorio → pestaña **Actions**. Verás la ejecución **build** (unos 5 minutos).
   - ✅ verde: entra en la ejecución y descarga **CausaFix-package** en *Artifacts*. Dentro está el zip instalable.
   - ❌ rojo: entra en la ejecución, abre el paso en rojo, copia el texto del error y pásamelo.

Para cambios posteriores: reemplaza los archivos en `C:\Repos\CausaFix`, **Commit** y **Push origin**; GitHub vuelve a compilar.

## Instalar lo que generó GitHub

Descomprime el zip descargado (dentro hay otro zip `CausaFix-0.1.0.zip`; descomprímelo también), cierra Revit y ejecuta `Install.cmd` (solo tú) o `Install-AllUsers.cmd` (todos los usuarios del PC).

## Versiones publicadas (opcional)

Para dejar una versión fija descargable: pestaña **Releases → Draft a new release → Choose a tag** → escribe `v0.1.0` → **Publish release**. GitHub compila y adjunta el zip al release.

## Nota técnica

En GitHub no hay Revit instalado: las referencias de compilación de la API se toman de los paquetes NuGet `Nice3point.Revit.Api.RevitAPI` / `RevitAPIUI` (solo para compilar; no se incluyen en el paquete). Compilar en GitHub confirma que el código compila; el funcionamiento real se comprueba en Revit con la prueba guiada (`GUIA.md §12`).
