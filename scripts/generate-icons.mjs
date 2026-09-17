// Generates every PNG image this plugin ships, from scratch, with no image
// dependencies and no network access. Re-run with `npm run icons` (or
// `node scripts/generate-icons.mjs`) any time you want to tweak the shapes
// below. See the companion Stream Deck project's scripts/png.mjs for the
// same encoder - copied verbatim since it has zero project-specific code.
import { mkdir, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { Canvas } from "./png.mjs";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const PLUGIN_DIR = path.join(__dirname, "..", "LightroomPresetsPlugin");

const PALETTE = {
	presetPurple: [111, 76, 219, 255],
	presetPurpleDark: [79, 53, 168, 255],
	white: [255, 255, 255, 255],
};

async function writePng(canvas, relativePath) {
	const absolute = path.join(PLUGIN_DIR, relativePath);
	await mkdir(path.dirname(absolute), { recursive: true });
	await writeFile(absolute, canvas.toPngBuffer());
	console.log(`wrote ${relativePath} (${canvas.width}x${canvas.height})`);
}

function background(size, colorTop, colorBottom) {
	const canvas = new Canvas(size, size);
	for (let y = 0; y < size; y++) {
		const t = y / size;
		const color = lerpColor(colorTop, colorBottom, t);
		canvas.fillRect(0, y, size, y + 1, color);
	}
	canvas.clipToRoundedRect(size * 0.18);
	return canvas;
}

function lerpColor(a, b, t) {
	return [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t, 255];
}

function drawPresetGlyph(canvas, cx, cy, r, color) {
	canvas.annulus(cx, cy, r * 0.72, r, color);
	canvas.fillCircle(cx, cy, r * 0.32, color);
	const angle = -Math.PI / 2.6;
	const x0 = cx + Math.cos(angle) * r * 0.86;
	const y0 = cy + Math.sin(angle) * r * 0.86;
	const x1 = cx + Math.cos(angle) * r * 1.18;
	const y1 = cy + Math.sin(angle) * r * 1.18;
	canvas.drawThickLine(x0, y0, x1, y1, Math.max(1, r * 0.16), color);
}

function drawCheckmark(canvas, cx, cy, size, thickness, color) {
	canvas.drawThickLine(cx - size * 0.5, cy, cx - size * 0.12, cy + size * 0.4, thickness, color);
	canvas.drawThickLine(cx - size * 0.12, cy + size * 0.4, cx + size * 0.55, cy - size * 0.35, thickness, color);
}

function drawWarningTriangle(canvas, cx, cy, size, fillColor, markColor) {
	const h = size * 0.9;
	const points = [
		[cx, cy - h * 0.62],
		[cx - size * 0.58, cy + h * 0.42],
		[cx + size * 0.58, cy + h * 0.42],
	];
	canvas.fillTriangle(points, fillColor);
	canvas.drawThickLine(cx, cy - h * 0.18, cx, cy + h * 0.08, size * 0.09, markColor);
	canvas.fillCircle(cx, cy + h * 0.24, size * 0.055, markColor);
}

async function generatePluginIcon() {
	const size = 256;
	const canvas = background(size, PALETTE.presetPurple, PALETTE.presetPurpleDark);
	drawPresetGlyph(canvas, size / 2, size / 2, size * 0.3, PALETTE.white);
	await writePng(canvas, "package/metadata/Icon256x256.png");
}

async function generateActionFeedbackImages() {
	// 80x80 matches Logitech's own DemoPlugin embedded action images
	// (images/ThumbUp.png, images/ThumbDown.png).
	const size = 80;

	const success = new Canvas(size, size);
	success.fillCircle(size / 2, size / 2, size * 0.46, [45, 163, 92, 255]);
	drawCheckmark(success, size / 2, size / 2, size * 0.42, size * 0.09, PALETTE.white);
	await writePng(success, "images/Success.png");

	const error = new Canvas(size, size);
	drawWarningTriangle(error, size / 2, size / 2 + size * 0.05, size * 0.78, [199, 62, 62, 255], PALETTE.white);
	await writePng(error, "images/Error.png");
}

await generatePluginIcon();
await generateActionFeedbackImages();

console.log("Icon generation complete.");
