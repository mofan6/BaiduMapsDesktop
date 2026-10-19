// npm install sharp, then: node tools/generate-icon.cjs
// An optional first argument selects an existing sharp installation.
const fs = require('node:fs');
const path = require('node:path');
const sharp = require(process.argv[2] || 'sharp');

async function main() {
  const assets = path.join(__dirname, '..', 'assets');
  const svg = fs.readFileSync(path.join(assets, 'AppIcon.svg'));
  const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
  const images = await Promise.all(sizes.map(size =>
    sharp(svg, { density: 384 }).resize(size, size).png().toBuffer()));
  const directory = Buffer.alloc(6 + 16 * sizes.length);
  directory.writeUInt16LE(1, 2);
  directory.writeUInt16LE(sizes.length, 4);
  let position = directory.length;
  images.forEach((png, index) => {
    const offset = 6 + 16 * index;
    directory[offset] = sizes[index] === 256 ? 0 : sizes[index];
    directory[offset + 1] = directory[offset];
    directory.writeUInt16LE(1, offset + 4);
    directory.writeUInt16LE(32, offset + 6);
    directory.writeUInt32LE(png.length, offset + 8);
    directory.writeUInt32LE(position, offset + 12);
    position += png.length;
  });
  fs.writeFileSync(path.join(assets, 'AppIcon.ico'), Buffer.concat([directory, ...images]));
  fs.writeFileSync(path.join(assets, 'AppIcon-preview.png'), images[images.length - 1]);
  process.stdout.write(`Generated ${sizes.length} native icon sizes from AppIcon.svg (${position} bytes).\n`);
}
main().catch(error => { process.stderr.write(error.stack + '\n'); process.exitCode = 1; });
