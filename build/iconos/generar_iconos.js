// Genera el catálogo de íconos BrosLMV: convierte los SVG de Lucide (licencia ISC) a .ico clásicos (BMP 32 bits, 16/32/48 px),
// que es lo que Comercial lee de …\ComercialSP\Icons. Uso: node generar_iconos.js <carpeta_salida> [color]
// Salida: BrosLMV_<nombre>.ico (uno por ícono), iconos.json (catálogo con etiquetas para el buscador) y LICENCIA_Lucide.txt.
const fs = require('fs');
const path = require('path');
const sharp = require('sharp');

const salida = path.resolve(process.argv[2] || 'salida');
const color = process.argv[3] || '#1F5FD6';   // azul de acento BrosLMV
const TAM = [16, 32, 48];
const lucide = path.join(__dirname, 'node_modules', 'lucide-static');
const dirSvg = path.join(lucide, 'icons');

// ICO clásico: cada imagen como DIB de 32 bits (BGRA, de abajo hacia arriba) + máscara AND en ceros.
function dib(rgba, w, h) {
  const filaAnd = Math.ceil(w / 32) * 4;
  const buf = Buffer.alloc(40 + w * h * 4 + filaAnd * h);
  buf.writeUInt32LE(40, 0); buf.writeInt32LE(w, 4); buf.writeInt32LE(h * 2, 8);
  buf.writeUInt16LE(1, 12); buf.writeUInt16LE(32, 14); buf.writeUInt32LE(0, 16);
  buf.writeUInt32LE(w * h * 4, 20);
  let o = 40;
  for (let y = h - 1; y >= 0; y--)
    for (let x = 0; x < w; x++) {
      const i = (y * w + x) * 4;
      buf[o++] = rgba[i + 2]; buf[o++] = rgba[i + 1]; buf[o++] = rgba[i]; buf[o++] = rgba[i + 3];
    }
  return buf;
}
function ico(imagenes) {
  const cab = Buffer.alloc(6 + 16 * imagenes.length);
  cab.writeUInt16LE(0, 0); cab.writeUInt16LE(1, 2); cab.writeUInt16LE(imagenes.length, 4);
  let off = cab.length;
  imagenes.forEach((im, i) => {
    const e = 6 + 16 * i;
    cab[e] = im.w >= 256 ? 0 : im.w; cab[e + 1] = im.w >= 256 ? 0 : im.w; cab[e + 2] = 0; cab[e + 3] = 0;
    cab.writeUInt16LE(1, e + 4); cab.writeUInt16LE(32, e + 6);
    cab.writeUInt32LE(im.data.length, e + 8); cab.writeUInt32LE(off, e + 12);
    off += im.data.length;
  });
  return Buffer.concat([cab, ...imagenes.map(i => i.data)]);
}

(async () => {
  fs.mkdirSync(salida, { recursive: true });
  const tags = JSON.parse(fs.readFileSync(path.join(lucide, 'tags.json'), 'utf8'));
  const nombres = fs.readdirSync(dirSvg).filter(f => f.endsWith('.svg')).map(f => f.slice(0, -4)).sort();
  const catalogo = [];
  let n = 0;
  for (const nombre of nombres) {
    const svg = fs.readFileSync(path.join(dirSvg, nombre + '.svg'), 'utf8').replace(/currentColor/g, color);
    const ims = [];
    for (const t of TAM) {
      const { data } = await sharp(Buffer.from(svg), { density: 96 * t / 24 * 2 }).resize(t, t).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
      ims.push({ w: t, data: dib(data, t, t) });
    }
    fs.writeFileSync(path.join(salida, 'BrosLMV_' + nombre + '.ico'), ico(ims));
    catalogo.push({ n: nombre, t: tags[nombre] || [] });
    if (++n % 400 === 0) console.log(n + ' / ' + nombres.length);
  }
  fs.writeFileSync(path.join(salida, 'iconos.json'), JSON.stringify({ origen: 'Lucide ' + require(path.join(lucide, 'package.json')).version + ' (ISC)', prefijo: 'BrosLMV_', iconos: catalogo }));
  fs.copyFileSync(path.join(lucide, 'LICENSE'), path.join(salida, 'LICENCIA_Lucide.txt'));
  console.log('OK: ' + n + ' íconos en ' + salida);
})().catch(e => { console.error(e); process.exit(1); });
