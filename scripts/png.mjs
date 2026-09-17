// Minimal, dependency-free PNG encoder + a tiny 2D raster canvas.
// Only implements what the icon generator needs: 8-bit RGBA, no palette,
// no interlacing. Uses Node's built-in zlib for the DEFLATE/CRC work so the
// icon pipeline has zero npm dependencies.
import { crc32, deflateSync } from "node:zlib";

export class Canvas {
	constructor(width, height) {
		this.width = width;
		this.height = height;
		this.data = new Uint8ClampedArray(width * height * 4);
	}

	setPixel(x, y, [r, g, b, a]) {
		if (x < 0 || y < 0 || x >= this.width || y >= this.height) {
			return;
		}
		const alpha = a / 255;
		const idx = (y * this.width + x) * 4;
		if (alpha >= 1) {
			this.data[idx] = r;
			this.data[idx + 1] = g;
			this.data[idx + 2] = b;
			this.data[idx + 3] = 255;
			return;
		}
		// Simple source-over blend so anti-aliased edges composite cleanly.
		const dstA = this.data[idx + 3] / 255;
		const outA = alpha + dstA * (1 - alpha);
		if (outA <= 0) {
			return;
		}
		for (let c = 0; c < 3; c++) {
			const src = [r, g, b][c];
			const dst = this.data[idx + c];
			this.data[idx + c] = (src * alpha + dst * dstA * (1 - alpha)) / outA;
		}
		this.data[idx + 3] = outA * 255;
	}

	fillRect(x0, y0, x1, y1, color) {
		for (let y = Math.max(0, Math.round(y0)); y < Math.min(this.height, Math.round(y1)); y++) {
			for (let x = Math.max(0, Math.round(x0)); x < Math.min(this.width, Math.round(x1)); x++) {
				this.setPixel(x, y, color);
			}
		}
	}

	fillRoundedRect(x0, y0, x1, y1, radius, color) {
		const r = Math.min(radius, (x1 - x0) / 2, (y1 - y0) / 2);
		for (let y = Math.floor(y0); y < Math.ceil(y1); y++) {
			for (let x = Math.floor(x0); x < Math.ceil(x1); x++) {
				const coverage = roundedRectCoverage(x + 0.5, y + 0.5, x0, y0, x1, y1, r);
				if (coverage > 0) {
					this.setPixel(x, y, withAlpha(color, color[3] * coverage));
				}
			}
		}
	}

	/** Zeroes alpha outside a rounded rect covering the whole canvas, without touching color already drawn (e.g. a gradient fill). */
	clipToRoundedRect(radius) {
		const r = Math.min(radius, this.width / 2, this.height / 2);
		for (let y = 0; y < this.height; y++) {
			for (let x = 0; x < this.width; x++) {
				const coverage = roundedRectCoverage(x + 0.5, y + 0.5, 0, 0, this.width, this.height, r);
				if (coverage < 1) {
					const idx = (y * this.width + x) * 4;
					this.data[idx + 3] = this.data[idx + 3] * coverage;
				}
			}
		}
	}

	fillCircle(cx, cy, r, color) {
		this.annulus(cx, cy, 0, r, color);
	}

	/** Fills the ring between innerR and outerR (a stroked circle when innerR = outerR - thickness). */
	annulus(cx, cy, innerR, outerR, color, startAngle = 0, endAngle = Math.PI * 2) {
		const box = Math.ceil(outerR) + 1;
		for (let y = -box; y <= box; y++) {
			for (let x = -box; x <= box; x++) {
				const px = cx + x + 0.5;
				const py = cy + y + 0.5;
				const dist = Math.hypot(px - cx, py - cy);
				if (dist > outerR + 0.75 || dist < innerR - 0.75) {
					continue;
				}
				let angle = Math.atan2(py - cy, px - cx);
				if (angle < 0) {
					angle += Math.PI * 2;
				}
				if (!angleInRange(angle, startAngle, endAngle)) {
					continue;
				}
				const outerCoverage = antiAliasCoverage(outerR - dist + 0.75, 0.75);
				const innerCoverage = innerR > 0 ? antiAliasCoverage(dist - innerR + 0.75, 0.75) : 1;
				const coverage = Math.min(outerCoverage, innerCoverage);
				if (coverage > 0) {
					this.setPixel(Math.round(cx + x), Math.round(cy + y), withAlpha(color, color[3] * coverage));
				}
			}
		}
	}

	drawThickLine(x0, y0, x1, y1, thickness, color) {
		const minX = Math.floor(Math.min(x0, x1) - thickness);
		const maxX = Math.ceil(Math.max(x0, x1) + thickness);
		const minY = Math.floor(Math.min(y0, y1) - thickness);
		const maxY = Math.ceil(Math.max(y0, y1) + thickness);
		for (let y = minY; y <= maxY; y++) {
			for (let x = minX; x <= maxX; x++) {
				const dist = distanceToSegment(x + 0.5, y + 0.5, x0, y0, x1, y1);
				const coverage = antiAliasCoverage(thickness / 2 - dist + 0.5, 0.5);
				if (coverage > 0) {
					this.setPixel(x, y, withAlpha(color, color[3] * coverage));
				}
			}
		}
	}

	fillTriangle(points, color) {
		const xs = points.map((p) => p[0]);
		const ys = points.map((p) => p[1]);
		const minX = Math.floor(Math.min(...xs));
		const maxX = Math.ceil(Math.max(...xs));
		const minY = Math.floor(Math.min(...ys));
		const maxY = Math.ceil(Math.max(...ys));
		for (let y = minY; y <= maxY; y++) {
			for (let x = minX; x <= maxX; x++) {
				if (pointInTriangle(x + 0.5, y + 0.5, points)) {
					this.setPixel(x, y, color);
				}
			}
		}
	}

	toPngBuffer() {
		return encodePng(this.width, this.height, this.data);
	}
}

function withAlpha(color, alpha) {
	return [color[0], color[1], color[2], alpha];
}

function antiAliasCoverage(signedDistanceInside, softness) {
	if (signedDistanceInside <= 0) {
		return 0;
	}
	if (signedDistanceInside >= softness) {
		return 1;
	}
	return signedDistanceInside / softness;
}

/** 1 = fully inside the rounded rect, 0 = fully outside, fractional right at the corner arcs. */
function roundedRectCoverage(px, py, x0, y0, x1, y1, radius) {
	const inCornerX = px < x0 + radius || px > x1 - radius;
	const inCornerY = py < y0 + radius || py > y1 - radius;
	if (!(inCornerX && inCornerY)) {
		return px >= x0 && px <= x1 && py >= y0 && py <= y1 ? 1 : 0;
	}
	const cx = px < x0 + radius ? x0 + radius : x1 - radius;
	const cy = py < y0 + radius ? y0 + radius : y1 - radius;
	const dist = Math.hypot(px - cx, py - cy);
	return antiAliasCoverage(radius - dist + 0.75, 0.75);
}

function angleInRange(angle, start, end) {
	if (start <= end) {
		return angle >= start && angle <= end;
	}
	return angle >= start || angle <= end;
}

function distanceToSegment(px, py, x0, y0, x1, y1) {
	const dx = x1 - x0;
	const dy = y1 - y0;
	const lengthSquared = dx * dx + dy * dy;
	if (lengthSquared === 0) {
		return Math.hypot(px - x0, py - y0);
	}
	let t = ((px - x0) * dx + (py - y0) * dy) / lengthSquared;
	t = Math.max(0, Math.min(1, t));
	const projX = x0 + t * dx;
	const projY = y0 + t * dy;
	return Math.hypot(px - projX, py - projY);
}

function pointInTriangle(px, py, [a, b, c]) {
	const sign = (p1, p2, p3) => (p1[0] - p3[0]) * (p2[1] - p3[1]) - (p2[0] - p3[0]) * (p1[1] - p3[1]);
	const d1 = sign([px, py], a, b);
	const d2 = sign([px, py], b, c);
	const d3 = sign([px, py], c, a);
	const hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
	const hasPos = d1 > 0 || d2 > 0 || d3 > 0;
	return !(hasNeg && hasPos);
}

function encodePng(width, height, rgba) {
	const signature = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);

	const ihdr = Buffer.alloc(13);
	ihdr.writeUInt32BE(width, 0);
	ihdr.writeUInt32BE(height, 4);
	ihdr[8] = 8; // bit depth
	ihdr[9] = 6; // color type: RGBA
	ihdr[10] = 0;
	ihdr[11] = 0;
	ihdr[12] = 0;

	const raw = Buffer.alloc((width * 4 + 1) * height);
	for (let y = 0; y < height; y++) {
		const rowStart = y * (width * 4 + 1);
		raw[rowStart] = 0; // no filter
		const pixelRow = Buffer.from(rgba.buffer, y * width * 4, width * 4);
		pixelRow.copy(raw, rowStart + 1);
	}
	const idatData = deflateSync(raw, { level: 9 });

	return Buffer.concat([
		signature,
		makeChunk("IHDR", ihdr),
		makeChunk("IDAT", idatData),
		makeChunk("IEND", Buffer.alloc(0)),
	]);
}

function makeChunk(type, data) {
	const typeBuffer = Buffer.from(type, "ascii");
	const length = Buffer.alloc(4);
	length.writeUInt32BE(data.length, 0);
	const crcInput = Buffer.concat([typeBuffer, data]);
	const crcValue = crc32(crcInput) >>> 0;
	const crcBuffer = Buffer.alloc(4);
	crcBuffer.writeUInt32BE(crcValue, 0);
	return Buffer.concat([length, typeBuffer, data, crcBuffer]);
}
